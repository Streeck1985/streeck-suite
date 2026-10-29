using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using StreeckSuite.Core;
using Windows.ApplicationModel.Appointments;

namespace StreeckSuite.Modules
{
    internal class AgendaEvent
    {
        public string Key;        // Fuente + id + inicio: para no avisar dos veces del mismo evento
        public string Title;
        public string Location;
        public DateTime Start;
        public DateTime End;
        public bool AllDay;
        public Color Color;
        public string JoinUrl;    // Enlace de Teams/Meet/Zoom, o null
        public string WebUrl;     // Dónde abrir el calendario al hacer clic (web del proveedor u Outlook)
    }

    /// <summary>
    /// Estado de cada enlace iCal para el panel de ajustes.
    /// </summary>
    internal class CalendarFeed
    {
        public string Url;
        public string Name;       // X-WR-CALNAME del calendario, o el proveedor si no lo trae
        public Color Color;
        public int Count = -1;    // Eventos hoy y mañana; -1 mientras se lee
        public string Error;      // null si se leyó bien
    }

    /// <summary>
    /// Eventos de hoy y mañana para la pestaña Agenda (mockup D2): del calendario de Windows (cuenta Microsoft) y de
    /// los enlaces iCal privados que pegue el usuario (Google, Outlook publicado, iCloud...), juntos y ordenados.
    /// Cada enlace tiene su color. Se recargan cada 5 minutos en segundo plano.
    /// </summary>
    internal static class AgendaService
    {
        private const int RefreshMs = 5 * 60 * 1000;
        // Un color por enlace, en el orden en que se añadieron
        private static readonly Color[] FeedColors =
        {
            Color.FromArgb(66, 133, 244), Color.FromArgb(167, 139, 250), Color.FromArgb(74, 222, 128),
            Color.FromArgb(244, 114, 182), Color.FromArgb(251, 146, 60), Color.FromArgb(45, 212, 191)
        };
        private static readonly Regex JoinLink = new Regex(@"https?://(meet\.google\.com|teams\.microsoft\.com|teams\.live\.com|[\w.-]*zoom\.us)/[^\s""<>\\]+",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly object Gate = new object();
        private static readonly AutoResetEvent Wake = new AutoResetEvent(false);
        private static Thread _thread;
        private static List<AgendaEvent> _events = new List<AgendaEvent>();
        private static List<CalendarFeed> _feeds = new List<CalendarFeed>();
        private static int _windowsCount = -1;

        /// <summary>
        /// La lista cambió (en un hilo de segundo plano).
        /// </summary>
        public static event Action Updated;

        public static List<AgendaEvent> Events
        {
            get { lock (Gate) return new List<AgendaEvent>(_events); }
        }

        /// <summary>
        /// Estado de cada enlace iCal (nombre, color, eventos o error) para el panel de ajustes.
        /// </summary>
        public static List<CalendarFeed> Feeds
        {
            get
            {
                lock (Gate)
                {
                    // Los enlaces recién pegados aparecen al momento como "Leyendo…"
                    List<CalendarFeed> feeds = new List<CalendarFeed>();
                    for (int i = 0; i < SuiteSettings.CalendarIcalUrls.Count; i++)
                    {
                        string url = SuiteSettings.CalendarIcalUrls[i];
                        CalendarFeed known = _feeds.Find(f => f.Url == url);
                        feeds.Add(known ?? new CalendarFeed { Url = url, Name = ProviderName(url), Color = ColorFor(i) });
                    }
                    return feeds;
                }
            }
        }

        public static Color ColorFor(int index)
        {
            return FeedColors[index % FeedColors.Length];
        }

        /// <summary>
        /// Eventos encontrados en el calendario de Windows hoy y mañana (-1 si no se pudo leer).
        /// </summary>
        public static int WindowsCount
        {
            get { lock (Gate) return _windowsCount; }
        }

        public static bool HasSource
        {
            get { return SuiteSettings.CalendarWindows || SuiteSettings.CalendarIcalUrls.Count > 0; }
        }

        public static void EnsureStarted()
        {
            lock (Gate)
            {
                if (_thread != null) return;
                _thread = new Thread(Run) { IsBackground = true, Name = "AgendaService", Priority = ThreadPriority.BelowNormal };
                _thread.Start();
            }
        }

        /// <summary>
        /// Recargar ya (al cambiar las fuentes en el panel).
        /// </summary>
        public static void RefreshNow()
        {
            EnsureStarted();
            Wake.Set();
        }

        private static void Run()
        {
            while (true)
            {
                DateTime from = DateTime.Today, to = DateTime.Today.AddDays(2);
                List<AgendaEvent> events = new List<AgendaEvent>();
                if (SuiteSettings.CalendarWindows) events.AddRange(ReadWindows(from, to));
                else lock (Gate) _windowsCount = -1;
                List<CalendarFeed> feeds = new List<CalendarFeed>();
                List<string> urls = new List<string>(SuiteSettings.CalendarIcalUrls);
                for (int i = 0; i < urls.Count; i++)
                {
                    CalendarFeed feed = new CalendarFeed { Url = urls[i], Name = ProviderName(urls[i]), Color = ColorFor(i) };
                    events.AddRange(ReadIcal(feed, from, to));
                    feeds.Add(feed);
                }
                lock (Gate) _feeds = feeds;

                events.Sort((a, b) => a.AllDay != b.AllDay && a.Start.Date == b.Start.Date ? (a.AllDay ? -1 : 1) : a.Start.CompareTo(b.Start));
                lock (Gate) _events = events;
                Action handler = Updated;
                if (handler != null) handler();

                Wake.WaitOne(RefreshMs);
            }
        }

        public static void Open(AgendaEvent agendaEvent, bool join)
        {
            string target = join && agendaEvent.JoinUrl != null ? agendaEvent.JoinUrl : agendaEvent.WebUrl;
            if (string.IsNullOrEmpty(target)) return;
            try
            {
                System.Diagnostics.Process.Start(target);
            }
            catch (Exception ex)
            {
                Log.Error("[AgendaService] No se pudo abrir {0}: {1}", target, ex.Message);
            }
        }

        #region Calendario de Windows

        private static List<AgendaEvent> ReadWindows(DateTime from, DateTime to)
        {
            List<AgendaEvent> events = new List<AgendaEvent>();
            try
            {
                AppointmentStore store = WinRtAsync.Wait(AppointmentManager.RequestStoreAsync(AppointmentStoreAccessType.AllCalendarsReadOnly));
                Dictionary<string, Color> colors = new Dictionary<string, Color>();
                foreach (AppointmentCalendar calendar in WinRtAsync.Wait(store.FindAppointmentCalendarsAsync()))
                {
                    Windows.UI.Color c = calendar.DisplayColor;
                    colors[calendar.LocalId] = c.A == 0 ? Color.FromArgb(56, 189, 248) : Color.FromArgb(c.R, c.G, c.B);
                }

                FindAppointmentsOptions options = new FindAppointmentsOptions();
                foreach (string property in new[] { AppointmentProperties.Subject, AppointmentProperties.Location, AppointmentProperties.StartTime,
                    AppointmentProperties.Duration, AppointmentProperties.AllDay, AppointmentProperties.Details, AppointmentProperties.OnlineMeetingLink })
                {
                    options.FetchProperties.Add(property);
                }
                foreach (Appointment a in WinRtAsync.Wait(store.FindAppointmentsAsync(from, to - from, options)))
                {
                    AgendaEvent e = new AgendaEvent();
                    e.Title = string.IsNullOrEmpty(a.Subject) ? "(Sin título)" : a.Subject;
                    e.Location = a.Location ?? "";
                    e.AllDay = a.AllDay;
                    e.Start = e.AllDay ? a.StartTime.Date : a.StartTime.LocalDateTime;
                    e.End = e.Start + a.Duration;
                    Color color;
                    e.Color = colors.TryGetValue(a.CalendarId ?? "", out color) ? color : Color.FromArgb(56, 189, 248);
                    e.JoinUrl = !string.IsNullOrEmpty(a.OnlineMeetingLink) ? a.OnlineMeetingLink : FindJoinLink(a.Location + " " + a.Details);
                    e.Key = "w|" + a.LocalId + "|" + e.Start.Ticks;
                    e.WebUrl = "outlookcal:";
                    events.Add(e);
                }
                lock (Gate) _windowsCount = events.Count;
            }
            catch (Exception ex)
            {
                Log.Error("[AgendaService] No se pudo leer el calendario de Windows: {0}", ex.Message);
                lock (Gate) _windowsCount = -1;
            }
            return events;
        }

        #endregion

        #region Enlaces iCal (Google, Outlook publicado, iCloud...)

        private static List<AgendaEvent> ReadIcal(CalendarFeed feed, DateTime from, DateTime to)
        {
            List<AgendaEvent> events = new List<AgendaEvent>();
            try
            {
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
                string ics;
                using (WebClient client = new WebClient())
                {
                    client.Encoding = Encoding.UTF8;
                    ics = client.DownloadString(feed.Url.Replace("webcal://", "https://"));
                }
                if (!ics.Contains("BEGIN:VCALENDAR")) throw new FormatException("el enlace no es un calendario iCal");
                Match name = Regex.Match(ics, @"^X-WR-CALNAME:(.+)$", RegexOptions.Multiline);
                if (name.Success && name.Groups[1].Value.Trim().Length > 0) feed.Name = Unescape(name.Groups[1].Value.Trim());
                events = ParseIcs(ics, from, to);
                string web = WebUrlFor(feed.Url);
                foreach (AgendaEvent e in events)
                {
                    e.Color = feed.Color;
                    e.WebUrl = web;
                }
                feed.Count = events.Count;
            }
            catch (Exception ex)
            {
                Log.Error("[AgendaService] No se pudo leer el calendario {0}: {1}", ProviderName(feed.Url), ex.Message);
                feed.Error = ex.Message;
            }
            return events;
        }

        // Nombre provisional mientras no se lee X-WR-CALNAME
        /// <summary>
        /// Herramienta de desarrollo (--test-agenda <enlace>): lee un enlace iCal y anota en el registro lo encontrado.
        /// </summary>
        internal static void TestFeed(string url)
        {
            CalendarFeed feed = new CalendarFeed { Url = url, Name = ProviderName(url), Color = ColorFor(0) };
            DateTime from = DateTime.Today, to = DateTime.Today.AddDays(30);
            List<AgendaEvent> events = ReadIcal(feed, from, to);
            Log.Info("[AgendaService] Prueba: {0} · {1} eventos en 30 días · error: {2}", feed.Name, events.Count, feed.Error ?? "ninguno");
            events.Sort((a, b) => a.Start.CompareTo(b.Start));
            foreach (AgendaEvent e in events) Log.Info("[AgendaService]   {0:dd/MM HH:mm}{1} {2}", e.Start, e.AllDay ? " (todo el día)" : "", e.Title);
        }

        // Nombre provisional mientras no se lee X-WR-CALNAME
        private static string ProviderName(string url)
        {
            string u = url.ToLowerInvariant();
            if (u.Contains("google.com")) return "Google Calendar";
            if (u.Contains("outlook") || u.Contains("office365") || u.Contains("live.com")) return "Outlook";
            if (u.Contains("icloud.com")) return "iCloud";
            try { return new Uri(url.Replace("webcal://", "https://")).Host; }
            catch (UriFormatException) { return "Calendario"; }
        }

        private static string WebUrlFor(string url)
        {
            string u = url.ToLowerInvariant();
            if (u.Contains("google.com")) return "https://calendar.google.com/calendar/r/day";
            if (u.Contains("outlook") || u.Contains("office365") || u.Contains("live.com")) return "https://outlook.live.com/calendar/";
            if (u.Contains("icloud.com")) return "https://www.icloud.com/calendar/";
            return null;
        }

        private class IcsEvent
        {
            public Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> Params = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public List<string> ExDates = new List<string>();
        }

        internal static List<AgendaEvent> ParseIcs(string ics, DateTime from, DateTime to)
        {
            // Las líneas largas continúan en la siguiente empezando por espacio o tabulador
            string unfolded = ics.Replace("\r\n", "\n").Replace("\n ", "").Replace("\n\t", "");
            List<IcsEvent> raw = new List<IcsEvent>();
            IcsEvent current = null;
            foreach (string line in unfolded.Split('\n'))
            {
                if (line == "BEGIN:VEVENT") { current = new IcsEvent(); continue; }
                if (line == "END:VEVENT") { if (current != null) raw.Add(current); current = null; continue; }
                if (current == null) continue;
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string head = line.Substring(0, colon), value = line.Substring(colon + 1);
                int semicolon = head.IndexOf(';');
                string name = semicolon > 0 ? head.Substring(0, semicolon) : head;
                if (name == "EXDATE") { current.ExDates.AddRange(value.Split(',')); continue; }
                current.Values[name] = value;
                current.Params[name] = semicolon > 0 ? head.Substring(semicolon + 1) : "";
            }

            // Ocurrencias modificadas (RECURRENCE-ID) sustituyen a la generada por la regla
            HashSet<string> overridden = new HashSet<string>();
            foreach (IcsEvent e in raw)
            {
                bool allDay;
                if (e.Values.ContainsKey("RECURRENCE-ID")) overridden.Add(Get(e, "UID") + "|" + ParseDate(e.Values["RECURRENCE-ID"], out allDay).Ticks);
            }

            List<AgendaEvent> result = new List<AgendaEvent>();
            foreach (IcsEvent e in raw)
            {
                if (Get(e, "STATUS").Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) || !e.Values.ContainsKey("DTSTART")) continue;
                bool allDay;
                DateTime start = ParseDate(e.Values["DTSTART"], out allDay);
                bool endAllDay;
                TimeSpan length = e.Values.ContainsKey("DTEND") ? ParseDate(e.Values["DTEND"], out endAllDay) - start
                    : allDay ? TimeSpan.FromDays(1) : TimeSpan.Zero;
                string uid = Get(e, "UID");
                HashSet<long> excluded = new HashSet<long>();
                foreach (string ex in e.ExDates) { bool ignored; excluded.Add(ParseDate(ex.Trim(), out ignored).Ticks); }

                IEnumerable<DateTime> starts = e.Values.ContainsKey("RRULE") && !e.Values.ContainsKey("RECURRENCE-ID")
                    ? Expand(start, e.Values["RRULE"], to) : new[] { start };
                foreach (DateTime occurrence in starts)
                {
                    if (occurrence + length <= from || occurrence >= to || excluded.Contains(occurrence.Ticks)) continue;
                    if (e.Values.ContainsKey("RRULE") && overridden.Contains(uid + "|" + occurrence.Ticks)) continue;
                    string description = Unescape(Get(e, "DESCRIPTION"));
                    string location = Unescape(Get(e, "LOCATION"));
                    result.Add(new AgendaEvent
                    {
                        Title = Get(e, "SUMMARY").Length > 0 ? Unescape(Get(e, "SUMMARY")) : "(Sin título)",
                        Location = location,
                        Start = occurrence,
                        End = occurrence + length,
                        AllDay = allDay,

                        JoinUrl = FindJoinLink(location + " " + description),

                        Key = "g|" + uid + "|" + occurrence.Ticks
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// Repeticiones de una regla RRULE hasta <paramref name="to"/>: DAILY, WEEKLY (con BYDAY), MONTHLY y YEARLY,
        /// con INTERVAL, COUNT y UNTIL. Suficiente para reuniones semanales, cumpleaños, etc.
        /// </summary>
        private static IEnumerable<DateTime> Expand(DateTime start, string rule, DateTime to)
        {
            Dictionary<string, string> parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in rule.Split(';'))
            {
                int eq = part.IndexOf('=');
                if (eq > 0) parts[part.Substring(0, eq)] = part.Substring(eq + 1);
            }
            string freq = parts.ContainsKey("FREQ") ? parts["FREQ"].ToUpperInvariant() : "DAILY";
            int interval = 1, count = int.MaxValue;
            if (parts.ContainsKey("INTERVAL")) int.TryParse(parts["INTERVAL"], out interval);
            if (parts.ContainsKey("COUNT")) int.TryParse(parts["COUNT"], out count);
            interval = Math.Max(1, interval);
            DateTime until = DateTime.MaxValue;
            bool ignored;
            if (parts.ContainsKey("UNTIL")) until = ParseDate(parts["UNTIL"], out ignored);

            List<DayOfWeek> byDay = new List<DayOfWeek>();
            if (freq == "WEEKLY" && parts.ContainsKey("BYDAY"))
            {
                string[] codes = { "SU", "MO", "TU", "WE", "TH", "FR", "SA" };
                foreach (string day in parts["BYDAY"].Split(','))
                {
                    int index = Array.IndexOf(codes, day.Trim().ToUpperInvariant());
                    if (index >= 0) byDay.Add((DayOfWeek)index);
                }
            }

            List<DateTime> result = new List<DateTime>();
            int produced = 0;
            for (int step = 0; step < 5000 && produced < count; step++)
            {
                DateTime anchor;
                switch (freq)
                {
                    case "WEEKLY": anchor = start.AddDays(7 * interval * step); break;
                    case "MONTHLY": anchor = start.AddMonths(interval * step); break;
                    case "YEARLY": anchor = start.AddYears(interval * step); break;
                    default: anchor = start.AddDays(interval * step); break;
                }
                if (anchor > until || anchor >= to.AddDays(7)) break;

                if (byDay.Count == 0)
                {
                    result.Add(anchor);
                    produced++;
                    continue;
                }
                // Semana de la ocurrencia: cada día pedido, a la misma hora que el primer evento
                DateTime weekStart = anchor.AddDays(-(int)anchor.DayOfWeek);
                foreach (DayOfWeek day in byDay)
                {
                    DateTime occurrence = weekStart.AddDays((int)day);
                    if (occurrence < start || occurrence > until || produced >= count) continue;
                    result.Add(occurrence);
                    produced++;
                }
            }
            return result;
        }

        /// <summary>
        /// "20261028" (día entero), "20261027T203000Z" (UTC) o "20261027T203000" (hora local; se asume la zona
        /// del equipo, que es la habitual en un calendario personal).
        /// </summary>
        private static DateTime ParseDate(string value, out bool allDay)
        {
            value = value.Trim();
            allDay = value.Length == 8;
            if (allDay) return DateTime.ParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture);
            bool utc = value.EndsWith("Z");
            DateTime parsed = DateTime.ParseExact(value.TrimEnd('Z'), "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
            return utc ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc).ToLocalTime() : parsed;
        }

        private static string Get(IcsEvent e, string name)
        {
            string value;
            return e.Values.TryGetValue(name, out value) ? value : "";
        }

        private static string Unescape(string text)
        {
            return text.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();
        }

        #endregion

        private static string FindJoinLink(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            Match match = JoinLink.Match(text);
            return match.Success ? match.Value : null;
        }

        /// <summary>
        /// "en 12 min", "ahora", "1 h", "Todo el día"...
        /// </summary>
        public static string WhenText(AgendaEvent e)
        {
            if (e.AllDay) return "";
            DateTime now = DateTime.Now;
            if (e.Start <= now && e.End > now) return "ahora";
            TimeSpan until = e.Start - now;
            if (until.TotalMinutes < 60) return string.Format("en {0:0} min", Math.Ceiling(until.TotalMinutes));
            TimeSpan length = e.End - e.Start;
            if (length.TotalMinutes < 60) return string.Format("{0:0} min", length.TotalMinutes);
            return length.TotalMinutes % 60 == 0 ? string.Format("{0:0} h", length.TotalHours) : string.Format("{0:0.#} h", length.TotalHours);
        }
    }
}
