using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Panel de ajustes de la suite (estilo negro de los mockups P1, P3, P4 y P5): menú lateral y cuatro páginas.
    /// Todo se dibuja a mano a 96 PPP y se escala a la pantalla; cada cambio se aplica al momento.
    /// </summary>
    internal class SettingsWindow : Form
    {
        public enum Page { Privacy, Island, Appearance, Tools, Notifications, Time, Tray, Assistant, Start, Taskbar, StartIcon, Widgets, Clock, Eyes, About }

        // Medidas del mockup (a 96 PPP)
        private const float W = 800, H = 808, SideW = 210, ContentX = 238, ContentW = 534;
        private const float ContentRight = ContentX + ContentW;
        private const float InnerX = ContentX + 16, InnerW = ContentW - 32;

        private static readonly Color Back = Color.FromArgb(11, 15, 23);
        private static readonly Color TextColor = Color.FromArgb(230, 233, 239);
        private static readonly Color Gray = Color.FromArgb(154, 164, 181);
        private static readonly Color Legend = Color.FromArgb(139, 147, 163);
        private static readonly Color Accent = Color.FromArgb(56, 189, 248);
        private static readonly Color ValueColor = Color.FromArgb(125, 211, 252);
        private static readonly Color TrackColor = Color.FromArgb(42, 48, 64);
        private static readonly Color ToggleOff = Color.FromArgb(58, 65, 80);
        private static readonly Color Field = Color.FromArgb(30, 36, 48);
        private static readonly Color ButtonText = Color.FromArgb(195, 201, 212);
        private static readonly Color CardFill = Color.FromArgb(13, 255, 255, 255);    // rgba(255,255,255,.05)
        private static readonly Color HoverFill = Color.FromArgb(15, 255, 255, 255);
        private static readonly Color NavActive = Color.FromArgb(31, 255, 255, 255);   // .12
        private static readonly Color ActiveFill = Color.FromArgb(36, 255, 255, 255);  // .14
        private static readonly Color Divider = Color.FromArgb(15, 255, 255, 255);     // .06
        private static readonly Color SideLine = Color.FromArgb(20, 255, 255, 255);    // .08
        private static readonly Color SoftBorder = Color.FromArgb(36, 255, 255, 255);  // .14
        private static readonly Color DashedBorder = Color.FromArgb(77, 255, 255, 255); // .3

        private static readonly Color[] TintPresets =
        {
            Color.FromArgb(5, 5, 5), Color.FromArgb(12, 18, 32), Color.FromArgb(44, 44, 44), Color.FromArgb(30, 16, 53)
        };
        private static readonly Color[] AccentPresets =
        {
            Color.FromArgb(0, 210, 255), Color.FromArgb(34, 197, 94), Color.FromArgb(167, 139, 250),
            Color.FromArgb(251, 146, 60), Color.FromArgb(244, 114, 182)
        };
        private static readonly string[] WidgetDescriptions = { "Temperatura, máxima, mínima y próximas horas", "Tus eventos de hoy", "Lo que suena, con botones", "CPU, RAM, GPU y disco", "Tu lista de notas.txt (clic para editar)", "Días que faltan para una fecha (clic para cambiarla)" };
        private static readonly CultureInfo Spanish = new CultureInfo("es-ES");

        private static SettingsWindow _instance;
        internal static bool ForceMultiMonitorPreview = false;
        internal static bool ForceMultiMonitorP2 = false;

        private class Hit
        {
            public RectangleF Rect;
            public Action Click;
            public RectangleF Track;      // Solo deslizadores
            public Action<float> Slide;   // Fracción 0..1 mientras se arrastra
            public Action Commit;         // Al soltar
        }

        private readonly float _scale;
        private readonly List<Hit> _hits = new List<Hit>();
        private readonly Font _title22, _bold15, _semi14, _semi13, _reg13, _bold13, _semi12, _reg12, _reg11, _bold14;
        private readonly Action<object> _privacyChanged;
        private readonly Action _clockChanged;
        private Page _page;
        private PointF _mouse = new PointF(-1, -1);
        private Hit _hovered, _dragging;

        /// <summary>
        /// Abre el panel (o lo trae al frente) en la página indicada.
        /// </summary>
        public static void Open(Page page)
        {
            if (_instance != null && !_instance.IsDisposed && _instance.InvokeRequired)
            {
                _instance.BeginInvoke((Action)(() => Open(page)));
                return;
            }
            if (_instance == null || _instance.IsDisposed) _instance = new SettingsWindow();
            _instance._page = page;
            if (!_instance.Visible) _instance.Show();
            if (_instance.WindowState == FormWindowState.Minimized) _instance.WindowState = FormWindowState.Normal;
            _instance.Activate();
            _instance.BringToFront();
            _instance.Invalidate();
        }

        private SettingsWindow()
        {
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) _scale = screen.DpiX / 96f;

            Text = "Streeck Suite · Ajustes";
            FormBorderStyle = FormBorderStyle.FixedSingle; // El marco se elimina en WM_NCCALCSIZE; conserva sombra y esquinas de Windows 11
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Back;
            DoubleBuffered = true;
            KeyPreview = true;
            Icon = TrayIconModule.LoadAppIcon(new Size(32, 32));

            _title22 = new Font("Segoe UI", 22, FontStyle.Bold, GraphicsUnit.Pixel);
            _bold15 = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            _bold14 = new Font("Segoe UI", 14, FontStyle.Bold, GraphicsUnit.Pixel);
            _bold13 = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel);
            _semi14 = new Font("Segoe UI Semibold", 14, FontStyle.Regular, GraphicsUnit.Pixel);
            _semi13 = new Font("Segoe UI Semibold", 13, FontStyle.Regular, GraphicsUnit.Pixel);
            _semi12 = new Font("Segoe UI Semibold", 12, FontStyle.Regular, GraphicsUnit.Pixel);
            _reg13 = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
            _reg12 = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel);
            _reg11 = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);

            // Lo que cambia fuera del panel (atajo de privacidad, análisis del fondo) se refleja al momento
            _privacyChanged = payload => InvalidateSafe();
            _clockChanged = InvalidateSafe;
            EventBus.Instance.Subscribe(Core.Events.PrivacyStateChanged, _privacyChanged);
            AgendaService.Updated += _clockChanged; // Estado de los calendarios en la página Tiempo y agenda
            UpdateService.Changed += _clockChanged; // Estado de la búsqueda de actualizaciones (Acerca de)
            if (DepthClockModule.Current != null) DepthClockModule.Current.LayersChanged += _clockChanged;
        }

        private void InvalidateSafe()
        {
            if (IsDisposed || !IsHandleCreated) return;
            if (InvokeRequired) BeginInvoke((Action)Invalidate); else Invalidate();
        }

        private int Px(float value)
        {
            return (int)Math.Round(value * _scale);
        }

        #region Ventana

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = 1;
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int corners = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
            int border = 45 | (49 << 8) | (56 << 16); // COLORREF del borde rgba(255,255,255,.14) sobre el fondo
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));

            // Recalcular el marco (queda en cero) y fijar el tamaño del mockup
            NativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_FRAMECHANGED);
            Size = new Size(Px(W), Px(H));
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCCALCSIZE = 0x83, WM_NCHITTEST = 0x84, HTCLIENT = 1, HTCAPTION = 2;
            if (m.Msg == WM_NCCALCSIZE && m.WParam != IntPtr.Zero)
            {
                m.Result = IntPtr.Zero; // Sin barra de título ni bordes: todo es área de dibujo
                return;
            }
            base.WndProc(ref m);

            // Arrastrar la ventana desde la franja superior o los huecos del menú lateral
            if (m.Msg == WM_NCHITTEST && m.Result.ToInt32() == HTCLIENT)
            {
                long lParam = m.LParam.ToInt64();
                Point client = PointToClient(new Point((short)(lParam & 0xFFFF), (short)((lParam >> 16) & 0xFFFF)));
                PointF p = new PointF(client.X / _scale, client.Y / _scale);
                if (HitAt(p) == null && (p.Y < 56 || p.X < SideW)) m.Result = new IntPtr(HTCAPTION);
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Escape) Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            EventBus.Instance.Unsubscribe(Core.Events.PrivacyStateChanged, _privacyChanged);
            AgendaService.Updated -= _clockChanged;
            UpdateService.Changed -= _clockChanged;
            if (DepthClockModule.Current != null) DepthClockModule.Current.LayersChanged -= _clockChanged;
            if (_instance == this) _instance = null;
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Font font in new[] { _title22, _bold15, _bold14, _bold13, _semi14, _semi13, _semi12, _reg13, _reg12, _reg11 }) font.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion

        #region Ratón

        private PointF Logical(Point p)
        {
            return new PointF(p.X / _scale, p.Y / _scale);
        }

        private Hit HitAt(PointF p)
        {
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                if (_hits[i].Rect.Contains(p)) return _hits[i];
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mouse = Logical(e.Location);
            if (_dragging != null)
            {
                SlideTo(_dragging, _mouse.X);
                return;
            }
            Hit hit = HitAt(_mouse);
            Cursor = hit != null ? Cursors.Hand : Cursors.Default;
            if (hit != _hovered)
            {
                _hovered = hit;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_dragging != null) return;
            _mouse = new PointF(-1, -1);
            _hovered = null;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Hit hit = HitAt(Logical(e.Location));
            if (hit == null) return;
            if (hit.Slide != null)
            {
                _dragging = hit;
                Capture = true;
                SlideTo(hit, Logical(e.Location).X);
            }
            else
            {
                hit.Click();
                if (!IsDisposed) Invalidate(); // El clic pudo cerrar la ventana
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_dragging == null) return;
            if (_dragging.Commit != null) _dragging.Commit();
            _dragging = null;
            Capture = false;
            Invalidate();
        }

        private void SlideTo(Hit hit, float x)
        {
            hit.Slide(Clamp01((x - hit.Track.X) / hit.Track.Width));
            Invalidate();
        }

        private bool Hover(RectangleF r)
        {
            return _dragging == null && r.Contains(_mouse);
        }

        #endregion

        #region Dibujo

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Todo se pinta en OnPaint (con doble búfer)
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            PaintAll(e.Graphics);
        }

        private void PaintAll(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.ScaleTransform(_scale, _scale);
            g.Clear(Back);
            _hits.Clear();

            DrawSidebar(g);
            switch (_page)
            {
                case Page.Privacy: DrawPrivacy(g); break;
                case Page.Island: DrawIsland(g); break;
                case Page.Appearance: DrawAppearance(g); break;
                case Page.Tools: DrawTools(g); break;
                case Page.Notifications: DrawNotifications(g); break;
                case Page.Time: DrawTime(g); break;
                case Page.Tray: DrawTray(g); break;
                case Page.Assistant: DrawAssistant(g); break;
                case Page.Start: DrawStart(g); break;
                case Page.Taskbar: DrawTaskbar(g); break;
                case Page.Widgets: DrawWidgets(g); break;
                case Page.Clock: DrawClock(g); break;
                case Page.Eyes: DrawEyes(g); break;
                case Page.StartIcon: DrawStartIcon(g); break;
                case Page.About: DrawAbout(g); break;
            }
            DrawCloseButton(g);
        }

        /// <summary>
        /// Herramienta de desarrollo (--preview-settings): guarda cada página en PNG sin mostrar la ventana.
        /// </summary>
        internal static void RenderPreviews(string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            using (SettingsWindow window = new SettingsWindow())
            {
                foreach (Page page in Enum.GetValues(typeof(Page)))
                {
                    window._page = page;
                    using (Bitmap bitmap = new Bitmap(window.Px(W), window.Px(H)))
                    {
                        using (Graphics g = Graphics.FromImage(bitmap)) window.PaintAll(g);
                        bitmap.Save(System.IO.Path.Combine(folder, "ajustes-" + page.ToString().ToLowerInvariant() + ".png"));
                    }
                }

                // Renderizar también la versión multimonitor para preview y documentación fiel
                ForceMultiMonitorPreview = true;
                ForceMultiMonitorP2 = false;
                window._page = Page.Clock;
                using (Bitmap bitmap = new Bitmap(window.Px(W), window.Px(H)))
                {
                    using (Graphics g = Graphics.FromImage(bitmap)) window.PaintAll(g);
                    bitmap.Save(System.IO.Path.Combine(folder, "ajustes-clock-multimonitor-p1.png"));
                }

                ForceMultiMonitorP2 = true;
                using (Bitmap bitmap = new Bitmap(window.Px(W), window.Px(H)))
                {
                    using (Graphics g = Graphics.FromImage(bitmap)) window.PaintAll(g);
                    bitmap.Save(System.IO.Path.Combine(folder, "ajustes-clock-multimonitor-p2.png"));
                }
                ForceMultiMonitorPreview = false;
                ForceMultiMonitorP2 = false;
            }
        }

        private void DrawSidebar(Graphics g)
        {
            using (Pen line = new Pen(SideLine, 1)) g.DrawLine(line, SideW, 0, SideW, H);
            DrawText(g, "Streeck Suite", _bold15, TextColor, new RectangleF(22, 18, 180, 20), StringAlignment.Near);

            string[] names = { "Privacidad", "Isla", "Apariencia", "Herramientas", "Avisos", "Tiempo y agenda", "Bandeja y búsqueda", "Asistente IA", "Menú de inicio", "Barra de tareas", "Botón de Inicio", "Widgets", "Reloj 3D", "Ojitos", "Acerca de" };
            for (int i = 0; i < names.Length; i++)
            {
                Page page = (Page)i;
                RectangleF item = new RectangleF(12, 52 + i * 40, SideW - 24, 36);
                bool active = page == _page;
                if (active) FillRound(g, item, 10, NavActive);
                else if (Hover(item)) FillRound(g, item, 10, HoverFill);
                Color color = active ? Color.White : Gray;
                DrawNavIcon(g, page, new RectangleF(item.X + 10, item.Y + 10, 16, 16), color);
                DrawText(g, names[i], active ? _semi13 : _reg13, color, new RectangleF(item.X + 36, item.Y, item.Width - 40, item.Height), StringAlignment.Near);
                AddHit(item, () => { _page = page; });
            }
        }

        private void DrawCloseButton(Graphics g)
        {
            RectangleF close = new RectangleF(W - 44, 12, 30, 30);
            if (Hover(close)) FillRound(g, close, 8, ActiveFill);
            float cx = close.X + close.Width / 2, cy = close.Y + close.Height / 2;
            using (Pen pen = new Pen(Hover(close) ? Color.White : Gray, 1.5f))
            {
                g.DrawLine(pen, cx - 5, cy - 5, cx + 5, cy + 5);
                g.DrawLine(pen, cx + 5, cy - 5, cx - 5, cy + 5);
            }
            AddHit(close, Close);
        }

        // Iconos del menú lateral, dibujados en una caja de 24 unidades como los SVG del mockup
        private static void DrawNavIcon(Graphics g, Page page, RectangleF r, Color color)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(r.X, r.Y);
            g.ScaleTransform(r.Width / 24f, r.Height / 24f);
            using (Pen pen = new Pen(color, 2f))
            {
                pen.LineJoin = LineJoin.Round;
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                switch (page)
                {
                    case Page.Privacy:
                        using (GraphicsPath shield = new GraphicsPath())
                        {
                            shield.AddBezier(12, 22, 16, 20, 20, 17, 20, 12);
                            shield.AddLine(20, 12, 20, 5);
                            shield.AddLine(20, 5, 12, 2);
                            shield.AddLine(12, 2, 4, 5);
                            shield.AddLine(4, 5, 4, 12);
                            shield.AddBezier(4, 12, 4, 17, 8, 20, 12, 22);
                            shield.CloseFigure();
                            g.DrawPath(pen, shield);
                        }
                        break;
                    case Page.Island:
                        using (GraphicsPath pill = LayeredWindow.RoundedRect(new RectangleF(3, 3, 18, 8), 4)) g.DrawPath(pen, pill);
                        g.DrawLine(pen, 3, 17, 21, 17);
                        break;
                    case Page.Tools:
                        g.DrawLine(pen, 5, 19, 13, 11);
                        g.DrawEllipse(pen, 12, 4, 8, 8);
                        break;
                    case Page.Appearance:
                        g.DrawEllipse(pen, 3, 3, 18, 18);
                        g.DrawLine(pen, 12, 3, 12, 21);
                        using (SolidBrush half = new SolidBrush(color)) g.FillPie(half, 3, 3, 18, 18, 90, 180);
                        break;
                    case Page.Widgets:
                        g.DrawRectangle(pen, 3, 3, 7, 7);
                        g.DrawRectangle(pen, 14, 3, 7, 7);
                        g.DrawRectangle(pen, 3, 14, 7, 7);
                        g.DrawRectangle(pen, 14, 14, 7, 7);
                        break;
                    case Page.Notifications:
                        using (GraphicsPath bell = new GraphicsPath())
                        {
                            bell.AddBezier(6, 8, 6, 4.7f, 8.7f, 2, 12, 2);
                            bell.AddBezier(12, 2, 15.3f, 2, 18, 4.7f, 18, 8);
                            bell.AddBezier(18, 8, 18, 15, 21, 17, 21, 17);
                            bell.AddLine(21, 17, 3, 17);
                            bell.AddBezier(3, 17, 3, 17, 6, 15, 6, 8);
                            bell.CloseFigure();
                            g.DrawPath(pen, bell);
                        }
                        g.DrawArc(pen, 10.3f, 19.3f, 3.4f, 3.4f, 0, 180);
                        break;
                    case Page.Time:
                        g.DrawEllipse(pen, 4, 5, 16, 16);
                        g.DrawLines(pen, new[] { new PointF(12, 9), new PointF(12, 13), new PointF(14, 15) });
                        g.DrawLine(pen, 9, 2, 15, 2);
                        break;
                    case Page.Tray:
                        using (GraphicsPath tray = new GraphicsPath())
                        {
                            tray.AddLines(new[] { new PointF(2, 12), new PointF(5.5f, 5), new PointF(18.5f, 5), new PointF(22, 12), new PointF(22, 20), new PointF(2, 20) });
                            tray.CloseFigure();
                            g.DrawPath(pen, tray);
                        }
                        g.DrawLines(pen, new[] { new PointF(2, 12), new PointF(8, 12), new PointF(10, 15), new PointF(14, 15), new PointF(16, 12), new PointF(22, 12) });
                        break;
                    case Page.Assistant:
                        g.DrawPolygon(pen, new[] { new PointF(12, 3), new PointF(13.9f, 8.6f), new PointF(19.5f, 10), new PointF(13.9f, 11.9f),
                            new PointF(12, 17.5f), new PointF(10.1f, 11.9f), new PointF(4.5f, 10), new PointF(10.1f, 8.6f) });
                        g.DrawPolygon(pen, new[] { new PointF(19, 15), new PointF(19.8f, 17.2f), new PointF(22, 18), new PointF(19.8f, 18.8f),
                            new PointF(19, 21), new PointF(18.2f, 18.8f), new PointF(16, 18), new PointF(18.2f, 17.2f) });
                        break;
                    case Page.Start:
                        g.DrawRectangle(pen, 3, 3, 8, 8);
                        g.DrawRectangle(pen, 13, 3, 8, 8);
                        g.DrawRectangle(pen, 3, 13, 8, 8);
                        g.DrawRectangle(pen, 13, 13, 8, 8);
                        break;
                    case Page.Taskbar:
                        using (GraphicsPath screen = LayeredWindow.RoundedRect(new RectangleF(2, 4, 20, 16), 3)) g.DrawPath(pen, screen);
                        g.DrawLine(pen, 6, 16, 18, 16);
                        break;
                    case Page.Clock:
                        g.DrawEllipse(pen, 3, 3, 18, 18);
                        g.DrawLines(pen, new[] { new PointF(12, 7), new PointF(12, 12), new PointF(15, 14) });
                        break;
                    case Page.Eyes:
                        using (GraphicsPath leftEye = LayeredWindow.RoundedRect(new RectangleF(4, 6, 6, 12), 3)) g.DrawPath(pen, leftEye);
                        using (GraphicsPath rightEye = LayeredWindow.RoundedRect(new RectangleF(14, 6, 6, 12), 3)) g.DrawPath(pen, rightEye);
                        break;
                    case Page.StartIcon:
                        using (GraphicsPath tile = LayeredWindow.RoundedRect(new RectangleF(3, 3, 18, 18), 5)) g.DrawPath(pen, tile);
                        g.DrawPolygon(pen, new[] { new PointF(12, 7), new PointF(13.4f, 10.6f), new PointF(17, 12), new PointF(13.4f, 13.4f),
                            new PointF(12, 17), new PointF(10.6f, 13.4f), new PointF(7, 12), new PointF(10.6f, 10.6f) });
                        break;
                    case Page.About:
                        g.DrawEllipse(pen, 3, 3, 18, 18);
                        g.DrawLine(pen, 12, 11, 12, 16.5f);
                        g.DrawLine(pen, 12, 7.5f, 12, 7.6f);
                        break;
                }
            }
            g.Restore(state);
        }

        #endregion

        #region Página Privacidad (P1)

        private void DrawPrivacy(Graphics g)
        {
            DrawTitle(g, "Modo privacidad");
            AeroDwmModule privacy = AeroDwmModule.Current;

            float y = 68;
            DrawCard(g, y, 66);
            DrawText(g, "Activado ahora", _semi14, TextColor, new RectangleF(InnerX, y + 14, 360, 20), StringAlignment.Near);
            string shortcut = privacy != null ? privacy.ActiveShortcut : null;
            DrawText(g, shortcut != null ? "Atajo: " + shortcut.Replace("+", " + ") : "Sin atajo: otro programa lo tiene ocupado",
                _reg12, Gray, new RectangleF(InnerX, y + 36, 360, 16), StringAlignment.Near);
            DrawToggle(g, ContentRight - 16 - 44, y + 21, privacy != null && privacy.IsPrivacyActive, false,
                () => EventBus.Instance.Publish(Core.Events.PrivacyToggle));

            y = 150;
            DrawCard(g, y, 172);
            float row = y + 14;
            DrawLabelValue(g, row, "Intensidad del desenfoque", SuiteSettings.PrivacyBlurPx + " px", _semi14, _bold14);
            row += 30;
            DrawSlider(g, new RectangleF(InnerX, row, InnerW, 6), SuiteSettings.PrivacyBlurPx / 40f,
                f => { SuiteSettings.PrivacyBlurPx = (int)Math.Round(f * 40); SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);
            row += 16;
            DrawText(g, "Sin desenfoque", _reg11, Legend, new RectangleF(InnerX, row, 200, 15), StringAlignment.Near);
            DrawText(g, "40 px", _reg11, Legend, new RectangleF(InnerX, row, InnerW, 15), StringAlignment.Far);
            row += 25;
            using (Pen line = new Pen(Divider, 1)) g.DrawLine(line, InnerX, row, InnerX + InnerW, row);
            row += 11;
            DrawLabelValue(g, row, "Oscurecimiento", SuiteSettings.PrivacyDarkenPercent + " %", _semi14, _bold14);
            row += 30;
            DrawSlider(g, new RectangleF(InnerX, row, InnerW, 6), SuiteSettings.PrivacyDarkenPercent / 100f,
                f => { SuiteSettings.PrivacyDarkenPercent = (int)Math.Round(f * 100); SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);
            row += 16;
            DrawText(g, "El desenfoque lo calcula la suite: mientras el modo está activo usa algo de CPU.", _reg12, Gray,
                new RectangleF(InnerX, row, InnerW, 16), StringAlignment.Near);

            y = 338;
            DrawCard(g, y, 160);
            DrawText(g, "Color del tinte", _semi14, TextColor, new RectangleF(InnerX, y + 14, 300, 20), StringAlignment.Near);
            float swatchY = y + 46;
            bool presetSelected = false;
            for (int i = 0; i < TintPresets.Length; i++)
            {
                Color color = TintPresets[i];
                RectangleF swatch = new RectangleF(InnerX + i * 56, swatchY, 44, 44);
                bool selected = SameColor(SuiteSettings.PrivacyTint, color);
                presetSelected |= selected;
                FillRound(g, swatch, 12, color);
                if (selected) StrokeRound(g, RectangleF.Inflate(swatch, -1, -1), 11, Accent, 2, false);
                else StrokeRound(g, swatch, 12, Hover(swatch) ? DashedBorder : SoftBorder, 1, false);
                AddHit(swatch, () => { SuiteSettings.PrivacyTint = color; SuiteSettings.Save(); });
            }
            DrawCustomColorButton(g, new RectangleF(InnerX + 4 * 56, swatchY, 136, 44), 12, presetSelected ? (Color?)null : SuiteSettings.PrivacyTint,
                () => PickColor(SuiteSettings.PrivacyTint, c => { SuiteSettings.PrivacyTint = c; SuiteSettings.Save(); }));

            // Vista previa: un trozo del fondo de escritorio desenfocado con el tinte y el oscurecimiento elegidos
            RectangleF preview = new RectangleF(InnerX, y + 102, InnerW, 44);
            Bitmap backdrop = DesktopBackdrop.Get();
            if (backdrop != null)
            {
                using (GraphicsPath path = LayeredWindow.RoundedRect(preview, 10))
                {
                    GraphicsState state = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    float sourceW = backdrop.Width * 0.5f;
                    g.DrawImage(backdrop, preview, new RectangleF(backdrop.Width * 0.25f, backdrop.Height * 0.35f, sourceW, sourceW * preview.Height / preview.Width), GraphicsUnit.Pixel);
                    g.Restore(state);
                }
                FillRound(g, preview, 10, Color.FromArgb(SuiteSettings.PrivacyDarkenPercent * 255 / 100, SuiteSettings.PrivacyTint));
            }
            else
            {
                FillRound(g, preview, 10, Color.FromArgb(217, SuiteSettings.PrivacyTint));
            }
            DrawText(g, "Vista previa del tinte", _reg12, Gray, preview, StringAlignment.Center);
        }

        #endregion

        #region Página Isla (P3)

        private void DrawIsland(Graphics g)
        {
            DrawTitle(g, "Isla");

            float y = 66;
            DrawCard(g, y, 130);
            DrawText(g, "Pestañas", _semi14, TextColor, new RectangleF(InnerX, y + 14, 300, 20), StringAlignment.Near);
            string[] tabs = { "Música", "Sonido", "Clima", "Sistema", "Tiempo", "Agenda" };
            bool[] shown = { SuiteSettings.IslandShowMusic, SuiteSettings.IslandShowSound, SuiteSettings.IslandShowWeather, SuiteSettings.IslandShowSystem,
                SuiteSettings.IslandShowTimer, SuiteSettings.IslandShowAgenda };
            float cellW = (InnerW - 16) / 3; // Tres columnas, dos filas
            for (int i = 0; i < tabs.Length; i++)
            {
                int index = i;
                bool on = shown[i];
                RectangleF cell = new RectangleF(InnerX + (i % 3) * (cellW + 8), y + 44 + (i / 3) * 40, cellW, 32);
                FillRound(g, cell, 10, CardFill);
                DrawText(g, tabs[i], _reg13, TextColor, new RectangleF(cell.X + 10, cell.Y, cellW - 50, cell.Height), StringAlignment.Near);
                DrawToggle(g, cell.Right - 10 - 30, cell.Y + 8, on, true, () => { SetTabShown(index, !on); SuiteSettings.Save(); });
            }

            y = 210;
            DrawCard(g, y, 98);
            DrawText(g, "Con una ventana maximizada", _semi14, TextColor, new RectangleF(InnerX, y + 14, 400, 20), StringAlignment.Near);
            DrawSegmented(g, new RectangleF(InnerX, y + 44, InnerW, 40),
                new[] { "Esconder", "Mostrar siempre", "Solo avisos", "Camuflaje" }, (int)SuiteSettings.IslandMaximized,
                i => { SuiteSettings.IslandMaximized = (SuiteSettings.MaximizedBehavior)i; SuiteSettings.Save(); });

            y = 322;
            DrawCard(g, y, 136);
            DrawText(g, "Abrir al pasar el ratón", _semi14, TextColor, new RectangleF(InnerX, y + 12, 400, 24), StringAlignment.Near);
            DrawToggle(g, ContentRight - 16 - 44, y + 12, SuiteSettings.IslandOpenOnHover, false,
                () => { SuiteSettings.IslandOpenOnHover = !SuiteSettings.IslandOpenOnHover; SuiteSettings.Save(); });
            DrawLabelValue(g, y + 46, "Cerrar al sacar el ratón tras", SecondsText(SuiteSettings.IslandCloseDelayMs), _reg13, _bold13);
            DrawSlider(g, new RectangleF(InnerX, y + 74, InnerW, 6), SuiteSettings.IslandCloseDelayMs / 3000f,
                f => { SuiteSettings.IslandCloseDelayMs = (int)Math.Round(f * 30) * 100; SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);
            DrawLabelValue(g, y + 90, "Mostrar una canción nueva durante",
                SuiteSettings.IslandSongPeekMs == 0 ? "No mostrar" : SecondsText(SuiteSettings.IslandSongPeekMs), _reg13, _bold13);
            DrawSlider(g, new RectangleF(InnerX, y + 118, InnerW, 6), SuiteSettings.IslandSongPeekMs / 10000f,
                f => { SuiteSettings.IslandSongPeekMs = (int)Math.Round(f * 10) * 1000; SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);

            y = 472;
            DrawCard(g, y, 118);
            DrawInlineSlider(g, y + 12, "Tamaño", SuiteSettings.IslandScalePercent + " %", (SuiteSettings.IslandScalePercent - 70) / 50f,
                f => { SuiteSettings.IslandScalePercent = 70 + (int)Math.Round(f * 10) * 5; SuiteSettings.NotifyChanged(); });
            // Opacidad: solo el fondo (las letras siempre se ven completas)
            DrawInlineSlider(g, y + 44, "Opacidad", SuiteSettings.IslandOpacityPercent + " %", (SuiteSettings.IslandOpacityPercent - 30) / 70f,
                f => { SuiteSettings.IslandOpacityPercent = 30 + (int)Math.Round(f * 70); SuiteSettings.NotifyChanged(); });
            // Velocidad de las animaciones al abrir y cambiar de vista (Normal = la original)
            DrawText(g, "Animación", _semi14, TextColor, new RectangleF(InnerX, y + 76, 120, 34), StringAlignment.Near);
            DrawSegmented(g, new RectangleF(ContentRight - 16 - 330, y + 76, 330, 32), new[] { "Lenta", "Normal", "Rápida" },
                (int)SuiteSettings.IslandAnimation, i => { SuiteSettings.IslandAnimation = (SuiteSettings.AnimationSpeed)i; SuiteSettings.Save(); });

            // Estilo (fase J1) y color del fondo, el mismo para los dos estilos
            y = 602;
            DrawCard(g, y, 188);
            // Varios monitores: la isla en el principal o en el del ratón
            DrawText(g, "Monitor", _semi14, TextColor, new RectangleF(InnerX, y + 142, 120, 40), StringAlignment.Near);
            DrawSegmented(g, new RectangleF(ContentRight - 16 - 330, y + 145, 330, 34), new[] { "Principal", "Donde esté el ratón" },
                (int)SuiteSettings.IslandMonitor, i => { SuiteSettings.IslandMonitor = (SuiteSettings.IslandMonitorKind)i; SuiteSettings.Save(); });
            // Isla cerrada (fase J6): widgets a los lados u ojitos
            DrawText(g, "Cerrada", _semi14, TextColor, new RectangleF(InnerX, y + 98, 120, 40), StringAlignment.Near);
            DrawSegmented(g, new RectangleF(ContentRight - 16 - 330, y + 101, 330, 34), new[] { "Widgets", "Ojitos" },
                (int)SuiteSettings.IslandIdle, i => { SuiteSettings.IslandIdle = (SuiteSettings.IslandIdleKind)i; SuiteSettings.Save(); });
            DrawText(g, "Estilo", _semi14, TextColor, new RectangleF(InnerX, y, 120, 56), StringAlignment.Near);
            DrawSegmented(g, new RectangleF(ContentRight - 16 - 330, y + 11, 330, 34), new[] { "Pegada arriba", "Flotante", "Redondeada" },
                (int)SuiteSettings.IslandStyle, i => { SuiteSettings.IslandStyle = (SuiteSettings.IslandStyleKind)i; SuiteSettings.Save(); });
            DrawText(g, "Color", _semi14, TextColor, new RectangleF(InnerX, y + 54, 120, 40), StringAlignment.Near);
            string[] colors = { "#000000", "#1c1c1e", "#0f172a", "#1e1b4b", "#2e1065", "#052e16", "#3f0d12" };
            const float swatch = 22, swatchGap = 10;
            float sx = ContentRight - 16 - (colors.Length * (swatch + swatchGap) - swatchGap);
            foreach (string hex in colors)
            {
                RectangleF circle = new RectangleF(sx, y + 63, swatch, swatch);
                using (SolidBrush fill = new SolidBrush(ColorTranslator.FromHtml(hex))) g.FillEllipse(fill, circle);
                using (Pen edge = new Pen(Color.FromArgb(60, 255, 255, 255), 1)) g.DrawEllipse(edge, circle); // Para ver el negro sobre el fondo oscuro
                if (string.Equals(SuiteSettings.IslandColor, hex, StringComparison.OrdinalIgnoreCase))
                {
                    using (Pen ring = new Pen(Accent, 2f)) g.DrawEllipse(ring, RectangleF.Inflate(circle, 3, 3));
                }
                string picked = hex;
                AddHit(circle, () => { SuiteSettings.IslandColor = picked; SuiteSettings.Save(); });
                sx += swatch + swatchGap;
            }
        }

        private static void SetTabShown(int index, bool shown)
        {
            switch (index)
            {
                case 0: SuiteSettings.IslandShowMusic = shown; break;
                case 1: SuiteSettings.IslandShowSound = shown; break;
                case 2: SuiteSettings.IslandShowWeather = shown; break;
                case 3: SuiteSettings.IslandShowSystem = shown; break;
                case 4: SuiteSettings.IslandShowTimer = shown; break;
                case 5: SuiteSettings.IslandShowAgenda = shown; break;
            }
        }

        // Fila con nombre, deslizador y valor en la misma línea (tamaño y opacidad de la isla)
        private void DrawInlineSlider(Graphics g, float y, string label, string value, float fraction, Action<float> slide)
        {
            DrawText(g, label, _semi14, TextColor, new RectangleF(InnerX, y, 90, 22), StringAlignment.Near);
            DrawText(g, value, _bold14, ValueColor, new RectangleF(ContentRight - 16 - 60, y, 60, 22), StringAlignment.Far);
            DrawSlider(g, new RectangleF(InnerX + 90, y + 8, InnerW - 90 - 76, 6), fraction, slide, SuiteSettings.Save);
        }

        private static string SecondsText(int ms)
        {
            return (ms / 1000.0).ToString("0.#", Spanish) + " s";
        }

        #endregion

        #region Página Avisos (C3)

        private const int AppRowsVisible = 2; // Filas de dos apps; la rueda pasa al resto
        private int _appsOffset;

        private void DrawNotifications(Graphics g)
        {
            DrawTitle(g, "Avisos");

            float y = 66;
            DrawCard(g, y, 168);
            DrawToggleRow(g, y, "Pausar notificaciones", "Modo No molestar (atajo rápido: Ctrl + Alt + N)", SuiteSettings.DoNotDisturb,
                () => { NotificationService.ToggleDoNotDisturb(); Invalidate(); });
            DrawRowDivider(g, y + 58);
            DrawToggleRow(g, y + 58, "Notificaciones en la isla", "Los avisos de tus apps aparecen arriba, además de en Windows",
                SuiteSettings.NotificationsEnabled,
                () => { SuiteSettings.NotificationsEnabled = !SuiteSettings.NotificationsEnabled; SuiteSettings.Save(); });
            DrawRowDivider(g, y + 116);
            DrawLabelValue(g, y + 120, "Mostrar cada aviso durante", SecondsText(SuiteSettings.NotificationDurationMs), _reg13, _bold13);
            DrawSlider(g, new RectangleF(InnerX, y + 148, InnerW, 6), (SuiteSettings.NotificationDurationMs - 1000) / 9000f,
                f => { SuiteSettings.NotificationDurationMs = 1000 + (int)Math.Round(f * 9) * 1000; SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);

            y = 66 + 168 + 14;
            List<NotificationApps.App> apps = NotificationApps.All;
            int rows = Math.Max(1, Math.Min(AppRowsVisible, (apps.Count + 1) / 2));
            float cardHeight = 36 + rows * 40 + 6;
            DrawCard(g, y, cardHeight);
            DrawText(g, "Qué apps avisan", _semi14, TextColor, new RectangleF(InnerX, y + 10, 200, 20), StringAlignment.Near);
            string hint = apps.Count > AppRowsVisible * 2 ? "Rueda del ratón para ver más" : "Aparecen al enviar su primer aviso";
            DrawText(g, hint, _reg12, Gray, new RectangleF(InnerX, y + 10, InnerW, 20), StringAlignment.Far);
            if (apps.Count == 0)
            {
                DrawText(g, "Todavía ninguna app ha enviado avisos.", _reg13, Gray, new RectangleF(InnerX, y + 36, InnerW, 40), StringAlignment.Near);
            }
            _appsOffset = Math.Max(0, Math.Min(_appsOffset, (apps.Count + 1) / 2 - AppRowsVisible));
            float columnW = (InnerW - 16) / 2;
            for (int i = _appsOffset * 2; i < apps.Count && i < (_appsOffset + AppRowsVisible) * 2; i++)
            {
                NotificationApps.App app = apps[i];
                int slot = i - _appsOffset * 2;
                float x = InnerX + (slot % 2) * (columnW + 16);
                float rowY = y + 36 + (slot / 2) * 40;
                RectangleF icon = new RectangleF(x, rowY + 8, 24, 24);
                Bitmap image = NotificationService.IconFor(app.Id);
                if (image != null)
                {
                    GraphicsState state = g.Save();
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(image, icon);
                    g.Restore(state);
                }
                else
                {
                    FillRound(g, icon, 7, ToggleOff);
                }
                DrawText(g, app.Name, _reg13, app.Muted ? Gray : TextColor, new RectangleF(x + 34, rowY, columnW - 34 - 40, 40), StringAlignment.Near);
                string id = app.Id;
                bool muted = app.Muted;
                DrawToggle(g, x + columnW - 30, rowY + 12, !muted, true, () => NotificationApps.SetMuted(id, !muted));
            }

            y += cardHeight + 14;
            DrawCard(g, y, 110);
            DrawToggleRow(g, y + 4, "Puntos de micrófono y cámara", "Naranja = micrófono, verde = cámara", SuiteSettings.CaptureDots,
                () => { SuiteSettings.CaptureDots = !SuiteSettings.CaptureDots; SuiteSettings.Save(); });
            DrawRowDivider(g, y + 62);
            DrawToggleRow(g, y + 62, "Avisar cuando una app empiece a usarlos", null, SuiteSettings.CaptureAlerts,
                () => { SuiteSettings.CaptureAlerts = !SuiteSettings.CaptureAlerts; SuiteSettings.Save(); });
            // Dispositivos Bluetooth (fase F)
            y += 110 + 14;
            DrawCard(g, y, 100);
            DrawToggleRow(g, y + 4, "Avisar al conectar un dispositivo Bluetooth", null, SuiteSettings.BluetoothConnectAlerts,
                () => { SuiteSettings.BluetoothConnectAlerts = !SuiteSettings.BluetoothConnectAlerts; SuiteSettings.Save(); });
            DrawLabelValue(g, y + 54, "Avisar de batería baja al",
                SuiteSettings.BluetoothLowBatteryPercent == 0 ? "Sin aviso" : SuiteSettings.BluetoothLowBatteryPercent + " %", _reg13, _bold13);
            DrawSlider(g, new RectangleF(InnerX, y + 82, InnerW, 6), SuiteSettings.BluetoothLowBatteryPercent / 50f,
                f => { SuiteSettings.BluetoothLowBatteryPercent = (int)Math.Round(f * 10) * 5; SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);

            // Aviso de volumen de Windows (fase J5): la isla muestra el suyo
            y += 100 + 14;
            DrawCard(g, y, 66);
            DrawToggleRow(g, y + 4, "Esconder el aviso de volumen de Windows", "La isla muestra el suyo al subir o bajar el volumen", SuiteSettings.HideWindowsVolume,
                () => { SuiteSettings.HideWindowsVolume = !SuiteSettings.HideWindowsVolume; SuiteSettings.Save(); NativeOsd.Apply(SuiteSettings.HideWindowsVolume); });
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (_page != Page.Notifications) return;
            _appsOffset += e.Delta < 0 ? 1 : -1; // Se ajusta al rango al dibujar
            if (_appsOffset < 0) _appsOffset = 0;
            Invalidate();
        }

        #endregion

        #region Página Tiempo y agenda (D3)

        private string _icalMessage; // Error al pegar un enlace que no es de calendario

        private void DrawTime(Graphics g)
        {
            DrawTitle(g, "Tiempo y agenda");

            float y = 66;
            DrawCard(g, y, 206);
            DrawText(g, "Pomodoro", _semi14, TextColor, new RectangleF(InnerX, y + 12, 300, 20), StringAlignment.Near);
            DrawLabelValue(g, y + 40, "Enfoque", SuiteSettings.PomodoroFocusMin + " min", _reg13, _bold13);
            DrawSlider(g, new RectangleF(InnerX, y + 64, InnerW, 6), (SuiteSettings.PomodoroFocusMin - 5) / 55f,
                f => { SuiteSettings.PomodoroFocusMin = 5 + (int)Math.Round(f * 11) * 5; SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);
            DrawLabelValue(g, y + 80, "Descanso corto", SuiteSettings.PomodoroShortBreakMin + " min", _reg13, _bold13);
            DrawSlider(g, new RectangleF(InnerX, y + 104, InnerW, 6), (SuiteSettings.PomodoroShortBreakMin - 1) / 14f,
                f => { SuiteSettings.PomodoroShortBreakMin = 1 + (int)Math.Round(f * 14); SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);
            DrawLabelValue(g, y + 120, "Descanso largo (cada " + TimerService.CyclesPerLongBreak + ")", SuiteSettings.PomodoroLongBreakMin + " min", _reg13, _bold13);
            DrawSlider(g, new RectangleF(InnerX, y + 144, InnerW, 6), (SuiteSettings.PomodoroLongBreakMin - 5) / 25f,
                f => { SuiteSettings.PomodoroLongBreakMin = 5 + (int)Math.Round(f * 5) * 5; SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);

            // Sonido al terminar, dentro de la misma tarjeta para dejar sitio a varios calendarios
            DrawRowDivider(g, y + 160);
            DrawToggleRow(g, y + 160, "Sonido al terminar (alarma de Windows)", null, SuiteSettings.TimerSound,
                () => { SuiteSettings.TimerSound = !SuiteSettings.TimerSound; SuiteSettings.Save(); });

            List<CalendarFeed> feeds = AgendaService.Feeds;
            y = 286;
            DrawCard(g, y, 196 + 32 * feeds.Count);
            DrawText(g, "Calendario", _semi14, TextColor, new RectangleF(InnerX, y + 12, 300, 20), StringAlignment.Near);

            int windowsCount = AgendaService.WindowsCount;
            string windowsStatus = !SuiteSettings.CalendarWindows ? "Desactivado"
                : windowsCount < 0 ? "Leyendo…" : windowsCount == 1 ? "1 evento hoy y mañana" : windowsCount + " eventos hoy y mañana";
            DrawToggleRow(g, y + 40, "Calendario de Windows (cuenta Microsoft)", windowsStatus, SuiteSettings.CalendarWindows,
                () => { SuiteSettings.CalendarWindows = !SuiteSettings.CalendarWindows; SuiteSettings.Save(); AgendaService.RefreshNow(); });
            DrawRowDivider(g, y + 98);

            // Una fila por enlace iCal: color con el que sale en la isla, nombre, eventos (o error) y ✕ para quitarlo
            float row = y + 104;
            foreach (CalendarFeed feed in feeds)
            {
                using (SolidBrush dot = new SolidBrush(feed.Color)) g.FillEllipse(dot, InnerX, row + 11, 10, 10);
                DrawText(g, feed.Name, _semi13, TextColor, new RectangleF(InnerX + 18, row, 230, 32), StringAlignment.Near);
                string status = feed.Error != null ? "No se pudo leer" : feed.Count < 0 ? "Leyendo…" : feed.Count == 1 ? "1 evento" : feed.Count + " eventos";
                DrawText(g, status, _reg12, feed.Error != null ? Color.FromArgb(253, 186, 116) : Gray, new RectangleF(InnerX + 256, row, 150, 32), StringAlignment.Near);
                RectangleF remove = new RectangleF(ContentRight - 16 - 26, row + 3, 26, 26);
                if (Hover(remove)) FillRound(g, remove, 8, ActiveFill);
                using (Pen cross = new Pen(Hover(remove) ? Color.White : Gray, 1.4f))
                {
                    float cx = remove.X + 13, cy = remove.Y + 13;
                    g.DrawLine(cross, cx - 4, cy - 4, cx + 4, cy + 4);
                    g.DrawLine(cross, cx + 4, cy - 4, cx - 4, cy + 4);
                }
                string url = feed.Url;
                AddHit(remove, () => RemoveIcalUrl(url));
                row += 32;
            }

            // Añadir: pega el enlace que haya en el portapapeles
            string hint = _icalMessage ?? (feeds.Count == 0 ? "Google, Outlook publicado, iCloud… (enlace .ics)" : "");
            DrawText(g, hint, _reg12, _icalMessage != null ? Color.FromArgb(253, 186, 116) : Gray, new RectangleF(InnerX, row + 2, InnerW - 190, 30), StringAlignment.Near);
            if (feeds.Count < MaxIcalFeeds)
            {
                const string addText = "+ Añadir enlace iCal";
                float buttonW = g.MeasureString(addText, _reg13).Width + 24;
                RectangleF button = new RectangleF(ContentRight - 16 - buttonW, row + 2, buttonW, 30);
                FillRound(g, button, 8, Hover(button) ? Color.FromArgb(40, 47, 62) : Field);
                StrokeRound(g, button, 8, SoftBorder, 1, false);
                DrawText(g, addText, _reg13, TextColor, button, StringAlignment.Center);
                AddHit(button, PasteIcalUrl);
            }
            DrawRowDivider(g, row + 40);

            DrawLabelValue(g, row + 50, "Avisar antes de cada evento",
                SuiteSettings.EventAlertMinutes == 0 ? "Sin aviso" : SuiteSettings.EventAlertMinutes + " min", _reg13, _bold13);
            DrawSlider(g, new RectangleF(InnerX, row + 76, InnerW, 6), SuiteSettings.EventAlertMinutes / 30f,
                f => { SuiteSettings.EventAlertMinutes = (int)Math.Round(f * 6) * 5; SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);

            // Mi ciudad para el clima (isla, reloj 3D y widget): elegida por ti o adivinada por la conexión
            float cityY = 286 + 196 + 32 * feeds.Count + 14;
            DrawCard(g, cityY, 58);
            bool manualCity = SuiteSettings.WeatherCity.Length > 0;
            WeatherService.Conditions now = WeatherService.Shared.Current;
            string autoCity = now != null && now.City.Length > 0 ? now.City : "tu zona";
            DrawText(g, "Ciudad del clima", _semi14, TextColor, new RectangleF(InnerX, cityY + 10, 260, 18), StringAlignment.Near);
            DrawText(g, manualCity ? "Mi ciudad · " + SuiteSettings.WeatherCity : "Automática por tu internet · " + autoCity, _reg12,
                manualCity ? Color.FromArgb(74, 222, 128) : Gray, new RectangleF(InnerX, cityY + 30, 300, 16), StringAlignment.Near);
            RectangleF choose = new RectangleF(ContentRight - 16 - 130, cityY + 13, 130, 32);
            FillRound(g, choose, 9, Hover(choose) ? Color.FromArgb(40, 47, 62) : Field);
            StrokeRound(g, choose, 9, SoftBorder, 1, false);
            DrawText(g, manualCity ? "Cambiar ciudad" : "Elegir ciudad", _semi13, TextColor, choose, StringAlignment.Center);
            AddHit(choose, () => ShowCityPicker(choose));
            if (manualCity)
            {
                RectangleF auto = new RectangleF(choose.X - 10 - 104, choose.Y, 104, 32);
                FillRound(g, auto, 9, Hover(auto) ? Color.FromArgb(40, 47, 62) : Field);
                StrokeRound(g, auto, 9, SoftBorder, 1, false);
                DrawText(g, "Automática", _reg13, TextColor, auto, StringAlignment.Center);
                AddHit(auto, () => SetWeatherCity(null));
            }
        }

        private void ShowCityPicker(RectangleF logical)
        {
            Rectangle onScreen = RectangleToScreen(new Rectangle(Px(logical.X), Px(logical.Y), Px(logical.Width), Px(logical.Height)));
            CityPicker picker = new CityPicker(_scale, SetWeatherCity);
            picker.Width = Px(320);
            picker.Location = new Point(onScreen.Right - picker.Width, onScreen.Bottom + Px(4));
            picker.Show(this);
        }

        // null = volver a «Automática»
        private void SetWeatherCity(WeatherService.Place place)
        {
            SuiteSettings.WeatherCity = place == null ? "" : place.Name + (place.Region.Length > 0 ? ", " + place.Region : "");
            SuiteSettings.WeatherLat = place == null ? double.NaN : place.Latitude;
            SuiteSettings.WeatherLon = place == null ? double.NaN : place.Longitude;
            SuiteSettings.Save();
            WeatherService.Shared.RefreshNow();
            Invalidate();
        }

        private const int MaxIcalFeeds = 4; // Los que caben en la página

        // El enlace iCal se pega desde el portapapeles (la ventana no tiene cuadros de texto)
        private void PasteIcalUrl()
        {
            string text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
            bool looksLikeIcal = (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
                                 && (text.Contains(".ics") || text.Contains("/ical/")) && text.IndexOf(' ') < 0;
            if (!looksLikeIcal)
            {
                _icalMessage = "El portapapeles no tiene un enlace iCal (termina en .ics)";
                return;
            }
            if (SuiteSettings.CalendarIcalUrls.Contains(text))
            {
                _icalMessage = "Ese calendario ya está añadido";
                return;
            }
            _icalMessage = null;
            SuiteSettings.CalendarIcalUrls.Add(text);
            SuiteSettings.Save();
            AgendaService.RefreshNow();
        }

        private void RemoveIcalUrl(string url)
        {
            _icalMessage = null;
            SuiteSettings.CalendarIcalUrls.Remove(url);
            SuiteSettings.Save();
            AgendaService.RefreshNow();
        }
        #endregion

        #region Página Bandeja y búsqueda (E3)

        private void DrawTray(Graphics g)
        {
            DrawTitle(g, "Bandeja y búsqueda");

            float y = 66;
            DrawCard(g, y, 124);
            DrawToggleRow(g, y + 4, "Bandeja de archivos", "Arrastra archivos a la isla para tenerlos a mano", SuiteSettings.FileTrayEnabled,
                () => { SuiteSettings.FileTrayEnabled = !SuiteSettings.FileTrayEnabled; SuiteSettings.Save(); });
            DrawRowDivider(g, y + 62);
            DrawToggleRow(g, y + 62, "Conservar al reiniciar", "Si no, la bandeja se vacía al cerrar la suite", SuiteSettings.FileTrayPersist,
                () => { SuiteSettings.FileTrayPersist = !SuiteSettings.FileTrayPersist; SuiteSettings.Save(); });

            y = 204;
            DrawCard(g, y, 124);
            DrawText(g, "Atajo de la búsqueda", _semi14, TextColor, new RectangleF(InnerX, y + 14, 300, 20), StringAlignment.Near);
            DrawSegmented(g, new RectangleF(InnerX, y + 44, InnerW, 40), new[] { "Ctrl + Alt + S", "Ctrl + Shift + Espacio", "Ctrl + Espacio" },
                (int)SuiteSettings.SearchHotkey, i => { SuiteSettings.SearchHotkey = (SuiteSettings.SearchShortcut)i; SuiteSettings.Save(); });
            DrawText(g, "Los atajos con Espacio desactivan ese mismo atajo en otros programas (p. ej. VS Code).", _reg12, Gray, new RectangleF(InnerX, y + 94, InnerW, 16), StringAlignment.Near);

            y = 342;
            DrawCard(g, y, 122);
            DrawToggleRow(g, y + 4, "Buscar también archivos", "Usa el índice de búsqueda de Windows", SuiteSettings.SearchFiles,
                () => { SuiteSettings.SearchFiles = !SuiteSettings.SearchFiles; SuiteSettings.Save(); });
            DrawRowDivider(g, y + 62);
            DrawText(g, "Buscar en la web con", _semi14, TextColor, new RectangleF(InnerX, y + 62, 220, 56), StringAlignment.Near);
            DrawSegmented(g, new RectangleF(ContentRight - 16 - 270, y + 70, 270, 40), new[] { "Google", "Bing", "DuckDuckGo" },
                (int)SuiteSettings.SearchEngine, i => { SuiteSettings.SearchEngine = (SuiteSettings.WebSearchEngine)i; SuiteSettings.Save(); });
        }

        #endregion

        #region Página Asistente IA (G2)

        private AssistantService.OllamaStatus _ollama;
        private DateTime _ollamaCheckedAt = DateTime.MinValue;
        private bool _ollamaChecking;
        private string _ollamaWork;      // "Instalando Ollama…", "Descargando modelo… 45 %" (null si no hay nada en marcha)
        private string _geminiMessage;   // Resultado de comprobar la clave
        private string _geminiModel;

        private void DrawAssistant(Graphics g)
        {
            DrawTitle(g, "Asistente IA");
            RefreshOllamaStatus();

            float y = 66;
            DrawCard(g, y, 216);
            DrawText(g, "Motor", _semi14, TextColor, new RectangleF(InnerX, y + 12, 300, 20), StringAlignment.Near);
            DrawSegmented(g, new RectangleF(InnerX, y + 40, InnerW, 40), new[] { "Local · gratis y privado", "Gemini · gratis con límite" },
                (int)SuiteSettings.AssistantEngine, i => { SuiteSettings.AssistantEngine = (SuiteSettings.AssistantEngineKind)i; SuiteSettings.Save(); });
            DrawRowDivider(g, y + 90);

            // IA local: estado de Ollama y del modelo
            string localStatus;
            Color localColor = Color.FromArgb(253, 186, 116);
            string localButton = null;
            Action localAction = null;
            if (_ollamaWork != null) { localStatus = _ollamaWork; localColor = Gray; }
            else if (_ollama == null) { localStatus = "Comprobando…"; localColor = Gray; }
            else if (!_ollama.Installed) { localStatus = "Ollama no está instalado · necesita ~5 GB"; localButton = "Instalar y descargar"; localAction = InstallOllama; }
            else if (!_ollama.Running) { localStatus = "Ollama está apagado"; localButton = "Encender"; localAction = () => { AssistantService.StartOllama(); _ollamaCheckedAt = DateTime.MinValue; }; }
            else if (!_ollama.HasModel) { localStatus = "Falta descargar el modelo (~5 GB)"; localButton = "Descargar"; localAction = DownloadModel; }
            else { localStatus = "Listo · responde en tu tarjeta gráfica"; localColor = Color.FromArgb(74, 222, 128); }
            DrawAssistantRow(g, y + 96, SuiteSettings.OllamaModel + " (IA local)", localStatus, localColor, localButton, localAction);
            DrawRowDivider(g, y + 150);

            // Gemini: clave gratuita de Google AI Studio
            bool hasKey = !string.IsNullOrEmpty(SuiteSettings.GeminiApiKey);
            string keyStatus = _geminiMessage ?? (hasKey ? "Configurada" + (_geminiModel != null ? " · " + _geminiModel : "") : "Sin configurar · clic aquí para conseguir una gratis");
            Color keyColor = _geminiMessage != null && !hasKey ? Color.FromArgb(253, 186, 116) : hasKey ? Color.FromArgb(74, 222, 128) : ValueColor;
            DrawAssistantRow(g, y + 156, "Clave de Gemini", keyStatus, keyColor, hasKey ? "Quitar" : "Pegar clave",
                hasKey ? (Action)(() => { SuiteSettings.GeminiApiKey = ""; _geminiModel = null; _geminiMessage = null; SuiteSettings.Save(); }) : PasteGeminiKey);
            if (!hasKey) AddHit(new RectangleF(InnerX, y + 178, 330, 18), () => OpenUrl("https://aistudio.google.com/apikey"));

            y = 296;
            DrawCard(g, y, 166);
            DrawText(g, "Dictado con el botón del micrófono", _semi14, TextColor, new RectangleF(InnerX, y + 14, 400, 18), StringAlignment.Near);
            DrawText(g, VoiceInput.IsInstalled ? "Whisper en tu PC · sin internet · detecta solo cuando terminas de hablar" : "Falta Whisper (whisper.cpp y su modelo, ~190 MB)", _reg12, Gray,
                new RectangleF(InnerX, y + 34, InnerW, 16), StringAlignment.Near);
            DrawRowDivider(g, y + 62);
            DrawToggleRow(g, y + 62, "Leer las respuestas en voz alta", null, SuiteSettings.AssistantSpeak,
                () => { SuiteSettings.AssistantSpeak = !SuiteSettings.AssistantSpeak; SuiteSettings.Save(); });
            // Charon (Gemini) con Piper Ald de respaldo si no hay cupo o internet; las demás, locales
            DrawSegmented(g, new RectangleF(InnerX, y + 112, InnerW, 40), new[] { "Charon (Gemini)", "Ald (local)", "Claude (local)", "Windows" },
                (int)SuiteSettings.AssistantVoice, i =>
                {
                    SuiteSettings.AssistantVoice = (SuiteSettings.AssistantVoiceKind)i;
                    SuiteSettings.Save();
                    VoiceOutput.Speak("Hola, así sueno yo."); // Muestra al elegir
                });

            y = 476;
            DrawCard(g, y, 98);
            DrawText(g, "Atajo del asistente", _semi14, TextColor, new RectangleF(InnerX, y + 14, 300, 20), StringAlignment.Near);
            DrawSegmented(g, new RectangleF(InnerX, y + 44, InnerW, 40), new[] { "Ctrl + Alt + A", "Solo desde la búsqueda (Ctrl + Alt + S)" },
                SuiteSettings.AssistantHotkey ? 0 : 1, i => { SuiteSettings.AssistantHotkey = i == 0; SuiteSettings.Save(); });

            // Micrófono automático al abrir con el atajo (el dictado se envía solo al dejar de hablar)
            y = 588;
            DrawCard(g, y, 58);
            DrawToggleRow(g, y, "Escuchar al abrir con Ctrl + Alt + A", "El micrófono se enciende solo; si empiezas a escribir, se apaga",
                SuiteSettings.AssistantAutoListen, () => { SuiteSettings.AssistantAutoListen = !SuiteSettings.AssistantAutoListen; SuiteSettings.Save(); });

            // Agentes de IA: la isla avisa cuando Claude Code trabaja, termina o pide permiso
            y = 660;
            DrawCard(g, y, 58);
            bool connected = AgentNotifier.ClaudeConnected;
            DrawAssistantRow(g, y + 4, "Avisos de Claude Code en la isla",
                connected ? "Conectado · te avisa al terminar o al pedir permiso" : "La isla te avisa cuando termina o te pide permiso",
                connected ? Color.FromArgb(74, 222, 128) : Gray, connected ? "Desconectar" : "Conectar",
                () =>
                {
                    string error = AgentNotifier.ConnectClaude(!connected);
                    if (error != null) EventBus.Instance.Publish(Core.Events.NotchShowMessage, "⚠️ No se pudo conectar con Claude Code");
                    Invalidate();
                });
        }

        // Fila con título, estado de color y un botón opcional a la derecha
        private void DrawAssistantRow(Graphics g, float y, string title, string status, Color statusColor, string button, Action action)
        {
            float buttonW = button != null ? g.MeasureString(button, _reg13).Width + 24 : 0;
            DrawText(g, title, _semi13, TextColor, new RectangleF(InnerX, y + 6, InnerW - buttonW - 16, 18), StringAlignment.Near);
            DrawText(g, status, _reg12, statusColor, new RectangleF(InnerX, y + 26, InnerW - buttonW - 16, 16), StringAlignment.Near);
            if (button == null) return;
            RectangleF rect = new RectangleF(ContentRight - 16 - buttonW, y + 10, buttonW, 30);
            FillRound(g, rect, 8, Hover(rect) ? Color.FromArgb(40, 47, 62) : Field);
            StrokeRound(g, rect, 8, SoftBorder, 1, false);
            DrawText(g, button, _reg13, TextColor, rect, StringAlignment.Center);
            AddHit(rect, action);
        }

        private void RefreshOllamaStatus()
        {
            if (_ollamaChecking || _ollamaWork != null || (DateTime.Now - _ollamaCheckedAt).TotalSeconds < 5) return;
            _ollamaChecking = true;
            System.Threading.ThreadPool.QueueUserWorkItem(state =>
            {
                AssistantService.OllamaStatus status = AssistantService.CheckOllama();
                _ollama = status;
                _ollamaCheckedAt = DateTime.Now;
                _ollamaChecking = false;
                InvalidateSafe();
            });
        }

        // Instala Ollama con winget (el instalador oficial de Windows) y después descarga el modelo
        private void InstallOllama()
        {
            _ollamaWork = "Instalando Ollama… (puede tardar un par de minutos)";
            System.Threading.ThreadPool.QueueUserWorkItem(state =>
            {
                try
                {
                    System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo("winget",
                        "install --id Ollama.Ollama --exact --silent --accept-package-agreements --accept-source-agreements")
                    { CreateNoWindow = true, UseShellExecute = false };
                    using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info)) process.WaitForExit();
                    AssistantService.StartOllama();
                    for (int i = 0; i < 30 && !AssistantService.CheckOllama().Running; i++) System.Threading.Thread.Sleep(1000);
                    _ollamaWork = null;
                    BeginInvoke((Action)DownloadModel);
                }
                catch (Exception ex)
                {
                    Log.Error("[SettingsWindow] No se pudo instalar Ollama: {0}", ex.Message);
                    _ollamaWork = null;
                    _ollamaCheckedAt = DateTime.MinValue;
                    InvalidateSafe();
                }
            });
        }

        private void DownloadModel()
        {
            _ollamaWork = "Descargando el modelo…";
            AssistantService.PullModel(
                (progress, status) =>
                {
                    _ollamaWork = progress >= 0 ? string.Format("Descargando el modelo… {0:0} %", progress * 100) : "Preparando el modelo…";
                    InvalidateSafe();
                },
                error =>
                {
                    _ollamaWork = null;
                    _ollamaCheckedAt = DateTime.MinValue;
                    if (error != null) Log.Error("[SettingsWindow] {0}", error);
                    InvalidateSafe();
                });
        }

        // La clave se pega del portapapeles y se comprueba con Google antes de guardarla
        private void PasteGeminiKey()
        {
            string key = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
            if (key.Length < 20 || key.IndexOf(' ') >= 0)
            {
                _geminiMessage = "El portapapeles no tiene una clave (copia la de aistudio.google.com/apikey)";
                return;
            }
            _geminiMessage = "Comprobando la clave…";
            System.Threading.ThreadPool.QueueUserWorkItem(state =>
            {
                try
                {
                    string model = AssistantService.PickGeminiModel(key);
                    SuiteSettings.GeminiApiKey = key;
                    _geminiModel = model;
                    _geminiMessage = null;
                    BeginInvoke((Action)SuiteSettings.Save);
                }
                catch (Exception ex)
                {
                    Log.Error("[SettingsWindow] Clave de Gemini rechazada: {0}", ex.Message);
                    _geminiMessage = "Google no aceptó esa clave";
                }
                InvalidateSafe();
            });
        }

        private static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(url); }
            catch (Exception ex) { Log.Error("[SettingsWindow] No se pudo abrir {0}: {1}", url, ex.Message); }
        }

        #endregion

        #region Página Menú de inicio (H2)

        private void DrawStart(Graphics g)
        {
            DrawTitle(g, "Menú de inicio");

            float y = 66;
            DrawCard(g, y, 124);
            DrawToggleRow(g, y + 4, "Abrir con la tecla Windows", "Sustituye al menú de Windows · Win + E, Win + D… siguen igual", SuiteSettings.StartMenuWinKey,
                () => { SuiteSettings.StartMenuWinKey = !SuiteSettings.StartMenuWinKey; SuiteSettings.Save(); });
            DrawRowDivider(g, y + 62);
            DrawToggleRow(g, y + 62, "Mostrar archivos recientes", "Tus últimos archivos abiertos en Windows", SuiteSettings.StartMenuShowRecent,
                () => { SuiteSettings.StartMenuShowRecent = !SuiteSettings.StartMenuShowRecent; SuiteSettings.Save(); });

            y = 204;
            DrawCard(g, y, 70);
            DrawText(g, "Apps ancladas", _semi14, TextColor, new RectangleF(InnerX, y + 14, 330, 18), StringAlignment.Near);
            DrawText(g, "Clic derecho en el menú para anclar o quitar · Restablecer pone tus 12 más usadas", _reg12, Gray, new RectangleF(InnerX, y + 36, InnerW - 130, 16), StringAlignment.Near);
            const string resetText = "Restablecer";
            float buttonW = g.MeasureString(resetText, _reg13).Width + 24;
            RectangleF reset = new RectangleF(ContentRight - 16 - buttonW, y + 20, buttonW, 30);
            FillRound(g, reset, 8, Hover(reset) ? Color.FromArgb(40, 47, 62) : Field);
            StrokeRound(g, reset, 8, SoftBorder, 1, false);
            DrawText(g, resetText, _reg13, TextColor, reset, StringAlignment.Center);
            AddHit(reset, StartMenuService.ResetPinned);
        }

        #endregion

        #region Página Barra de tareas (I2)

        private void DrawTaskbar(Graphics g)
        {
            DrawTitle(g, "Barra de tareas");

            float y = 66;
            DrawCard(g, y, 66);
            DrawToggleRow(g, y + 4, "Islas en la barra de Windows", "Tu barra real con islas; al apagarlo o cerrar la suite vuelve la normal", SuiteSettings.TaskbarEnabled,
                () => { SuiteSettings.TaskbarEnabled = !SuiteSettings.TaskbarEnabled; SuiteSettings.Save(); });

            // Por ahora solo los formatos que no mueven piezas (juntas al centro y cápsula siguen en pruebas)
            y = 146;
            DrawCard(g, y, 110);
            DrawText(g, "Formato", _semi14, TextColor, new RectangleF(InnerX, y + 14, 400, 20), StringAlignment.Near);
            int format = SuiteSettings.TaskbarFormat == SuiteSettings.TaskbarFormatKind.Full ? 1 : 0;
            DrawSegmented(g, new RectangleF(InnerX, y + 44, InnerW, 40), new[] { "3 islas a los extremos", "Completa" }, format, i =>
            {
                SuiteSettings.TaskbarFormat = i == 1 ? SuiteSettings.TaskbarFormatKind.Full : SuiteSettings.TaskbarFormatKind.Islands;
                SuiteSettings.TaskbarEdges = true;
                SuiteSettings.Save();
            });
            DrawText(g, "Juntas al centro y cápsula: en pruebas", _reg12, Gray, new RectangleF(InnerX, y + 88, InnerW, 16), StringAlignment.Near);

            y = 270;
            DrawCard(g, y, 96);
            DrawText(g, "Búsqueda en la barra", _semi14, TextColor, new RectangleF(InnerX, y + 14, 400, 20), StringAlignment.Near);
            DrawSegmented(g, new RectangleF(InnerX, y + 44, InnerW, 40), new[] { "Completa", "Solo icono", "Oculta" }, (int)SuiteSettings.TaskbarSearch,
                i => { SuiteSettings.TaskbarSearch = (SuiteSettings.TaskbarSearchKind)i; SuiteSettings.Save(); });

            // (El «Estilo de cristal» se quitó: solo cambiaba el menú de inicio y no se notaba; el usuario lo pidió)
            // Mini reproductor (sugerencia del mockup de la barra): con él, la isla de arriba no repite la música
            y = 380;
            DrawCard(g, y, 66);
            DrawToggleRow(g, y + 4, "Mini reproductor en la barra", "Una isla más con la canción; la isla de arriba deja de mostrar música", SuiteSettings.TaskbarMiniPlayer,
                () => { SuiteSettings.TaskbarMiniPlayer = !SuiteSettings.TaskbarMiniPlayer; SuiteSettings.Save(); });

            // La skin (compartida con la isla) se elige en Apariencia, con vista previa
            y += 66 + 14;
            DrawCard(g, y, 58);
            DrawAssistantRow(g, y + 4, "Skin: " + IslandSkin.Names[Math.Max(0, Math.Min(IslandSkin.Count - 1, SuiteSettings.TaskbarSkin))],
                "La misma para la isla y la barra", Gray, "Elegir en Apariencia", () => { _page = Page.Appearance; Invalidate(); });
        }
        #endregion

        #region Página Herramientas (todo lo que tiene atajo, también con botón)

        private void DrawTools(Graphics g)
        {
            DrawTitle(g, "Herramientas");
            float y = 66;
            var rows = new[]
            {
                new { Title = "Claude en la isla", Detail = "Ctrl + Alt + C · claude.ai con tu cuenta, sin API", Button = "Abrir", Action = (Action)(() => IslandTools.Run(IslandTools.OpenClaude)) },
                new { Title = "Historial del portapapeles", Detail = "Ctrl + Alt + V · lo último que copiaste, con buscador", Button = "Abrir", Action = (Action)(() => IslandTools.Run(IslandTools.OpenClipboard)) },
                new { Title = "Enviar y recibir con el celular", Detail = "Ctrl + Alt + R · QR por tu Wi-Fi, sin internet", Button = "Abrir", Action = (Action)(() => IslandTools.Run(IslandTools.OpenPhone)) },
                new { Title = "Calculadora", Detail = "Escribe la cuenta en la isla · Enter copia el resultado", Button = "Abrir", Action = (Action)(() => IslandTools.Run(IslandTools.OpenCalculator)) },
                new { Title = "Captura de pantalla", Detail = "Win + Shift + S · el recorte de Windows", Button = "Capturar", Action = (Action)(() => { Close(); IslandTools.Screenshot(); }) },
                new { Title = "Bloquear la PC", Detail = "Win + L", Button = "Bloquear", Action = (Action)IslandTools.Lock },
                new { Title = "Silenciar el micrófono", Detail = "Ctrl + Alt + M · la isla muestra un aviso rojo mientras está silenciado", Button = "Probar", Action = (Action)(() => IslandTools.Run(IslandTools.ToggleMic)) },
                new { Title = "Pausar avisos (No molestar)", Detail = "Ctrl + Alt + N", Button = SuiteSettings.DoNotDisturb ? "Reanudar" : "Pausar", Action = (Action)(() => NotificationService.ToggleDoNotDisturb()) },
                new { Title = "Asistente IA", Detail = SuiteSettings.AssistantHotkey ? "Ctrl + Alt + A" : "Desde la búsqueda", Button = "Ajustes", Action = (Action)(() => { _page = Page.Assistant; }) },
                new { Title = "Novedades de esta versión", Detail = "Lo nuevo de la " + AppInfo.Version + ", con botones para probarlo", Button = "Ver", Action = (Action)(() => new WhatsNewWindow().Show()) }
            };
            DrawCard(g, y, 8 + rows.Length * 56);
            for (int i = 0; i < rows.Length; i++)
            {
                DrawAssistantRow(g, y + 4 + i * 56, rows[i].Title, rows[i].Detail, Gray, rows[i].Button, rows[i].Action);
                if (i < rows.Length - 1) DrawRowDivider(g, y + 4 + i * 56 + 54);
            }
            DrawText(g, "Los botones Claude, Portapapeles y Celular también están en el Centro de control de la isla.", _reg12, Gray,
                new RectangleF(InnerX, y + 8 + rows.Length * 56 + 10, InnerW, 18), StringAlignment.Near);
        }

        #endregion

        #region Página Apariencia (mockup «Skins de la isla y la barra»)

        private void DrawAppearance(Graphics g)
        {
            DrawTitle(g, "Apariencia");

            // Skins: 16 cuadros con una mini isla pintada por el skin real
            float y = 66;
            DrawCard(g, y, 300);
            DrawText(g, "Skin de la isla y la barra", _semi14, TextColor, new RectangleF(InnerX, y + 10, 300, 20), StringAlignment.Near);
            float gap = 8, tileW = (InnerW - 3 * gap) / 4, tileH = 58;
            for (int i = 0; i < IslandSkin.Count; i++)
            {
                int index = i;
                RectangleF tile = new RectangleF(InnerX + (i % 4) * (tileW + gap), y + 38 + (i / 4) * (tileH + 6), tileW, tileH);
                bool selected = SuiteSettings.TaskbarSkin == i;
                FillRound(g, tile, 10, selected ? Color.FromArgb(46, Accent) : Hover(tile) ? Color.FromArgb(40, 47, 62) : Field);
                StrokeRound(g, tile, 10, selected ? Color.FromArgb(140, Accent) : SoftBorder, 1, false);
                // Mini isla sobre un trocito de "fondo de pantalla"
                RectangleF wall = new RectangleF(tile.X + 6, tile.Y + 6, tile.Width - 12, 30);
                using (LinearGradientBrush sky = new LinearGradientBrush(wall, Color.FromArgb(91, 143, 214), Color.FromArgb(75, 43, 107), 90f))
                using (GraphicsPath wallPath = LayeredWindow.RoundedRect(wall, 6)) g.FillPath(sky, wallPath);
                RectangleF pill = new RectangleF(wall.X + 8, wall.Y + 4, wall.Width - 16, 22);
                IslandSkin.PreviewOverride = i;
                try
                {
                    using (GraphicsPath shape = LayeredWindow.RoundedRect(pill, 11))
                    using (GraphicsPath outline = LayeredWindow.RoundedRect(RectangleF.Inflate(pill, -0.5f, -0.5f), 10.5f))
                    {
                        SmoothingMode old = g.SmoothingMode;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        IslandSkin.Paint(g, shape, outline, pill, ColorTranslator.FromHtml(SuiteSettings.IslandColor));
                        g.SmoothingMode = old;
                    }
                    IslandEyes.DrawPreview(g, new PointF(pill.X + 20, pill.Y + pill.Height / 2), 0.36f, IslandEyes.Mood.Idle);
                    DrawText(g, "22°", _reg11, IslandPainter.InkBase, new RectangleF(pill.X + 34, pill.Y, pill.Width - 38, pill.Height), StringAlignment.Center);
                }
                finally
                {
                    IslandSkin.PreviewOverride = null;
                }
                DrawText(g, IslandSkin.Names[i], _reg12, selected ? ValueColor : TextColor, new RectangleF(tile.X, wall.Bottom, tile.Width, tile.Bottom - wall.Bottom), StringAlignment.Center);
                AddHit(tile, () => { SuiteSettings.TaskbarSkin = index; SuiteSettings.Save(); });
            }

            // Forma, letra, borde y fondo
            y += 300 + 14;
            DrawCard(g, y, 204);
            string[] labels = { "Forma", "Letra", "Borde", "Fondo" };
            for (int r = 0; r < labels.Length; r++)
            {
                float row = y + 6 + r * 50;
                DrawText(g, labels[r], _semi14, TextColor, new RectangleF(InnerX, row, 100, 40), StringAlignment.Near);
                RectangleF seg = new RectangleF(ContentRight - 16 - 360, row + 4, 360, 32);
                if (r == 0) DrawSegmented(g, seg, new[] { "Pegada arriba", "Flotante", "Redondeada" }, (int)SuiteSettings.IslandStyle,
                    i => { SuiteSettings.IslandStyle = (SuiteSettings.IslandStyleKind)i; SuiteSettings.Save(); });
                else if (r == 1) DrawSegmented(g, seg, new[] { "Moderna", "Geométrica", "Terminal" }, (int)SuiteSettings.IslandFont,
                    i => { SuiteSettings.IslandFont = (SuiteSettings.FontKind)i; SuiteSettings.Save(); });
                else if (r == 2) DrawSegmented(g, seg, new[] { "Del skin", "Sin borde", "Fino", "Brillo" }, (int)SuiteSettings.IslandBorder,
                    i => { SuiteSettings.IslandBorder = (SuiteSettings.BorderKind)i; SuiteSettings.Save(); });
                else DrawSegmented(g, seg, new[] { "Sólido", "Cristal" }, SuiteSettings.IslandGlass ? 1 : 0,
                    i => { SuiteSettings.IslandGlass = i == 1; SuiteSettings.Save(); });
                if (r < labels.Length - 1) DrawRowDivider(g, row + 46);
            }

            // Skins claros (Papel, Hielo): la suite pone Windows en modo claro y al volver deja el modo que tenías
        }

        private static bool WindowsLightTaskbar
        {
            get
            {
                object v = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0);
                return v is int && (int)v == 1;
            }
        }

        #endregion
        #region Página Ojitos (mockup «Ojitos del notch»)

        private void DrawEyes(Graphics g)
        {
            DrawTitle(g, "Ojitos");

            float y = 66;

            // Isla cerrada: widgets, o los ojitos con lo que va a su lado. Cuadros con vista previa, como los estilos
            DrawCard(g, y, 156);
            DrawText(g, "Isla cerrada", _semi14, TextColor, new RectangleF(InnerX, y + 10, 200, 20), StringAlignment.Near);
            DrawText(g, "Un tiempo o la música reemplazan esto solos", _reg12, Gray, new RectangleF(InnerX + 110, y + 10, InnerW - 110, 20), StringAlignment.Far);
            string[] sideNames = { "Widgets", "Solo ojitos", "Clima", "Hora", "Hora y clima", "Fecha", "CPU y RAM", "Próximo evento" };
            WeatherService.Conditions weather = WeatherService.Shared.Current;
            string temp = weather != null ? string.Format("{0:0}°", weather.TemperatureC) : "24°";
            string[] sideSamples = { temp + "  CPU 12 %", null, temp, DateTime.Now.ToString("HH:mm"), DateTime.Now.ToString("HH:mm") + " · " + temp,
                                     DateTime.Now.ToString("ddd d MMM", new System.Globalization.CultureInfo("es-MX")).Replace(".", ""), "CPU 12 %", "18:00 Gym" };
            bool eyesOn = SuiteSettings.IslandIdle == SuiteSettings.IslandIdleKind.Eyes;
            float tileGap = 8, tileW = (InnerW - 3 * tileGap) / 4, tileH = 52;
            for (int i = 0; i < sideNames.Length; i++)
            {
                int index = i;
                RectangleF tile = new RectangleF(InnerX + (i % 4) * (tileW + tileGap), y + 38 + (i / 4) * (tileH + 8), tileW, tileH);
                bool selected = i == 0 ? !eyesOn : eyesOn && (int)SuiteSettings.EyesSide == i - 1;
                FillRound(g, tile, 10, selected ? Color.FromArgb(46, Accent) : Hover(tile) ? Color.FromArgb(40, 47, 62) : Field);
                StrokeRound(g, tile, 10, selected ? Color.FromArgb(140, Accent) : SoftBorder, 1, false);

                // Mini isla de muestra
                RectangleF pill = new RectangleF(tile.X + 6, tile.Y + 6, tile.Width - 12, 24);
                FillRound(g, pill, 12, Color.Black);
                string sample = sideSamples[i];
                if (i == 0)
                {
                    DrawText(g, sample, _reg11, Color.White, pill, StringAlignment.Center);
                }
                else if (sample == null)
                {
                    IslandEyes.DrawPreview(g, new PointF(pill.X + pill.Width / 2, pill.Y + pill.Height / 2), 0.42f, IslandEyes.Mood.Idle);
                }
                else
                {
                    IslandEyes.DrawPreview(g, new PointF(pill.X + 20, pill.Y + pill.Height / 2), 0.34f, IslandEyes.Mood.Idle);
                    DrawText(g, sample, _reg11, Color.White, new RectangleF(pill.X + 36, pill.Y, pill.Width - 40, pill.Height), StringAlignment.Center);
                }
                DrawText(g, sideNames[i], _reg12, selected ? ValueColor : TextColor, new RectangleF(tile.X, pill.Bottom + 2, tile.Width, 18), StringAlignment.Center);
                AddHit(tile, () =>
                {
                    if (index == 0) SuiteSettings.IslandIdle = SuiteSettings.IslandIdleKind.Widgets;
                    else { SuiteSettings.IslandIdle = SuiteSettings.IslandIdleKind.Eyes; SuiteSettings.EyesSide = (SuiteSettings.EyesSideKind)(index - 1); }
                    SuiteSettings.Save();
                });
            }

            // Muestra: el estilo elegido en cuatro estados
            y += 156 + 14;
            DrawCard(g, y, 96);
            IslandEyes.Mood[] moods = { IslandEyes.Mood.Idle, IslandEyes.Mood.Happy, IslandEyes.Mood.Surprised, IslandEyes.Mood.Sleep };
            string[] moodNames = { "Normal", "Feliz", "Sorpresa", "Dormido" };
            float cellW = (InnerW - 3 * 10) / 4;
            for (int i = 0; i < moods.Length; i++)
            {
                RectangleF pill = new RectangleF(InnerX + i * (cellW + 10) + (cellW - 110) / 2, y + 14, 110, 44);
                FillRound(g, pill, 22, Color.Black);
                IslandEyes.DrawPreview(g, new PointF(pill.X + pill.Width / 2, pill.Y + pill.Height / 2), 0.95f, moods[i]);
                DrawText(g, moodNames[i], _reg12, Gray, new RectangleF(InnerX + i * (cellW + 10), y + 64, cellW, 20), StringAlignment.Center);
            }

            // Estilo: 25 opciones en cinco filas
            y += 96 + 14;
            DrawCard(g, y, 206);
            DrawText(g, "Estilo", _semi14, TextColor, new RectangleF(InnerX, y + 10, 200, 20), StringAlignment.Near);
            string[] styles = { "Actual", "Con pupila", "Anime", "Gato", "Robot", "Puntitos", "Pixel", "Visor LED", "Cíclope",
                                "Corazones", "Estrellas", "Lentes", "Búho", "Somnoliento", "Ciber",
                                "Fantasma", "Kawaii", "Pirata", "Espiral", "Cachorro", "Monóculo", "Alien", "Panda", "Kaomoji", "Zombi" };
            float chipGap = 6, chipW = (InnerW - 4 * chipGap) / 5;
            for (int i = 0; i < styles.Length; i++)
            {
                int index = i;
                RectangleF chip = new RectangleF(InnerX + (i % 5) * (chipW + chipGap), y + 40 + (i / 5) * 32, chipW, 28);
                bool selected = (int)SuiteSettings.EyesStyle == i;
                FillRound(g, chip, 8, selected ? Color.FromArgb(46, Accent) : Hover(chip) ? Color.FromArgb(40, 47, 62) : Field);
                StrokeRound(g, chip, 8, selected ? Color.FromArgb(140, Accent) : SoftBorder, 1, false);
                DrawText(g, styles[i], _reg12, selected ? ValueColor : TextColor, chip, StringAlignment.Center);
                AddHit(chip, () => { SuiteSettings.EyesStyle = (SuiteSettings.EyesStyleKind)index; SuiteSettings.Save(); });
            }

            // Color y extras
            y += 206 + 14;
            DrawCard(g, y, 96);
            DrawText(g, "Color", _semi14, TextColor, new RectangleF(InnerX, y + 4, 120, 40), StringAlignment.Near);
            string[] colors = { "#FFFFFF", "#38BDF8", "#F472B6", "#A3E635", "#FBBF24" };
            const float swatch = 22, swatchGap = 10;
            float sx = ContentRight - 16 - (colors.Length * (swatch + swatchGap) - swatchGap);
            foreach (string hex in colors)
            {
                RectangleF circle = new RectangleF(sx, y + 13, swatch, swatch);
                using (SolidBrush fill = new SolidBrush(ColorTranslator.FromHtml(hex))) g.FillEllipse(fill, circle);
                if (string.Equals(SuiteSettings.EyesColor, hex, StringComparison.OrdinalIgnoreCase))
                {
                    using (Pen ring = new Pen(Accent, 2f)) g.DrawEllipse(ring, RectangleF.Inflate(circle, 3, 3));
                }
                string picked = hex;
                AddHit(circle, () => { SuiteSettings.EyesColor = picked; SuiteSettings.Save(); });
                sx += swatch + swatchGap;
            }
            string[] extras = { "Cejas", "Boquita", "Rubor" };
            bool[] extraOn = { SuiteSettings.EyesBrows, SuiteSettings.EyesMouth, SuiteSettings.EyesBlush };
            float extraW = (InnerW - 2 * 8) / 3;
            for (int i = 0; i < extras.Length; i++)
            {
                int index = i;
                bool on = extraOn[i];
                RectangleF cell = new RectangleF(InnerX + i * (extraW + 8), y + 50, extraW, 32);
                FillRound(g, cell, 10, CardFill);
                DrawText(g, extras[i], _reg13, TextColor, new RectangleF(cell.X + 10, cell.Y, extraW - 50, cell.Height), StringAlignment.Near);
                DrawToggle(g, cell.Right - 10 - 30, cell.Y + 8, on, true, () =>
                {
                    if (index == 0) SuiteSettings.EyesBrows = !on;
                    else if (index == 1) SuiteSettings.EyesMouth = !on;
                    else SuiteSettings.EyesBlush = !on;
                    SuiteSettings.Save();
                });
            }

            // Personalidad
            y += 96 + 14;
            DrawCard(g, y, 118);
            DrawToggleRow(g, y, "Reaccionan a lo que pasa", "Música, avisos, sin internet, Bloq Mayús, tareas, clic derecho…", SuiteSettings.EyesMoods,
                () => { SuiteSettings.EyesMoods = !SuiteSettings.EyesMoods; SuiteSettings.Save(); });
            DrawRowDivider(g, y + 59);
            DrawToggleRow(g, y + 60, "Hacen cositas cuando no usas la PC", "Bostezan, cabecean, silban o se estiran; al minuto se duermen", SuiteSettings.EyesFidgets,
                () => { SuiteSettings.EyesFidgets = !SuiteSettings.EyesFidgets; SuiteSettings.Save(); });
        }

        #endregion

        #region Página Botón de Inicio (mockup «Icono de Inicio»)

        private static Bitmap[] _startIconPreviews;

        private void DrawStartIcon(Graphics g)
        {
            DrawTitle(g, "Botón de Inicio");
            if (_startIconPreviews == null)
            {
                _startIconPreviews = new Bitmap[StartIconRenderer.Names.Length];
                for (int i = 0; i < _startIconPreviews.Length; i++) _startIconPreviews[i] = StartIconRenderer.Render(i, 72);
            }

            // Galería: 7 iconos en dos filas
            float y = 66;
            DrawCard(g, y, 230);
            DrawText(g, "Icono", _semi14, TextColor, new RectangleF(InnerX, y + 10, 200, 20), StringAlignment.Near);
            const int perRow = 4;
            float gap = 10, tileW = (InnerW - (perRow - 1) * gap) / perRow, tileH = 82;
            for (int i = 0; i < StartIconRenderer.Names.Length; i++)
            {
                int index = i;
                RectangleF tile = new RectangleF(InnerX + (i % perRow) * (tileW + gap), y + 40 + (i / perRow) * (tileH + gap), tileW, tileH);
                bool selected = SuiteSettings.StartIcon == i;
                FillRound(g, tile, 10, selected ? Color.FromArgb(46, Accent) : Hover(tile) ? Color.FromArgb(40, 47, 62) : Field);
                StrokeRound(g, tile, 10, selected ? Color.FromArgb(140, Accent) : SoftBorder, 1, false);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_startIconPreviews[i], tile.X + (tile.Width - 32) / 2, tile.Y + 12, 32, 32);
                DrawText(g, StartIconRenderer.Names[i], _reg12, selected ? ValueColor : TextColor, new RectangleF(tile.X, tile.Y + 50, tile.Width, 22), StringAlignment.Center);
                AddHit(tile, () =>
                {
                    if (index == StartIconRenderer.Custom && !PickStartImage()) return;
                    SuiteSettings.StartIcon = index;
                    SuiteSettings.Save();
                });
            }

            // Tamaño y efecto
            y += 230 + 14;
            DrawCard(g, y, 108);
            DrawInlineSlider(g, y + 14, "Tamaño", SuiteSettings.StartIconSize + " %", (SuiteSettings.StartIconSize - 70) / 70f,
                f => { SuiteSettings.StartIconSize = 70 + (int)Math.Round(f * 14) * 5; SuiteSettings.NotifyChanged(); });
            DrawText(g, "Al pasar el ratón", _semi14, TextColor, new RectangleF(InnerX, y + 54, 160, 40), StringAlignment.Near);
            DrawSegmented(g, new RectangleF(ContentRight - 16 - 300, y + 58, 300, 34), new[] { "Nada", "Crece", "Gira" }, SuiteSettings.StartIconHover,
                i => { SuiteSettings.StartIconHover = i; SuiteSettings.Save(); });

            // Qué abre el clic (el derecho siempre da el menú Win + X de Windows)
            y += 108 + 14;
            DrawCard(g, y, 76);
            DrawText(g, "Al darle clic abre", _semi14, TextColor, new RectangleF(InnerX, y + 4, 200, 40), StringAlignment.Near);
            DrawSegmented(g, new RectangleF(ContentRight - 16 - 300, y + 8, 300, 34), new[] { "Menú de Streeck", "Menú de Windows" },
                SuiteSettings.StartButtonOpensSuite ? 0 : 1, i => { SuiteSettings.StartButtonOpensSuite = i == 0; SuiteSettings.Save(); });
            DrawText(g, "El clic derecho sigue mostrando el menú de Windows (Win + X).", _reg12, Gray, new RectangleF(InnerX, y + 48, InnerW, 20), StringAlignment.Near);

            y += 76 + 14;
            DrawCard(g, y, 64);
            string note = SuiteSettings.TaskbarEnabled
                ? "«Windows original» deja el icono como siempre."
                : "Activa «Islas en la barra de Windows» en Barra de tareas para ver el icono.";
            DrawText(g, note, _reg12, SuiteSettings.TaskbarEnabled ? Gray : Color.FromArgb(251, 191, 36), new RectangleF(InnerX, y + 8, InnerW, 20), StringAlignment.Near);
            DrawText(g, "«Tu imagen»: mejor un PNG cuadrado con fondo transparente (mínimo 64×64).", _reg12, Gray, new RectangleF(InnerX, y + 34, InnerW, 20), StringAlignment.Near);
        }

        // «Tu imagen»: elegir un PNG, ICO, JPG o BMP
        private bool PickStartImage()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Elige la imagen para el botón de Inicio";
                dialog.Filter = "Imágenes|*.png;*.ico;*.jpg;*.jpeg;*.bmp;*.gif";
                if (dialog.ShowDialog(this) != DialogResult.OK) return false;
                if (!StartIconRenderer.ImportCustom(dialog.FileName))
                {
                    MessageBox.Show(this, "No se pudo abrir esa imagen.", "Streeck Suite", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            if (_startIconPreviews != null)
            {
                _startIconPreviews[StartIconRenderer.Custom].Dispose();
                _startIconPreviews[StartIconRenderer.Custom] = StartIconRenderer.Render(StartIconRenderer.Custom, 72);
            }
            return true;
        }

        #endregion

        #region Página Acerca de (mockup «Actualizaciones Streeck»)

        private static Image _appLogo;

        private void DrawAbout(Graphics g)
        {
            DrawTitle(g, "Acerca de");
            UpdateService.State state = UpdateService.Current;

            float y = 66;
            bool downloading = state == UpdateService.State.Downloading;
            DrawCard(g, y, downloading ? 96 : 78);
            if (_appLogo == null)
            {
                try { _appLogo = Icon.ExtractAssociatedIcon(Application.ExecutablePath).ToBitmap(); }
                catch (Exception) { }
            }
            RectangleF logo = new RectangleF(InnerX, y + 16, 46, 46);
            if (_appLogo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(_appLogo, logo);
            }
            DrawText(g, "Streeck Suite " + AppInfo.Version, _semi14, TextColor, new RectangleF(InnerX + 60, y + 18, 260, 20), StringAlignment.Near);
            string status;
            switch (state)
            {
                case UpdateService.State.Checking: status = "Revisando GitHub…"; break;
                case UpdateService.State.UpToDate: status = "Al día (en GitHub: " + UpdateService.LatestVersion + ") · revisado " + Ago(UpdateService.LastChecked); break;
                case UpdateService.State.Available: status = string.Format("Hay una versión nueva: {0} ({1:0.0} MB)", UpdateService.LatestVersion, UpdateService.SizeBytes / 1048576.0); break;
                case UpdateService.State.Downloading: status = "Descargando " + UpdateService.LatestVersion + "… se cierra y se vuelve a abrir sola"; break;
                case UpdateService.State.Error: status = "No se pudo revisar: " + UpdateService.ErrorText; break;
                default: status = "Aún no se ha revisado"; break;
            }
            DrawText(g, status, _reg12, state == UpdateService.State.Available ? ValueColor : Gray, new RectangleF(InnerX + 60, y + 40, 300, 18), StringAlignment.Near);

            string action = state == UpdateService.State.Available ? "Actualizar ahora"
                          : state == UpdateService.State.Checking ? "Buscando…"
                          : downloading ? "Descargando…"
                          : state == UpdateService.State.Error ? "Reintentar" : "Buscar actualizaciones";
            RectangleF button = new RectangleF(ContentRight - 16 - 170, y + 22, 170, 34);
            bool primary = state == UpdateService.State.Available;
            FillRound(g, button, 9, primary ? Accent : Hover(button) ? Color.FromArgb(40, 47, 62) : Field);
            if (!primary) StrokeRound(g, button, 9, SoftBorder, 1, false);
            DrawText(g, action, _semi13, primary ? Back : TextColor, button, StringAlignment.Center);
            if (state != UpdateService.State.Checking && !downloading)
            {
                AddHit(button, () => { if (UpdateService.Current == UpdateService.State.Available) UpdateService.Install(); else UpdateService.Check(); });
            }
            if (downloading)
            {
                RectangleF track = new RectangleF(InnerX, y + 76, InnerW, 6);
                FillRound(g, track, 3, TrackColor);
                FillRound(g, new RectangleF(track.X, track.Y, Math.Max(6, track.Width * UpdateService.Progress), 6), 3, Accent);
            }
            y += (downloading ? 96 : 78) + 14;

            // Novedades de la versión nueva (las notas del release en GitHub)
            if (state == UpdateService.State.Available && !string.IsNullOrEmpty(UpdateService.Notes))
            {
                string[] lines = UpdateService.Notes.Replace("\r", "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int count = Math.Min(6, lines.Length);
                DrawCard(g, y, 44 + count * 20);
                DrawText(g, "Novedades de la " + UpdateService.LatestVersion, _semi14, TextColor, new RectangleF(InnerX, y + 12, 400, 20), StringAlignment.Near);
                for (int i = 0; i < count; i++)
                {
                    string line = lines[i].Trim().TrimStart('-', '*', '#', ' ');
                    DrawText(g, "•  " + line, _reg12, Gray, new RectangleF(InnerX, y + 38 + i * 20, InnerW, 18), StringAlignment.Near);
                }
                y += 44 + count * 20 + 14;
            }

            DrawCard(g, y, 118);
            DrawToggleRow(g, y, "Buscar solo al iniciar y cada día", "Revisa GitHub en segundo plano; si hay algo nuevo te avisa la isla", SuiteSettings.UpdateAuto,
                () => { SuiteSettings.UpdateAuto = !SuiteSettings.UpdateAuto; SuiteSettings.Save(); });
            DrawRowDivider(g, y + 59);
            DrawToggleRow(g, y + 60, "Instalar sola", "Cuando no uses la PC (nunca a media partida) y al volver ves las novedades",
                SuiteSettings.UpdateInstallAuto, () => { SuiteSettings.UpdateInstallAuto = !SuiteSettings.UpdateInstallAuto; SuiteSettings.Save(); });
            y += 118 + 14;

            DrawCard(g, y, 52);
            DrawText(g, "github.com/" + AppInfo.GitHubRepo, _reg13, ValueColor, new RectangleF(InnerX, y, 360, 52), StringAlignment.Near);
            RectangleF open = new RectangleF(ContentRight - 16 - 120, y + 10, 120, 32);
            FillRound(g, open, 9, Hover(open) ? Color.FromArgb(40, 47, 62) : Field);
            StrokeRound(g, open, 9, SoftBorder, 1, false);
            DrawText(g, "Abrir página", _semi13, TextColor, open, StringAlignment.Center);
            AddHit(open, () => OpenUrl("https://github.com/" + AppInfo.GitHubRepo + "/releases"));
            y += 52 + 14;

            // Diagnóstico: registro y mapa de la barra (barra_skin.log, barra_arbol.txt) para reportar fallas
            DrawCard(g, y, 58);
            DrawText(g, "Diagnóstico", _semi14, TextColor, new RectangleF(InnerX, y + 10, 300, 18), StringAlignment.Near);
            DrawText(g, "Registros de la suite y de la barra, para reportar una falla", _reg12, Gray, new RectangleF(InnerX, y + 30, 360, 16), StringAlignment.Near);
            RectangleF folder = new RectangleF(ContentRight - 16 - 120, y + 13, 120, 32);
            FillRound(g, folder, 9, Hover(folder) ? Color.FromArgb(40, 47, 62) : Field);
            StrokeRound(g, folder, 9, SoftBorder, 1, false);
            DrawText(g, "Abrir carpeta", _semi13, TextColor, folder, StringAlignment.Center);
            AddHit(folder, () =>
            {
                try { System.Diagnostics.Process.Start("explorer.exe", System.IO.Path.GetDirectoryName(Log.FilePath)); }
                catch (Exception ex) { Log.Error("[Settings] No se pudo abrir la carpeta: {0}", ex.Message); }
            });
        }

        private static string Ago(DateTime when)
        {
            double minutes = (DateTime.Now - when).TotalMinutes;
            if (minutes < 1) return "ahora";
            if (minutes < 60) return "hace " + (int)minutes + " min";
            if (minutes < 1440) return "hace " + (int)(minutes / 60) + " h";
            return "hace " + (int)(minutes / 1440) + " días";
        }

        #endregion

        #region Página Widgets (P4)

        private void DrawWidgets(Graphics g)
        {
            DrawTitle(g, "Widgets");

            float y = 66;
            string[] ids = WidgetVisibility.AllIds;
            DrawCard(g, y, 8 + ids.Length * 54);
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                float row = y + 4 + i * 54;
                DrawText(g, WidgetVisibility.DisplayName(id), _semi14, TextColor, new RectangleF(InnerX, row + 10, 360, 18), StringAlignment.Near);
                DrawText(g, i < WidgetDescriptions.Length ? WidgetDescriptions[i] : "", _reg12, Gray, new RectangleF(InnerX, row + 30, 360, 16), StringAlignment.Near);
                DrawToggle(g, ContentRight - 16 - 44, row + 15, !WidgetVisibility.IsHidden(id), false,
                    () => EventBus.Instance.Publish(Core.Events.WidgetToggle, id));
                if (i < ids.Length - 1)
                {
                    using (Pen line = new Pen(Divider, 1)) g.DrawLine(line, InnerX, row + 54, InnerX + InnerW, row + 54);
                }
            }

            y += 8 + ids.Length * 54 + 14;
            DrawCard(g, y, 96);
            DrawText(g, "Color de acento", _semi14, TextColor, new RectangleF(InnerX, y + 14, 300, 20), StringAlignment.Near);
            bool presetSelected = false;
            for (int i = 0; i < AccentPresets.Length; i++)
            {
                Color color = AccentPresets[i];
                RectangleF dot = new RectangleF(InnerX + i * 48, y + 46, 36, 36);
                bool selected = SameColor(SuiteSettings.WidgetAccent, color);
                presetSelected |= selected;
                using (SolidBrush brush = new SolidBrush(color)) g.FillEllipse(brush, dot);
                if (selected || Hover(dot))
                {
                    using (Pen ring = new Pen(selected ? Color.White : DashedBorder, 2)) g.DrawEllipse(ring, RectangleF.Inflate(dot, -1, -1));
                }
                AddHit(dot, () => { SuiteSettings.WidgetAccent = color; SuiteSettings.Save(); });
            }
            DrawCustomColorButton(g, new RectangleF(InnerX + AccentPresets.Length * 48, y + 46, 136, 36), 18,
                presetSelected ? (Color?)null : SuiteSettings.WidgetAccent,
                () => PickColor(SuiteSettings.WidgetAccent, c => { SuiteSettings.WidgetAccent = c; SuiteSettings.Save(); }));

            y += 96 + 14;
            DrawCard(g, y, 64);
            DrawLabelValue(g, y + 14, "Opacidad del cristal", SuiteSettings.WidgetGlassPercent + " %", _semi14, _bold14);
            DrawSlider(g, new RectangleF(InnerX, y + 44, InnerW, 6), SuiteSettings.WidgetGlassPercent / 100f,
                f => { SuiteSettings.WidgetGlassPercent = (int)Math.Round(f * 100); SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);

            y += 64 + 14;
            const string resetText = "Recolocar widgets en su sitio";
            float buttonW = g.MeasureString(resetText, _reg13).Width + 28;
            RectangleF reset = new RectangleF(ContentRight - buttonW, y, buttonW, 34);
            FillRound(g, reset, 8, Hover(reset) ? Color.FromArgb(40, 47, 62) : Field);
            StrokeRound(g, reset, 8, SoftBorder, 1, false);
            DrawText(g, resetText, _reg13, TextColor, reset, StringAlignment.Center);
            AddHit(reset, () => EventBus.Instance.Publish(Core.Events.WidgetsResetLayout));
        }

        #endregion

        #region Página Reloj 3D (P5)

        private void DrawClock(Graphics g)
        {
            DepthClockModule clock = DepthClockModule.Current;
            ClockSettings s = clock != null ? clock.CurrentSettings : ClockSettings.Load();

            DrawTitle(g, "Reloj 3D");
            // A la izquierda del botón de cerrar (que ocupa la esquina superior derecha)
            DrawToggle(g, W - 44 - 12 - 44, 25, s.Enabled, false, () => ChangeClock(c => c.Enabled = !c.Enabled));

            // «Mover reloj»: arrastrarlo a cualquier parte de la pantalla, como en Samsung
            const string moveText = "Mover reloj";
            float moveW = g.MeasureString(moveText, _reg13).Width + 24;
            RectangleF move = new RectangleF(W - 44 - 12 - 44 - 16 - moveW, 22, moveW, 30);
            FillRound(g, move, 8, Hover(move) ? Color.FromArgb(40, 47, 62) : Field);
            StrokeRound(g, move, 8, SoftBorder, 1, false);
            DrawText(g, moveText, _reg13, TextColor, move, StringAlignment.Center);
            AddHit(move, ClockPlacementWindow.Open);

            // Con varios monitores y el fondo sin «Extender», Windows encoge o recorta la imagen panorámica en cada
            // pantalla (y el reloj la copia igual): un clic la pone de un monitor a otro
            if (Screen.AllScreens.Length > 1 && WallpaperInfo.GetStyle() != 22)
            {
                const string spanText = "Extender fondo en mis monitores";
                float spanW = g.MeasureString(spanText, _reg13).Width + 24;
                RectangleF span = new RectangleF(move.X - 10 - spanW, 22, spanW, 30);
                FillRound(g, span, 8, Hover(span) ? Color.FromArgb(46, Accent) : Color.FromArgb(30, Accent));
                StrokeRound(g, span, 8, Color.FromArgb(140, Accent), 1, false);
                DrawText(g, spanText, _reg13, ValueColor, span, StringAlignment.Center);
                AddHit(span, () => { WallpaperInfo.ApplySpan(); Invalidate(); });
            }

            // Estilo: clásico (el sujeto pasa por delante) o estirado (estilo Samsung, mockup R1)
            bool stretched = s.Style == ClockStyle.Samsung;
            float y = 62;
            DrawCard(g, y, 66);
            Screen[] screens = Screen.AllScreens;
            bool isMulti = ForceMultiMonitorPreview || (screens != null && screens.Length > 1);
            if (isMulti)
            {
                float gap = 16;
                float halfW = (InnerW - gap) / 2;

                // Columna izquierda: Estilo
                DrawText(g, "Estilo", _semi13, TextColor, new RectangleF(InnerX, y + 8, halfW, 18), StringAlignment.Near);
                DrawSegmented(g, new RectangleF(InnerX, y + 28, halfW, 30), new[] { "Detrás", "Contorno" },
                    (int)s.Style, i => ChangeClock(c => c.Style = (ClockStyle)i));

                // Columna derecha: Pantalla destino
                float rightX = InnerX + halfW + gap;
                DrawText(g, "Pantalla", _semi13, TextColor, new RectangleF(rightX, y + 8, halfW, 18), StringAlignment.Near);

                string[] screenNames;
                if (ForceMultiMonitorPreview && (screens == null || screens.Length <= 1))
                {
                    screenNames = new[] { "P1 (1080p ★)", "P2 (1440p)" };
                }
                else
                {
                    screenNames = new string[screens.Length];
                    for (int i = 0; i < screens.Length; i++)
                    {
                        if (screens.Length == 2)
                            screenNames[i] = string.Format("P{0} ({1}p{2})", i + 1, Math.Min(screens[i].Bounds.Width, screens[i].Bounds.Height), screens[i].Primary ? " ★" : "");
                        else
                            screenNames[i] = string.Format("P{0}{1}", i + 1, screens[i].Primary ? " ★" : "");
                    }
                }

                int selectedMonitor = ForceMultiMonitorP2 ? 1 : Math.Max(0, Math.Min(s.MonitorIndex, screenNames.Length - 1));
                DrawSegmented(g, new RectangleF(rightX, y + 28, halfW, 30), screenNames,
                    selectedMonitor, i => ChangeClock(c => c.MonitorIndex = i));
            }
            else
            {
                DrawText(g, "Estilo", _semi13, TextColor, new RectangleF(InnerX, y + 8, 200, 18), StringAlignment.Near);
                DrawSegmented(g, new RectangleF(InnerX, y + 28, InnerW, 30), new[] { "Detrás del sujeto", "Contorno" },
                    (int)s.Style, i => ChangeClock(c => c.Style = (ClockStyle)i));
            }

            // Posición y tamaño (el estirado suma cuánto puede crecer un número)
            y = 136;
            DrawCard(g, y, 60);
            string[] labels = stretched ? new[] { "Horizontal", "Vertical", "Tamaño", "Estiramiento" } : new[] { "Horizontal", "Vertical", "Tamaño" };
            float[] fractions = { s.CenterX, s.Top / 0.8f, (s.Size - 0.15f) / 0.65f, (s.SamsungMaxStretch - 0.25f) / 0.65f };
            Action<float>[] setters =
            {
                f => ChangeClock(c => c.CenterX = f),
                f => ChangeClock(c => c.Top = f * 0.8f),
                f => ChangeClock(c => c.Size = 0.15f + f * 0.65f),
                f => ChangeClock(c => c.SamsungMaxStretch = 0.25f + f * 0.65f)
            };
            float columnW = (InnerW - 12 * (labels.Length - 1)) / labels.Length;
            for (int i = 0; i < labels.Length; i++)
            {
                float x = InnerX + i * (columnW + 12);
                DrawText(g, labels[i], _semi13, TextColor, new RectangleF(x, y + 14, columnW, 18), StringAlignment.Near);
                DrawSlider(g, new RectangleF(x, y + 40, columnW, 6), fractions[i], setters[i], null);
            }

            // Fuente (los dos estilos), grosor y color (estirado) y lo que acompaña a la hora
            y = 204;
            DrawCard(g, y, stretched ? 296 : 204);
            float row = y + 4;
            DrawText(g, "Fuente", _semi14, TextColor, new RectangleF(InnerX, row, 200, 50), StringAlignment.Near);
            RectangleF combo = new RectangleF(ContentRight - 16 - 220, row + 9, 220, 32);
            FillRound(g, combo, 8, Hover(combo) ? Color.FromArgb(40, 47, 62) : Field);
            StrokeRound(g, combo, 8, Color.FromArgb(26, 255, 255, 255), 1, false);
            DrawText(g, s.FontFamily, _reg13, TextColor, new RectangleF(combo.X + 10, combo.Y, combo.Width - 36, combo.Height), StringAlignment.Near);
            using (Pen chevron = new Pen(TextColor, 1.6f))
            {
                float cx = combo.Right - 18, cy = combo.Y + combo.Height / 2;
                g.DrawLines(chevron, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
            }
            AddHit(combo, () => ShowFontPicker(combo, s.FontFamily));
            DrawRowDivider(g, row + 50);
            row += 50;

            if (stretched)
            {
                DrawText(g, "Grosor", _semi14, TextColor, new RectangleF(InnerX, row, 200, 46), StringAlignment.Near);
                DrawSegmented(g, new RectangleF(ContentRight - 16 - 240, row + 8, 240, 30), new[] { "Fino", "Normal", "Grueso" }, s.SamsungWeight,
                    i => ChangeClock(c => c.SamsungWeight = i));
                DrawRowDivider(g, row + 46);
                row += 46;

                DrawText(g, "Color", _semi14, TextColor, new RectangleF(InnerX, row, 200, 46), StringAlignment.Near);
                // Colores rápidos (con negro) y uno personalizado, para fondos claros u oscuros
                string[] colors = { "#ffffff", "#000000", "#ffd666", "#ffebf0", "#26c6da", "#a78bfa" };
                const float swatch = 22, swatchGap = 10;
                const float customW = 130;
                float sx = ContentRight - 16 - customW - 12 - (colors.Length * (swatch + swatchGap) - swatchGap);
                bool presetSelected = false;
                foreach (string hex in colors)
                {
                    RectangleF circle = new RectangleF(sx, row + 12, swatch, swatch);
                    using (SolidBrush fill = new SolidBrush(ColorTranslator.FromHtml(hex))) g.FillEllipse(fill, circle);
                    using (Pen edge = new Pen(Color.FromArgb(60, 255, 255, 255), 1)) g.DrawEllipse(edge, circle); // Para ver el negro
                    if (string.Equals(s.SamsungColor, hex, StringComparison.OrdinalIgnoreCase))
                    {
                        presetSelected = true;
                        using (Pen ring = new Pen(Accent, 2f)) g.DrawEllipse(ring, RectangleF.Inflate(circle, 3, 3));
                    }
                    string picked = hex;
                    AddHit(circle, () => ChangeClock(c => c.SamsungColor = picked));
                    sx += swatch + swatchGap;
                }
                Color currentColor;
                try { currentColor = ColorTranslator.FromHtml(s.SamsungColor); } catch (Exception) { currentColor = Color.White; }
                DrawCustomColorButton(g, new RectangleF(ContentRight - 16 - customW, row + 7, customW, 32), 16,
                    presetSelected ? (Color?)null : currentColor,
                    () => PickColor(currentColor, c => ChangeClock(cs => cs.SamsungColor = ColorTranslator.ToHtml(c))));
                DrawRowDivider(g, row + 46);
                row += 46;
            }
            DrawToggleRow(g, row, "Fecha encima de la hora", null, s.ShowDate, () => ChangeClock(c => c.ShowDate = !c.ShowDate));
            DrawRowDivider(g, row + 44);
            row += 44;
            DrawToggleRow(g, row, "Mostrar segundos", null, s.ShowSeconds, () => ChangeClock(c => c.ShowSeconds = !c.ShowSeconds));
            DrawRowDivider(g, row + 44);
            row += 44;
            DrawToggleRow(g, row, "Clima debajo de la hora", "Usa tu ubicación aproximada por IP", s.ShowWeather,
                () => ChangeClock(c => c.ShowWeather = !c.ShowWeather));

            // Silueta (estirado) o qué pasa por delante de la hora (clásico)
            y = stretched ? 512 : 416;
            bool? subject = clock != null ? clock.SubjectFound : (bool?)null;
            if (stretched)
            {
                DrawCard(g, y, 62);
                DrawText(g, "Silueta de tu fondo", _semi14, TextColor, new RectangleF(InnerX, y + 10, 400, 20), StringAlignment.Near);
                string status = subject == null ? "Analizando tu fondo…"
                    : subject.Value ? "La IA detectó el sujeto: cada número se estira hasta 12 px antes de su silueta."
                    : "Tu fondo no tiene un sujeto claro: los números se detienen en lo cercano.";
                DrawText(g, status, _reg12, Gray, new RectangleF(InnerX, y + 34, InnerW, 18), StringAlignment.Near);
            }
            else
            {
                DrawCard(g, y, 124);
                DrawText(g, "Qué pasa por delante de la hora", _semi14, TextColor, new RectangleF(InnerX, y + 14, 400, 20), StringAlignment.Near);
                DrawSegmented(g, new RectangleF(InnerX, y + 44, InnerW, 40), new[] { "Automático", "Solo el sujeto", "Todo lo cercano" },
                    (int)s.Mode, i => ChangeClock(c => c.Mode = (OcclusionMode)i));
                string status = subject == null ? "Analizando tu fondo…"
                    : subject.Value ? "La IA encontró un sujeto principal en tu fondo."
                    : "Tu fondo no tiene un sujeto claro: se usa la profundidad.";
                DrawText(g, status, _reg12, Gray, new RectangleF(InnerX, y + 94, InnerW, 16), StringAlignment.Near);
            }
        }

        // Cada cambio parte de una copia nueva: el módulo compara con sus ajustes anteriores para saber qué recalcular
        private void ChangeClock(Action<ClockSettings> edit)
        {
            DepthClockModule clock = DepthClockModule.Current;
            if (clock == null) return;
            ClockSettings settings = clock.CurrentSettings;
            edit(settings);
            clock.ApplySettings(settings);
            Invalidate();
        }

        private void DrawToggleRow(Graphics g, float row, string label, string description, bool on, Action toggle)
        {
            float height = description == null ? 44 : 58;
            if (description == null)
            {
                DrawText(g, label, _semi14, TextColor, new RectangleF(InnerX, row, 400, height), StringAlignment.Near);
            }
            else
            {
                DrawText(g, label, _semi14, TextColor, new RectangleF(InnerX, row + 10, 400, 18), StringAlignment.Near);
                DrawText(g, description, _reg12, Gray, new RectangleF(InnerX, row + 30, 400, 16), StringAlignment.Near);
            }
            DrawToggle(g, ContentRight - 16 - 44, row + (height - 24) / 2, on, false, toggle);
        }

        private void DrawRowDivider(Graphics g, float y)
        {
            using (Pen line = new Pen(Divider, 1)) g.DrawLine(line, InnerX, y, InnerX + InnerW, y);
        }

        private void ShowFontPicker(RectangleF logical, string current)
        {
            Rectangle onScreen = RectangleToScreen(new Rectangle(Px(logical.X), Px(logical.Y), Px(logical.Width), Px(logical.Height)));
            FontPicker picker = new FontPicker(current, _scale, name => ChangeClock(c => c.FontFamily = name));
            picker.Location = new Point(onScreen.Left, onScreen.Bottom + Px(4));
            picker.Width = onScreen.Width;
            picker.Show(this);
        }

        #endregion

        #region Piezas comunes

        private void AddHit(RectangleF rect, Action click)
        {
            _hits.Add(new Hit { Rect = rect, Click = click });
        }

        private void DrawTitle(Graphics g, string title)
        {
            DrawText(g, title, _title22, TextColor, new RectangleF(ContentX, 22, ContentW - 60, 30), StringAlignment.Near);
        }

        private static void DrawCard(Graphics g, float y, float height)
        {
            FillRound(g, new RectangleF(ContentX, y, ContentW, height), 12, CardFill);
        }

        private void DrawLabelValue(Graphics g, float y, string label, string value, Font labelFont, Font valueFont)
        {
            DrawText(g, label, labelFont, TextColor, new RectangleF(InnerX, y, InnerW - 110, 20), StringAlignment.Near);
            DrawText(g, value, valueFont, ValueColor, new RectangleF(InnerX + InnerW - 110, y, 110, 20), StringAlignment.Far);
        }

        // Interruptor: 44×24 (o 30×16 en las celdas de pestañas)
        private void DrawToggle(Graphics g, float x, float y, bool on, bool small, Action toggle)
        {
            float width = small ? 30 : 44, height = small ? 16 : 24, pad = small ? 2 : 3, knob = height - 2 * pad;
            RectangleF track = new RectangleF(x, y, width, height);
            FillRound(g, track, height / 2, on ? Accent : ToggleOff);
            float knobX = on ? track.Right - pad - knob : track.X + pad;
            using (SolidBrush brush = new SolidBrush(on ? Back : Gray)) g.FillEllipse(brush, knobX, y + pad, knob, knob);
            AddHit(RectangleF.Inflate(track, 4, 4), toggle);
        }

        // Deslizador: pista de 6 px con el relleno de color; el tirador aparece al pasar el ratón o arrastrar
        private void DrawSlider(Graphics g, RectangleF track, float fraction, Action<float> slide, Action commit)
        {
            fraction = Clamp01(fraction);
            FillRound(g, track, 3, TrackColor);
            if (fraction > 0) FillRound(g, new RectangleF(track.X, track.Y, Math.Max(6, track.Width * fraction), track.Height), 3, Accent);

            RectangleF area = new RectangleF(track.X - 6, track.Y - 9, track.Width + 12, track.Height + 18);
            _hits.Add(new Hit { Rect = area, Track = track, Slide = slide, Commit = commit });
            bool active = (_dragging != null && _dragging.Track == track) || Hover(area);
            if (active)
            {
                float knob = 14, x = track.X + track.Width * fraction;
                using (SolidBrush brush = new SolidBrush(TextColor)) g.FillEllipse(brush, x - knob / 2, track.Y + track.Height / 2 - knob / 2, knob, knob);
            }
        }

        // Botones segmentados: cada opción tan ancha como su texto y el espacio sobrante repartido
        private void DrawSegmented(Graphics g, RectangleF area, string[] labels, int selected, Action<int> pick)
        {
            FillRound(g, area, 10, CardFill);
            const float pad = 4, gap = 4;
            float available = area.Width - 2 * pad - gap * (labels.Length - 1);
            float[] widths = new float[labels.Length];
            float natural = 0;
            for (int i = 0; i < labels.Length; i++)
            {
                widths[i] = g.MeasureString(labels[i], _semi12).Width + 24;
                natural += widths[i];
            }
            float x = area.X + pad;
            for (int i = 0; i < labels.Length; i++)
            {
                int index = i;
                float width = natural <= available ? widths[i] + (available - natural) / labels.Length : widths[i] * available / natural;
                RectangleF button = new RectangleF(x, area.Y + pad, width, area.Height - 2 * pad);
                if (i == selected) FillRound(g, button, 8, ActiveFill);
                else if (Hover(button)) FillRound(g, button, 8, HoverFill);
                DrawText(g, labels[i], _semi12, i == selected ? Color.White : Gray, button, StringAlignment.Center);
                AddHit(button, () => pick(index));
                x += width + gap;
            }
        }

        // "Personalizado…": borde discontinuo; si el color actual no es ninguno de los predefinidos, se marca y lo muestra
        private void DrawCustomColorButton(Graphics g, RectangleF button, float radius, Color? custom, Action click)
        {
            if (Hover(button)) FillRound(g, button, radius, HoverFill);
            float textX = button.X + 14;
            if (custom != null)
            {
                StrokeRound(g, RectangleF.Inflate(button, -1, -1), radius - 1, Accent, 2, false);
                float dot = 14;
                using (SolidBrush brush = new SolidBrush(custom.Value)) g.FillEllipse(brush, button.X + 12, button.Y + (button.Height - dot) / 2, dot, dot);
                using (Pen ring = new Pen(SoftBorder, 1)) g.DrawEllipse(ring, button.X + 12, button.Y + (button.Height - dot) / 2, dot, dot);
                textX += 20;
            }
            else
            {
                StrokeRound(g, button, radius, DashedBorder, 1, true);
            }
            DrawText(g, "Personalizado…", _reg13, ButtonText, new RectangleF(textX, button.Y, button.Right - textX - 8, button.Height), StringAlignment.Near);
            AddHit(button, click);
        }

        private void PickColor(Color current, Action<Color> apply)
        {
            using (ColorDialog dialog = new ColorDialog())
            {
                dialog.Color = current;
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) == DialogResult.OK) apply(Color.FromArgb(255, dialog.Color));
            }
            Invalidate();
        }

        private static void FillRound(Graphics g, RectangleF rect, float radius, Color color)
        {
            using (GraphicsPath path = LayeredWindow.RoundedRect(rect, radius))
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        private static void StrokeRound(Graphics g, RectangleF rect, float radius, Color color, float width, bool dashed)
        {
            using (GraphicsPath path = LayeredWindow.RoundedRect(rect, radius))
            using (Pen pen = new Pen(color, width))
            {
                if (dashed) pen.DashPattern = new float[] { 3, 3 };
                g.DrawPath(pen, path);
            }
        }

        private static void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect, StringAlignment align)
        {
            using (StringFormat format = new StringFormat())
            using (SolidBrush brush = new SolidBrush(color))
            {
                format.Alignment = align;
                format.LineAlignment = StringAlignment.Center;
                format.Trimming = StringTrimming.EllipsisCharacter;
                format.FormatFlags = StringFormatFlags.NoWrap;
                g.DrawString(text ?? "", font, brush, rect, format);
            }
        }

        private static bool SameColor(Color a, Color b)
        {
            return a.R == b.R && a.G == b.G && a.B == b.B;
        }

        private static float Clamp01(float value)
        {
            return Math.Max(0f, Math.Min(1f, value));
        }

        #endregion
    }

    /// <summary>
    /// Lista desplegable de fuentes instaladas para la página Reloj 3D: cada nombre se ve con su propia fuente.
    /// Se cierra al elegir una, con Esc o al hacer clic fuera.
    /// </summary>
    internal class FontPicker : Form
    {
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        private static readonly Color Back = Color.FromArgb(30, 36, 48);
        private static readonly Color Selected = Color.FromArgb(50, 56, 189, 248);
        private static readonly Color TextColor = Color.FromArgb(230, 233, 239);

        private readonly ListBox _list = new ListBox();
        private readonly Dictionary<string, Font> _fonts = new Dictionary<string, Font>();
        private readonly Action<string> _choose;
        private readonly float _scale;
        private bool _chosen;

        public FontPicker(string current, float scale, Action<string> choose)
        {
            _choose = choose;
            _scale = scale;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(58, 65, 80);
            Padding = new Padding(1);
            Height = (int)(300 * scale);

            _list.Dock = DockStyle.Fill;
            _list.BorderStyle = BorderStyle.None;
            _list.BackColor = Back;
            _list.ForeColor = TextColor;
            _list.DrawMode = DrawMode.OwnerDrawFixed;
            _list.ItemHeight = (int)(28 * scale);
            _list.IntegralHeight = false;
            using (InstalledFontCollection installed = new InstalledFontCollection())
            {
                foreach (FontFamily family in installed.Families) _list.Items.Add(family.Name);
            }
            _list.SelectedItem = current;
            _list.DrawItem += OnDrawItem;
            _list.MouseClick += (s, e) => Choose();
            _list.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter) Choose();
                else if (e.KeyCode == Keys.Escape) Close();
            };
            Controls.Add(_list);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int corners = NativeMethods.DWMWCP_ROUNDSMALL;
            NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref corners, sizeof(int));
            SetWindowTheme(_list.Handle, "DarkMode_Explorer", null); // Barra de desplazamiento oscura
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_list.SelectedIndex > 3) _list.TopIndex = _list.SelectedIndex - 3;
            _list.Focus();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!IsDisposed) BeginInvoke((Action)Close);
        }

        private void Choose()
        {
            string name = _list.SelectedItem as string;
            if (name == null || _chosen) return;
            _chosen = true;
            _choose(name);
            Close();
        }

        private void OnDrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            string name = (string)_list.Items[e.Index];
            bool selected = (e.State & DrawItemState.Selected) != 0;
            using (SolidBrush back = new SolidBrush(Back)) e.Graphics.FillRectangle(back, e.Bounds);
            if (selected)
            {
                using (SolidBrush highlight = new SolidBrush(Selected)) e.Graphics.FillRectangle(highlight, e.Bounds);
            }
            e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            Rectangle text = new Rectangle(e.Bounds.X + (int)(10 * _scale), e.Bounds.Y, e.Bounds.Width - (int)(14 * _scale), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, name, FontFor(name), text, TextColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        // Fuente de muestra para cada nombre (las que no se pueden crear, p. ej. sin estilo normal, usan Segoe UI)
        private Font FontFor(string name)
        {
            Font font;
            if (_fonts.TryGetValue(name, out font)) return font;
            try
            {
                font = new Font(name, 13 * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            catch (ArgumentException)
            {
                font = new Font("Segoe UI", 13 * _scale, FontStyle.Regular, GraphicsUnit.Pixel);
            }
            _fonts[name] = font;
            return font;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foreach (Font font in _fonts.Values) font.Dispose();
                _fonts.Clear();
            }
            base.Dispose(disposing);
        }
    }
}
