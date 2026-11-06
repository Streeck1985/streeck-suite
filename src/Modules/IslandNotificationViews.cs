using System;
using System.Collections.Generic;
using System.Drawing;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Piezas comunes de las vistas de avisos: icono de la app y hora relativa.
    /// </summary>
    internal static class NotificationDrawing
    {
        public static void DrawAppIcon(Graphics g, IslandPainter p, IslandNotification n, RectangleF rect, float radius, int alpha)
        {
            if (n.Icon != null)
            {
                p.DrawRoundedImage(g, n.Icon, rect, radius, alpha);
                return;
            }
            // Sin icono: cuadro del color de la app con su inicial
            IslandPainter.FillRounded(g, rect, radius, IslandPainter.Fade(n.Accent, alpha));
            string initial = string.IsNullOrEmpty(n.AppName) ? "?" : n.AppName.Substring(0, 1).ToUpperInvariant();
            p.DrawText(g, initial, p.Bold15, Color.FromArgb(alpha, Color.White), rect, StringAlignment.Center);
        }

        public static string RelativeTime(DateTime time)
        {
            TimeSpan age = DateTime.Now - time;
            if (age.TotalMinutes < 1) return "ahora";
            if (age.TotalMinutes < 60) return string.Format("{0:0} min", Math.Floor(age.TotalMinutes));
            if (age.TotalHours < 24) return string.Format("{0:0} h", Math.Floor(age.TotalHours));
            return time.ToString("dd/MM");
        }
    }

    /// <summary>
    /// Aviso de una notificación (mockup C1): icono real de la app, nombre y hora, título, texto en dos líneas
    /// y «+N» si hay más esperando. Borde con el color de la app. Clic: abre la app.
    /// </summary>
    internal class NotificationAlertView : IslandView
    {
        public IslandNotification Notification;
        public int Pending;   // Avisos en cola detrás de este

        // Aviso más pequeño y nítido (mockup «Aviso más nítido»): 360 de ancho, icono de 32 y letras más finas.
        // Texto: 360 - márgenes (18 + 18) - icono (32) - separación (12) - hueco para «+N»
        private const float Width = 360, IconSize = 32;
        private const float BodyWidth = Width - 36 - IconSize - 12 - 40;
        private static readonly Font MeasureFont = new Font("Segoe UI", 9f, FontStyle.Regular);
        private IslandNotification _measured;
        private int _bodyLines;

        // Alto según el texto: sin cuerpo 54, una línea 70, dos líneas 86
        public override SizeF BaseSize
        {
            get { return new SizeF(Width, 54 + 16 * BodyLines); }
        }

        private int BodyLines
        {
            get
            {
                if (Notification != _measured)
                {
                    _measured = Notification;
                    _bodyLines = 0;
                    if (Notification != null && !string.IsNullOrEmpty(Notification.Body))
                    {
                        using (Bitmap bitmap = new Bitmap(1, 1))
                        using (Graphics g = Graphics.FromImage(bitmap))
                        {
                            float height = g.MeasureString(Notification.Body, MeasureFont, (int)BodyWidth).Height;
                            _bodyLines = height > MeasureFont.GetHeight(g) * 1.5f ? 2 : 1;
                        }
                    }
                }
                return _bodyLines;
            }
        }

        public override Color? BorderColor
        {
            get { return Notification != null ? Color.FromArgb(115, Notification.Accent) : (Color?)null; }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            IslandNotification n = Notification;
            if (n == null) return;
            float x = p.Px(PadX);
            Color white = Color.FromArgb(alpha, Color.White);
            Color gray = IslandPainter.Fade(IslandPainter.Gray, alpha);

            RectangleF icon = new RectangleF(x, (size.Height - p.Px(IconSize)) / 2, p.Px(IconSize), p.Px(IconSize)); // Centrado en vertical
            NotificationDrawing.DrawAppIcon(g, p, n, icon, p.Px(10), alpha);

            float badgeWidth = 0;
            if (Pending > 0)
            {
                string badgeText = "+" + Pending;
                badgeWidth = g.MeasureString(badgeText, p.Bold12).Width + p.Px(8);
                RectangleF badge = new RectangleF(size.Width - x - badgeWidth, (size.Height - p.Px(18)) / 2, badgeWidth, p.Px(18));
                IslandPainter.FillRounded(g, badge, badge.Height / 2, gray);
                p.DrawText(g, badgeText, p.Bold12, IslandPainter.Fade(Color.FromArgb(11, 15, 23), alpha), badge, StringAlignment.Center);
                badgeWidth += p.Px(12);
            }

            float textX = icon.Right + p.Px(12);
            float textWidth = size.Width - textX - x - badgeWidth;
            p.DrawText(g, n.AppName, p.Text11, gray, new RectangleF(textX, p.Px(8), textWidth - p.Px(50), p.Px(16)), StringAlignment.Near);
            p.DrawText(g, NotificationDrawing.RelativeTime(n.Time), p.Text11, gray, new RectangleF(textX, p.Px(8), textWidth, p.Px(16)), StringAlignment.Far);
            p.DrawText(g, n.Title, p.Semibold13, white, new RectangleF(textX, p.Px(24), textWidth, p.Px(20)), StringAlignment.Near);

            using (StringFormat wrap = new StringFormat())
            using (SolidBrush brush = new SolidBrush(IslandPainter.Fade(Color.FromArgb(195, 201, 212), alpha)))
            {
                wrap.Trimming = StringTrimming.EllipsisWord;
                g.DrawString(n.Body ?? "", p.Text12, brush, new RectangleF(textX, p.Px(43), textWidth, p.Px(16) * BodyLines + p.Px(2)), wrap);
            }
        }

        public override bool OnClick(PointF point)
        {
            if (Notification != null) NotificationService.OpenApp(Notification);
            return true;
        }
    }

    /// <summary>
    /// Pestaña Avisos (mockup C1): los avisos recientes de 3 en 3 (la rueda pasa al resto), clic para abrir la app
    /// y «Borrar todo», que los quita de la isla pero no de Windows.
    /// </summary>
    internal class NotificationsView : IslandView
    {
        private const int VisibleRows = 3;
        private const float RowHeight = 44;
        private const float FooterHeight = 24;

        private int _offset;
        private readonly List<KeyValuePair<RectangleF, IslandNotification>> _rows = new List<KeyValuePair<RectangleF, IslandNotification>>();
        private RectangleF _clearButton;

        public override string TabName
        {
            get { return "Avisos"; }
        }

        public override string TabGlyph
        {
            get { return ""; }
        }

        public override bool IsAvailable
        {
            get { return SuiteSettings.NotificationsEnabled; }
        }

        public override SizeF BaseSize
        {
            get
            {
                int count = Math.Min(VisibleRows, NotificationService.History.Count);
                return count == 0 ? new SizeF(440, 56) : new SizeF(440, count * RowHeight + FooterHeight);
            }
        }

        public override void OnShown()
        {
            _offset = 0;
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            List<IslandNotification> all = NotificationService.History;
            float x = p.Px(PadX), width = size.Width - 2 * x;
            Color white = Color.FromArgb(alpha, Color.White);
            Color gray = IslandPainter.Fade(IslandPainter.Gray, alpha);
            Color legend = IslandPainter.Fade(Color.FromArgb(139, 147, 163), alpha);
            _rows.Clear();
            _clearButton = RectangleF.Empty;

            if (all.Count == 0)
            {
                p.DrawText(g, "Sin avisos nuevos", p.Text13, gray, new RectangleF(x, 0, width, p.Px(40)), StringAlignment.Center);
                return;
            }

            _offset = Math.Max(0, Math.Min(_offset, all.Count - VisibleRows));
            for (int i = 0; i < VisibleRows && _offset + i < all.Count; i++)
            {
                IslandNotification n = all[_offset + i];
                float y = i * p.Px(RowHeight);
                RectangleF icon = new RectangleF(x, y, p.Px(32), p.Px(32));
                NotificationDrawing.DrawAppIcon(g, p, n, icon, p.Px(10), alpha);

                float textX = icon.Right + p.Px(12);
                float timeWidth = p.Px(44);
                float textWidth = x + width - textX - timeWidth;
                p.DrawText(g, n.Title, p.Semibold13, white, new RectangleF(textX, y - p.Px(1), textWidth, p.Px(18)), StringAlignment.Near);
                p.DrawText(g, string.IsNullOrEmpty(n.Body) ? n.AppName : n.Body, p.Text12, gray,
                    new RectangleF(textX, y + p.Px(16), textWidth, p.Px(17)), StringAlignment.Near);
                p.DrawText(g, NotificationDrawing.RelativeTime(n.Time), p.Text11, legend,
                    new RectangleF(x + width - timeWidth, y, timeWidth, p.Px(32)), StringAlignment.Far);
                _rows.Add(new KeyValuePair<RectangleF, IslandNotification>(new RectangleF(x, y, width, p.Px(32)), n));
            }

            float footerY = Math.Min(VisibleRows, all.Count) * p.Px(RowHeight) - p.Px(4);
            if (all.Count > VisibleRows)
            {
                string position = string.Format("{0}-{1} de {2} · rueda para ver más", _offset + 1, Math.Min(_offset + VisibleRows, all.Count), all.Count);
                p.DrawText(g, position, p.Text12, legend, new RectangleF(x, footerY, width - p.Px(100), p.Px(20)), StringAlignment.Near);
            }
            SizeF clearSize = g.MeasureString("Borrar todo", p.Bold12);
            _clearButton = new RectangleF(x + width - clearSize.Width, footerY, clearSize.Width, p.Px(20));
            p.DrawText(g, "Borrar todo", p.Bold12, IslandPainter.Fade(Color.FromArgb(125, 211, 252), alpha), _clearButton, StringAlignment.Far);
        }

        public override bool OnClick(PointF point)
        {
            if (_clearButton.Contains(point))
            {
                NotificationService.ClearHistory();
                _offset = 0;
                return true;
            }
            foreach (KeyValuePair<RectangleF, IslandNotification> row in _rows)
            {
                if (row.Key.Contains(point))
                {
                    NotificationService.OpenApp(row.Value);
                    return true;
                }
            }
            return false;
        }

        public override bool OnWheel(PointF point, int delta)
        {
            int count = NotificationService.History.Count;
            int next = Math.Max(0, Math.Min(count - VisibleRows, _offset + (delta < 0 ? 1 : -1)));
            if (next == _offset) return false; // En el borde: la rueda cambia de pestaña
            _offset = next;
            return true;
        }
    }

    /// <summary>
    /// Aviso de 3 s cuando una app empieza a usar el micrófono (naranja) o la cámara (verde). Mockup C2.
    /// </summary>
    internal class CaptureAlertView : IslandView
    {
        private const string GlyphMicrophone = "";
        private const string GlyphCamera = "";

        public CaptureMonitor.Device Device;
        public string AppName = "";

        private Color Main
        {
            get { return Device == CaptureMonitor.Device.Microphone ? IslandPainter.Orange : Color.FromArgb(134, 239, 172); }
        }

        private Color Base
        {
            get { return Device == CaptureMonitor.Device.Microphone ? Color.FromArgb(251, 146, 60) : Color.FromArgb(34, 197, 94); }
        }

        public override SizeF BaseSize
        {
            get { return new SizeF(360, 62); }
        }

        public override Color? BorderColor
        {
            get { return Color.FromArgb(128, Base); }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            float x = p.Px(PadX);
            RectangleF badge = new RectangleF(x, (size.Height - p.Px(34)) / 2, p.Px(34), p.Px(34));
            IslandPainter.FillRounded(g, badge, p.Px(10), IslandPainter.Fade(Color.FromArgb(46, Base), alpha));
            p.DrawText(g, Device == CaptureMonitor.Device.Microphone ? GlyphMicrophone : GlyphCamera, p.Glyph16,
                IslandPainter.Fade(Main, alpha), badge, StringAlignment.Center);

            float textX = badge.Right + p.Px(14);
            string title = Device == CaptureMonitor.Device.Microphone ? "Micrófono en uso" : "Cámara en uso";
            p.DrawText(g, title, p.Bold15, IslandPainter.Fade(Main, alpha), new RectangleF(textX, p.Px(10), size.Width - textX - x, p.Px(22)), StringAlignment.Near);
            p.DrawText(g, AppName, p.Text12, IslandPainter.Fade(IslandPainter.Gray, alpha),
                new RectangleF(textX, p.Px(32), size.Width - textX - x, p.Px(18)), StringAlignment.Near);
        }
    }
}
