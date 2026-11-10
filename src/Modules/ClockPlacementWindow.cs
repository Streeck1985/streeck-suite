using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// «Mover reloj»: pantalla completa con el fondo y el reloj en vivo. Se arrastra a cualquier sitio (por ejemplo
    /// sobre la estela de la nube), la rueda cambia el tamaño, Enter o «Listo» guarda y Esc cancela.
    /// </summary>
    internal class ClockPlacementWindow : Form
    {
        private static ClockPlacementWindow _instance;

        private readonly DepthClockModule _clock;
        private readonly DepthClockLayers _layers;
        private readonly ClockSettings _settings;
        private readonly Timer _tick;
        private Point _dragStart;
        private float _startX, _startY;
        private bool _dragging;
        private Rectangle _done, _cancel;

        public static void Open()
        {
            DepthClockModule clock = DepthClockModule.Current;
            if (clock == null || clock.Layers == null)
            {
                MessageBox.Show("El reloj aún está analizando tu fondo. Inténtalo en unos segundos.", "Streeck Suite");
                return;
            }
            if (_instance != null && !_instance.IsDisposed) { _instance.Activate(); return; }
            _instance = new ClockPlacementWindow(clock);
            _instance.Show();
            _instance.Activate();
        }

        private ClockPlacementWindow(DepthClockModule clock)
        {
            _clock = clock;
            _layers = clock.Layers;
            _settings = clock.CurrentSettings;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Screen targetScreen = DepthClockModule.GetTargetScreen(_settings);
            Bounds = (targetScreen != null ? targetScreen : Screen.PrimaryScreen).Bounds;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.SizeAll;

            _tick = new Timer { Interval = 1000 };
            _tick.Tick += (s, e) => Invalidate();
            _tick.Start();

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += (s, e) => _dragging = false;
            MouseWheel += (s, e) =>
            {
                _settings.Size = Math.Max(0.10f, Math.Min(0.80f, _settings.Size + (e.Delta > 0 ? 0.01f : -0.01f)));
                Invalidate();
            };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) Save();
                else if (e.KeyCode == Keys.Escape) Close();
            };
        }

        private void OnMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (_done.Contains(e.Location)) { Save(); return; }
            if (_cancel.Contains(e.Location)) { Close(); return; }
            _dragging = true;
            _dragStart = e.Location;
            _startX = _settings.CenterX;
            _startY = _settings.Top;
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            Cursor = _done.Contains(e.Location) || _cancel.Contains(e.Location) ? Cursors.Hand : Cursors.SizeAll;
            if (!_dragging) return;
            _settings.CenterX = Math.Max(0f, Math.Min(1f, _startX + (e.X - _dragStart.X) / (float)ClientSize.Width));
            _settings.Top = Math.Max(0f, Math.Min(0.9f, _startY + (e.Y - _dragStart.Y) / (float)ClientSize.Height));
            Invalidate();
        }

        private void Save()
        {
            _clock.ApplySettings(_settings.Clone());
            Close();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Size screen = ClientSize;
            try
            {
                DesktopClockForm.PaintClock(g, screen, _settings, _layers, _clock.Weather, DateTime.Now);
            }
            catch (Exception)
            {
                g.Clear(Color.Black);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            // Marco punteado alrededor de la hora
            RectangleF area = DepthClockLayout.DigitsArea(screen, _settings);
            RectangleF frame = new RectangleF(area.X * screen.Width, area.Y * screen.Height, area.Width * screen.Width, area.Height * screen.Height);
            frame.Inflate(24, 24);
            using (Pen dash = new Pen(Color.FromArgb(220, 56, 189, 248), 2f) { DashStyle = DashStyle.Dash })
            {
                g.DrawRectangle(dash, frame.X, frame.Y, frame.Width, frame.Height);
            }

            // Barra de ayuda arriba y botones abajo
            using (Font font = new Font("Segoe UI", 11f))
            using (Font bold = new Font("Segoe UI Semibold", 11f))
            using (SolidBrush panel = new SolidBrush(Color.FromArgb(215, 11, 15, 23)))
            using (SolidBrush text = new SolidBrush(Color.FromArgb(230, 233, 239)))
            {
                const string hint = "Arrastra el reloj a donde quieras  ·  Rueda del ratón: tamaño  ·  Enter guarda  ·  Esc cancela";
                SizeF size = g.MeasureString(hint, font);
                RectangleF bar = new RectangleF((screen.Width - size.Width) / 2 - 18, 18, size.Width + 36, size.Height + 16);
                using (GraphicsPath pill = StreeckSuite.Core.LayeredWindow.RoundedRect(bar, bar.Height / 2)) g.FillPath(panel, pill);
                g.DrawString(hint, font, text, bar.X + 18, bar.Y + 8);

                _done = new Rectangle(screen.Width / 2 + 8, screen.Height - 90, 120, 40);
                _cancel = new Rectangle(screen.Width / 2 - 128, screen.Height - 90, 120, 40);
                using (SolidBrush accent = new SolidBrush(Color.FromArgb(56, 189, 248)))
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(11, 15, 23)))
                using (StringFormat center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    using (GraphicsPath p = StreeckSuite.Core.LayeredWindow.RoundedRect(_cancel, 20)) g.FillPath(panel, p);
                    g.DrawString("Cancelar", bold, text, _cancel, center);
                    using (GraphicsPath p = StreeckSuite.Core.LayeredWindow.RoundedRect(_done, 20)) g.FillPath(accent, p);
                    g.DrawString("Listo", bold, dark, _done, center);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _tick.Dispose();
            base.Dispose(disposing);
        }
    }
}
