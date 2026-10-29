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
        public enum Page { Privacy, Island, Notifications, Time, Tray, Widgets, Clock }

        // Medidas del mockup (a 96 PPP)
        private const float W = 800, H = 592, SideW = 210, ContentX = 238, ContentW = 534;
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
        private static readonly string[] WidgetDescriptions = { "CPU, RAM y GPU", "Lo que suena en cualquier app", "Tu lista de notas.txt" };
        private static readonly CultureInfo Spanish = new CultureInfo("es-ES");

        private static SettingsWindow _instance;

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
            if (_instance == null || _instance.IsDisposed) _instance = new SettingsWindow();
            _instance._page = page;
            if (!_instance.Visible) _instance.Show();
            if (_instance.WindowState == FormWindowState.Minimized) _instance.WindowState = FormWindowState.Normal;
            _instance.Activate();
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
                case Page.Notifications: DrawNotifications(g); break;
                case Page.Time: DrawTime(g); break;
                case Page.Tray: DrawTray(g); break;
                case Page.Widgets: DrawWidgets(g); break;
                case Page.Clock: DrawClock(g); break;
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
            }
        }

        private void DrawSidebar(Graphics g)
        {
            using (Pen line = new Pen(SideLine, 1)) g.DrawLine(line, SideW, 0, SideW, H);
            DrawText(g, "Streeck Suite", _bold15, TextColor, new RectangleF(22, 18, 180, 20), StringAlignment.Near);

            string[] names = { "Privacidad", "Isla", "Avisos", "Tiempo y agenda", "Bandeja y búsqueda", "Widgets", "Reloj 3D" };
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
                    case Page.Clock:
                        g.DrawEllipse(pen, 3, 3, 18, 18);
                        g.DrawLines(pen, new[] { new PointF(12, 7), new PointF(12, 12), new PointF(15, 14) });
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
                new[] { "Esconder (pegar el ratón arriba)", "Mostrar siempre", "Solo avisos" }, (int)SuiteSettings.IslandMaximized,
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
            DrawCard(g, y, 78);
            DrawInlineSlider(g, y + 12, "Tamaño", SuiteSettings.IslandScalePercent + " %", (SuiteSettings.IslandScalePercent - 70) / 50f,
                f => { SuiteSettings.IslandScalePercent = 70 + (int)Math.Round(f * 10) * 5; SuiteSettings.NotifyChanged(); });
            DrawInlineSlider(g, y + 44, "Opacidad", SuiteSettings.IslandOpacityPercent + " %", (SuiteSettings.IslandOpacityPercent - 50) / 50f,
                f => { SuiteSettings.IslandOpacityPercent = 50 + (int)Math.Round(f * 50); SuiteSettings.NotifyChanged(); });
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
            DrawCard(g, y, 102);
            DrawText(g, "Notificaciones en la isla", _semi14, TextColor, new RectangleF(InnerX, y + 12, 400, 18), StringAlignment.Near);
            DrawText(g, "Los avisos de tus apps aparecen arriba, además de en Windows", _reg12, Gray, new RectangleF(InnerX, y + 32, 420, 16), StringAlignment.Near);
            DrawToggle(g, ContentRight - 16 - 44, y + 19, SuiteSettings.NotificationsEnabled, false,
                () => { SuiteSettings.NotificationsEnabled = !SuiteSettings.NotificationsEnabled; SuiteSettings.Save(); });
            DrawLabelValue(g, y + 56, "Mostrar cada aviso durante", SecondsText(SuiteSettings.NotificationDurationMs), _reg13, _bold13);
            DrawSlider(g, new RectangleF(InnerX, y + 84, InnerW, 6), (SuiteSettings.NotificationDurationMs - 1000) / 9000f,
                f => { SuiteSettings.NotificationDurationMs = 1000 + (int)Math.Round(f * 9) * 1000; SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);

            y = 182;
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

        private string _googleMessage; // Error al pegar un enlace que no es de calendario

        private void DrawTime(Graphics g)
        {
            DrawTitle(g, "Tiempo y agenda");

            float y = 66;
            DrawCard(g, y, 162);
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

            y = 242;
            DrawCard(g, y, 66);
            DrawToggleRow(g, y + 4, "Sonido al terminar", "Usa el sonido de alarma de Windows", SuiteSettings.TimerSound,
                () => { SuiteSettings.TimerSound = !SuiteSettings.TimerSound; SuiteSettings.Save(); });

            y = 322;
            DrawCard(g, y, 210);
            DrawText(g, "Calendario", _semi14, TextColor, new RectangleF(InnerX, y + 12, 300, 20), StringAlignment.Near);

            int windowsCount = AgendaService.WindowsCount;
            string windowsStatus = !SuiteSettings.CalendarWindows ? "Desactivado"
                : windowsCount < 0 ? "Leyendo…" : windowsCount == 1 ? "1 evento hoy y mañana" : windowsCount + " eventos hoy y mañana";
            DrawToggleRow(g, y + 40, "Calendario de Windows (cuenta Microsoft)", windowsStatus, SuiteSettings.CalendarWindows,
                () => { SuiteSettings.CalendarWindows = !SuiteSettings.CalendarWindows; SuiteSettings.Save(); AgendaService.RefreshNow(); });
            DrawRowDivider(g, y + 98);

            bool hasGoogle = !string.IsNullOrEmpty(SuiteSettings.CalendarGoogleUrl);
            string googleStatus = _googleMessage ?? (hasGoogle ? (AgendaService.GoogleStatus.Length > 0 ? AgendaService.GoogleStatus : "Leyendo…")
                : "Copia el «enlace secreto en formato iCal» de tu Google Calendar");
            DrawText(g, "Google Calendar", _semi14, TextColor, new RectangleF(InnerX, y + 108, 360, 18), StringAlignment.Near);
            DrawText(g, googleStatus, _reg12, _googleMessage != null ? Color.FromArgb(253, 186, 116) : Gray, new RectangleF(InnerX, y + 128, InnerW - 130, 16), StringAlignment.Near);
            string buttonText = hasGoogle ? "Quitar" : "Pegar enlace";
            float buttonW = g.MeasureString(buttonText, _reg13).Width + 24;
            RectangleF button = new RectangleF(ContentRight - 16 - buttonW, y + 111, buttonW, 30);
            FillRound(g, button, 8, Hover(button) ? Color.FromArgb(40, 47, 62) : Field);
            StrokeRound(g, button, 8, SoftBorder, 1, false);
            DrawText(g, buttonText, _reg13, TextColor, button, StringAlignment.Center);
            AddHit(button, hasGoogle ? (Action)(() => SetGoogleUrl("")) : PasteGoogleUrl);
            DrawRowDivider(g, y + 156);

            DrawLabelValue(g, y + 166, "Avisar antes de cada evento",
                SuiteSettings.EventAlertMinutes == 0 ? "Sin aviso" : SuiteSettings.EventAlertMinutes + " min", _reg13, _bold13);
            DrawSlider(g, new RectangleF(InnerX, y + 192, InnerW, 6), SuiteSettings.EventAlertMinutes / 30f,
                f => { SuiteSettings.EventAlertMinutes = (int)Math.Round(f * 6) * 5; SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);
        }

        // El enlace iCal se pega desde el portapapeles (la ventana no tiene cuadros de texto)
        private void PasteGoogleUrl()
        {
            string text = Clipboard.ContainsText() ? Clipboard.GetText().Trim() : "";
            bool looksLikeIcal = (text.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase))
                                 && (text.Contains(".ics") || text.Contains("/ical/"));
            if (!looksLikeIcal)
            {
                _googleMessage = "El portapapeles no tiene un enlace iCal (termina en .ics)";
                return;
            }
            SetGoogleUrl(text);
        }

        private void SetGoogleUrl(string url)
        {
            _googleMessage = null;
            SuiteSettings.CalendarGoogleUrl = url;
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

        #region Página Widgets (P4)

        private void DrawWidgets(Graphics g)
        {
            DrawTitle(g, "Widgets");

            float y = 66;
            DrawCard(g, y, 170);
            string[] ids = WidgetVisibility.AllIds;
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

            y = 250;
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

            y = 360;
            DrawCard(g, y, 64);
            DrawLabelValue(g, y + 14, "Opacidad del cristal", SuiteSettings.WidgetGlassPercent + " %", _semi14, _bold14);
            DrawSlider(g, new RectangleF(InnerX, y + 44, InnerW, 6), SuiteSettings.WidgetGlassPercent / 100f,
                f => { SuiteSettings.WidgetGlassPercent = (int)Math.Round(f * 100); SuiteSettings.NotifyChanged(); }, SuiteSettings.Save);

            y = 438;
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
            if (clock == null)
            {
                DrawTitle(g, "Reloj 3D");
                DrawText(g, "El reloj 3D no está en marcha.", _reg13, Gray, new RectangleF(ContentX, 70, ContentW, 20), StringAlignment.Near);
                return;
            }
            ClockSettings s = clock.CurrentSettings;

            DrawTitle(g, "Reloj 3D");
            // A la izquierda del botón de cerrar (que ocupa la esquina superior derecha)
            DrawToggle(g, W - 44 - 12 - 44, 25, s.Enabled, false, () => ChangeClock(c => c.Enabled = !c.Enabled));

            float y = 66;
            DrawCard(g, y, 60);
            float columnW = (InnerW - 24) / 3;
            string[] labels = { "Horizontal", "Vertical", "Tamaño" };
            float[] fractions = { s.CenterX, s.Top / 0.8f, (s.Size - 0.15f) / 0.65f };
            Action<float>[] setters =
            {
                f => ChangeClock(c => c.CenterX = f),
                f => ChangeClock(c => c.Top = f * 0.8f),
                f => ChangeClock(c => c.Size = 0.15f + f * 0.65f)
            };
            for (int i = 0; i < 3; i++)
            {
                float x = InnerX + i * (columnW + 12);
                DrawText(g, labels[i], _semi13, TextColor, new RectangleF(x, y + 14, columnW, 18), StringAlignment.Near);
                DrawSlider(g, new RectangleF(x, y + 40, columnW, 6), fractions[i], setters[i], null);
            }

            y = 140;
            DrawCard(g, y, 204);
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
            DrawToggleRow(g, row, "Fecha encima de la hora", null, s.ShowDate, () => ChangeClock(c => c.ShowDate = !c.ShowDate));
            DrawRowDivider(g, row + 44);
            row += 44;
            DrawToggleRow(g, row, "Mostrar segundos", null, s.ShowSeconds, () => ChangeClock(c => c.ShowSeconds = !c.ShowSeconds));
            DrawRowDivider(g, row + 44);
            row += 44;
            DrawToggleRow(g, row, "Clima debajo de la hora", "Usa tu ubicación aproximada por IP", s.ShowWeather,
                () => ChangeClock(c => c.ShowWeather = !c.ShowWeather));

            y = 358;
            DrawCard(g, y, 124);
            DrawText(g, "Qué pasa por delante de la hora", _semi14, TextColor, new RectangleF(InnerX, y + 14, 400, 20), StringAlignment.Near);
            DrawSegmented(g, new RectangleF(InnerX, y + 44, InnerW, 40), new[] { "Automático", "Solo el sujeto", "Todo lo cercano" },
                (int)s.Mode, i => ChangeClock(c => c.Mode = (OcclusionMode)i));
            bool? subject = clock.SubjectFound;
            string status = subject == null ? "Analizando tu fondo…"
                : subject.Value ? "La IA encontró un sujeto principal en tu fondo."
                : "Tu fondo no tiene un sujeto claro: se usa la profundidad.";
            DrawText(g, status, _reg12, Gray, new RectangleF(InnerX, y + 94, InnerW, 16), StringAlignment.Near);
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
