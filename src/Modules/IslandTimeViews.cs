using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Colores de la fase D: enfoque en rojo, descanso en verde, temporizador simple en azul.
    /// </summary>
    internal static class TimeColors
    {
        public static readonly Color Focus = Color.FromArgb(248, 113, 113);
        public static readonly Color FocusLight = Color.FromArgb(252, 165, 165);
        public static readonly Color Break = Color.FromArgb(74, 222, 128);
        public static readonly Color BreakLight = Color.FromArgb(134, 239, 172);
        public static readonly Color Link = Color.FromArgb(125, 211, 252);
        public static readonly Color Watch = Color.FromArgb(251, 191, 36);
        public static readonly Color WatchLight = Color.FromArgb(253, 224, 71);

        public static Color Current
        {
            get
            {
                if (TimerService.Mode == TimerService.TimerMode.Stopwatch) return Watch;
                if (TimerService.Mode == TimerService.TimerMode.Timer) return IslandPainter.Sky;
                return TimerService.CurrentPhase == TimerService.Phase.Focus ? Focus : Break;
            }
        }

        public static Color CurrentLight
        {
            get
            {
                if (TimerService.Mode == TimerService.TimerMode.Stopwatch) return WatchLight;
                if (TimerService.Mode == TimerService.TimerMode.Timer) return Link;
                return TimerService.CurrentPhase == TimerService.Phase.Focus ? FocusLight : BreakLight;
            }
        }
    }

    /// <summary>
    /// Pestaña Tiempo (mockup D1): anillo con la cuenta atrás, modo Temporizador/Pomodoro, atajos de 5/10/15/25 min,
    /// iniciar/pausar y reiniciar. La rueda sobre el anillo suma o resta un minuto.
    /// </summary>
    internal class TimerView : IslandView
    {
        private const string GlyphPlay = "";
        private const string GlyphPause = "";
        private const string GlyphReset = "";
        private static readonly int[] Presets = { 5, 10, 15, 25 };

        private RectangleF _ring, _timerMode, _pomodoroMode, _watchMode, _play, _reset;
        private readonly RectangleF[] _presets = new RectangleF[Presets.Length];

        public override string TabName { get { return "Tiempo"; } }
        public override string TabGlyph { get { return ""; } }

        public override bool IsAvailable
        {
            get { return SuiteSettings.IslandShowTimer || TimerService.IsActive; }
        }

        public override SizeF BaseSize
        {
            get { return new SizeF(440, 116); }
        }

        public override Color? BorderColor
        {
            get { return TimerService.IsActive ? Color.FromArgb(115, TimeColors.Current) : (Color?)null; }
        }

        public override bool IsAnimated
        {
            get { return TimerService.IsRunning; }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            float x = p.Px(PadX);
            Color white = Color.FromArgb(alpha, IslandPainter.InkBase);
            Color gray = IslandPainter.Fade(IslandPainter.Gray, alpha);
            Color accent = TimeColors.Current;

            // Anillo de 112 px con el tiempo restante y la fase
            _ring = new RectangleF(x, (size.Height - p.Px(112)) / 2, p.Px(112), p.Px(112));
            p.DrawRing(g, _ring, TimerService.Progress, accent, alpha, p.Px(8));
            p.DrawText(g, TimerService.Format(TimerService.Shown), p.Bold18, white,
                new RectangleF(_ring.X, _ring.Y + p.Px(30), _ring.Width, p.Px(30)), StringAlignment.Center);
            p.DrawText(g, PhaseLabel(), p.Label11, IslandPainter.Fade(TimeColors.CurrentLight, alpha),
                new RectangleF(_ring.X, _ring.Y + p.Px(60), _ring.Width, p.Px(16)), StringAlignment.Center);

            float right = _ring.Right + p.Px(22);
            float width = size.Width - x - right;

            // Temporizador | Pomodoro | Cronómetro
            RectangleF modes = new RectangleF(right, _ring.Y, width, p.Px(30));
            IslandPainter.FillRounded(g, modes, p.Px(10), Color.FromArgb(alpha * 15 / 255, IslandPainter.InkBase));
            float third = (modes.Width - p.Px(14)) / 3;
            _timerMode = new RectangleF(modes.X + p.Px(3), modes.Y + p.Px(3), third, modes.Height - p.Px(6));
            _pomodoroMode = new RectangleF(_timerMode.Right + p.Px(4), _timerMode.Y, third, _timerMode.Height);
            _watchMode = new RectangleF(_pomodoroMode.Right + p.Px(4), _timerMode.Y, third, _timerMode.Height);
            TimerService.TimerMode mode = TimerService.Mode;
            RectangleF selectedMode = mode == TimerService.TimerMode.Pomodoro ? _pomodoroMode : mode == TimerService.TimerMode.Stopwatch ? _watchMode : _timerMode;
            IslandPainter.FillRounded(g, selectedMode, p.Px(8), Color.FromArgb(alpha * 36 / 255, IslandPainter.InkBase));
            bool onTimer = mode == TimerService.TimerMode.Timer, onPomodoro = mode == TimerService.TimerMode.Pomodoro, onWatch = mode == TimerService.TimerMode.Stopwatch;
            p.DrawText(g, "Temporizador", onTimer ? p.Tab12 : p.Text12, onTimer ? white : gray, _timerMode, StringAlignment.Center);
            p.DrawText(g, "Pomodoro", onPomodoro ? p.Tab12 : p.Text12, onPomodoro ? white : gray, _pomodoroMode, StringAlignment.Center);
            p.DrawText(g, "Cronómetro", onWatch ? p.Tab12 : p.Text12, onWatch ? white : gray, _watchMode, StringAlignment.Center);

            // Atajos de minutos
            float chipX = right, chipY = modes.Bottom + p.Px(10);
            for (int i = 0; i < Presets.Length; i++)
            {
                string label = i == 0 ? Presets[i] + " min" : Presets[i].ToString();
                float chipW = g.MeasureString(label, p.Text12).Width + p.Px(12);
                _presets[i] = new RectangleF(chipX, chipY, chipW, p.Px(24));
                bool selected = !onWatch && TimerService.Duration == TimeSpan.FromMinutes(Presets[i]);
                IslandPainter.FillRounded(g, _presets[i], p.Px(9), selected ? Color.FromArgb(alpha * 51 / 255, accent) : Color.FromArgb(alpha * 15 / 255, IslandPainter.InkBase));
                p.DrawText(g, label, selected ? p.Bold12 : p.Text12, selected ? IslandPainter.Fade(TimeColors.CurrentLight, alpha) : IslandPainter.Fade(Color.FromArgb(195, 201, 212), alpha),
                    _presets[i], StringAlignment.Center);
                chipX = _presets[i].Right + p.Px(6);
            }

            // Iniciar/pausar, reiniciar y pista de la rueda
            float buttonsY = chipY + p.Px(32);
            _play = new RectangleF(right, buttonsY, p.Px(40), p.Px(40));
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(alpha, IslandPainter.InkBase))) g.FillEllipse(fill, _play);
            p.DrawText(g, TimerService.IsRunning ? GlyphPause : GlyphPlay, p.Glyph16, IslandPainter.Fade(IslandPainter.InkContrast, alpha), _play, StringAlignment.Center);
            _reset = new RectangleF(_play.Right + p.Px(10), buttonsY + p.Px(4), p.Px(32), p.Px(32));
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(alpha * 20 / 255, IslandPainter.InkBase))) g.FillEllipse(fill, _reset);
            p.DrawText(g, GlyphReset, p.Glyph16, IslandPainter.Fade(Color.FromArgb(195, 201, 212), alpha), _reset, StringAlignment.Center);
            p.DrawText(g, onWatch ? "Cuenta hacia arriba desde 00:00" : "Rueda sobre el anillo: ±1 min", p.Text11, IslandPainter.Fade(Color.FromArgb(139, 147, 163), alpha),
                new RectangleF(_reset.Right + p.Px(10), buttonsY, size.Width - x - _reset.Right - p.Px(10), p.Px(40)), StringAlignment.Near);
        }

        private static string PhaseLabel()
        {
            if (TimerService.Mode == TimerService.TimerMode.Timer) return TimerService.IsRunning ? "TEMPORIZADOR" : "EN PAUSA";
            if (TimerService.Mode == TimerService.TimerMode.Stopwatch) return TimerService.IsRunning ? "CRONÓMETRO" : "EN PAUSA";
            switch (TimerService.CurrentPhase)
            {
                case TimerService.Phase.ShortBreak: return "DESCANSO";
                case TimerService.Phase.LongBreak: return "DESCANSO LARGO";
                default: return string.Format("ENFOQUE {0}/{1}", TimerService.Cycle, TimerService.CyclesPerLongBreak);
            }
        }

        public override bool OnClick(PointF point)
        {
            if (_play.Contains(point)) { TimerService.Toggle(); return true; }
            if (_reset.Contains(point)) { TimerService.Reset(); return true; }
            if (_timerMode.Contains(point)) { TimerService.SetMode(TimerService.TimerMode.Timer); return true; }
            if (_pomodoroMode.Contains(point)) { TimerService.SetMode(TimerService.TimerMode.Pomodoro); return true; }
            if (_watchMode.Contains(point)) { TimerService.SetMode(TimerService.TimerMode.Stopwatch); return true; }
            for (int i = 0; i < Presets.Length; i++)
            {
                if (_presets[i].Contains(point)) { TimerService.SetMinutes(Presets[i]); return true; }
            }
            return false;
        }

        public override bool OnWheel(PointF point, int delta)
        {
            if (!_ring.Contains(point) || TimerService.Mode == TimerService.TimerMode.Stopwatch) return false;
            TimerService.AddMinutes(delta > 0 ? 1 : -1);
            return true;
        }
    }

    /// <summary>
    /// Aviso al terminar un tiempo (mockup D1): se queda hasta hacer clic. En Pomodoro, el clic empieza la fase siguiente.
    /// </summary>
    internal class TimerAlertView : IslandView
    {
        public override SizeF BaseSize
        {
            get { return new SizeF(360, 62); }
        }

        private static bool BreakNext
        {
            get { return TimerService.Mode == TimerService.TimerMode.Pomodoro && TimerService.NextPhase != TimerService.Phase.Focus; }
        }

        public override Color? BorderColor
        {
            get { return Color.FromArgb(128, BreakNext || TimerService.Mode == TimerService.TimerMode.Timer ? TimeColors.Break : TimeColors.Focus); }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            float x = p.Px(PadX);
            bool pomodoro = TimerService.Mode == TimerService.TimerMode.Pomodoro;
            Color main = BreakNext || !pomodoro ? TimeColors.BreakLight : TimeColors.FocusLight;
            Color baseColor = BreakNext || !pomodoro ? TimeColors.Break : TimeColors.Focus;

            RectangleF badge = new RectangleF(x, (size.Height - p.Px(34)) / 2, p.Px(34), p.Px(34));
            IslandPainter.FillRounded(g, badge, p.Px(10), IslandPainter.Fade(Color.FromArgb(46, baseColor), alpha));
            p.DrawText(g, !pomodoro ? "⏰" : BreakNext ? "☕" : "🎯", p.EmojiFont, Color.FromArgb(alpha, IslandPainter.InkBase), badge, StringAlignment.Center);

            string title, detail;
            if (!pomodoro)
            {
                title = "¡Tiempo!";
                detail = string.Format("Temporizador de {0:0} min terminado", TimerService.Duration.TotalMinutes);
            }
            else if (BreakNext)
            {
                title = string.Format("¡Descanso! {0:0} min", TimerService.PhaseDuration(TimerService.NextPhase).TotalMinutes);
                detail = string.Format("Enfoque {0} de {1} completado · clic para empezar", TimerService.Cycle, TimerService.CyclesPerLongBreak);
            }
            else
            {
                title = string.Format("A enfocarse: {0:0} min", TimerService.PhaseDuration(TimerService.Phase.Focus).TotalMinutes);
                detail = "Descanso terminado · clic para empezar";
            }

            float textX = badge.Right + p.Px(14);
            p.DrawText(g, title, p.Bold15, IslandPainter.Fade(main, alpha), new RectangleF(textX, p.Px(10), size.Width - textX - x, p.Px(22)), StringAlignment.Near);
            p.DrawText(g, detail, p.Text12, IslandPainter.Fade(IslandPainter.Gray, alpha), new RectangleF(textX, p.Px(32), size.Width - textX - x, p.Px(18)), StringAlignment.Near);
        }

        public override bool OnClick(PointF point)
        {
            if (TimerService.Mode == TimerService.TimerMode.Pomodoro) TimerService.StartNextPhase();
            else TimerService.Reset();
            return true;
        }
    }

    /// <summary>
    /// Pestaña Agenda (mockup D2): eventos de hoy y mañana con el color de su calendario; el siguiente resaltado y
    /// botón «Unirse» si tiene enlace de reunión. La rueda pasa al resto.
    /// </summary>
    internal class AgendaView : IslandView
    {
        private const int VisibleEvents = 3;
        private const float RowHeight = 46, HeaderHeight = 22;
        private static readonly CultureInfo Spanish = new CultureInfo("es-ES");

        private int _offset;
        private readonly List<KeyValuePair<RectangleF, AgendaEvent>> _rows = new List<KeyValuePair<RectangleF, AgendaEvent>>();
        private readonly List<KeyValuePair<RectangleF, AgendaEvent>> _joins = new List<KeyValuePair<RectangleF, AgendaEvent>>();

        public override string TabName { get { return "Agenda"; } }
        public override string TabGlyph { get { return ""; } }

        public override bool IsAvailable
        {
            get { return SuiteSettings.IslandShowAgenda && AgendaService.HasSource; }
        }

        public override void OnShown()
        {
            _offset = 0;
        }

        private static List<AgendaEvent> Upcoming()
        {
            DateTime now = DateTime.Now;
            return AgendaService.Events.FindAll(e => e.End > now);
        }

        // Filas visibles con su encabezado de día delante cuando cambia el día
        private List<object> Layout(List<AgendaEvent> events)
        {
            List<object> items = new List<object>();
            DateTime? day = null;
            for (int i = _offset; i < events.Count && i < _offset + VisibleEvents; i++)
            {
                DateTime eventDay = events[i].Start < DateTime.Today ? DateTime.Today : events[i].Start.Date;
                if (day != eventDay) { items.Add(eventDay); day = eventDay; }
                items.Add(events[i]);
            }
            return items;
        }

        public override SizeF BaseSize
        {
            get
            {
                List<AgendaEvent> events = Upcoming();
                if (events.Count == 0) return new SizeF(440, 56);
                _offset = Math.Max(0, Math.Min(_offset, events.Count - VisibleEvents));
                float height = 0;
                foreach (object item in Layout(events)) height += item is DateTime ? HeaderHeight : RowHeight;
                return new SizeF(440, height);
            }
        }

        public override bool IsAnimated
        {
            get { return false; } // «en 12 min» cambia cada minuto: basta con el repintado de cada segundo
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            float x = p.Px(PadX), width = size.Width - 2 * x;
            Color white = Color.FromArgb(alpha, IslandPainter.InkBase);
            Color gray = IslandPainter.Fade(IslandPainter.Gray, alpha);
            Color legend = IslandPainter.Fade(Color.FromArgb(139, 147, 163), alpha);
            _rows.Clear();
            _joins.Clear();

            List<AgendaEvent> events = Upcoming();
            if (events.Count == 0)
            {
                p.DrawText(g, "Nada más en tu agenda hoy ni mañana", p.Text13, gray, new RectangleF(x, 0, width, p.Px(40)), StringAlignment.Center);
                return;
            }

            AgendaEvent next = events.Find(e => !e.AllDay);
            float y = 0;
            foreach (object item in Layout(events))
            {
                if (item is DateTime)
                {
                    DateTime day = (DateTime)item;
                    string label = day == DateTime.Today ? "HOY · " + day.ToString("dddd d", Spanish).ToUpper(Spanish)
                        : day == DateTime.Today.AddDays(1) ? "MAÑANA" : day.ToString("dddd d", Spanish).ToUpper(Spanish);
                    p.DrawText(g, label, p.Label11, legend, new RectangleF(x, y, width, p.Px(18)), StringAlignment.Near);
                    y += p.Px(HeaderHeight);
                    continue;
                }

                AgendaEvent e = (AgendaEvent)item;
                RectangleF row = new RectangleF(x, y, width, p.Px(RowHeight - 6));
                if (e == next) IslandPainter.FillRounded(g, row, p.Px(12), Color.FromArgb(alpha * 26 / 255, IslandPainter.Sky));
                IslandPainter.FillRounded(g, new RectangleF(row.X + p.Px(10), row.Y + p.Px(7), p.Px(4), row.Height - p.Px(14)), p.Px(2), IslandPainter.Fade(e.Color, alpha));

                float timeX = row.X + p.Px(24);
                if (e.AllDay)
                {
                    p.DrawText(g, "Todo el día", p.Bold12, white, new RectangleF(timeX, row.Y, p.Px(80), row.Height), StringAlignment.Near);
                }
                else
                {
                    p.DrawText(g, TimeText.Short(e.Start), p.Bold12, white, new RectangleF(timeX, row.Y + p.Px(3), p.Px(80), p.Px(18)), StringAlignment.Near);
                    p.DrawText(g, AgendaService.WhenText(e), e == next ? p.Bold12 : p.Text11, e == next ? IslandPainter.Fade(TimeColors.Link, alpha) : legend,
                        new RectangleF(timeX, row.Y + p.Px(20), p.Px(80), p.Px(16)), StringAlignment.Near);
                }

                float titleX = timeX + p.Px(84);
                float joinWidth = 0;
                if (e.JoinUrl != null)
                {
                    joinWidth = g.MeasureString("Unirse", p.Bold12).Width + p.Px(16);
                    RectangleF join = new RectangleF(row.Right - p.Px(10) - joinWidth, row.Y + (row.Height - p.Px(24)) / 2, joinWidth, p.Px(24));
                    IslandPainter.FillRounded(g, join, p.Px(8), IslandPainter.Fade(TimeColors.Link, alpha));
                    p.DrawText(g, "Unirse", p.Bold12, IslandPainter.Fade(IslandPainter.InkContrast, alpha), join, StringAlignment.Center);
                    _joins.Add(new KeyValuePair<RectangleF, AgendaEvent>(join, e));
                    joinWidth += p.Px(14);
                }
                float titleWidth = row.Right - p.Px(10) - joinWidth - titleX;
                bool hasLocation = !string.IsNullOrEmpty(e.Location);
                p.DrawText(g, e.Title, p.Semibold13, white, new RectangleF(titleX, row.Y + (hasLocation ? p.Px(3) : 0), titleWidth, hasLocation ? p.Px(18) : row.Height), StringAlignment.Near);
                if (hasLocation) p.DrawText(g, e.Location, p.Text12, gray, new RectangleF(titleX, row.Y + p.Px(20), titleWidth, p.Px(16)), StringAlignment.Near);
                _rows.Add(new KeyValuePair<RectangleF, AgendaEvent>(row, e));
                y += p.Px(RowHeight);
            }
        }

        public override bool OnClick(PointF point)
        {
            foreach (KeyValuePair<RectangleF, AgendaEvent> join in _joins)
            {
                if (join.Key.Contains(point)) { AgendaService.Open(join.Value, true); return true; }
            }
            foreach (KeyValuePair<RectangleF, AgendaEvent> row in _rows)
            {
                if (row.Key.Contains(point)) { AgendaService.Open(row.Value, false); return true; }
            }
            return false;
        }

        public override bool OnWheel(PointF point, int delta)
        {
            int count = Upcoming().Count;
            int next = Math.Max(0, Math.Min(count - VisibleEvents, _offset + (delta < 0 ? 1 : -1)));
            if (next == _offset) return false;
            _offset = next;
            return true;
        }
    }

    /// <summary>
    /// Aviso unos minutos antes de un evento (mockup D2), con «Unirse» si tiene enlace de reunión.
    /// </summary>
    internal class EventAlertView : IslandView
    {
        private const string GlyphCalendar = "";
        public AgendaEvent Event;
        private RectangleF _join;

        public override SizeF BaseSize
        {
            get { return new SizeF(400, 62); }
        }

        public override Color? BorderColor
        {
            get { return Color.FromArgb(128, IslandPainter.Sky); }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            AgendaEvent e = Event;
            if (e == null) return;
            float x = p.Px(PadX);
            RectangleF badge = new RectangleF(x, (size.Height - p.Px(34)) / 2, p.Px(34), p.Px(34));
            IslandPainter.FillRounded(g, badge, p.Px(10), IslandPainter.Fade(Color.FromArgb(46, IslandPainter.Sky), alpha));
            p.DrawText(g, GlyphCalendar, p.Glyph16, IslandPainter.Fade(TimeColors.Link, alpha), badge, StringAlignment.Center);

            float right = size.Width - x;
            _join = RectangleF.Empty;
            if (e.JoinUrl != null)
            {
                float joinWidth = g.MeasureString("Unirse", p.Bold12).Width + p.Px(16);
                _join = new RectangleF(right - joinWidth, (size.Height - p.Px(26)) / 2, joinWidth, p.Px(26));
                IslandPainter.FillRounded(g, _join, p.Px(8), IslandPainter.Fade(TimeColors.Link, alpha));
                p.DrawText(g, "Unirse", p.Bold12, IslandPainter.Fade(IslandPainter.InkContrast, alpha), _join, StringAlignment.Center);
                right = _join.X - p.Px(12);
            }

            float textX = badge.Right + p.Px(14);
            string detail = AgendaService.WhenText(e) + " · " + TimeText.Short(e.Start) + (string.IsNullOrEmpty(e.Location) ? "" : " · " + e.Location);
            detail = detail.Substring(0, 1).ToUpperInvariant() + detail.Substring(1);
            p.DrawText(g, e.Title, p.Bold15, Color.FromArgb(alpha, IslandPainter.InkBase), new RectangleF(textX, p.Px(10), right - textX, p.Px(22)), StringAlignment.Near);
            p.DrawText(g, detail, p.Text12, IslandPainter.Fade(IslandPainter.Gray, alpha), new RectangleF(textX, p.Px(32), right - textX, p.Px(18)), StringAlignment.Near);
        }

        public override bool OnClick(PointF point)
        {
            if (Event != null) AgendaService.Open(Event, _join.Contains(point));
            return true;
        }
    }
}
