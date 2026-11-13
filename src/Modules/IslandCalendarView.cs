using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Calendario (fase J4, mockup N2, como Bloom + Nusa): el mes con el día de hoy marcado y un puntito en los días con
    /// eventos de tu agenda; a la derecha el Pomodoro (clic en el anillo empieza o pausa), los eventos de hoy y tus tareas
    /// con casilla. Las tareas se agregan hablándole al asistente.
    /// </summary>
    internal class CalendarView : IslandView
    {
        private static readonly CultureInfo Spanish = new CultureInfo("es-MX");
        private static readonly Color Blue = Color.FromArgb(10, 132, 255);
        private static readonly Color Orange = Color.FromArgb(249, 115, 22);
        private static readonly Color EventDot = Color.FromArgb(251, 191, 36);

        private RectangleF _ring;
        private readonly List<KeyValuePair<RectangleF, TasksService.TaskItem>> _taskHits = new List<KeyValuePair<RectangleF, TasksService.TaskItem>>();
        private readonly List<KeyValuePair<RectangleF, AgendaEvent>> _eventHits = new List<KeyValuePair<RectangleF, AgendaEvent>>();

        public override string TabName
        {
            get { return "Calendario"; }
        }

        public override string TabGlyph
        {
            get { return ""; }
        }

        public override SizeF BaseSize
        {
            get { return new SizeF(520, 188); }
        }

        public override float PanelWidth
        {
            get { return 520; }
        }

        public override bool IsAnimated
        {
            get { return TimerService.IsRunning; } // El anillo del Pomodoro avanza
        }

        public override void OnShown()
        {
            AgendaService.EnsureStarted();
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            float x = p.Px(PadX);
            float monthWidth = p.Px(250);
            DrawMonth(g, new RectangleF(x, 0, monthWidth, size.Height), alpha, p);
            float right = x + monthWidth + p.Px(20);
            DrawSide(g, new RectangleF(right, 0, size.Width - right - p.Px(PadX), size.Height), alpha, p);
        }

        private void DrawMonth(Graphics g, RectangleF area, int alpha, IslandPainter p)
        {
            DateTime today = DateTime.Today;
            Color white = Color.FromArgb(alpha, IslandPainter.InkBase), gray = IslandPainter.Fade(IslandPainter.Gray, alpha);
            string title = Spanish.TextInfo.ToTitleCase(today.ToString("MMMM yyyy", Spanish));
            p.DrawText(g, title, p.Bold15, white, new RectangleF(area.X, 0, area.Width, p.Px(22)), StringAlignment.Near);

            float cell = area.Width / 7, rowHeight = p.Px(23);
            string[] letters = { "L", "M", "M", "J", "V", "S", "D" };
            for (int i = 0; i < 7; i++) p.DrawText(g, letters[i], p.Text11, gray, new RectangleF(area.X + i * cell, p.Px(26), cell, p.Px(16)), StringAlignment.Center);

            // Días con eventos (de las fuentes de la agenda)
            HashSet<int> withEvents = new HashSet<int>();
            foreach (AgendaEvent e in AgendaService.Events) if (e.Start.Year == today.Year && e.Start.Month == today.Month) withEvents.Add(e.Start.Day);

            DateTime first = new DateTime(today.Year, today.Month, 1);
            int offset = ((int)first.DayOfWeek + 6) % 7; // Lunes primero
            int days = DateTime.DaysInMonth(today.Year, today.Month);
            for (int day = 1; day <= days; day++)
            {
                int index = offset + day - 1;
                RectangleF box = new RectangleF(area.X + (index % 7) * cell, p.Px(44) + (index / 7) * rowHeight, cell, rowHeight);
                bool isToday = day == today.Day;
                if (isToday)
                {
                    float d = Math.Min(cell, rowHeight) - p.Px(2);
                    IslandPainter.FillRounded(g, new RectangleF(box.X + (box.Width - d) / 2, box.Y + (box.Height - d) / 2, d, d), d / 2, Color.FromArgb(alpha, Blue));
                }
                p.DrawText(g, day.ToString(), isToday ? p.Bold12 : p.Text12, day < today.Day ? gray : white, box, StringAlignment.Center);
                if (withEvents.Contains(day) && !isToday)
                {
                    float dot = p.Px(4);
                    IslandPainter.FillRounded(g, new RectangleF(box.X + (box.Width - dot) / 2, box.Bottom - dot - p.Px(1), dot, dot), dot / 2, Color.FromArgb(alpha, EventDot));
                }
            }
        }

        private void DrawSide(Graphics g, RectangleF area, int alpha, IslandPainter p)
        {
            Color white = Color.FromArgb(alpha, IslandPainter.InkBase), gray = IslandPainter.Fade(IslandPainter.Gray, alpha);
            _taskHits.Clear();
            _eventHits.Clear();

            // Pomodoro: anillo con lo que falta; clic empieza o pausa
            float ringSize = p.Px(46);
            _ring = new RectangleF(area.X, 0, ringSize, ringSize);
            using (Pen track = new Pen(Color.FromArgb(alpha * 30 / 255, IslandPainter.InkBase), p.Px(4)))
            using (Pen done = new Pen(Color.FromArgb(alpha, Orange), p.Px(4)))
            {
                RectangleF ring = RectangleF.Inflate(_ring, -p.Px(2), -p.Px(2));
                g.DrawEllipse(track, ring);
                float progress = TimerService.IsActive ? TimerService.Progress : 0;
                if (progress > 0) g.DrawArc(done, ring, -90, 360 * progress);
            }
            TimeSpan remaining = TimerService.IsActive ? TimerService.Shown : TimerService.PhaseDuration(TimerService.CurrentPhase);
            p.DrawText(g, TimerService.Format(remaining), p.Bold12, white, _ring, StringAlignment.Center);
            p.DrawText(g, "Pomodoro", p.Semibold13, white, new RectangleF(_ring.Right + p.Px(10), p.Px(4), area.Width - ringSize - p.Px(10), p.Px(18)), StringAlignment.Near);
            p.DrawText(g, TimerService.IsRunning ? "En marcha · clic para pausar" : "Clic en el anillo para empezar", p.Text11, gray,
                new RectangleF(_ring.Right + p.Px(10), p.Px(24), area.Width - ringSize - p.Px(10), p.Px(16)), StringAlignment.Near);

            // Hoy: próximos eventos de la agenda
            float y = ringSize + p.Px(10);
            p.DrawText(g, "HOY", p.Label11, gray, new RectangleF(area.X, y, area.Width, p.Px(14)), StringAlignment.Near);
            y += p.Px(16);
            int shown = 0;
            foreach (AgendaEvent e in AgendaService.Events)
            {
                if (e.Start.Date != DateTime.Today || (e.End < DateTime.Now && !e.AllDay)) continue;
                RectangleF row = new RectangleF(area.X, y, area.Width, p.Px(17));
                string when = e.AllDay ? "Todo el día" : StreeckSuite.Core.TimeText.Short(e.Start);
                p.DrawText(g, when + " · " + e.Title, p.Text12, white, row, StringAlignment.Near);
                _eventHits.Add(new KeyValuePair<RectangleF, AgendaEvent>(row, e));
                y += p.Px(17);
                if (++shown == 2) break;
            }
            if (shown == 0) { p.DrawText(g, "Sin eventos", p.Text12, gray, new RectangleF(area.X, y, area.Width, p.Px(17)), StringAlignment.Near); y += p.Px(17); }

            // Tareas con casilla
            y += p.Px(6);
            List<TasksService.TaskItem> tasks = TasksService.Tasks;
            if (tasks.Count == 0)
            {
                p.DrawText(g, "Dile al asistente: «recuérdame … a las 6»", p.Text11, gray, new RectangleF(area.X, y, area.Width, p.Px(16)), StringAlignment.Near);
                return;
            }
            for (int i = 0; i < Math.Min(3, tasks.Count); i++)
            {
                TasksService.TaskItem task = tasks[i];
                RectangleF row = new RectangleF(area.X, y, area.Width, p.Px(19));
                RectangleF box = new RectangleF(row.X, row.Y + p.Px(3), p.Px(13), p.Px(13));
                if (task.Done)
                {
                    IslandPainter.FillRounded(g, box, p.Px(3), Color.FromArgb(alpha, Blue));
                    p.DrawText(g, "", p.Text11, white, box, StringAlignment.Center);
                }
                else
                {
                    using (Pen edge = new Pen(Color.FromArgb(alpha * 150 / 255, IslandPainter.InkBase), 1)) g.DrawRectangle(edge, box.X, box.Y, box.Width, box.Height);
                }
                string when = TasksService.TimeText(task);
                bool late = !task.Done && task.Due != null && task.Due.Value < DateTime.Now;
                p.DrawText(g, task.Text + (when.Length > 0 ? " · " + when : ""), p.Text12, task.Done ? gray : late ? Color.FromArgb(alpha, 253, 186, 116) : white,
                    new RectangleF(box.Right + p.Px(7), row.Y, row.Width - p.Px(20), row.Height), StringAlignment.Near);
                _taskHits.Add(new KeyValuePair<RectangleF, TasksService.TaskItem>(row, task));
                y += p.Px(20);
            }
        }

        public override bool OnClick(PointF point)
        {
            if (_ring.Contains(point)) { TimerService.Toggle(); return true; }
            foreach (KeyValuePair<RectangleF, TasksService.TaskItem> hit in _taskHits)
            {
                if (hit.Key.Contains(point)) { TasksService.Toggle(hit.Value); return true; }
            }
            foreach (KeyValuePair<RectangleF, AgendaEvent> hit in _eventHits)
            {
                if (hit.Key.Contains(point)) { AgendaService.Open(hit.Value, false); return true; }
            }
            return false;
        }
    }
}
