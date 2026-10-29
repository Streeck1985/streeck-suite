using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Qué apps están usando el micrófono o la cámara ahora mismo. Windows lo anota en el registro
    /// (CapabilityAccessManager\ConsentStore): mientras una app los usa, su LastUsedTimeStop vale 0.
    /// Se consulta cada 2 s en segundo plano; leer unas pocas claves del registro apenas cuesta nada.
    /// </summary>
    internal static class CaptureMonitor
    {
        public enum Device { Microphone, Camera }

        private const int PollMs = 2000;
        private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, string> FriendlyNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Timer _timer;
        private static List<string> _microphone = new List<string>();
        private static List<string> _camera = new List<string>();

        /// <summary>
        /// Una app empezó a usar el micrófono o la cámara (en un hilo de segundo plano).
        /// </summary>
        public static event Action<Device, string> Started;

        /// <summary>
        /// Cambió la lista de apps que los usan (en un hilo de segundo plano).
        /// </summary>
        public static event Action Changed;

        public static List<string> MicrophoneApps
        {
            get { lock (Gate) return new List<string>(_microphone); }
        }

        public static List<string> CameraApps
        {
            get { lock (Gate) return new List<string>(_camera); }
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
                // Micrófono: sesiones de grabación activas en el audio de Windows (fiable en todas las versiones);
                // el registro se mantiene por si alguna app solo aparece ahí. En Windows 11 recientes el registro
                // dejó de actualizarse, así que para la cámara puede no haber datos.
                List<string> microphone = RecordingApps();
                foreach (string app in InUse("microphone")) AddUnique(microphone, app);
                List<string> camera = InUse("webcam");

                List<string> oldMicrophone, oldCamera;
                lock (Gate)
                {
                    oldMicrophone = _microphone;
                    oldCamera = _camera;
                    _microphone = microphone;
                    _camera = camera;
                }

                foreach (string app in microphone) if (!oldMicrophone.Contains(app)) Log.Info("[CaptureMonitor] Micrófono en uso: {0}", app);
                foreach (string app in camera) if (!oldCamera.Contains(app)) Log.Info("[CaptureMonitor] Cámara en uso: {0}", app);

                Action<Device, string> started = Started;
                if (started != null)
                {
                    foreach (string app in microphone) if (!oldMicrophone.Contains(app)) started(Device.Microphone, app);
                    foreach (string app in camera) if (!oldCamera.Contains(app)) started(Device.Camera, app);
                }

                bool changed = !SameList(microphone, oldMicrophone) || !SameList(camera, oldCamera);
                Action handler = Changed;
                if (changed && handler != null) handler();
            }
            catch (Exception ex)
            {
                Log.Error("[CaptureMonitor] No se pudo leer el uso del micrófono o la cámara: {0}", ex.Message);
            }
        }

        // Apps de la tienda: una subclave por paquete. Apps de escritorio: dentro de NonPackaged, una por ruta del .exe
        private static List<string> InUse(string capability)
        {
            List<string> apps = new List<string>();
            using (RegistryKey store = Registry.CurrentUser.OpenSubKey(ConsentStore + capability))
            {
                if (store == null) return apps;
                foreach (string name in store.GetSubKeyNames())
                {
                    using (RegistryKey key = store.OpenSubKey(name))
                    {
                        if (key == null) continue;
                        if (name == "NonPackaged")
                        {
                            foreach (string exe in key.GetSubKeyNames())
                            {
                                using (RegistryKey app = key.OpenSubKey(exe))
                                {
                                    if (IsActive(app)) AddUnique(apps, DesktopAppName(exe));
                                }
                            }
                        }
                        else if (IsActive(key))
                        {
                            AddUnique(apps, PackagedAppName(name));
                        }
                    }
                }
            }
            return apps;
        }

        private const int AudioSessionStateActive = 1;

        /// <summary>
        /// Apps con una sesión de grabación activa en cualquier micrófono conectado.
        /// </summary>
        private static List<string> RecordingApps()
        {
            List<string> apps = new List<string>();
            CoreAudio.IMMDeviceEnumerator enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumeratorComObject();
            CoreAudio.IMMDeviceCollection devices = null;
            try
            {
                if (enumerator.EnumAudioEndpoints(CoreAudio.EDataFlow.Capture, CoreAudio.DEVICE_STATE_ACTIVE, out devices) != 0 || devices == null) return apps;
                uint count;
                devices.GetCount(out count);
                for (uint i = 0; i < count; i++)
                {
                    CoreAudio.IMMDevice device;
                    if (devices.Item(i, out device) != 0 || device == null) continue;
                    try
                    {
                        Guid iid = CoreAudio.IID_IAudioSessionManager2;
                        object instance;
                        if (device.Activate(ref iid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out instance) != 0) continue;
                        CoreAudio.IAudioSessionManager2 manager = (CoreAudio.IAudioSessionManager2)instance;
                        CoreAudio.IAudioSessionEnumerator sessions;
                        if (manager.GetSessionEnumerator(out sessions) == 0 && sessions != null)
                        {
                            int sessionCount;
                            sessions.GetCount(out sessionCount);
                            for (int s = 0; s < sessionCount; s++)
                            {
                                CoreAudio.IAudioSessionControl2 session;
                                if (sessions.GetSession(s, out session) != 0 || session == null) continue;
                                int state;
                                uint processId;
                                if (session.GetState(out state) == 0 && state == AudioSessionStateActive
                                    && session.IsSystemSoundsSession() != 0 && session.GetProcessId(out processId) == 0 && processId != 0)
                                {
                                    AddUnique(apps, ProcessName(processId));
                                }
                                Marshal.ReleaseComObject(session);
                            }
                            Marshal.ReleaseComObject(sessions);
                        }
                        Marshal.ReleaseComObject(manager);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(device);
                    }
                }
            }
            finally
            {
                if (devices != null) Marshal.ReleaseComObject(devices);
                Marshal.ReleaseComObject(enumerator);
            }
            return apps;
        }

        // Descripción del ejecutable ("Grabadora de sonido", "Discord"); si no se puede leer, el nombre del proceso
        private static string ProcessName(uint processId)
        {
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    try
                    {
                        string description = process.MainModule.FileVersionInfo.FileDescription;
                        if (!string.IsNullOrEmpty(description)) return description.Trim();
                    }
                    catch (Exception)
                    {
                        // Proceso con más privilegios: no se puede leer su módulo
                    }
                    return process.ProcessName;
                }
            }
            catch (ArgumentException)
            {
                return "Una app"; // Ya terminó
            }
        }

        private static bool IsActive(RegistryKey key)
        {
            if (key == null) return false;
            object start = key.GetValue("LastUsedTimeStart");
            object stop = key.GetValue("LastUsedTimeStop");
            return start is long && (long)start != 0 && stop is long && (long)stop == 0;
        }

        // "C:#Program Files#Discord#app#Discord.exe" -> descripción del ejecutable ("Discord")
        private static string DesktopAppName(string encodedPath)
        {
            lock (Gate)
            {
                string cached;
                if (FriendlyNames.TryGetValue(encodedPath, out cached)) return cached;
            }

            string path = encodedPath.Replace('#', '\\');
            string name = Path.GetFileNameWithoutExtension(path);
            try
            {
                if (File.Exists(path))
                {
                    string description = FileVersionInfo.GetVersionInfo(path).FileDescription;
                    if (!string.IsNullOrEmpty(description)) name = description.Trim();
                }
            }
            catch (Exception)
            {
                // Sin permiso para leer el .exe: se queda el nombre del archivo
            }
            lock (Gate) FriendlyNames[encodedPath] = name;
            return name;
        }

        // "Microsoft.WindowsCamera_8wekyb3d8bbwe" -> "WindowsCamera"
        private static string PackagedAppName(string packageFamily)
        {
            string name = packageFamily;
            int underscore = name.IndexOf('_');
            if (underscore > 0) name = name.Substring(0, underscore);
            int dot = name.LastIndexOf('.');
            if (dot >= 0 && dot < name.Length - 1) name = name.Substring(dot + 1);
            return name;
        }

        private static void AddUnique(List<string> list, string item)
        {
            if (!list.Contains(item)) list.Add(item);
        }

        private static bool SameList(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            foreach (string item in a) if (!b.Contains(item)) return false;
            return true;
        }
    }
}
