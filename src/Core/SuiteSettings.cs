using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;

namespace StreeckSuite.Core
{
    /// <summary>
    /// Ajustes generales de la suite (página Privacidad, Isla y Widgets del panel de configuración), guardados en
    /// %LOCALAPPDATA%\StreeckSuite\config.ini. Los del reloj 3D siguen en reloj.ini y los widgets ocultos en su archivo.
    /// Se leen y cambian desde el hilo de la interfaz; Changed avisa a los módulos para aplicarlos al momento.
    /// </summary>
    public static class SuiteSettings
    {
        public enum MaximizedBehavior { Tuck, AlwaysShow, AlertsOnly }

        // Privacidad
        public static int PrivacyBlurPx = 25;           // 0..40
        public static int PrivacyDarkenPercent = 60;    // 0..100
        public static Color PrivacyTint = Color.FromArgb(5, 5, 5);

        // Isla
        public static bool IslandShowMusic = true;
        public static bool IslandShowSound = true;
        public static bool IslandShowWeather = true;
        public static bool IslandShowSystem = true;
        public static bool IslandShowTimer = true;
        public static bool IslandShowAgenda = true;
        public static MaximizedBehavior IslandMaximized = MaximizedBehavior.Tuck;
        public static bool IslandOpenOnHover = true;
        public static int IslandCloseDelayMs = 500;     // 0..3000
        public static int IslandSongPeekMs = 4000;      // 0..10000
        public static int IslandOpacityPercent = 100;   // 50..100
        public static int IslandScalePercent = 100;     // 70..120: tamaño de toda la isla (letras incluidas)

        // Avisos (las apps silenciadas se guardan aparte: NotificationApps)
        public static bool NotificationsEnabled = true;
        public static int NotificationDurationMs = 4000; // 1000..10000
        public static bool CaptureDots = true;           // Puntos de micrófono y cámara en la isla
        public static bool CaptureAlerts = true;         // Aviso cuando una app empieza a usarlos

        // Tiempo y agenda
        public static int PomodoroFocusMin = 25;         // 5..60
        public static int PomodoroShortBreakMin = 5;     // 1..15
        public static int PomodoroLongBreakMin = 15;     // 5..30
        public static bool TimerSound = true;
        public static bool CalendarWindows = true;       // Calendario de Windows (cuenta Microsoft)
        public static string CalendarGoogleUrl = "";     // Enlace privado iCal de Google Calendar
        public static int EventAlertMinutes = 10;        // 0 = sin aviso; 0..30

        // Widgets
        public static Color WidgetAccent = Color.FromArgb(0, 210, 255);
        public static int WidgetGlassPercent = 60;      // 0..100

        /// <summary>
        /// Algún ajuste cambió (lo lanzan Save y NotifyChanged).
        /// </summary>
        public static event Action Changed;

        private static string FilePath
        {
            get { return Path.Combine(Path.GetDirectoryName(Log.FilePath), "config.ini"); }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return;
                Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }

                PrivacyBlurPx = ReadInt(values, "privacidad.desenfoque", PrivacyBlurPx, 0, 40);
                PrivacyDarkenPercent = ReadInt(values, "privacidad.oscurecimiento", PrivacyDarkenPercent, 0, 100);
                PrivacyTint = ReadColor(values, "privacidad.tinte", PrivacyTint);

                IslandShowMusic = ReadBool(values, "isla.musica", IslandShowMusic);
                IslandShowSound = ReadBool(values, "isla.sonido", IslandShowSound);
                IslandShowWeather = ReadBool(values, "isla.clima", IslandShowWeather);
                IslandShowSystem = ReadBool(values, "isla.sistema", IslandShowSystem);
                IslandShowTimer = ReadBool(values, "isla.tiempo", IslandShowTimer);
                IslandShowAgenda = ReadBool(values, "isla.agenda", IslandShowAgenda);
                MaximizedBehavior behavior;
                string raw;
                if (values.TryGetValue("isla.maximizada", out raw) && Enum.TryParse(raw, out behavior)) IslandMaximized = behavior;
                IslandOpenOnHover = ReadBool(values, "isla.abrirAlPasar", IslandOpenOnHover);
                IslandCloseDelayMs = ReadInt(values, "isla.cerrarTrasMs", IslandCloseDelayMs, 0, 3000);
                IslandSongPeekMs = ReadInt(values, "isla.cancionNuevaMs", IslandSongPeekMs, 0, 10000);
                IslandOpacityPercent = ReadInt(values, "isla.opacidad", IslandOpacityPercent, 50, 100);
                IslandScalePercent = ReadInt(values, "isla.tamano", IslandScalePercent, 70, 120);
                NotificationsEnabled = ReadBool(values, "avisos.activos", NotificationsEnabled);
                NotificationDurationMs = ReadInt(values, "avisos.duracionMs", NotificationDurationMs, 1000, 10000);
                CaptureDots = ReadBool(values, "avisos.puntosMicroCamara", CaptureDots);
                CaptureAlerts = ReadBool(values, "avisos.avisoMicroCamara", CaptureAlerts);
                PomodoroFocusMin = ReadInt(values, "tiempo.enfoqueMin", PomodoroFocusMin, 5, 60);
                PomodoroShortBreakMin = ReadInt(values, "tiempo.descansoMin", PomodoroShortBreakMin, 1, 15);
                PomodoroLongBreakMin = ReadInt(values, "tiempo.descansoLargoMin", PomodoroLongBreakMin, 5, 30);
                TimerSound = ReadBool(values, "tiempo.sonido", TimerSound);
                CalendarWindows = ReadBool(values, "agenda.windows", CalendarWindows);
                if (values.TryGetValue("agenda.google", out raw)) CalendarGoogleUrl = raw;
                EventAlertMinutes = ReadInt(values, "agenda.avisoMin", EventAlertMinutes, 0, 30);

                WidgetAccent = ReadColor(values, "widgets.acento", WidgetAccent);
                WidgetGlassPercent = ReadInt(values, "widgets.cristal", WidgetGlassPercent, 0, 100);
            }
            catch (IOException ex)
            {
                Log.Error("[SuiteSettings] No se pudo leer {0}: {1}", FilePath, ex.Message);
            }
        }

        public static void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, new[]
                {
                    "privacidad.desenfoque=" + PrivacyBlurPx,
                    "privacidad.oscurecimiento=" + PrivacyDarkenPercent,
                    "privacidad.tinte=" + ToHex(PrivacyTint),
                    "isla.musica=" + (IslandShowMusic ? 1 : 0),
                    "isla.sonido=" + (IslandShowSound ? 1 : 0),
                    "isla.clima=" + (IslandShowWeather ? 1 : 0),
                    "isla.sistema=" + (IslandShowSystem ? 1 : 0),
                    "isla.tiempo=" + (IslandShowTimer ? 1 : 0),
                    "isla.agenda=" + (IslandShowAgenda ? 1 : 0),
                    "isla.maximizada=" + IslandMaximized,
                    "isla.abrirAlPasar=" + (IslandOpenOnHover ? 1 : 0),
                    "isla.cerrarTrasMs=" + IslandCloseDelayMs,
                    "isla.cancionNuevaMs=" + IslandSongPeekMs,
                    "isla.opacidad=" + IslandOpacityPercent,
                    "isla.tamano=" + IslandScalePercent,
                    "avisos.activos=" + (NotificationsEnabled ? 1 : 0),
                    "avisos.duracionMs=" + NotificationDurationMs,
                    "avisos.puntosMicroCamara=" + (CaptureDots ? 1 : 0),
                    "avisos.avisoMicroCamara=" + (CaptureAlerts ? 1 : 0),
                    "tiempo.enfoqueMin=" + PomodoroFocusMin,
                    "tiempo.descansoMin=" + PomodoroShortBreakMin,
                    "tiempo.descansoLargoMin=" + PomodoroLongBreakMin,
                    "tiempo.sonido=" + (TimerSound ? 1 : 0),
                    "agenda.windows=" + (CalendarWindows ? 1 : 0),
                    "agenda.google=" + CalendarGoogleUrl,
                    "agenda.avisoMin=" + EventAlertMinutes,
                    "widgets.acento=" + ToHex(WidgetAccent),
                    "widgets.cristal=" + WidgetGlassPercent
                });
            }
            catch (IOException ex)
            {
                Log.Error("[SuiteSettings] No se pudo guardar {0}: {1}", FilePath, ex.Message);
            }
            NotifyChanged();
        }

        /// <summary>
        /// Aplica los valores actuales sin escribir el archivo (mientras se arrastra un deslizador).
        /// </summary>
        public static void NotifyChanged()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        private static int ReadInt(Dictionary<string, string> values, string key, int fallback, int min, int max)
        {
            string raw;
            int value;
            if (!values.TryGetValue(key, out raw) || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) return fallback;
            return Math.Max(min, Math.Min(max, value));
        }

        private static bool ReadBool(Dictionary<string, string> values, string key, bool fallback)
        {
            string raw;
            return values.TryGetValue(key, out raw) ? raw != "0" : fallback;
        }

        private static Color ReadColor(Dictionary<string, string> values, string key, Color fallback)
        {
            string raw;
            int rgb;
            if (!values.TryGetValue(key, out raw) || !int.TryParse(raw.TrimStart('#'), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb)) return fallback;
            return Color.FromArgb(255, Color.FromArgb(rgb));
        }

        private static string ToHex(Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }
    }
}
