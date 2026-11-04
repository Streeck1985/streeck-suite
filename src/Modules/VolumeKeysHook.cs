using System;
using System.Runtime.InteropServices;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Teclas de volumen atrapadas antes de que las vea Windows (como el Bloom del usuario): la suite cambia el volumen
    /// ella misma y muestra el aviso de la isla, así Windows nunca dibuja el suyo. Solo con «Esconder el aviso de
    /// volumen de Windows» activado. Las teclas de música (pausa, siguiente) no se tocan.
    /// </summary>
    internal class VolumeKeysHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104, WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105;
        private const int VkVolumeMute = 0xAD, VkVolumeDown = 0xAE, VkVolumeUp = 0xAF;
        private const uint LLKHF_INJECTED = 0x10;
        private const float Step = 0.02f; // Lo mismo que sube Windows por pulsación

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr extraInfo;
        }

        private readonly NativeMethods.LowLevelMouseProc _proc; // Misma firma; referencia viva para el GC
        private readonly AudioService _audio;
        private readonly Action<float, bool> _changed;
        private IntPtr _hook;

        public VolumeKeysHook(AudioService audio, Action<float, bool> changed)
        {
            _audio = audio;
            _changed = changed;
            _proc = OnKey;
        }

        public void Apply()
        {
            bool wanted = SuiteSettings.HideWindowsVolume && _audio != null;
            if (wanted && _hook == IntPtr.Zero)
            {
                _hook = NativeMethods.SetWindowsHookEx(WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
                if (_hook == IntPtr.Zero) Log.Error("[VolumeKeys] No se pudo instalar el gancho de las teclas de volumen");
            }
            else if (!wanted && _hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                KBDLLHOOKSTRUCT key = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(data, typeof(KBDLLHOOKSTRUCT));
                bool volumeKey = key.vkCode == VkVolumeUp || key.vkCode == VkVolumeDown || key.vkCode == VkVolumeMute;
                // También las simuladas: algunos teclados (software de Logitech, etc.) mandan así sus teclas de volumen
                if (volumeKey)
                {
                    int msg = message.ToInt32();
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                    {
                        try
                        {
                            if (key.vkCode == VkVolumeMute) _audio.ToggleMute();
                            else _audio.SetLevel(Math.Max(0f, Math.Min(1f, _audio.Level + (key.vkCode == VkVolumeUp ? Step : -Step))));
                            _changed(_audio.Level, _audio.Muted); // Aviso de la isla
                        }
                        catch (Exception ex)
                        {
                            Log.Error("[VolumeKeys] {0}", ex.Message);
                        }
                    }
                    if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN || msg == WM_KEYUP || msg == WM_SYSKEYUP) return new IntPtr(1); // Windows no la ve
                }
            }
            return NativeMethods.CallNextHookEx(_hook, code, message, data);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }
}
