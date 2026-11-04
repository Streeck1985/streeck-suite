using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Threading;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Búsqueda rápida (mockup E2): tarjeta como la isla, arriba al centro, que aparece con el atajo de teclado.
    /// A diferencia de la isla sí toma el foco (hay que escribir); al pulsar Esc o hacer clic fuera se oculta y
    /// Windows devuelve el foco a la ventana anterior. ↑ ↓ eligen, Enter abre.
    /// </summary>
    internal class SearchWindow : LayeredWindow
    {
        private const string GlyphSearch = "";
        private const float CardWidth = 440, TopMargin = 14, Pad = 16, InputHeight = 44, HeaderHeight = 22, RowHeight = 48;
        private const int FileSearchDelayMs = 150;

        private readonly float _scale;
        private readonly IslandPainter _painter;
        private readonly Font _inputFont;
        private readonly System.Windows.Forms.Timer _caretTimer;
        private readonly System.Windows.Forms.Timer _fileTimer;
        private string _query = "";
        private List<SearchResult> _quick = new List<SearchResult>();
        private List<SearchResult> _files = new List<SearchResult>();
        private int _selected;
        private bool _caretVisible = true;
        private int _queryVersion;
        private readonly List<KeyValuePair<RectangleF, int>> _rowRects = new List<KeyValuePair<RectangleF, int>>();

        public SearchWindow()
        {
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) _scale = screen.DpiX / 96f;
            _painter = new IslandPainter(_scale, 1f);
            _inputFont = new Font("Segoe UI", 11.25f, FontStyle.Regular);
            TopMost = true;
            KeyPreview = true;
            Text = "Búsqueda · Streeck Suite";

            _caretTimer = new System.Windows.Forms.Timer { Interval = 530 };
            _caretTimer.Tick += (s, e) => { _caretVisible = !_caretVisible; RenderSurface(); };
            _fileTimer = new System.Windows.Forms.Timer { Interval = FileSearchDelayMs };
            _fileTimer.Tick += (s, e) => { _fileTimer.Stop(); StartFileSearch(); };
            MouseClick += OnResultClick;
            MouseMove += (s, e) =>
            {
                int row = RowAt(e.Location);
                if (row >= 0 && row != _selected) { _selected = row; RenderSurface(); }
            };
        }

        // Al contrario que el resto de ventanas de la suite, esta sí debe poder activarse para recibir el teclado
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle &= ~0x08000000; // Sin WS_EX_NOACTIVATE
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        private float Px(float value)
        {
            return value * _scale;
        }

        private int ShadowPad
        {
            get { return (int)Px(20); }
        }

        public void Toggle()
        {
            if (Visible) { HideSearch(); return; }
            SearchService.WarmUp();
            _query = "";
            _quick = new List<SearchResult>();
            _files = new List<SearchResult>();
            _selected = 0;
            Relayout();
            Show();
            Activate();
            NativeMethods.SetForegroundWindow(Handle);
            _caretTimer.Start();
        }

        private void HideSearch()
        {
            _caretTimer.Stop();
            _fileTimer.Stop();
            Hide();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HideSearch(); // Clic fuera
        }

        #region Teclado

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            List<SearchResult> results = Results;
            switch (keyData)
            {
                case Keys.Escape:
                    HideSearch();
                    return true;
                case Keys.Down:
                    if (results.Count > 0) _selected = (_selected + 1) % results.Count;
                    RenderSurface();
                    return true;
                case Keys.Up:
                    if (results.Count > 0) _selected = (_selected - 1 + results.Count) % results.Count;
                    RenderSurface();
                    return true;
                case Keys.Enter:
                    if (_selected >= 0 && _selected < results.Count) RunResult(results[_selected]);
                    return true;
                case Keys.Control | Keys.V:
                    if (Clipboard.ContainsText()) SetQuery(_query + Clipboard.GetText().Replace("\r", " ").Replace("\n", " "));
                    return true;
                case Keys.Control | Keys.Back:
                    string trimmed = _query.TrimEnd();
                    int space = trimmed.LastIndexOf(' ');
                    SetQuery(space >= 0 ? trimmed.Substring(0, space + 1) : "");
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (e.KeyChar == '\b')
            {
                if (_query.Length > 0) SetQuery(_query.Substring(0, _query.Length - 1));
            }
            else if (!char.IsControl(e.KeyChar))
            {
                SetQuery(_query + e.KeyChar);
            }
            e.Handled = true;
        }

        private void SetQuery(string query)
        {
            _query = query;
            _quick = SearchService.QuickResults(query);
            _files = new List<SearchResult>();
            _selected = 0;
            _caretVisible = true;
            _queryVersion++;
            _fileTimer.Stop();
            _fileTimer.Start(); // Los archivos se buscan cuando se deja de escribir un momento
            Relayout();
        }

        private void StartFileSearch()
        {
            string query = _query;
            int version = _queryVersion;
            ThreadPool.QueueUserWorkItem(state =>
            {
                List<SearchResult> files = SearchService.FindFiles(query);
                if (files.Count == 0) return;
                try
                {
                    BeginInvoke((Action)(() =>
                    {
                        if (version != _queryVersion || !Visible) return; // Ya se escribió otra cosa
                        _files = files;
                        Relayout();
                    }));
                }
                catch (InvalidOperationException)
                {
                    // La ventana se cerró mientras se buscaba
                }
            });
        }

        #endregion

        #region Resultados

        // Orden: calculadora, apps, archivos, preguntar a la IA y por último la web
        private List<SearchResult> Results
        {
            get
            {
                List<SearchResult> all = new List<SearchResult>();
                all.AddRange(_quick.FindAll(r => r.Type == SearchResult.Kind.Calculator || r.Type == SearchResult.Kind.App));
                all.AddRange(_files);
                // Preguntar a la IA y buscar en la web al final: Enter abre antes una app o archivo que coincida
                all.AddRange(_quick.FindAll(r => r.Type == SearchResult.Kind.Assistant));
                all.AddRange(_quick.FindAll(r => r.Type == SearchResult.Kind.Web));
                return all;
            }
        }

        private void RunResult(SearchResult result)
        {
            HideSearch();
            SearchService.Run(result);
        }

        private void OnResultClick(object sender, MouseEventArgs e)
        {
            int row = RowAt(e.Location);
            List<SearchResult> results = Results;
            if (row >= 0 && row < results.Count) RunResult(results[row]);
        }

        private int RowAt(Point location)
        {
            PointF point = new PointF(location.X - ShadowPad, location.Y - ShadowPad);
            foreach (KeyValuePair<RectangleF, int> row in _rowRects)
            {
                if (row.Key.Contains(point)) return row.Value;
            }
            return -1;
        }

        #endregion

        #region Dibujo

        /// <summary>
        /// Herramienta de desarrollo (--preview-search): busca de verdad y guarda la tarjeta en PNG sin mostrarla.
        /// </summary>
        internal void RenderPreview(string query, string file)
        {
            SearchService.LoadAppsNow();
            _query = query;
            _quick = SearchService.QuickResults(query);
            _files = SearchService.FindFiles(query);
            _caretVisible = true;
            int width = (int)Px(CardWidth) + 2 * ShadowPad, height = (int)Px(ContentHeight()) + 2 * ShadowPad;
            Size = new Size(width, height);
            using (Bitmap bitmap = new Bitmap(width, height))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.FromArgb(28, 36, 51));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawSurface(g);
                }
                bitmap.Save(file);
            }
        }

        private float ContentHeight()
        {
            float height = Pad + InputHeight + Pad;
            List<SearchResult> results = Results;
            if (results.Count == 0) return height;
            height += 4;
            SearchResult.Kind? section = null;
            foreach (SearchResult r in results)
            {
                if (SectionTitle(r.Type) != null && r.Type != section) height += HeaderHeight;
                section = r.Type;
                height += RowHeight;
            }
            return height;
        }

        private static string SectionTitle(SearchResult.Kind kind)
        {
            switch (kind)
            {
                case SearchResult.Kind.App: return "APPS";
                case SearchResult.Kind.File: return "ARCHIVOS";
                default: return null;
            }
        }

        private Screen _monitor; // Monitor donde se abrió (el del ratón); no cambia mientras está abierta

        private void Relayout()
        {
            if (!Visible || _monitor == null) _monitor = Screen.FromPoint(Cursor.Position);
            Rectangle screen = _monitor.Bounds;
            int width = (int)Px(CardWidth), height = (int)Px(ContentHeight());
            Bounds = new Rectangle(screen.X + (screen.Width - width) / 2 - ShadowPad, screen.Y + (int)Px(TopMargin) - ShadowPad,
                width + 2 * ShadowPad, height + 2 * ShadowPad);
            RenderSurface();
        }

        protected override void DrawSurface(Graphics g)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            RectangleF card = new RectangleF(ShadowPad, ShadowPad, Width - 2 * ShadowPad - 1, Height - 2 * ShadowPad - 1);
            DrawSoftShadow(g, card, Px(26), ShadowPad * 0.8f, Px(6), 200);
            using (GraphicsPath path = RoundedRect(card, Px(26)))
            using (SolidBrush fill = new SolidBrush(IslandPainter.Background))
            using (Pen border = new Pen(Color.FromArgb(128, IslandPainter.Sky), 1))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            g.TranslateTransform(ShadowPad, ShadowPad);
            _rowRects.Clear();

            Color white = Color.White;
            Color gray = IslandPainter.Gray;
            Color legend = Color.FromArgb(139, 147, 163);
            Color link = Color.FromArgb(125, 211, 252);

            // Cuadro de texto
            RectangleF input = new RectangleF(Px(Pad), Px(Pad), card.Width - 2 * Px(Pad), Px(InputHeight));
            IslandPainter.FillRounded(g, input, Px(14), Color.FromArgb(15, 255, 255, 255));
            _painter.DrawText(g, GlyphSearch, _painter.Glyph16, link, new RectangleF(input.X + Px(12), input.Y, Px(20), input.Height), StringAlignment.Center);
            float textX = input.X + Px(42);
            if (_query.Length == 0)
            {
                _painter.DrawText(g, "Buscar apps, archivos o en la web", _inputFont, legend, new RectangleF(textX, input.Y, input.Width - Px(150), input.Height), StringAlignment.Near);
            }
            else
            {
                _painter.DrawText(g, _query, _inputFont, white, new RectangleF(textX, input.Y, input.Width - Px(150), input.Height), StringAlignment.Near);
            }
            if (_caretVisible)
            {
                float caretX = textX + (_query.Length == 0 ? 0 : g.MeasureString(_query, _inputFont, PointF.Empty, StringFormat.GenericTypographic).Width + Px(1));
                caretX = Math.Min(caretX, input.Right - Px(110));
                using (Pen caret = new Pen(white, Px(1.2f))) g.DrawLine(caret, caretX, input.Y + Px(13), caretX, input.Bottom - Px(13));
            }
            _painter.DrawText(g, "Esc para cerrar", _painter.Text11, legend, new RectangleF(input.Right - Px(110), input.Y, Px(98), input.Height), StringAlignment.Far);

            // Resultados por secciones
            List<SearchResult> results = Results;
            float y = input.Bottom + Px(4) + Px(Pad) / 2;
            SearchResult.Kind? section = null;
            for (int i = 0; i < results.Count; i++)
            {
                SearchResult r = results[i];
                string title = SectionTitle(r.Type);
                if (title != null && r.Type != section)
                {
                    _painter.DrawText(g, title, _painter.Label11, legend, new RectangleF(input.X + Px(10), y, input.Width, Px(HeaderHeight)), StringAlignment.Near);
                    y += Px(HeaderHeight);
                }
                section = r.Type;

                RectangleF row = new RectangleF(input.X, y + Px(2), input.Width, Px(RowHeight - 4));
                if (i == _selected) IslandPainter.FillRounded(g, row, Px(12), Color.FromArgb(36, IslandPainter.Sky));
                RectangleF icon = new RectangleF(row.X + Px(10), row.Y + (row.Height - Px(32)) / 2, Px(32), Px(32));
                DrawResultIcon(g, r, icon);

                float labelX = icon.Right + Px(12);
                float hintWidth = i == _selected ? Px(54) : 0;
                float labelWidth = row.Right - Px(10) - hintWidth - labelX;
                _painter.DrawText(g, r.Title, _painter.Semibold13, white, new RectangleF(labelX, row.Y + Px(3), labelWidth, Px(19)), StringAlignment.Near);
                _painter.DrawText(g, r.Subtitle, _painter.Text12, gray, new RectangleF(labelX, row.Y + Px(21), labelWidth, Px(17)), StringAlignment.Near);
                if (i == _selected) _painter.DrawText(g, "Enter ↵", _painter.Bold12, link, new RectangleF(row.Right - Px(10) - hintWidth, row.Y, hintWidth, row.Height), StringAlignment.Far);

                _rowRects.Add(new KeyValuePair<RectangleF, int>(row, i));
                y += Px(RowHeight);
            }
        }

        private void DrawResultIcon(Graphics g, SearchResult r, RectangleF rect)
        {
            if (r.Icon != null)
            {
                _painter.DrawRoundedImage(g, r.Icon, rect, Px(9), 255);
                return;
            }
            string glyph;
            Color color;
            switch (r.Type)
            {
                case SearchResult.Kind.Calculator: glyph = ""; color = Color.FromArgb(74, 222, 128); break;
                case SearchResult.Kind.Assistant: glyph = ""; color = Color.FromArgb(167, 139, 250); break;
                case SearchResult.Kind.Web: glyph = ""; color = Color.FromArgb(195, 201, 212); break;
                case SearchResult.Kind.File: glyph = ""; color = IslandPainter.Sky; break;
                default: glyph = ""; color = IslandPainter.Sky; break;
            }
            IslandPainter.FillRounded(g, rect, Px(9), Color.FromArgb(46, color));
            _painter.DrawText(g, glyph, _painter.Glyph16, color, rect, StringAlignment.Center);
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _caretTimer.Dispose();
                _fileTimer.Dispose();
                _painter.Dispose();
                _inputFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Atajo global de la búsqueda (Ctrl+Alt+S por defecto; se cambia en el panel de ajustes).
    /// </summary>
    internal class SearchHotkey : NativeWindow, IDisposable
    {
        private const int HotkeyId = 0xB200;
        private const uint KeySpace = 0x20;
        private const uint KeyS = 0x53;

        private readonly Action _pressed;
        private bool _registered;

        public SearchHotkey(Action pressed)
        {
            _pressed = pressed;
            CreateHandle(new CreateParams());
        }

        /// <summary>
        /// Registra el atajo elegido en los ajustes. Devuelve false si otro programa ya lo usa.
        /// </summary>
        public bool Register()
        {
            if (_registered) { NativeMethods.UnregisterHotKey(Handle, HotkeyId); _registered = false; }
            uint modifiers = NativeMethods.MOD_NOREPEAT;
            uint key = KeySpace;
            switch (SuiteSettings.SearchHotkey)
            {
                case SuiteSettings.SearchShortcut.CtrlShiftSpace: modifiers |= NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT; break;
                case SuiteSettings.SearchShortcut.CtrlSpace: modifiers |= NativeMethods.MOD_CONTROL; break;
                default: modifiers |= NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT; key = KeyS; break;
            }            _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers, key);
            if (!_registered) Log.Error("[SearchHotkey] El atajo {0} está ocupado por otro programa", SuiteSettings.SearchHotkey);
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
