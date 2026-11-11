using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Claude en la isla (mockup aprobado): un cajón negro que baja desde la isla con claude.ai adentro, en modo
    /// oscuro y con la sesión normal del usuario (sin API). Ctrl+Alt+C lo abre o lo esconde; Esc o un clic afuera
    /// lo esconden y la conversación sigue ahí. Pestañas para otros sitios fijados (Gemini).
    /// Usa WebView2 (motor de Edge que trae Windows 11); si falta, la isla avisa en lugar de abrirlo.
    /// </summary>
    internal class WebDrawerWindow : Form
    {
        private const float LogicalW = 460, LogicalH = 640, HeaderH = 44, Pad = 10, Radius = 26;

        private static readonly string[][] Sites =
        {
            new[] { "Claude", "https://claude.ai/new" },
            new[] { "Gemini", "https://gemini.google.com/app" }
        };

        private readonly WebView2 _web;
        private readonly float _scale;
        private int _site;
        private bool _starting, _ready;
        private RectangleF[] _siteRects = new RectangleF[0];
        private RectangleF _openRect, _closeRect;
        private DateTime _shownAt;

        public WebDrawerWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            DoubleBuffered = true;
            Text = "Streeck Suite · Claude";
            using (Graphics g = CreateGraphics()) _scale = g.DpiX / 96f;

            _web = new WebView2();
            _web.DefaultBackgroundColor = Color.Black;
            _web.Visible = false; // Hasta que cargue la primera página, así no hay destello blanco
            Controls.Add(_web);
        }

        /// <summary>
        /// Crea el cajón; null si faltan las DLL de WebView2 (el método aparte evita que falle quien lo llama).
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static WebDrawerWindow TryCreate()
        {
            try
            {
                return new WebDrawerWindow();
            }
            catch (Exception ex)
            {
                Log.Error("[WebDrawer] No se pudo crear el cajón web: {0}", ex.Message);
                return null;
            }
        }

        private float Px(float v) { return v * _scale; }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80; // WS_EX_TOOLWINDOW: fuera de Alt+Tab
                return cp;
            }
        }

        public void Toggle()
        {
            if (Visible) HideDrawer(); else ShowDrawer();
        }

        public void ShowDrawer()
        {
            // Pegado arriba, centrado sobre la isla (en su monitor)
            Rectangle island = IslandEyes.IslandRect;
            Screen screen = island.Width > 0 ? Screen.FromRectangle(island) : Screen.PrimaryScreen;
            int w = (int)Px(LogicalW), h = Math.Min((int)Px(LogicalH), screen.WorkingArea.Height - (int)Px(40));
            int centerX = island.Width > 0 ? island.Left + island.Width / 2 : screen.Bounds.Left + screen.Bounds.Width / 2;
            int x = Math.Max(screen.Bounds.Left, Math.Min(screen.Bounds.Right - w, centerX - w / 2));
            Bounds = new Rectangle(x, screen.Bounds.Top, w, h);
            using (GraphicsPath shape = BottomRounded(new RectangleF(0, 0, w, h), Px(Radius))) Region = new Region(shape);
            _web.Bounds = new Rectangle((int)Px(Pad), (int)Px(HeaderH), w - (int)Px(2 * Pad), h - (int)Px(HeaderH + Pad));

            Show();
            Activate();
            _shownAt = DateTime.Now;
            if (!_starting) Start();
            else if (_ready)
            {
                try { _web.CoreWebView2.Resume(); } catch (Exception) { }
                _web.Focus();
            }
        }

        public void HideDrawer()
        {
            Hide();
            // Al esconderlo, el navegador interno se duerme para no gastar (la conversación no se pierde)
            if (_ready)
            {
                try { _web.CoreWebView2.TrySuspendAsync(); } catch (Exception) { }
            }
        }

        private async void Start()
        {
            _starting = true;
            try
            {
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreeckSuite", "WebView2");
                CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null, data, null);
                await _web.EnsureCoreWebView2Async(environment);
                CoreWebView2 core = _web.CoreWebView2;
                core.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark; // Modo oscuro: combina con el notch
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                // Enlaces que abren otra ventana (fuentes, archivos): al navegador del usuario
                core.NewWindowRequested += (s, e) =>
                {
                    e.Handled = true;
                    OpenExternal(e.Uri);
                };
                core.NavigationCompleted += (s, e) => { if (!_web.Visible) { _web.Visible = true; _web.Focus(); } };
                _ready = true;
                core.Navigate(Sites[_site][1]);
                Log.Info("[WebDrawer] Cajón listo (WebView2 {0})", environment.BrowserVersionString);
            }
            catch (Exception ex)
            {
                _starting = false;
                Log.Error("[WebDrawer] No se pudo iniciar WebView2: {0}", ex.Message);
                HideDrawer();
                EventBus.Instance.Publish(StreeckSuite.Core.Events.NotchShowMessage, "⚠️ Falta el componente WebView2 de Microsoft para abrir Claude");
            }
        }

        private static void OpenExternal(string url)
        {
            if (string.IsNullOrEmpty(url) || !(url.StartsWith("https://") || url.StartsWith("http://"))) return;
            try { Process.Start(url); } catch (Exception) { }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // Clic afuera: se esconde (salvo justo al abrir, cuando el foco aún se está acomodando)
            if (Visible && (DateTime.Now - _shownAt).TotalMilliseconds > 400) HideDrawer();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { HideDrawer(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.Clear(Color.Black);
            float mid = Px(HeaderH) / 2 + Px(2);

            // Ojitos del usuario a la izquierda, como en la isla
            IslandEyes.DrawPreview(g, new PointF(Px(34), mid), 0.55f * _scale, IslandEyes.Mood.Happy);

            // Pestañas de los sitios
            _siteRects = new RectangleF[Sites.Length];
            float x = Px(70);
            using (Font font = new Font("Segoe UI Semibold", 9.5f))
            {
                for (int i = 0; i < Sites.Length; i++)
                {
                    SizeF text = g.MeasureString(Sites[i][0], font);
                    RectangleF chip = new RectangleF(x, mid - Px(13), text.Width + Px(20), Px(26));
                    _siteRects[i] = chip;
                    bool on = i == _site;
                    IslandPainter.FillRounded(g, chip, chip.Height / 2, on ? Color.White : Color.FromArgb(30, 32, 38));
                    TextRenderer.DrawText(g, Sites[i][0], font, Rectangle.Round(chip), on ? Color.Black : Color.FromArgb(205, 208, 216),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    x = chip.Right + Px(6);
                }
            }

            // Abrir en el navegador y cerrar
            _closeRect = new RectangleF(Width - Px(Pad) - Px(28), mid - Px(14), Px(28), Px(28));
            _openRect = new RectangleF(_closeRect.X - Px(34), _closeRect.Y, Px(28), Px(28));
            using (Font glyphs = new Font("Segoe Fluent Icons", 10f))
            {
                foreach (RectangleF r in new[] { _openRect, _closeRect })
                {
                    IslandPainter.FillRounded(g, r, Px(8), Color.FromArgb(30, 32, 38));
                }
                TextRenderer.DrawText(g, "", glyphs, Rectangle.Round(_openRect), Color.FromArgb(205, 208, 216), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, "", glyphs, Rectangle.Round(_closeRect), Color.FromArgb(205, 208, 216), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_closeRect.Contains(e.Location)) { HideDrawer(); return; }
            if (_openRect.Contains(e.Location))
            {
                OpenExternal(_ready && _web.Source != null ? _web.Source.ToString() : Sites[_site][1]);
                HideDrawer();
                return;
            }
            for (int i = 0; i < _siteRects.Length; i++)
            {
                if (!_siteRects[i].Contains(e.Location) || i == _site) continue;
                _site = i;
                Invalidate();
                if (_ready) _web.CoreWebView2.Navigate(Sites[i][1]);
                return;
            }
        }

        private static GraphicsPath BottomRounded(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2;
            p.AddLine(r.Left, r.Top, r.Right, r.Top);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>
    /// Ctrl+Alt+C: abre o esconde Claude en la isla.
    /// </summary>
    internal class WebDrawerHotkey : NativeWindow, IDisposable
    {
        private const int HotkeyId = 0x5343; // 'S' 'C' (Streeck Claude)
        private const uint KeyC = 0x43;
        private readonly Action _pressed;
        private bool _registered;

        public WebDrawerHotkey(Action pressed)
        {
            _pressed = pressed;
            CreateHandle(new CreateParams());
        }

        public bool Apply()
        {
            if (_registered) { NativeMethods.UnregisterHotKey(Handle, HotkeyId); _registered = false; }
            _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, NativeMethods.MOD_NOREPEAT | NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, KeyC);
            if (!_registered) Log.Error("[WebDrawerHotkey] Ctrl+Alt+C está ocupado por otro programa");
            return _registered;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId && _pressed != null) _pressed();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            if (_registered) NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }
    }
}
