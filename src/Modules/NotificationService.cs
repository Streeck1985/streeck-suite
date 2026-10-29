using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using StreeckSuite.Core;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Un aviso de una app tal como lo muestra la isla.
    /// </summary>
    internal class IslandNotification
    {
        public uint Id;
        public string AppId;      // AUMID: identifica la app y sirve para abrirla
        public string AppName;
        public string Title;
        public string Body;
        public DateTime Time;
        public Bitmap Icon;       // Compartido por todos los avisos de la misma app (no liberar)
        public Color Accent;
    }

    /// <summary>
    /// Lee las notificaciones de Windows (las del centro de notificaciones) cada poco tiempo en segundo plano.
    /// Avisa de las que llegan nuevas y mantiene el historial para la pestaña Avisos, sin las apps silenciadas
    /// ni las que el usuario borró en la isla. No las quita de Windows.
    /// </summary>
    internal static class NotificationService
    {
        private const int PollMs = 1500;
        private const int IconPx = 64;

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, Bitmap> Icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, Color> Accents = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<uint> Seen = new HashSet<uint>();
        private static readonly HashSet<uint> Dismissed = new HashSet<uint>();
        private static List<IslandNotification> _history = new List<IslandNotification>();
        private static Thread _thread;
        private static bool _firstPoll = true;

        /// <summary>
        /// Llegó un aviso nuevo (en el hilo de segundo plano).
        /// </summary>
        public static event Action<IslandNotification> Arrived;

        /// <summary>
        /// Historial actual, del más reciente al más antiguo.
        /// </summary>
        public static List<IslandNotification> History
        {
            get { lock (Gate) return new List<IslandNotification>(_history); }
        }

        public static void EnsureStarted()
        {
            lock (Gate)
            {
                if (_thread != null) return;
                _thread = new Thread(Run) { IsBackground = true, Name = "NotificationService", Priority = ThreadPriority.BelowNormal };
                _thread.Start();
            }
        }

        /// <summary>
        /// "Borrar todo" de la pestaña Avisos: se ocultan de la isla (en Windows siguen).
        /// </summary>
        public static void ClearHistory()
        {
            lock (Gate)
            {
                foreach (IslandNotification n in _history) Dismissed.Add(n.Id);
                _history = new List<IslandNotification>();
            }
        }

        /// <summary>
        /// Abre la app que envió el aviso (la isla no puede pulsar los botones del aviso original).
        /// </summary>
        public static void OpenApp(IslandNotification notification)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", "shell:AppsFolder\\" + notification.AppId);
            }
            catch (Exception ex)
            {
                Log.Error("[NotificationService] No se pudo abrir {0}: {1}", notification.AppId, ex.Message);
            }
        }

        private static void Run()
        {
            UserNotificationListener listener;
            try
            {
                listener = UserNotificationListener.Current;
                UserNotificationListenerAccessStatus access = listener.GetAccessStatus();
                if (access != UserNotificationListenerAccessStatus.Allowed) access = WinRtAsync.Wait(listener.RequestAccessAsync());
                if (access != UserNotificationListenerAccessStatus.Allowed)
                {
                    Log.Error("[NotificationService] Windows no da acceso a las notificaciones ({0})", access);
                    return;
                }
                Log.Info("[NotificationService] Leyendo las notificaciones de Windows");
            }
            catch (Exception ex)
            {
                Log.Error("[NotificationService] No se pudo acceder a las notificaciones: {0}", ex.Message);
                return;
            }

            while (true)
            {
                try
                {
                    if (SuiteSettings.NotificationsEnabled) Poll(listener);
                }
                catch (Exception ex)
                {
                    Log.Error("[NotificationService] Error al leer las notificaciones: {0}", ex.Message);
                }
                Thread.Sleep(PollMs);
            }
        }

        private static void Poll(UserNotificationListener listener)
        {
            IReadOnlyList<UserNotification> current = WinRtAsync.Wait(listener.GetNotificationsAsync(NotificationKinds.Toast));
            List<IslandNotification> history = new List<IslandNotification>();
            List<IslandNotification> arrived = new List<IslandNotification>();

            foreach (UserNotification raw in current)
            {
                IslandNotification n;
                lock (Gate)
                {
                    if (Dismissed.Contains(raw.Id)) continue;
                    n = _history.Find(h => h.Id == raw.Id);
                }
                if (n == null)
                {
                    n = Convert(raw);
                    if (n == null) continue;
                }
                NotificationApps.Remember(n.AppId, n.AppName);
                if (NotificationApps.IsMuted(n.AppId)) continue;

                history.Add(n);
                bool isNew;
                lock (Gate) isNew = Seen.Add(n.Id);
                // Las que ya estaban al arrancar la suite van al historial, pero no saltan como aviso
                if (isNew && !_firstPoll && (DateTime.Now - n.Time).TotalSeconds < 60) arrived.Add(n);
            }

            history.Sort((a, b) => b.Time.CompareTo(a.Time));
            lock (Gate) _history = history;
            _firstPoll = false;

            arrived.Sort((a, b) => a.Time.CompareTo(b.Time));
            Action<IslandNotification> handler = Arrived;
            if (handler != null)
            {
                foreach (IslandNotification n in arrived) handler(n);
            }
        }

        private static IslandNotification Convert(UserNotification raw)
        {
            IslandNotification n = new IslandNotification();
            n.Id = raw.Id;
            n.Time = raw.CreationTime.LocalDateTime;
            try
            {
                n.AppId = raw.AppInfo.AppUserModelId;
                n.AppName = raw.AppInfo.DisplayInfo.DisplayName;
            }
            catch (Exception)
            {
                return null; // App desinstalada o sin información
            }

            // Primer texto = título; el resto = cuerpo. Se quitan las marcas de dirección de texto que mete Discord
            List<string> texts = new List<string>();
            NotificationBinding binding = raw.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
            if (binding != null)
            {
                foreach (AdaptiveNotificationText text in binding.GetTextElements())
                {
                    string clean = (text.Text ?? "").Replace("⁨", "").Replace("⁩", "").Trim();
                    if (clean.Length > 0) texts.Add(clean);
                }
            }
            n.Title = texts.Count > 0 ? texts[0] : n.AppName;
            n.Body = texts.Count > 1 ? string.Join(" ", texts.GetRange(1, texts.Count - 1).ToArray()) : "";

            n.Icon = IconFor(n.AppId);
            Color accent;
            lock (Gate)
            {
                if (!Accents.TryGetValue(n.AppId, out accent))
                {
                    accent = n.Icon != null ? MediaService.DominantColor(n.Icon) : IslandPainter.Sky;
                    Accents[n.AppId] = accent;
                }
            }
            n.Accent = accent;
            return n;
        }

        #region Icono real de la app

        /// <summary>
        /// Icono de la app tal como sale en el menú Inicio (funciona con apps de la tienda y de escritorio).
        /// </summary>
        internal static Bitmap IconFor(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return null;
            lock (Gate)
            {
                Bitmap cached;
                if (Icons.TryGetValue(appId, out cached)) return cached;
            }
            Bitmap icon = ShellImage.Get("shell:AppsFolder\\" + appId, IconPx, ShellImage.IconOnly);
            lock (Gate) Icons[appId] = icon;
            return icon;
        }

        #endregion
    }

    /// <summary>
    /// Apps que han enviado algún aviso y cuáles silenció el usuario (%LOCALAPPDATA%\StreeckSuite\avisos_apps.txt,
    /// una línea "1|0 TAB AUMID TAB nombre"). Las apps nuevas empiezan activadas.
    /// </summary>
    internal static class NotificationApps
    {
        public class App
        {
            public string Id;
            public string Name;
            public bool Muted;
        }

        private static readonly object Gate = new object();
        private static readonly string FilePath = Path.Combine(Path.GetDirectoryName(Log.FilePath), "avisos_apps.txt");
        private static List<App> _apps;

        public static List<App> All
        {
            get
            {
                lock (Gate)
                {
                    Load();
                    List<App> copy = new List<App>();
                    foreach (App app in _apps) copy.Add(new App { Id = app.Id, Name = app.Name, Muted = app.Muted });
                    return copy;
                }
            }
        }

        public static bool IsMuted(string id)
        {
            lock (Gate)
            {
                Load();
                App app = _apps.Find(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
                return app != null && app.Muted;
            }
        }

        public static void SetMuted(string id, bool muted)
        {
            lock (Gate)
            {
                Load();
                App app = _apps.Find(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
                if (app == null || app.Muted == muted) return;
                app.Muted = muted;
                Save();
            }
        }

        public static void Remember(string id, string name)
        {
            if (string.IsNullOrEmpty(id)) return;
            lock (Gate)
            {
                Load();
                if (_apps.Exists(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase))) return;
                _apps.Add(new App { Id = id, Name = string.IsNullOrEmpty(name) ? id : name });
                _apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                Save();
            }
        }

        private static void Load()
        {
            if (_apps != null) return;
            _apps = new List<App>();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length == 3) _apps.Add(new App { Muted = parts[0] == "0", Id = parts[1], Name = parts[2] });
                }
            }
            catch (IOException ex)
            {
                Log.Error("[NotificationService] No se pudo leer {0}: {1}", FilePath, ex.Message);
            }
        }

        private static void Save()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (App app in _apps) lines.Add((app.Muted ? "0" : "1") + "\t" + app.Id + "\t" + app.Name);
                File.WriteAllLines(FilePath, lines.ToArray());
            }
            catch (IOException ex)
            {
                Log.Error("[NotificationService] No se pudo guardar {0}: {1}", FilePath, ex.Message);
            }
        }
    }
}
