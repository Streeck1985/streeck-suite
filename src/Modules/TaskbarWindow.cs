using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Barra de tareas de la suite (fase I, mockup I1) con las opciones del simulador: 3 islas (juntas, flotantes o a
    /// los extremos), cápsula o completa; búsqueda completa, solo icono u oculta; y el estilo de cristal global.
    /// Isla 1: inicio y búsqueda. Isla 2: apps ancladas y abiertas con sus iconos originales. Isla 3: privacidad,
    /// red, sonido, batería y reloj. Se pinta con UpdateLayeredWindow: fuera de las islas los clics pasan de largo.
    /// </summary>
    internal class TaskbarWindow : LayeredWindow
    {
        public const float BarHeight = 46, BottomMargin = 10;
        private const float Button = 40, IconSize = 26, IslandPad = 7, Radius = 22, EdgeMargin = 12, SearchWidth = 190;
        private static readonly Color Sky = Color.FromArgb(56, 189, 248);

        private readonly float _scale;
        private readonly IslandPainter _painter;
        private readonly EventBus _eventBus;
        private readonly Timer _refresh;
        private readonly ToolTip _tooltip = new ToolTip { InitialDelay = 400, ShowAlways = true };
        private List<TaskbarApp> _apps = new List<TaskbarApp>();
        private IntPtr _foreground;
        private string _clock = "";
        private readonly List<KeyValuePair<RectangleF, Action>> _hits = new List<KeyValuePair<RectangleF, Action>>();
        private readonly List<KeyValuePair<RectangleF, TaskbarApp>> _appHits = new List<KeyValuePair<RectangleF, TaskbarApp>>();
        private readonly List<KeyValuePair<RectangleF, string>> _tips = new List<KeyValuePair<RectangleF, string>>();
        private PointF _mouse = new PointF(-1, -1);
        private string _shownTip;
        private AudioService _audio;
        private string _networkGlyph = IslandPainter.GlyphOffline;
        private DateTime _statusAt = DateTime.MinValue;

        public TaskbarWindow(EventBus eventBus)
        {
            _eventBus = eventBus;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) _scale = screen.DpiX / 96f;
            _painter = new IslandPainter(_scale, 1f);
            TopMost = true;
            try { _audio = new AudioService(); } catch (Exception ex) { Log.Error("[Taskbar] Sin audio: {0}", ex.Message); }

            _refresh = new Timer { Interval = 500 };
            _refresh.Tick += (s, e) => RefreshApps();
            _refresh.Start();
            MouseClick += OnBarClick;
            MouseMove += (s, e) => { _mouse = new PointF(e.X, e.Y); UpdateTip(); RenderSurface(); };
            MouseLeave += (s, e) => { _mouse = new PointF(-1, -1); _tooltip.Hide(this); _shownTip = null; RenderSurface(); };
        }

        private float Px(float value) { return value * _scale; }

        /// <summary>
        /// Mensaje con el que Windows avisa a la AppBar (pantalla completa, cambios de posición).
        /// </summary>
        public const int AppBarCallback = 0x8000 + 0x2A1;
        private static readonly int TaskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");

        public event Action<int, IntPtr> AppBarNotify;
        public event Action ExplorerRestarted;

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == AppBarCallback && AppBarNotify != null) AppBarNotify((int)m.WParam, m.LParam);
            else if (m.Msg == TaskbarCreatedMessage && ExplorerRestarted != null) ExplorerRestarted();
            base.WndProc(ref m);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string message);

        /// <summary>
        /// Alto que la barra le reserva a las ventanas maximizadas (barra + margen inferior en los modos flotantes).
        /// </summary>
        public int ReservedHeight
        {
            get { return (int)Px(BarHeight + (SuiteSettings.TaskbarFormat == SuiteSettings.TaskbarFormatKind.Full ? 0 : BottomMargin + 4)); }
        }

        // Zona extra arriba para la sombra y el resplandor neón
        private int ShadowPad { get { return (int)Px(22); } }

        public void Relayout()
        {
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            int height = ReservedHeight + ShadowPad;
            Bounds = new Rectangle(screen.X, screen.Bottom - height, screen.Width, height);
            RenderSurface();
        }

        // Cada medio segundo: apps abiertas, ventana activa, reloj y (cada 5 s) red y batería
        private void RefreshApps()
        {
            if (!Visible) return;
            bool changed = false;
            List<TaskbarApp> apps = TaskbarService.Snapshot();
            if (!SameApps(apps, _apps)) { _apps = apps; changed = true; }
            else _apps = apps;
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground != _foreground) { _foreground = foreground; changed = true; }
            string clock = DateTime.Now.ToString("HH:mm");
            if (clock != _clock) { _clock = clock; changed = true; }
            if ((DateTime.Now - _statusAt).TotalSeconds >= 5) { _statusAt = DateTime.Now; _networkGlyph = NetworkGlyph(); changed = true; }
            if (changed) RenderSurface();
        }

        private static bool SameApps(List<TaskbarApp> a, List<TaskbarApp> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (a[i].Key != b[i].Key || a[i].Windows.Count != b[i].Windows.Count) return false;
            }
            return true;
        }

        #region Ratón

        private void OnBarClick(object sender, MouseEventArgs e)
        {
            PointF point = new PointF(e.X, e.Y);
            foreach (KeyValuePair<RectangleF, TaskbarApp> hit in _appHits)
            {
                if (!hit.Key.Contains(point)) continue;
                if (e.Button == MouseButtons.Right) ShowAppMenu(hit.Value, e.Location);
                else if (e.Button == MouseButtons.Middle) TaskbarService.Launch(hit.Value);
                else TaskbarService.Activate(hit.Value);
                RefreshApps();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            foreach (KeyValuePair<RectangleF, Action> hit in _hits)
            {
                if (hit.Key.Contains(point)) { hit.Value(); return; }
            }
        }

        // Clic derecho en una app: sus ventanas, abrir otra, anclar/desanclar y cerrar
        private void ShowAppMenu(TaskbarApp app, Point location)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            TrayMenuStyle.Apply(menu);
            foreach (IntPtr hwnd in app.Windows)
            {
                IntPtr target = hwnd;
                string title = TaskbarService.WindowTitle(hwnd);
                menu.Items.Add(TrayMenuStyle.Item(title.Length > 48 ? title.Substring(0, 47) + "…" : title, "", (s, e) => TaskbarService.Focus(target)));
            }
            if (app.Windows.Count > 0) menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(TrayMenuStyle.Item(app.Name, "", (s, e) => TaskbarService.Launch(app)));
            if (app.StartApp != null)
            {
                menu.Items.Add(TrayMenuStyle.Item(app.Pinned ? "Desanclar de la barra" : "Anclar a la barra", app.Pinned ? "" : "", (s, e) =>
                {
                    if (app.Pinned) TaskbarService.Unpin(app.StartApp.Id); else TaskbarService.Pin(app.StartApp.Id);
                    RefreshApps();
                }));
            }
            if (app.Windows.Count > 0)
            {
                menu.Items.Add(TrayMenuStyle.Item(app.Windows.Count > 1 ? "Cerrar todas las ventanas" : "Cerrar ventana", "", (s, e) => TaskbarService.CloseAll(app)));
            }
            menu.Show(this, location);
        }

        private void UpdateTip()
        {
            string tip = null;
            foreach (KeyValuePair<RectangleF, string> t in _tips) if (t.Key.Contains(_mouse)) { tip = t.Value; break; }
            if (tip == _shownTip) return;
            _shownTip = tip;
            if (tip == null) _tooltip.Hide(this);
            else _tooltip.Show(tip, this, (int)_mouse.X, (int)(ShadowPad - Px(30)), 2500);
        }

        private bool Hover(RectangleF rect) { return rect.Contains(_mouse); }

        #endregion

        #region Dibujo

        protected override void DrawSurface(Graphics g)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            _hits.Clear();
            _appHits.Clear();
            _tips.Clear();

            float top = ShadowPad;
            float barHeight = Px(BarHeight);
            float islandWidth1 = StartIslandWidth(g), islandWidth2 = AppsIslandWidth(), islandWidth3 = TrayIslandWidth(g);

            switch (SuiteSettings.TaskbarFormat)
            {
                case SuiteSettings.TaskbarFormatKind.Full:
                {
                    // Completa: todo el ancho, inicio a la izquierda, apps al centro y bandeja a la derecha
                    RectangleF bar = new RectangleF(0, top, Width, barHeight);
                    using (SolidBrush fill = new SolidBrush(Glass.Fill)) g.FillRectangle(fill, bar);
                    using (Pen line = new Pen(Glass.Border, 1)) g.DrawLine(line, 0, top, Width, top);
                    DrawStartContent(g, Px(EdgeMargin), top);
                    DrawAppsContent(g, (Width - islandWidth2) / 2, top);
                    DrawTrayContent(g, Width - Px(EdgeMargin) - islandWidth3, top);
                    break;
                }
                case SuiteSettings.TaskbarFormatKind.Capsule:
                {
                    // Cápsula: una sola píldora centrada con separadores
                    float separator = Px(18);
                    float total = islandWidth1 + islandWidth2 + islandWidth3 + 2 * separator;
                    float x = (Width - total) / 2;
                    Glass.DrawCard(g, new RectangleF(x, top, total, barHeight), Px(Radius + 2), Px(16), Px(6));
                    DrawStartContent(g, x, top);
                    DrawSeparator(g, x + islandWidth1 + separator / 2, top);
                    DrawAppsContent(g, x + islandWidth1 + separator, top);
                    DrawSeparator(g, x + islandWidth1 + separator + islandWidth2 + separator / 2, top);
                    DrawTrayContent(g, x + islandWidth1 + islandWidth2 + 2 * separator, top);
                    break;
                }
                default:
                {
                    // 3 islas: juntas, separadas por la distancia elegida o a los extremos de la pantalla
                    float x1, x2, x3;
                    if (SuiteSettings.TaskbarEdges)
                    {
                        x1 = Px(EdgeMargin);
                        x2 = (Width - islandWidth2) / 2;
                        x3 = Width - Px(EdgeMargin) - islandWidth3;
                    }
                    else
                    {
                        float gap = Px(SuiteSettings.TaskbarGap);
                        float total = islandWidth1 + islandWidth2 + islandWidth3 + 2 * gap;
                        x1 = (Width - total) / 2;
                        x2 = x1 + islandWidth1 + gap;
                        x3 = x2 + islandWidth2 + gap;
                    }
                    foreach (KeyValuePair<float, float> island in new[] { new KeyValuePair<float, float>(x1, islandWidth1), new KeyValuePair<float, float>(x2, islandWidth2), new KeyValuePair<float, float>(x3, islandWidth3) })
                    {
                        Glass.DrawCard(g, new RectangleF(island.Key, top, island.Value, barHeight), Px(Radius), Px(16), Px(6));
                    }
                    DrawStartContent(g, x1, top);
                    DrawAppsContent(g, x2, top);
                    DrawTrayContent(g, x3, top);
                    break;
                }
            }
        }

        private void DrawSeparator(Graphics g, float x, float top)
        {
            using (Pen line = new Pen(Color.FromArgb(30, 255, 255, 255), 1)) g.DrawLine(line, x, top + Px(12), x, top + Px(BarHeight) - Px(12));
        }

        private RectangleF ButtonRect(float x, float top, float width)
        {
            return new RectangleF(x, top + (Px(BarHeight) - Px(Button)) / 2, width, Px(Button));
        }

        // Isla 1: botón de inicio (logo de Windows en azul, como el simulador) y búsqueda
        private float StartIslandWidth(Graphics g)
        {
            float width = 2 * Px(IslandPad) + Px(Button);
            if (SuiteSettings.TaskbarSearch == SuiteSettings.TaskbarSearchKind.Full) width += Px(6) + Px(SearchWidth);
            else if (SuiteSettings.TaskbarSearch == SuiteSettings.TaskbarSearchKind.Icon) width += Px(4) + Px(Button);
            return width;
        }

        private void DrawStartContent(Graphics g, float x, float top)
        {
            x += Px(IslandPad);
            RectangleF start = ButtonRect(x, top, Px(Button));
            if (Hover(start)) IslandPainter.FillRounded(g, start, Px(10), Color.FromArgb(25, 255, 255, 255));
            float logo = Px(18), cell = logo / 2 - Px(1);
            float lx = start.X + (start.Width - logo) / 2, ly = start.Y + (start.Height - logo) / 2;
            using (SolidBrush blue = new SolidBrush(Sky))
            {
                g.FillRectangle(blue, lx, ly, cell, cell);
                g.FillRectangle(blue, lx + logo - cell, ly, cell, cell);
                g.FillRectangle(blue, lx, ly + logo - cell, cell, cell);
                g.FillRectangle(blue, lx + logo - cell, ly + logo - cell, cell, cell);
            }
            _hits.Add(new KeyValuePair<RectangleF, Action>(start, () => _eventBus.Publish(Core.Events.StartMenuToggle)));
            _tips.Add(new KeyValuePair<RectangleF, string>(start, "Inicio"));
            x = start.Right;

            if (SuiteSettings.TaskbarSearch == SuiteSettings.TaskbarSearchKind.Full)
            {
                RectangleF search = new RectangleF(x + Px(6), top + (Px(BarHeight) - Px(32)) / 2, Px(SearchWidth), Px(32));
                IslandPainter.FillRounded(g, search, Px(14), Color.FromArgb(Hover(search) ? 34 : 20, 255, 255, 255));
                _painter.DrawText(g, "", _painter.Glyph16, Color.FromArgb(195, 201, 212), new RectangleF(search.X + Px(10), search.Y, Px(18), search.Height), StringAlignment.Center);
                _painter.DrawText(g, "Buscar o preguntar a IA", _painter.Text12, IslandPainter.Gray, new RectangleF(search.X + Px(34), search.Y, search.Width - Px(40), search.Height), StringAlignment.Near);
                _hits.Add(new KeyValuePair<RectangleF, Action>(search, () => _eventBus.Publish(Core.Events.SearchOpen)));
            }
            else if (SuiteSettings.TaskbarSearch == SuiteSettings.TaskbarSearchKind.Icon)
            {
                RectangleF search = ButtonRect(x + Px(4), top, Px(Button));
                IslandPainter.FillRounded(g, search, Px(10), Color.FromArgb(Hover(search) ? 34 : 20, 255, 255, 255));
                _painter.DrawText(g, "", _painter.Glyph16, Color.FromArgb(195, 201, 212), search, StringAlignment.Center);
                _hits.Add(new KeyValuePair<RectangleF, Action>(search, () => _eventBus.Publish(Core.Events.SearchOpen)));
                _tips.Add(new KeyValuePair<RectangleF, string>(search, "Buscar (Ctrl + Alt + S)"));
            }
        }

        // Isla 2: apps con su icono original; raya azul bajo las abiertas (más larga en la que está delante)
        private float AppsIslandWidth()
        {
            return 2 * Px(IslandPad) + Math.Max(1, _apps.Count) * Px(Button + 4) - Px(4);
        }

        private void DrawAppsContent(Graphics g, float x, float top)
        {
            x += Px(IslandPad);
            foreach (TaskbarApp app in _apps)
            {
                RectangleF button = ButtonRect(x, top, Px(Button));
                bool focused = app.Windows.Contains(_foreground);
                if (Hover(button) || focused) IslandPainter.FillRounded(g, button, Px(10), Color.FromArgb(focused ? 30 : 20, 255, 255, 255));
                RectangleF icon = new RectangleF(button.X + (button.Width - Px(IconSize)) / 2, button.Y + (button.Height - Px(IconSize)) / 2 - Px(1), Px(IconSize), Px(IconSize));
                if (app.Icon != null)
                {
                    GraphicsState state = g.Save();
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(app.Icon, icon);
                    g.Restore(state);
                }
                else
                {
                    IslandPainter.FillRounded(g, icon, Px(7), Color.FromArgb(60, Sky));
                    _painter.DrawText(g, app.Name.Length > 0 ? app.Name.Substring(0, 1) : "?", _painter.Bold12, Color.White, icon, StringAlignment.Center);
                }
                if (app.Windows.Count > 0)
                {
                    float lineWidth = focused ? Px(16) : Px(6);
                    IslandPainter.FillRounded(g, new RectangleF(button.X + (button.Width - lineWidth) / 2, button.Bottom - Px(3), lineWidth, Px(3)), Px(1.5f),
                        focused ? Sky : Color.FromArgb(150, 255, 255, 255));
                }
                _appHits.Add(new KeyValuePair<RectangleF, TaskbarApp>(button, app));
                _tips.Add(new KeyValuePair<RectangleF, string>(button, app.Windows.Count > 0 ? TaskbarService.WindowTitle(app.Windows[0]) : app.Name));
                x = button.Right + Px(4);
            }
        }

        // Isla 3: ⌃ (iconos de Windows), privacidad, red/sonido/batería (configuración rápida) y reloj (notificaciones)
        private float TrayIslandWidth(Graphics g)
        {
            float clock = Math.Max(g.MeasureString("00:00", _painter.Semibold13).Width, g.MeasureString("00/00/0000", _painter.Text11).Width) + Px(12);
            float status = Px(24) * (Battery != null ? 3 : 2) + Px(Battery != null ? 34 : 8);
            return 2 * Px(IslandPad) + Px(28) + Px(34) + status + clock + Px(8);
        }

        private void DrawTrayContent(Graphics g, float x, float top)
        {
            x += Px(IslandPad);
            Color text = Color.FromArgb(230, 233, 239);

            RectangleF chevron = ButtonRect(x, top, Px(28));
            if (Hover(chevron)) IslandPainter.FillRounded(g, chevron, Px(8), Color.FromArgb(25, 255, 255, 255));
            _painter.DrawText(g, "", _painter.Glyph16, IslandPainter.Gray, chevron, StringAlignment.Center);
            _hits.Add(new KeyValuePair<RectangleF, Action>(chevron, () => _eventBus.Publish(Core.Events.TaskbarShowWindowsTray)));
            _tips.Add(new KeyValuePair<RectangleF, string>(chevron, "Iconos de Windows"));

            RectangleF shield = ButtonRect(chevron.Right, top, Px(34));
            bool privacy = AeroDwmModule.Current != null && AeroDwmModule.Current.IsPrivacyActive;
            if (Hover(shield) || privacy) IslandPainter.FillRounded(g, shield, Px(8), Color.FromArgb(privacy ? 50 : 25, privacy ? Sky : Color.White));
            _painter.DrawText(g, "", _painter.Glyph16, privacy ? Sky : text, shield, StringAlignment.Center);
            _hits.Add(new KeyValuePair<RectangleF, Action>(shield, () => _eventBus.Publish(Core.Events.PrivacyToggle)));
            _tips.Add(new KeyValuePair<RectangleF, string>(shield, "Modo privacidad (Ctrl + Alt + P)"));

            // Red, sonido y batería juntos: abren la configuración rápida de Windows
            float statusWidth = Px(24) * (Battery != null ? 3 : 2) + Px(Battery != null ? 34 : 8);
            RectangleF status = ButtonRect(shield.Right + Px(4), top, statusWidth);
            if (Hover(status)) IslandPainter.FillRounded(g, status, Px(8), Color.FromArgb(25, 255, 255, 255));
            float sx = status.X + Px(4);
            _painter.DrawText(g, _networkGlyph, _painter.Glyph16, text, new RectangleF(sx, status.Y, Px(24), status.Height), StringAlignment.Center);
            sx += Px(24);
            float level = _audio != null ? _audio.Level : 0.5f;
            bool muted = _audio != null && _audio.Muted;
            _painter.DrawText(g, VolumeGlyphs.ForLevel(level, muted), _painter.Glyph16, text, new RectangleF(sx, status.Y, Px(24), status.Height), StringAlignment.Center);
            sx += Px(24);
            if (Battery != null)
            {
                _painter.DrawText(g, IslandPainter.GlyphBattery, _painter.Glyph16, text, new RectangleF(sx, status.Y, Px(24), status.Height), StringAlignment.Center);
                _painter.DrawText(g, Battery, _painter.Text11, text, new RectangleF(sx + Px(22), status.Y, Px(36), status.Height), StringAlignment.Near);
            }
            _hits.Add(new KeyValuePair<RectangleF, Action>(status, () => PressWin(0x41))); // Win + A
            _tips.Add(new KeyValuePair<RectangleF, string>(status, "Red, sonido y más"));

            RectangleF clock = ButtonRect(status.Right + Px(4), top, x + TrayIslandWidth(g) - Px(2 * IslandPad) - status.Right - Px(4));
            if (Hover(clock)) IslandPainter.FillRounded(g, clock, Px(8), Color.FromArgb(25, 255, 255, 255));
            _painter.DrawText(g, _clock, _painter.Semibold13, text, new RectangleF(clock.X, clock.Y + Px(2), clock.Width - Px(6), Px(19)), StringAlignment.Far);
            _painter.DrawText(g, DateTime.Now.ToString("dd/MM/yyyy"), _painter.Text11, IslandPainter.Gray, new RectangleF(clock.X, clock.Y + Px(20), clock.Width - Px(6), Px(16)), StringAlignment.Far);
            _hits.Add(new KeyValuePair<RectangleF, Action>(clock, () => PressWin(0x4E))); // Win + N
            _tips.Add(new KeyValuePair<RectangleF, string>(clock, DateTime.Now.ToString("dddd, d 'de' MMMM 'de' yyyy", new System.Globalization.CultureInfo("es-MX"))));
        }

        // Batería en % (null en un equipo de escritorio)
        private static string Battery
        {
            get
            {
                PowerStatus power = SystemInformation.PowerStatus;
                return (power.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) != 0 ? null : string.Format("{0:0}%", power.BatteryLifePercent * 100);
            }
        }

        private static string NetworkGlyph()
        {
            try
            {
                NetworkInterface active = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
                    n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Tunnel && n.GetIPProperties().GatewayAddresses.Count > 0);
                if (active == null) return IslandPainter.GlyphOffline;
                return active.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? IslandPainter.GlyphWifi : IslandPainter.GlyphEthernet;
            }
            catch (NetworkInformationException)
            {
                return IslandPainter.GlyphOffline;
            }
        }

        // Atajos de Windows (Win + A configuración rápida, Win + N notificaciones)
        private static void PressWin(byte key)
        {
            NativeMethods.keybd_event(0x5B, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(key, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(key, 0, 2, UIntPtr.Zero);
            NativeMethods.keybd_event(0x5B, 0, 2, UIntPtr.Zero);
        }

        #endregion

        /// <summary>
        /// Herramienta de desarrollo (--preview-taskbar): cada formato y estilo en PNG, sin tocar la barra de Windows.
        /// Los ajustes se cambian solo en memoria.
        /// </summary>
        internal void RenderPreviews(string folder)
        {
            _apps = TaskbarService.Snapshot();
            _clock = DateTime.Now.ToString("HH:mm");
            _networkGlyph = NetworkGlyph();
            var cases = new[]
            {
                new { Name = "islas-flotantes", Format = SuiteSettings.TaskbarFormatKind.Islands, Gap = 14, Edges = false, Search = SuiteSettings.TaskbarSearchKind.Full, Glass = SuiteSettings.GlassStyleKind.Aero },
                new { Name = "islas-juntas", Format = SuiteSettings.TaskbarFormatKind.Islands, Gap = 0, Edges = false, Search = SuiteSettings.TaskbarSearchKind.Full, Glass = SuiteSettings.GlassStyleKind.Aero },
                new { Name = "islas-extremos", Format = SuiteSettings.TaskbarFormatKind.Islands, Gap = 14, Edges = true, Search = SuiteSettings.TaskbarSearchKind.Icon, Glass = SuiteSettings.GlassStyleKind.Aero },
                new { Name = "capsula", Format = SuiteSettings.TaskbarFormatKind.Capsule, Gap = 14, Edges = false, Search = SuiteSettings.TaskbarSearchKind.Full, Glass = SuiteSettings.GlassStyleKind.Aero },
                new { Name = "completa", Format = SuiteSettings.TaskbarFormatKind.Full, Gap = 14, Edges = false, Search = SuiteSettings.TaskbarSearchKind.Full, Glass = SuiteSettings.GlassStyleKind.Aero },
                new { Name = "neon", Format = SuiteSettings.TaskbarFormatKind.Islands, Gap = 14, Edges = false, Search = SuiteSettings.TaskbarSearchKind.Hidden, Glass = SuiteSettings.GlassStyleKind.Neon },
                new { Name = "transparente", Format = SuiteSettings.TaskbarFormatKind.Islands, Gap = 14, Edges = false, Search = SuiteSettings.TaskbarSearchKind.Full, Glass = SuiteSettings.GlassStyleKind.Transparent },
                new { Name = "obsidiana", Format = SuiteSettings.TaskbarFormatKind.Capsule, Gap = 14, Edges = false, Search = SuiteSettings.TaskbarSearchKind.Icon, Glass = SuiteSettings.GlassStyleKind.Obsidian },
            };
            foreach (var c in cases)
            {
                SuiteSettings.TaskbarFormat = c.Format;
                SuiteSettings.TaskbarGap = c.Gap;
                SuiteSettings.TaskbarEdges = c.Edges;
                SuiteSettings.TaskbarSearch = c.Search;
                SuiteSettings.GlassStyle = c.Glass;
                int width = Screen.PrimaryScreen.Bounds.Width, height = ReservedHeight + ShadowPad;
                Size = new Size(width, height);
                using (Bitmap bitmap = new Bitmap(width, height))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        using (LinearGradientBrush wallpaper = new LinearGradientBrush(new Rectangle(0, 0, width, height), Color.FromArgb(40, 31, 61), Color.FromArgb(20, 60, 90), 0f))
                        {
                            g.FillRectangle(wallpaper, 0, 0, width, height);
                        }
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        DrawSurface(g);
                    }
                    bitmap.Save(System.IO.Path.Combine(folder, "barra-" + c.Name + ".png"));
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _refresh.Dispose();
                _tooltip.Dispose();
                _painter.Dispose();
                if (_audio != null) _audio.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
