using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Controles rápidos nuevos (mockup aprobado): luz nocturna y modo de energía.
    /// La luz nocturna es propia (rampa de color de la pantalla, tonos cálidos), así no depende del ajuste de Windows;
    /// el modo de energía es el mismo de Configuración › Energía (Mejor eficiencia / Equilibrado / Mejor rendimiento).
    /// </summary>
    internal static class SystemTweaks
    {
        public enum PowerMode { Saver, Balanced, Performance }

        private static readonly Guid SaverGuid = new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a");
        private static readonly Guid PerformanceGuid = new Guid("ded574b5-45a0-4f42-8737-46345c09c238");
        private static bool _started;

        [DllImport("gdi32.dll")] private static extern bool SetDeviceGammaRamp(IntPtr hdc, ushort[] ramp);
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateDC(string driver, string device, string output, IntPtr devMode);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("powrprof.dll")] private static extern uint PowerGetEffectiveOverlayScheme(out Guid scheme);
        [DllImport("powrprof.dll")] private static extern uint PowerSetActiveOverlayScheme(Guid scheme);

        /// <summary>
        /// Al arrancar: vuelve a poner la luz nocturna si estaba encendida, y la repone si cambian las pantallas.
        /// </summary>
        public static void Start()
        {
            if (_started) return;
            _started = true;
            if (SuiteSettings.NightLight) ApplyGamma(true);
            SystemEvents.DisplaySettingsChanged += (s, e) => { if (SuiteSettings.NightLight) ApplyGamma(true); };
            // Skins claros: Windows en modo claro (barra e inicio con letras oscuras); al volver, el modo que tenías
            SuiteSettings.Changed += SyncWindowsMode;
            SyncWindowsMode();
            SystemEvents.PowerModeChanged += (s, e) => { if (e.Mode == Microsoft.Win32.PowerModes.Resume && SuiteSettings.NightLight) ApplyGamma(true); };
        }

        public static bool NightLight
        {
            get { return SuiteSettings.NightLight; }
        }

        public static void ToggleNightLight()
        {
            SuiteSettings.NightLight = !SuiteSettings.NightLight;
            SuiteSettings.Save();
            ApplyGamma(SuiteSettings.NightLight);
        }

        public static void Stop()
        {
            if (SuiteSettings.NightLight) ApplyGamma(false); // Al cerrar la suite, la pantalla vuelve a la normalidad
        }

        // Rampa por canal: rojo igual, verde y azul más bajos (tono cálido). Windows rechaza rampas muy
        // alejadas de la normal, así que si no la acepta se prueba una más suave.
        private static void ApplyGamma(bool warm)
        {
            float[][] tries = warm ? new[] { new[] { 1f, 0.86f, 0.68f }, new[] { 1f, 0.9f, 0.78f } } : new[] { new[] { 1f, 1f, 1f } };
            foreach (Screen screen in Screen.AllScreens)
            {
                IntPtr dc = CreateDC(null, screen.DeviceName, null, IntPtr.Zero);
                if (dc == IntPtr.Zero) continue;
                try
                {
                    bool done = false;
                    foreach (float[] k in tries)
                    {
                        ushort[] ramp = new ushort[768];
                        for (int i = 0; i < 256; i++)
                        {
                            ramp[i] = (ushort)Math.Min(65535, i * 257 * k[0]);
                            ramp[256 + i] = (ushort)Math.Min(65535, i * 257 * k[1]);
                            ramp[512 + i] = (ushort)Math.Min(65535, i * 257 * k[2]);
                        }
                        if (SetDeviceGammaRamp(dc, ramp)) { done = true; break; }
                    }
                    if (!done) Log.Error("[SystemTweaks] La pantalla {0} no aceptó la luz nocturna", screen.DeviceName);
                }
                finally
                {
                    DeleteDC(dc);
                }
            }
        }

        public static PowerMode CurrentPowerMode
        {
            get
            {
                Guid scheme;
                if (PowerGetEffectiveOverlayScheme(out scheme) != 0) return PowerMode.Balanced;
                if (scheme == SaverGuid) return PowerMode.Saver;
                if (scheme == PerformanceGuid) return PowerMode.Performance;
                return PowerMode.Balanced;
            }
        }

        /// <summary>
        /// Ahorro → Equilibrado → Rendimiento → Ahorro.
        /// </summary>
        public static PowerMode CyclePowerMode()
        {
            PowerMode next = (PowerMode)(((int)CurrentPowerMode + 1) % 3);
            Guid scheme = next == PowerMode.Saver ? SaverGuid : next == PowerMode.Performance ? PerformanceGuid : Guid.Empty;
            uint result = PowerSetActiveOverlayScheme(scheme);
            if (result != 0) Log.Error("[SystemTweaks] Windows no cambió el modo de energía (código {0})", result);
            return CurrentPowerMode;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

        /// <summary>
        /// Modo claro de Windows para la barra y el inicio (iconos y hora oscuros), como en Configuración › Colores.
        /// </summary>
        public static void SetLightTaskbar(bool light)
        {
            Registry.SetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", light ? 1 : 0);
            IntPtr result;
            SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, "ImmersiveColorSet", 2, 2000, out result); // WM_SETTINGCHANGE a todas las ventanas
        }

        private static bool _syncing;

        private static void SyncWindowsMode()
        {
            if (_syncing) return;
            _syncing = true;
            try
            {
                bool lightSkin = IslandSkin.IsLight;
                object raw = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
                int current = raw is int ? (int)raw : 0;
                if (lightSkin && current == 0)
                {
                    SuiteSettings.WindowsModeBeforeLightSkin = current;
                    SuiteSettings.Save();
                    SetLightTaskbar(true);
                    Log.Info("[SystemTweaks] Skin claro: Windows en modo claro");
                }
                else if (!lightSkin && SuiteSettings.WindowsModeBeforeLightSkin >= 0)
                {
                    int previous = SuiteSettings.WindowsModeBeforeLightSkin;
                    SuiteSettings.WindowsModeBeforeLightSkin = -1;
                    SuiteSettings.Save();
                    if (current != previous) SetLightTaskbar(previous == 1);
                    Log.Info("[SystemTweaks] Skin oscuro: Windows vuelve a su modo de antes");
                }
            }
            catch (Exception ex)
            {
                Log.Error("[SystemTweaks] No se pudo cambiar el modo de Windows: {0}", ex.Message);
            }
            finally
            {
                _syncing = false;
            }
        }

        public static string PowerModeName(PowerMode mode)
        {
            return mode == PowerMode.Saver ? "Ahorro" : mode == PowerMode.Performance ? "Rendimiento" : "Equilibrado";
        }
    }
}
