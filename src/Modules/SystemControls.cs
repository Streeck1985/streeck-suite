using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using StreeckSuite.Core;
using Windows.Devices.Radios;
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

        #region Brillo (DDC/CI)

        // Monitores de escritorio: el brillo se cambia por DDC/CI (dxva2). Es lento (~50 ms), así que va en un hilo y
        // solo se aplica el último valor pedido mientras se arrastra.
        private static IntPtr[] _monitors;
        private static int _brightness = -1;        // 0..100, -1 = el monitor no lo permite (o aún no se sabe)
        private static int _pendingBrightness = -1;
        private static bool _brightnessLoaded;
        private static readonly AutoResetEvent BrightnessRequested = new AutoResetEvent(false);
        private static Thread _brightnessThread;

        public static int Brightness
        {
            get { EnsureBrightness(); return _pendingBrightness >= 0 ? _pendingBrightness : _brightness; }
        }

        public static bool BrightnessSupported
        {
            get { EnsureBrightness(); return _brightness >= 0; }
        }

        public static bool BrightnessLoaded
        {
            get { return _brightnessLoaded; }
        }

        public static void SetBrightness(int percent)
        {
            if (!BrightnessSupported) return;
            _pendingBrightness = Math.Max(0, Math.Min(100, percent));
            BrightnessRequested.Set();
        }

        private static void EnsureBrightness()
        {
            lock (Sync)
            {
                if (_brightnessThread != null) return;
                _brightnessThread = new Thread(BrightnessLoop) { IsBackground = true, Name = "Brillo DDC/CI" };
                _brightnessThread.Start();
            }
        }

        private static void BrightnessLoop()
        {
            try
            {
                List<IntPtr> handles = new List<IntPtr>();
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, dc, rect, data) =>
                {
                    uint count;
                    if (GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out count) && count > 0)
                    {
                        PHYSICAL_MONITOR[] physical = new PHYSICAL_MONITOR[count];
                        if (GetPhysicalMonitorsFromHMONITOR(monitor, count, physical)) foreach (PHYSICAL_MONITOR m in physical) handles.Add(m.Handle);
                    }
                    return true;
                }, IntPtr.Zero);
                _monitors = handles.ToArray();
                foreach (IntPtr handle in _monitors)
                {
                    uint min, current, max;
                    if (GetMonitorBrightness(handle, out min, out current, out max) && max > min)
                    {
                        _brightness = (int)Math.Round(100.0 * (current - min) / (max - min));
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SystemControls] No se pudo leer el brillo: {0}", ex.Message);
            }
            _brightnessLoaded = true;
            if (_brightness < 0) return;

            for (;;)
            {
                BrightnessRequested.WaitOne();
                int wanted = _pendingBrightness;
                if (wanted < 0) continue;
                foreach (IntPtr handle in _monitors)
                {
                    uint min, current, max;
                    if (GetMonitorBrightness(handle, out min, out current, out max) && max > min)
                    {
                        SetMonitorBrightness(handle, (uint)(min + (max - min) * wanted / 100.0));
                    }
                }
                _brightness = wanted;
                if (_pendingBrightness == wanted) _pendingBrightness = -1;
            }
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct PHYSICAL_MONITOR
        {
            public IntPtr Handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        }

        private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr dc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("dxva2.dll")]
        private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);

        [DllImport("dxva2.dll")]
        private static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PHYSICAL_MONITOR[] monitors);

        [DllImport("dxva2.dll")]
        private static extern bool GetMonitorBrightness(IntPtr monitor, out uint minimum, out uint current, out uint maximum);

        [DllImport("dxva2.dll")]
        private static extern bool SetMonitorBrightness(IntPtr monitor, uint value);

        #endregion
    }
}
