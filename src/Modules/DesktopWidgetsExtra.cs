using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Widget Clima (mockup «Widgets nuevos»): temperatura grande con su icono, máxima y mínima, y las próximas horas.
    /// </summary>
    internal class WeatherWidget : WidgetWindow
    {
        private readonly Font _bigFont = new Font("Segoe UI Semibold", 20f);

        public WeatherWidget(WidgetLayout layout) : base(layout, "clima")
        {
            int padding = Px(BasePadding);
            SetCardHeight(padding + HeaderHeight + Px(40) + Px(8) + Px(16) + Px(4) + Px(16) + padding);
        }

        protected override void PaintContent(Graphics g, Rectangle card)
        {
            WeatherService.Conditions w = WeatherService.Shared.Current;
            Rectangle header = DrawHeader(g, card, w != null && w.City.Length > 0 ? "CLIMA · " + w.City.ToUpperInvariant() : "CLIMA");
            int y = header.Bottom + Px(12);
            if (w == null)
            {
                DrawText(g, "Buscando el clima…", LabelFont, Muted, new Rectangle(header.X, y, header.Width, Px(20)), StringAlignment.Near);
                return;
            }

            int icon = Px(40);
            WeatherIcons.Draw(g, new RectangleF(header.X, y, icon, icon), w.Code, w.IsDay);
            Rectangle temp = new Rectangle(header.X + icon + Px(8), y, Px(62), icon);
            DrawText(g, string.Format("{0:0}°", w.TemperatureC), _bigFont, Color.White, temp, StringAlignment.Near);
            DrawText(g, w.Description, SmallFont, Secondary, new Rectangle(temp.Right, y, header.Right - temp.Right, icon), StringAlignment.Far);

            y += icon + Px(8);
            DrawText(g, string.Format("Máx {0:0}° · Mín {1:0}°", w.MaxC, w.MinC), LabelFont, Secondary, new Rectangle(header.X, y, header.Width, Px(16)), StringAlignment.Near);
            y += Px(16) + Px(4);
            List<string> hours = new List<string>();
            foreach (WeatherService.HourForecast h in w.NextHours)
            {
                hours.Add(string.Format("{0} {1:0}°", h.Time, h.TemperatureC));
                if (hours.Count == 3) break;
            }
            DrawText(g, string.Join("  ·  ", hours.ToArray()), SmallFont, Muted, new Rectangle(header.X, y, header.Width, Px(16)), StringAlignment.Near);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _bigFont.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Widget Agenda de hoy: los próximos eventos del calendario (los mismos de la isla). Clic: abre el calendario.
    /// </summary>
    internal class AgendaWidget : WidgetWindow
    {
        private const int Rows = 4;

        public AgendaWidget(WidgetLayout layout) : base(layout, "agenda")
        {
            int padding = Px(BasePadding);
            SetCardHeight(padding + HeaderHeight + Rows * Px(18) + (Rows - 1) * Px(6) + padding);
        }

        private static List<AgendaEvent> Today()
        {
            DateTime now = DateTime.Now;
            List<AgendaEvent> list = new List<AgendaEvent>();
            foreach (AgendaEvent e in AgendaService.Events)
            {
                if (e.Start.Date != now.Date || (!e.AllDay && e.End <= now)) continue;
                list.Add(e);
            }
            return list;
        }

        protected override void OnCardClick(Point location)
        {
            List<AgendaEvent> today = Today();
            string url = today.Count > 0 ? today[0].WebUrl : null;
            try { Process.Start(string.IsNullOrEmpty(url) ? "outlookcal:" : url); } catch (Exception) { }
        }

        protected override void PaintContent(Graphics g, Rectangle card)
        {
            Rectangle header = DrawHeader(g, card, "HOY · " + DateTime.Now.ToString("dddd d", new CultureInfo("es-MX")).ToUpperInvariant());
            List<AgendaEvent> today = Today();
            if (today.Count > 0) DrawBadge(g, header, today.Count == 1 ? "1 evento" : today.Count + " eventos");
            int y = header.Bottom + Px(12);
            if (today.Count == 0)
            {
                DrawText(g, "Sin eventos hoy", LabelFont, Muted, new Rectangle(header.X, y, header.Width, Px(18)), StringAlignment.Near);
                return;
            }
            for (int i = 0; i < Math.Min(Rows, today.Count); i++)
            {
                AgendaEvent e = today[i];
                int dot = Px(6);
                using (SolidBrush b = new SolidBrush(e.Color.IsEmpty ? Accent : e.Color)) g.FillEllipse(b, header.X, y + (Px(18) - dot) / 2, dot, dot);
                string time = e.AllDay ? "Todo el día" : TimeText.Short(e.Start);
                int timeW = e.AllDay ? Px(70) : Px(40);
                DrawText(g, time, ValueFont, Accent, new Rectangle(header.X + Px(12), y, timeW, Px(18)), StringAlignment.Near);
                DrawText(g, e.Title, LabelFont, Secondary, new Rectangle(header.X + Px(12) + timeW, y, header.Right - header.X - Px(12) - timeW, Px(18)), StringAlignment.Near);
                y += Px(18) + Px(6);
            }
        }
    }

    /// <summary>
    /// Widget Cuenta regresiva: días que faltan para una fecha. Se cambia en cuenta_regresiva.txt (clic lo abre),
    /// con una línea "Nombre=AAAA-MM-DD". Por defecto, Navidad.
    /// </summary>
    internal class CountdownWidget : WidgetWindow
    {
        private readonly string _path = Path.Combine(Path.GetDirectoryName(Log.FilePath), "cuenta_regresiva.txt");
        private readonly Font _bigFont = new Font("Segoe UI Semibold", 20f);

        public CountdownWidget(WidgetLayout layout) : base(layout, "cuenta")
        {
            int padding = Px(BasePadding);
            SetCardHeight(padding + HeaderHeight + Px(40) + Px(16) + padding);
        }

        private void Read(out string name, out DateTime date)
        {
            DateTime today = DateTime.Today;
            name = "Navidad";
            date = new DateTime(today.Year, 12, 25);
            if (date < today) date = date.AddYears(1);
            try
            {
                if (!File.Exists(_path))
                {
                    File.WriteAllLines(_path, new[] { "# Escribe: Nombre=AAAA-MM-DD (solo se usa la primera línea sin #)", name + "=" + date.ToString("yyyy-MM-dd") });
                    return;
                }
                foreach (string raw in File.ReadAllLines(_path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int eq = line.LastIndexOf('=');
                    DateTime parsed;
                    if (eq > 0 && DateTime.TryParseExact(line.Substring(eq + 1).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                    {
                        name = line.Substring(0, eq).Trim();
                        date = parsed;
                    }
                    return;
                }
            }
            catch (IOException) { }
        }

        protected override void OnCardClick(Point location)
        {
            try { Process.Start("notepad.exe", "\"" + _path + "\""); } catch (Exception) { }
        }

        protected override void PaintContent(Graphics g, Rectangle card)
        {
            string name;
            DateTime date;
            Read(out name, out date);
            Rectangle header = DrawHeader(g, card, "FALTAN");
            int days = (int)(date.Date - DateTime.Today).TotalDays;
            string big = days > 1 ? days + " días" : days == 1 ? "1 día" : days == 0 ? "¡Hoy!" : "Ya pasó";
            int y = header.Bottom + Px(8);
            DrawText(g, big, _bigFont, Color.White, new Rectangle(header.X, y, header.Width, Px(40)), StringAlignment.Near);
            DrawText(g, "para " + name + " · " + date.ToString("d 'de' MMMM", new CultureInfo("es-MX")), LabelFont, Secondary,
                new Rectangle(header.X, y + Px(40), header.Width, Px(16)), StringAlignment.Near);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _bigFont.Dispose();
            base.Dispose(disposing);
        }
    }
}
