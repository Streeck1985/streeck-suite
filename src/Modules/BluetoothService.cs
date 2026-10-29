using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    internal class BluetoothDevice
    {
        public enum DeviceKind { Headphones, Keyboard, Mouse, Controller, Phone, Other }

        public string Key;        // Nombre limpio: un mismo aparato aparece en Windows como varios servicios
        public string Name;
        public DeviceKind Kind;
        public int Battery;       // 0..100
        public bool Connected;
    }

    /// <summary>
    /// Batería de los dispositivos Bluetooth (fase F) tal como la conoce Windows: la misma que enseña
    /// Configuración → Bluetooth. Se leen las propiedades de los dispositivos cada 10 s en segundo plano.
    /// </summary>
    internal static class BluetoothService
    {
        private const int PollMs = 10000;

        // Propiedades de dispositivo de Windows: nivel de batería y "conectado ahora"
        private static readonly Guid BatteryKey = new Guid("104EA319-6EE2-4701-BD47-8DDBF425BBE5");
        private const int BatteryPid = 2;
        private static readonly Guid ConnectedKey = new Guid("83DA6326-97A6-4088-9453-A1923F573B29");
        private const int ConnectedPid = 15;
        private static readonly Guid FriendlyNameKey = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0");
        private const int FriendlyNamePid = 14;

        private static readonly object Gate = new object();
        private static Timer _timer;
        private static List<BluetoothDevice> _devices = new List<BluetoothDevice>();
        private static readonly HashSet<string> LowBatteryWarned = new HashSet<string>();
        private static bool _firstPoll = true;

        /// <summary>
        /// Un dispositivo acaba de conectarse (hilo de segundo plano).
        /// </summary>
        public static event Action<BluetoothDevice> Connected;

        /// <summary>
        /// Un dispositivo conectado llegó al nivel de batería baja (una vez hasta que se cargue).
        /// </summary>
        public static event Action<BluetoothDevice> LowBattery;

        public static List<BluetoothDevice> ConnectedDevices
        {
            get { lock (Gate) return _devices.FindAll(d => d.Connected); }
        }

        public static void EnsureStarted()
        {
            lock (Gate)
            {
                if (_timer != null) return;
                _timer = new Timer(state => Poll(), null, 0, PollMs);
            }
        }

        private static void Poll()
        {
            try
            {
                List<BluetoothDevice> devices = ReadDevices();
                List<BluetoothDevice> previous;
                lock (Gate)
                {
                    previous = _devices;
                    _devices = devices;
                }

                Action<BluetoothDevice> connected = Connected, low = LowBattery;
                foreach (BluetoothDevice device in devices)
                {
                    BluetoothDevice before = previous.Find(d => d.Key == device.Key);
                    bool justConnected = device.Connected && (before == null || !before.Connected);
                    if (justConnected && !_firstPoll)
                    {
                        Log.Info("[BluetoothService] Conectado: {0} ({1} %)", device.Name, device.Battery);
                        if (connected != null) connected(device);
                    }

                    int threshold = SuiteSettings.BluetoothLowBatteryPercent;
                    if (threshold > 0 && device.Connected && device.Battery <= threshold && LowBatteryWarned.Add(device.Key))
                    {
                        Log.Info("[BluetoothService] Batería baja: {0} ({1} %)", device.Name, device.Battery);
                        if (low != null) low(device);
                    }
                    else if (device.Battery > threshold + 5)
                    {
                        LowBatteryWarned.Remove(device.Key); // Ya se cargó: puede volver a avisar
                    }
                }
                _firstPoll = false;
            }
            catch (Exception ex)
            {
                Log.Error("[BluetoothService] No se pudieron leer los dispositivos: {0}", ex.Message);
            }
        }

        #region Lectura con SetupAPI

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
            public DEVPROPKEY(Guid fmtid, int pid) { this.fmtid = fmtid; this.pid = (uint)pid; }
        }

        private const int DIGCF_PRESENT = 0x2;
        private const int DIGCF_ALLCLASSES = 0x4;

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string enumerator, IntPtr parent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref SP_DEVINFO_DATA data);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref SP_DEVINFO_DATA data, StringBuilder id, int size, out int required);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr set, ref SP_DEVINFO_DATA data, ref DEVPROPKEY key, out uint type,
            byte[] buffer, int size, out int required, int flags);

        private static List<BluetoothDevice> ReadDevices()
        {
            Dictionary<string, BluetoothDevice> byKey = new Dictionary<string, BluetoothDevice>(StringComparer.OrdinalIgnoreCase);
            IntPtr set = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (set == new IntPtr(-1)) return new List<BluetoothDevice>();
            try
            {
                SP_DEVINFO_DATA data = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };
                StringBuilder id = new StringBuilder(512);
                for (int i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
                {
                    int required;
                    if (!SetupDiGetDeviceInstanceId(set, ref data, id, id.Capacity, out required)) continue;
                    string instance = id.ToString();
                    if (!instance.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)) continue;

                    byte[] battery = ReadProperty(set, ref data, new DEVPROPKEY(BatteryKey, BatteryPid));
                    if (battery == null || battery.Length < 1) continue; // Solo los que informan batería
                    byte[] connected = ReadProperty(set, ref data, new DEVPROPKEY(ConnectedKey, ConnectedPid));
                    byte[] name = ReadProperty(set, ref data, new DEVPROPKEY(FriendlyNameKey, FriendlyNamePid));
                    if (name == null) continue;

                    string friendly = CleanName(Encoding.Unicode.GetString(name).TrimEnd('\0'));
                    BluetoothDevice device = new BluetoothDevice
                    {
                        Key = friendly,
                        Name = friendly,
                        Battery = Math.Min(100, (int)battery[0]),
                        // DEVPROP_BOOLEAN: 0xFF = verdadero
                        Connected = connected != null && connected.Length > 0 && connected[0] != 0,
                        Kind = Classify(friendly, instance)
                    };

                    BluetoothDevice existing;
                    if (byKey.TryGetValue(device.Key, out existing))
                    {
                        existing.Connected |= device.Connected;
                        existing.Battery = Math.Max(existing.Battery, device.Battery);
                    }
                    else
                    {
                        byKey[device.Key] = device;
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
            List<BluetoothDevice> list = new List<BluetoothDevice>(byKey.Values);
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        private static byte[] ReadProperty(IntPtr set, ref SP_DEVINFO_DATA data, DEVPROPKEY key)
        {
            uint type;
            int required;
            SetupDiGetDevicePropertyW(set, ref data, ref key, out type, null, 0, out required, 0);
            if (required <= 0) return null;
            byte[] buffer = new byte[required];
            return SetupDiGetDevicePropertyW(set, ref data, ref key, out type, buffer, buffer.Length, out required, 0) ? buffer : null;
        }

        #endregion

        // "Galaxy Buds Pro (006A) Hands-Free AG" -> "Galaxy Buds Pro"
        private static string CleanName(string name)
        {
            foreach (string suffix in new[] { " Hands-Free AG", " Hands-Free HF", " Hands-Free", " Transporte AVRCP", " Avrcp Transport", " Stereo" })
            {
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - suffix.Length);
            }
            int paren = name.LastIndexOf(" (");
            if (paren > 0 && name.EndsWith(")") && name.Length - paren <= 8) name = name.Substring(0, paren); // "(006A)"
            return name.Trim();
        }

        private static BluetoothDevice.DeviceKind Classify(string name, string instance)
        {
            string n = name.ToLowerInvariant();
            if (n.Contains("controller") || n.Contains("mando") || n.Contains("xbox") || n.Contains("dualsense") || n.Contains("gamepad")) return BluetoothDevice.DeviceKind.Controller;
            if (n.Contains("keys") || n.Contains("keyboard") || n.Contains("teclado")) return BluetoothDevice.DeviceKind.Keyboard;
            if (n.Contains("mouse") || n.Contains("ratón") || n.Contains("raton") || n.Contains("mx master") || n.Contains("mx anywhere")) return BluetoothDevice.DeviceKind.Mouse;
            // Perfil manos libres: 111F lo anuncian los teléfonos, 111E auriculares y altavoces
            if (instance.IndexOf("0000111F", StringComparison.OrdinalIgnoreCase) >= 0) return BluetoothDevice.DeviceKind.Phone;
            if (instance.IndexOf("0000111E", StringComparison.OrdinalIgnoreCase) >= 0) return BluetoothDevice.DeviceKind.Headphones;
            return BluetoothDevice.DeviceKind.Other;
        }

        public static string Glyph(BluetoothDevice.DeviceKind kind)
        {
            switch (kind)
            {
                case BluetoothDevice.DeviceKind.Headphones: return "";
                case BluetoothDevice.DeviceKind.Keyboard: return "";
                case BluetoothDevice.DeviceKind.Mouse: return "";
                case BluetoothDevice.DeviceKind.Controller: return "";
                case BluetoothDevice.DeviceKind.Phone: return "";
                default: return "";
            }
        }
    }
}
