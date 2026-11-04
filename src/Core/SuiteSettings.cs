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
        public enum IslandStyleKind { Attached, Floating }               // Pegada arriba (notch de Bloom) o flotante
        public enum SearchShortcut { CtrlAltS, CtrlShiftSpace, CtrlSpace }  // Alt+Espacio y similares los usa PowerToys
        public enum WebSearchEngine { Google, Bing, DuckDuckGo }
        public enum AssistantEngineKind { Local, Gemini }
        public enum AssistantVoiceKind { GeminiCharon, PiperAld, PiperClaude, Windows }
        public enum TaskbarFormatKind { Islands, Capsule, Full }      // Como el simulador: 3 islas, cápsula, completa
        public enum TaskbarSearchKind { Full, Icon, Hidden }
        public enum GlassStyleKind { Aero, Transparent, Obsidian, Neon }

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
        public static int IslandOpacityPercent = 86;    // 30..100: opacidad del fondo de la isla (las letras no se transparentan)
        public static string IslandColor = "#000000";   // Color de fondo de la isla (negro como el notch de Bloom)
        public static bool DoNotDisturb = false;        // Centro de control: la isla no muestra avisos de apps
        public static int IslandScalePercent = 100;     // 70..120: tamaño de toda la isla (letras incluidas)
        public static IslandStyleKind IslandStyle = IslandStyleKind.Attached;

        // Avisos (las apps silenciadas se guardan aparte: NotificationApps)
        public static bool NotificationsEnabled = true;
        public static int NotificationDurationMs = 4000; // 1000..10000
        public static bool CaptureDots = true;           // Puntos de micrófono y cámara en la isla
        public static bool CaptureAlerts = true;         // Aviso cuando una app empieza a usarlos
        public static bool BluetoothConnectAlerts = true; // Aviso al conectar un dispositivo Bluetooth
        public static int BluetoothLowBatteryPercent = 20; // 0 = sin aviso; 0..50

        // Tiempo y agenda
        public static int PomodoroFocusMin = 25;         // 5..60
        public static int PomodoroShortBreakMin = 5;     // 1..15
        public static int PomodoroLongBreakMin = 15;     // 5..30
        public static bool TimerSound = true;
        public static bool CalendarWindows = true;       // Calendario de Windows (cuenta Microsoft)
        // Enlaces iCal privados (Google, Outlook publicado, iCloud...); se guardan separados por espacios
        public static List<string> CalendarIcalUrls = new List<string>();
        public static int EventAlertMinutes = 10;        // 0 = sin aviso; 0..30

        // Bandeja y búsqueda
        public static bool FileTrayEnabled = true;
        public static bool FileTrayPersist = true;        // Conservar la bandeja al reiniciar la suite
        public static SearchShortcut SearchHotkey = SearchShortcut.CtrlAltS;
        public static bool SearchFiles = true;
        public static WebSearchEngine SearchEngine = WebSearchEngine.Google;

        // Asistente IA (fase G)
        public static AssistantEngineKind AssistantEngine = AssistantEngineKind.Local;
        public static string OllamaModel = "llama3.1:8b";
        public static string GeminiApiKey = "";          // En el archivo va cifrada con DPAPI (solo este usuario de Windows la lee)
        public static bool AssistantSpeak = false;       // Leer las respuestas en voz alta
        public static AssistantVoiceKind AssistantVoice = AssistantVoiceKind.GeminiCharon; // Charon con Piper Ald de respaldo
        public static bool AssistantHotkey = true;       // Ctrl+Alt+A; si no, solo desde la búsqueda

        // Menú de inicio (fase H)
        public static bool StartMenuWinKey = true;       // La tecla Windows abre el menú de la suite
        public static bool StartMenuShowRecent = true;

        // Barra de tareas (fase I) y estilo de cristal global
        public static bool TaskbarEnabled = true;       // Islas sobre la barra real de Windows (I3)
        public static TaskbarFormatKind TaskbarFormat = TaskbarFormatKind.Islands;
        public static int TaskbarGap = 14;               // 0..80 px entre globos
        public static bool TaskbarEdges = true;          // Islas a los extremos (formato B, el aprobado por defecto)
        public static TaskbarSearchKind TaskbarSearch = TaskbarSearchKind.Full;
        public static GlassStyleKind GlassStyle = GlassStyleKind.Aero;

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
                IslandOpacityPercent = ReadInt(values, "isla.opacidad", IslandOpacityPercent, 30, 100);
                if (values.TryGetValue("isla.color", out raw) && raw.Length > 0) IslandColor = raw;
                DoNotDisturb = ReadBool(values, "isla.nomolestar", DoNotDisturb);
                IslandScalePercent = ReadInt(values, "isla.tamano", IslandScalePercent, 70, 120);
                IslandStyleKind islandStyle;
                if (values.TryGetValue("isla.estilo", out raw) && Enum.TryParse(raw, out islandStyle)) IslandStyle = islandStyle;
                NotificationsEnabled = ReadBool(values, "avisos.activos", NotificationsEnabled);
                NotificationDurationMs = ReadInt(values, "avisos.duracionMs", NotificationDurationMs, 1000, 10000);
                CaptureDots = ReadBool(values, "avisos.puntosMicroCamara", CaptureDots);
                CaptureAlerts = ReadBool(values, "avisos.avisoMicroCamara", CaptureAlerts);
                BluetoothConnectAlerts = ReadBool(values, "avisos.bluetoothConectar", BluetoothConnectAlerts);
                BluetoothLowBatteryPercent = ReadInt(values, "avisos.bluetoothBateriaBaja", BluetoothLowBatteryPercent, 0, 50);
                PomodoroFocusMin = ReadInt(values, "tiempo.enfoqueMin", PomodoroFocusMin, 5, 60);
                PomodoroShortBreakMin = ReadInt(values, "tiempo.descansoMin", PomodoroShortBreakMin, 1, 15);
                PomodoroLongBreakMin = ReadInt(values, "tiempo.descansoLargoMin", PomodoroLongBreakMin, 5, 30);
                TimerSound = ReadBool(values, "tiempo.sonido", TimerSound);
                CalendarWindows = ReadBool(values, "agenda.windows", CalendarWindows);
                CalendarIcalUrls = new List<string>();
                if (values.TryGetValue("agenda.ical", out raw))
                {
                    foreach (string url in raw.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) CalendarIcalUrls.Add(url);
                }
                if (values.TryGetValue("agenda.google", out raw) && raw.Length > 0 && !CalendarIcalUrls.Contains(raw)) CalendarIcalUrls.Add(raw); // Versión anterior: un solo enlace
                EventAlertMinutes = ReadInt(values, "agenda.avisoMin", EventAlertMinutes, 0, 30);
                FileTrayEnabled = ReadBool(values, "bandeja.activa", FileTrayEnabled);
                FileTrayPersist = ReadBool(values, "bandeja.conservar", FileTrayPersist);
                SearchShortcut shortcut;
                if (values.TryGetValue("busqueda.atajo", out raw) && Enum.TryParse(raw, out shortcut)) SearchHotkey = shortcut;
                SearchFiles = ReadBool(values, "busqueda.archivos", SearchFiles);
                WebSearchEngine engine;
                if (values.TryGetValue("busqueda.web", out raw) && Enum.TryParse(raw, out engine)) SearchEngine = engine;
                AssistantEngineKind assistant;
                if (values.TryGetValue("asistente.motor", out raw) && Enum.TryParse(raw, out assistant)) AssistantEngine = assistant;
                if (values.TryGetValue("asistente.modeloLocal", out raw) && raw.Length > 0) OllamaModel = raw;
                if (values.TryGetValue("asistente.claveGemini", out raw)) GeminiApiKey = Unprotect(raw);
                AssistantSpeak = ReadBool(values, "asistente.leerEnVozAlta", AssistantSpeak);
                AssistantVoiceKind voiceKind;
                if (values.TryGetValue("asistente.voz", out raw) && Enum.TryParse(raw, out voiceKind)) AssistantVoice = voiceKind;
                AssistantHotkey = ReadBool(values, "asistente.atajo", AssistantHotkey);
                StartMenuWinKey = ReadBool(values, "inicio.teclaWindows", StartMenuWinKey);
                StartMenuShowRecent = ReadBool(values, "inicio.recientes", StartMenuShowRecent);
                TaskbarEnabled = ReadBool(values, "barra.islas", TaskbarEnabled);
                TaskbarFormatKind format;
                if (values.TryGetValue("barra.formato", out raw) && Enum.TryParse(raw, out format)) TaskbarFormat = format;
                TaskbarGap = ReadInt(values, "barra.separacion", TaskbarGap, 0, 80);
                TaskbarEdges = ReadBool(values, "barra.extremos2", TaskbarEdges);
                TaskbarSearchKind search;
                if (values.TryGetValue("barra.busqueda", out raw) && Enum.TryParse(raw, out search)) TaskbarSearch = search;
                GlassStyleKind glass;
                if (values.TryGetValue("cristal.estilo", out raw) && Enum.TryParse(raw, out glass)) GlassStyle = glass;

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
                    "isla.color=" + IslandColor,
                    "isla.nomolestar=" + (DoNotDisturb ? 1 : 0),
                    "isla.tamano=" + IslandScalePercent,
                    "isla.estilo=" + IslandStyle,
                    "avisos.activos=" + (NotificationsEnabled ? 1 : 0),
                    "avisos.duracionMs=" + NotificationDurationMs,
                    "avisos.puntosMicroCamara=" + (CaptureDots ? 1 : 0),
                    "avisos.avisoMicroCamara=" + (CaptureAlerts ? 1 : 0),
                    "avisos.bluetoothConectar=" + (BluetoothConnectAlerts ? 1 : 0),
                    "avisos.bluetoothBateriaBaja=" + BluetoothLowBatteryPercent,
                    "tiempo.enfoqueMin=" + PomodoroFocusMin,
                    "tiempo.descansoMin=" + PomodoroShortBreakMin,
                    "tiempo.descansoLargoMin=" + PomodoroLongBreakMin,
                    "tiempo.sonido=" + (TimerSound ? 1 : 0),
                    "agenda.windows=" + (CalendarWindows ? 1 : 0),
                    "agenda.ical=" + string.Join(" ", CalendarIcalUrls.ToArray()),
                    "agenda.avisoMin=" + EventAlertMinutes,
                    "bandeja.activa=" + (FileTrayEnabled ? 1 : 0),
                    "bandeja.conservar=" + (FileTrayPersist ? 1 : 0),
                    "busqueda.atajo=" + SearchHotkey,
                    "busqueda.archivos=" + (SearchFiles ? 1 : 0),
                    "busqueda.web=" + SearchEngine,
                    "asistente.motor=" + AssistantEngine,
                    "asistente.modeloLocal=" + OllamaModel,
                    "asistente.claveGemini=" + Protect(GeminiApiKey),
                    "asistente.leerEnVozAlta=" + (AssistantSpeak ? 1 : 0),
                    "asistente.voz=" + AssistantVoice,
                    "asistente.atajo=" + (AssistantHotkey ? 1 : 0),
                    "inicio.teclaWindows=" + (StartMenuWinKey ? 1 : 0),
                    "inicio.recientes=" + (StartMenuShowRecent ? 1 : 0),
                    "barra.islas=" + (TaskbarEnabled ? 1 : 0),
                    "barra.formato=" + TaskbarFormat,
                    "barra.separacion=" + TaskbarGap,
                    "barra.extremos2=" + (TaskbarEdges ? 1 : 0),
                    "barra.busqueda=" + TaskbarSearch,
                    "cristal.estilo=" + GlassStyle,
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

        // Claves de API: cifradas con DPAPI para el usuario actual de Windows (Base64 en el archivo)
        private static string Protect(string secret)
        {
            if (string.IsNullOrEmpty(secret)) return "";
            byte[] data = System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(secret), null,
                System.Security.Cryptography.DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(data);
        }

        private static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return "";
            try
            {
                byte[] data = System.Security.Cryptography.ProtectedData.Unprotect(Convert.FromBase64String(stored), null,
                    System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(data);
            }
            catch (Exception)
            {
                return ""; // Otro usuario u otro equipo: hay que volver a pegar la clave
            }
        }

        private static string ToHex(Color color)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }
    }
}
