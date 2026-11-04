using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using StreeckSuite.Core;
using Windows.Devices.Radios;
using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Controles del Centro de control (fase J3, como Bloom): radios de Wi-Fi y Bluetooth (Windows.Devices.Radios),
    /// nombre de la red y brillo del monitor por DDC/CI. Todo lo lento corre en segundo plano y se cachea; la isla solo
    /// lee el último estado conocido.
    /// </summary>
    internal static class SystemControls
    {
        private static readonly object Sync = new object();
        private static Radio _wifi, _bluetooth;
        private static bool _radiosLoaded, _radiosLoading;
        private static string _networkName;
        private static DateTime _networkReadAt = DateTime.MinValue;

        #region Radios

        public static bool? WifiOn
        {
            get { EnsureRadios(); lock (Sync) return _wifi == null ? (bool?)null : _wifi.State == RadioState.On; }
        }

        public static bool? BluetoothOn
        {
            get { EnsureRadios(); lock (Sync) return _bluetooth == null ? (bool?)null : _bluetooth.State == RadioState.On; }
        }

        private static void EnsureRadios()
        {
            lock (Sync)
            {
                if (_radiosLoaded || _radiosLoading) return;
                _radiosLoading = true;
            }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    WinRtAsync.Wait(Radio.RequestAccessAsync());
                    foreach (Radio radio in WinRtAsync.Wait(Radio.GetRadiosAsync()))
                    {
                        lock (Sync)
                        {
                            if (radio.Kind == RadioKind.WiFi && _wifi == null) _wifi = radio;
                            else if (radio.Kind == RadioKind.Bluetooth && _bluetooth == null) _bluetooth = radio;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[SystemControls] No se pudieron leer las radios: {0}", ex.Message);
                }
                lock (Sync) { _radiosLoaded = true; _radiosLoading = false; }
            });
        }

        public static void ToggleWifi() { Toggle(_wifi, "Wi-Fi"); }
        public static void ToggleBluetooth() { Toggle(_bluetooth, "Bluetooth"); }

        private static void Toggle(Radio radio, string name)
        {
            if (radio == null) return;
            RadioState next = radio.State == RadioState.On ? RadioState.Off : RadioState.On;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    RadioAccessStatus status = WinRtAsync.Wait(radio.SetStateAsync(next));
                    if (status != RadioAccessStatus.Allowed) Log.Error("[SystemControls] Windows no dejó cambiar {0}: {1}", name, status);
                }
                catch (Exception ex)
                {
                    Log.Error("[SystemControls] No se pudo cambiar {0}: {1}", name, ex.Message);
                }
            });
        }

        /// <summary>
        /// Nombre de la red Wi-Fi conectada (o null). Se consulta como mucho cada 5 s.
        /// </summary>
        public static string NetworkName
        {
            get
            {
                if ((DateTime.Now - _networkReadAt).TotalSeconds < 5) return _networkName;
                _networkReadAt = DateTime.Now;
                try
                {
                    ConnectionProfile profile = NetworkInformation.GetInternetConnectionProfile();
                    _networkName = profile == null ? null : profile.ProfileName;
                }
                catch (Exception)
                {
                    _networkName = null;
                }
                return _networkName;
            }
        }

        #endregion

        #region Redes Wi-Fi

        internal class WifiNetwork
        {
            public string Name;
            public int Bars;          // 0..4
            public bool Secured;
            public bool Connected;
            public WiFiAvailableNetwork Network;
        }

        private static WiFiAdapter _adapter;
        private static List<WifiNetwork> _networks = new List<WifiNetwork>();
        private static bool _scanning;
        private static string _wifiProblem;   // Por qué no hay lista (sin adaptador, ubicación apagada...)

        public static List<WifiNetwork> Networks
        {
            get { lock (Sync) return _networks; }
        }

        public static bool Scanning
        {
            get { return _scanning; }
        }

        public static string WifiProblem
        {
            get { return _wifiProblem; }
        }

        /// <summary>
        /// ¿Se navega por cable? (para no mostrar el nombre de la conexión por cable como si fuera Wi-Fi)
        /// </summary>
        public static bool OnEthernet
        {
            get
            {
                try
                {
                    ConnectionProfile profile = NetworkInformation.GetInternetConnectionProfile();
                    return profile != null && !profile.IsWlanConnectionProfile;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Busca redes en segundo plano (tarda 2-4 s). Windows 11 pide la ubicación activada para dar la lista.
        /// </summary>
        public static void ScanWifi()
        {
            if (_scanning) return;
            _scanning = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (_adapter == null)
                    {
                        if (WinRtAsync.Wait(WiFiAdapter.RequestAccessAsync()) != WiFiAccessStatus.Allowed)
                        {
                            _wifiProblem = "Activa la ubicación en Configuración → Privacidad para ver las redes";
                            return;
                        }
                        IReadOnlyList<WiFiAdapter> adapters = WinRtAsync.Wait(WiFiAdapter.FindAllAdaptersAsync());
                        if (adapters.Count == 0) { _wifiProblem = "Este equipo no tiene Wi-Fi"; return; }
                        _adapter = adapters[0];
                    }
                    WinRtAsync.Wait(_adapter.ScanAsync(), TimeSpan.FromSeconds(10));
                    string connected = null;
                    ConnectionProfile current = WinRtAsync.Wait(_adapter.NetworkAdapter.GetConnectedProfileAsync());
                    if (current != null && current.IsWlanConnectionProfile) connected = current.WlanConnectionProfileDetails.GetConnectedSsid();

                    Dictionary<string, WifiNetwork> best = new Dictionary<string, WifiNetwork>();
                    foreach (WiFiAvailableNetwork network in _adapter.NetworkReport.AvailableNetworks)
                    {
                        if (string.IsNullOrEmpty(network.Ssid)) continue; // Redes ocultas
                        WifiNetwork existing;
                        if (best.TryGetValue(network.Ssid, out existing) && existing.Bars >= network.SignalBars) continue;
                        best[network.Ssid] = new WifiNetwork
                        {
                            Name = network.Ssid,
                            Bars = network.SignalBars,
                            Secured = network.SecuritySettings.NetworkAuthenticationType != NetworkAuthenticationType.Open80211,
                            Connected = network.Ssid == connected,
                            Network = network
                        };
                    }
                    List<WifiNetwork> list = new List<WifiNetwork>(best.Values);
                    list.Sort((a, b) => a.Connected != b.Connected ? (a.Connected ? -1 : 1) : b.Bars.CompareTo(a.Bars));
                    lock (Sync) _networks = list;
                    _wifiProblem = list.Count == 0 ? "No se encontraron redes" : null;
                }
                catch (Exception ex)
                {
                    _wifiProblem = "No se pudieron buscar redes";
                    Log.Error("[SystemControls] Búsqueda de redes: {0}", ex.Message);
                }
                finally
                {
                    _scanning = false;
                }
            });
        }

        /// <summary>
        /// Conecta a una red. Las guardadas y las abiertas entran directo; si pide contraseña nueva se abre la lista
        /// de redes de Windows (la isla no puede recibir el teclado sin robarle el foco a tu ventana).
        /// </summary>
        public static void ConnectWifi(WifiNetwork network)
        {
            if (_adapter == null || network == null || network.Connected) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    WiFiConnectionResult result = WinRtAsync.Wait(_adapter.ConnectAsync(network.Network, WiFiReconnectionKind.Automatic), TimeSpan.FromSeconds(20));
                    if (result.ConnectionStatus == WiFiConnectionStatus.Success) { ScanWifi(); return; }
                    if (network.Secured) System.Diagnostics.Process.Start("ms-availablenetworks:");
                    Log.Info("[SystemControls] No se conectó a {0}: {1}", network.Name, result.ConnectionStatus);
                }
                catch (Exception ex)
                {
                    Log.Error("[SystemControls] Conectar a {0}: {1}", network.Name, ex.Message);
                }
            });
        }

        #endregion

        #region Dispositivos Bluetooth

        internal class PairedDevice
        {
            public string Name;
            public bool Connected;
            public bool IsAudio;      // Audífonos y bocinas: se conectan y desconectan desde la isla
            public int Battery = -1;
            public BLUETOOTH_DEVICE_INFO Info;
        }

        private static readonly Guid AudioSink = new Guid("0000110B-0000-1000-8000-00805F9B34FB");
        private static readonly Guid Handsfree = new Guid("0000111E-0000-1000-8000-00805F9B34FB");

        /// <summary>
        /// Dispositivos Bluetooth emparejados (los clásicos; los de bajo consumo como algunos mouse se conectan solos).
        /// </summary>
        public static List<PairedDevice> PairedDevices()
        {
            List<PairedDevice> devices = new List<PairedDevice>();
            BLUETOOTH_DEVICE_SEARCH_PARAMS search = new BLUETOOTH_DEVICE_SEARCH_PARAMS();
            search.dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_SEARCH_PARAMS));
            search.fReturnAuthenticated = true;
            search.fReturnRemembered = true;
            search.fReturnConnected = true;
            BLUETOOTH_DEVICE_INFO info = new BLUETOOTH_DEVICE_INFO();
            info.dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO));
            IntPtr find = BluetoothFindFirstDevice(ref search, ref info);
            if (find == IntPtr.Zero) return devices;
            try
            {
                List<BluetoothDevice> known = BluetoothService.ConnectedDevices;
                do
                {
                    // Clase de dispositivo: el grupo mayor 0x04 es audio/vídeo
                    bool audio = ((info.ulClassofDevice >> 8) & 0x1F) == 0x04;
                    PairedDevice device = new PairedDevice { Name = info.szName, Connected = info.fConnected, IsAudio = audio, Info = info };
                    foreach (BluetoothDevice k in known)
                    {
                        if (!string.IsNullOrEmpty(k.Name) && info.szName.StartsWith(k.Name, StringComparison.OrdinalIgnoreCase)) device.Battery = k.Battery;
                    }
                    devices.Add(device);
                    info = new BLUETOOTH_DEVICE_INFO();
                    info.dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO));
                } while (BluetoothFindNextDevice(find, ref info));
            }
            finally
            {
                BluetoothFindDeviceClose(find);
            }
            devices.Sort((a, b) => a.Connected != b.Connected ? (a.Connected ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return devices;
        }

        /// <summary>
        /// Conecta o desconecta unos audífonos o una bocina apagando y encendiendo su servicio de audio (lo mismo que
        /// hacen otras apps; Windows no tiene una función oficial para esto).
        /// </summary>
        public static void ToggleAudioDevice(PairedDevice device)
        {
            if (device == null || !device.IsAudio) return;
            BLUETOOTH_DEVICE_INFO info = device.Info;
            bool connect = !device.Connected;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                foreach (Guid service in new[] { AudioSink, Handsfree })
                {
                    Guid id = service;
                    uint result = BluetoothSetServiceState(IntPtr.Zero, ref info, ref id, connect ? 1u : 0u);
                    if (result != 0 && result != 87) Log.Info("[SystemControls] Servicio {0} de {1}: código {2}", service, info.szName, result);
                }
            });
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SYSTEMTIME_BT
        {
            public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct BLUETOOTH_DEVICE_INFO
        {
            public int dwSize;
            public ulong Address;
            public uint ulClassofDevice;
            [MarshalAs(UnmanagedType.Bool)] public bool fConnected;
            [MarshalAs(UnmanagedType.Bool)] public bool fRemembered;
            [MarshalAs(UnmanagedType.Bool)] public bool fAuthenticated;
            private SYSTEMTIME_BT stLastSeen;
            private SYSTEMTIME_BT stLastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)] public string szName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public int dwSize;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnAuthenticated;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnRemembered;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnUnknown;
            [MarshalAs(UnmanagedType.Bool)] public bool fReturnConnected;
            [MarshalAs(UnmanagedType.Bool)] public bool fIssueInquiry;
            public byte cTimeoutMultiplier;
            public IntPtr hRadio;
        }

        [DllImport("bthprops.cpl", CharSet = CharSet.Unicode)]
        private static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS search, ref BLUETOOTH_DEVICE_INFO info);

        [DllImport("bthprops.cpl", CharSet = CharSet.Unicode)]
        private static extern bool BluetoothFindNextDevice(IntPtr find, ref BLUETOOTH_DEVICE_INFO info);

        [DllImport("bthprops.cpl")]
        private static extern bool BluetoothFindDeviceClose(IntPtr find);

        [DllImport("bthprops.cpl", CharSet = CharSet.Unicode)]
        private static extern uint BluetoothSetServiceState(IntPtr radio, ref BLUETOOTH_DEVICE_INFO info, ref Guid service, uint flags);
        #endregion
    }
}
