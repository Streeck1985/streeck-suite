using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Volumen y dispositivos de salida a través de Core Audio. Debe crearse y usarse desde el hilo de la interfaz;
    /// los avisos de Windows (cambio de volumen o de dispositivo) llegan en otro hilo y se reenvían como eventos.
    /// </summary>
    internal class AudioService : IDisposable
    {
        public class Device
        {
            public string Id;
            public string Name;
            public bool IsDefault;
        }

        public class AppVolume
        {
            public string Name;
            public string ExePath;   // Para mostrar el icono real de la app (null si no se puede leer)
            public float Level;
            public bool Muted;
            public bool Active;
            internal CoreAudio.ISimpleAudioVolume Control;
        }

        // Identifica los cambios hechos desde la suite, para no mostrar el aviso de volumen por ellos
        private static readonly Guid SuiteContext = new Guid("6f1d3c1e-5a52-4f0b-9a3b-53747265656b");

        private readonly CoreAudio.IMMDeviceEnumerator _enumerator;
        private readonly VolumeCallback _volumeCallback;
        private readonly DeviceCallback _deviceCallback;
        private CoreAudio.IAudioEndpointVolume _endpoint;

        /// <summary>
        /// Volumen general cambiado fuera de la suite (teclas, mezclador...): nivel 0..1 y silencio. Hilo de audio.
        /// </summary>
        public event Action<float, bool> VolumeChanged;

        /// <summary>
        /// Cambió el dispositivo de salida predeterminado (p. ej. se conectaron auriculares). Hilo de audio.
        /// </summary>
        public event Action DefaultDeviceChanged;

        public AudioService()
        {
            _enumerator = (CoreAudio.IMMDeviceEnumerator)new CoreAudio.MMDeviceEnumeratorComObject();
            _volumeCallback = new VolumeCallback(this);
            _deviceCallback = new DeviceCallback(this);
            _enumerator.RegisterEndpointNotificationCallback(_deviceCallback);
            AttachDefaultDevice();
        }

        /// <summary>
        /// Vuelve a engancharse al dispositivo predeterminado (llamar tras DefaultDeviceChanged, en el hilo de la interfaz).
        /// </summary>
        public void AttachDefaultDevice()
        {
            if (_endpoint != null)
            {
                _endpoint.UnregisterControlChangeNotify(_volumeCallback);
                Marshal.ReleaseComObject(_endpoint);
                _endpoint = null;
            }

            CoreAudio.IMMDevice device;
            if (_enumerator.GetDefaultAudioEndpoint(CoreAudio.EDataFlow.Render, CoreAudio.ERole.Multimedia, out device) != 0 || device == null) return;

            object instance;
            Guid iid = CoreAudio.IID_IAudioEndpointVolume;
            if (device.Activate(ref iid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out instance) == 0)
            {
                _endpoint = (CoreAudio.IAudioEndpointVolume)instance;
                _endpoint.RegisterControlChangeNotify(_volumeCallback);
            }
            Marshal.ReleaseComObject(device);
        }

        #region Volumen general

        public float Level
        {
            get
            {
                float level = 0;
                if (_endpoint != null) _endpoint.GetMasterVolumeLevelScalar(out level);
                return level;
            }
        }

        public bool Muted
        {
            get
            {
                bool muted = false;
                if (_endpoint != null) _endpoint.GetMute(out muted);
                return muted;
            }
        }

        public void SetLevel(float level)
        {
            if (_endpoint == null) return;
            Guid context = SuiteContext;
            _endpoint.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, level)), ref context);
            if (level > 0 && Muted) _endpoint.SetMute(false, ref context);
        }

        public void ToggleMute()
        {
            if (_endpoint == null) return;
            Guid context = SuiteContext;
            _endpoint.SetMute(!Muted, ref context);
        }

        #endregion

        #region Micrófono (Centro de control)

        private CoreAudio.IAudioEndpointVolume _mic;
        private DateTime _micAttachedAt = DateTime.MinValue;

        // Micrófono predeterminado; se vuelve a buscar cada 5 s por si cambió (p. ej. se conectaron audífonos)
        private CoreAudio.IAudioEndpointVolume Mic
        {
            get
            {
                if (_mic != null && (DateTime.Now - _micAttachedAt).TotalSeconds < 5) return _mic;
                if (_mic != null) { Marshal.ReleaseComObject(_mic); _mic = null; }
                _micAttachedAt = DateTime.Now;
                CoreAudio.IMMDevice device;
                if (_enumerator.GetDefaultAudioEndpoint(CoreAudio.EDataFlow.Capture, CoreAudio.ERole.Communications, out device) != 0 || device == null) return null;
                object instance;
                Guid iid = CoreAudio.IID_IAudioEndpointVolume;
                if (device.Activate(ref iid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out instance) == 0) _mic = (CoreAudio.IAudioEndpointVolume)instance;
                Marshal.ReleaseComObject(device);
                return _mic;
            }
        }

        public bool HasMic
        {
            get { return Mic != null; }
        }

        public float MicLevel
        {
            get
            {
                float level = 0;
                CoreAudio.IAudioEndpointVolume mic = Mic;
                if (mic != null) mic.GetMasterVolumeLevelScalar(out level);
                return level;
            }
        }

        public bool MicMuted
        {
            get
            {
                bool muted = false;
                CoreAudio.IAudioEndpointVolume mic = Mic;
                if (mic != null) mic.GetMute(out muted);
                return muted;
            }
        }

        public void SetMicLevel(float level)
        {
            CoreAudio.IAudioEndpointVolume mic = Mic;
            if (mic == null) return;
            Guid context = SuiteContext;
            mic.SetMasterVolumeLevelScalar(Math.Max(0f, Math.Min(1f, level)), ref context);
            if (level > 0 && MicMuted) mic.SetMute(false, ref context);
        }

        public void ToggleMicMute()
        {
            CoreAudio.IAudioEndpointVolume mic = Mic;
            if (mic == null) return;
            Guid context = SuiteContext;
            mic.SetMute(!MicMuted, ref context);
        }

        #endregion

        #region Dispositivos

        public string DefaultDeviceName
        {
            get
            {
                foreach (Device device in GetOutputDevices())
                {
                    if (device.IsDefault) return device.Name;
                }
                return "Sin salida de audio";
            }
        }

        public List<Device> GetOutputDevices()
        {
            List<Device> devices = new List<Device>();
            string defaultId = null;
            CoreAudio.IMMDevice defaultDevice;
            if (_enumerator.GetDefaultAudioEndpoint(CoreAudio.EDataFlow.Render, CoreAudio.ERole.Multimedia, out defaultDevice) == 0 && defaultDevice != null)
            {
                defaultDevice.GetId(out defaultId);
                Marshal.ReleaseComObject(defaultDevice);
            }

            CoreAudio.IMMDeviceCollection collection;
            if (_enumerator.EnumAudioEndpoints(CoreAudio.EDataFlow.Render, CoreAudio.DEVICE_STATE_ACTIVE, out collection) != 0) return devices;
            uint count;
            collection.GetCount(out count);
            for (uint i = 0; i < count; i++)
            {
                CoreAudio.IMMDevice device;
                if (collection.Item(i, out device) != 0) continue;
                Device info = new Device();
                device.GetId(out info.Id);
                info.Name = ReadFriendlyName(device) ?? "Dispositivo de audio";
                info.IsDefault = info.Id == defaultId;
                devices.Add(info);
                Marshal.ReleaseComObject(device);
            }
            Marshal.ReleaseComObject(collection);
            return devices;
        }

        /// <summary>
        /// Pone como predeterminado el dispositivo indicado (para música, sistema y comunicaciones).
        /// </summary>
        public void SetDefaultDevice(string deviceId)
        {
            CoreAudio.IPolicyConfig policy = (CoreAudio.IPolicyConfig)new CoreAudio.PolicyConfigClient();
            try
            {
                policy.SetDefaultEndpoint(deviceId, CoreAudio.ERole.Console);
                policy.SetDefaultEndpoint(deviceId, CoreAudio.ERole.Multimedia);
                policy.SetDefaultEndpoint(deviceId, CoreAudio.ERole.Communications);
            }
            finally
            {
                Marshal.ReleaseComObject(policy);
            }
        }

        private static string ReadFriendlyName(CoreAudio.IMMDevice device)
        {
            CoreAudio.IPropertyStore store;
            if (device.OpenPropertyStore(CoreAudio.STGM_READ, out store) != 0) return null;
            try
            {
                CoreAudio.PROPERTYKEY key = CoreAudio.PKEY_Device_FriendlyName;
                CoreAudio.PROPVARIANT value;
                if (store.GetValue(ref key, out value) != 0) return null;
                string name = value.AsString();
                CoreAudio.PropVariantClear(ref value);
                return name;
            }
            finally
            {
                Marshal.ReleaseComObject(store);
            }
        }

        #endregion

        #region Volumen por aplicación

        /// <summary>
        /// Apps con audio en el dispositivo predeterminado (primero las que están sonando), sin los sonidos del sistema.
        /// </summary>
        public List<AppVolume> GetAppVolumes()
        {
            List<AppVolume> apps = new List<AppVolume>();
            CoreAudio.IMMDevice device;
            if (_enumerator.GetDefaultAudioEndpoint(CoreAudio.EDataFlow.Render, CoreAudio.ERole.Multimedia, out device) != 0 || device == null) return apps;

            try
            {
                object instance;
                Guid iid = CoreAudio.IID_IAudioSessionManager2;
                if (device.Activate(ref iid, CoreAudio.CLSCTX_ALL, IntPtr.Zero, out instance) != 0) return apps;
                CoreAudio.IAudioSessionManager2 manager = (CoreAudio.IAudioSessionManager2)instance;

                CoreAudio.IAudioSessionEnumerator sessions;
                if (manager.GetSessionEnumerator(out sessions) != 0) return apps;
                int count;
                sessions.GetCount(out count);

                Dictionary<string, AppVolume> byName = new Dictionary<string, AppVolume>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < count; i++)
                {
                    CoreAudio.IAudioSessionControl2 session;
                    if (sessions.GetSession(i, out session) != 0 || session == null) continue;

                    int state;
                    session.GetState(out state);
                    uint processId;
                    session.GetProcessId(out processId);
                    // IsSystemSoundsSession devuelve S_OK (0) para los sonidos del sistema; 2 = sesión caducada.
                    // Una sola entrada por app: los navegadores abren varias sesiones
                    string exePath = null;
                    string name = session.IsSystemSoundsSession() == 0 || state == 2 ? null : AppNameFor(processId, out exePath);
                    if (name == null || byName.ContainsKey(name))
                    {
                        Marshal.ReleaseComObject(session);
                        continue;
                    }

                    CoreAudio.ISimpleAudioVolume volume = (CoreAudio.ISimpleAudioVolume)session;
                    AppVolume app = new AppVolume();
                    app.Name = name;
                    app.ExePath = exePath;
                    app.Active = state == 1;
                    app.Control = volume;
                    volume.GetMasterVolume(out app.Level);
                    volume.GetMute(out app.Muted);
                    byName[name] = app;
                    apps.Add(app);
                }
                Marshal.ReleaseComObject(sessions);
                Marshal.ReleaseComObject(manager);
            }
            finally
            {
                Marshal.ReleaseComObject(device);
            }

            apps.Sort((a, b) => b.Active.CompareTo(a.Active));
            return apps;
        }

        public void SetAppLevel(AppVolume app, float level)
        {
            Guid context = SuiteContext;
            app.Control.SetMasterVolume(Math.Max(0f, Math.Min(1f, level)), ref context);
            if (app.Muted && level > 0) app.Control.SetMute(false, ref context);
        }

        public void ToggleAppMute(AppVolume app)
        {
            Guid context = SuiteContext;
            app.Control.SetMute(!app.Muted, ref context);
        }

        private static string AppNameFor(uint processId, out string exePath)
        {
            exePath = null;
            if (processId == 0) return null;
            try
            {
                using (Process process = Process.GetProcessById((int)processId))
                {
                    string description = null;
                    try
                    {
                        exePath = process.MainModule.FileName;
                        description = process.MainModule.FileVersionInfo.FileDescription;
                    }
                    catch (Exception)
                    {
                        // Procesos protegidos o de 32 bits: basta con el nombre del proceso
                    }
                    string name = string.IsNullOrWhiteSpace(description) ? process.ProcessName : description;
                    return name.Length > 0 ? char.ToUpper(name[0]) + name.Substring(1) : name;
                }
            }
            catch (ArgumentException)
            {
                return null; // El proceso ya terminó
            }
        }

        #endregion

        public void Dispose()
        {
            if (_endpoint != null)
            {
                _endpoint.UnregisterControlChangeNotify(_volumeCallback);
                Marshal.ReleaseComObject(_endpoint);
                _endpoint = null;
            }
            _enumerator.UnregisterEndpointNotificationCallback(_deviceCallback);
            Marshal.ReleaseComObject(_enumerator);
        }

        #region Avisos de Windows

        [ComVisible(true)]
        private class VolumeCallback : CoreAudio.IAudioEndpointVolumeCallback
        {
            private readonly AudioService _owner;
            public VolumeCallback(AudioService owner) { _owner = owner; }

            public int OnNotify(IntPtr notifyData)
            {
                CoreAudio.AUDIO_VOLUME_NOTIFICATION_DATA data = (CoreAudio.AUDIO_VOLUME_NOTIFICATION_DATA)
                    Marshal.PtrToStructure(notifyData, typeof(CoreAudio.AUDIO_VOLUME_NOTIFICATION_DATA));
                Action<float, bool> handler = _owner.VolumeChanged;
                if (data.guidEventContext != SuiteContext && handler != null) handler(data.fMasterVolume, data.bMuted);
                return 0;
            }
        }

        [ComVisible(true)]
        private class DeviceCallback : CoreAudio.IMMNotificationClient
        {
            private readonly AudioService _owner;
            public DeviceCallback(AudioService owner) { _owner = owner; }

            public void OnDefaultDeviceChanged(CoreAudio.EDataFlow flow, CoreAudio.ERole role, string defaultDeviceId)
            {
                // Windows avisa una vez por cada rol; basta con el de multimedia
                if (flow != CoreAudio.EDataFlow.Render || role != CoreAudio.ERole.Multimedia) return;
                Action handler = _owner.DefaultDeviceChanged;
                if (handler != null) handler();
            }

            public void OnDeviceStateChanged(string deviceId, int newState) { }
            public void OnDeviceAdded(string deviceId) { }
            public void OnDeviceRemoved(string deviceId) { }
            public void OnPropertyValueChanged(string deviceId, CoreAudio.PROPERTYKEY key) { }
        }

        #endregion
    }
}
