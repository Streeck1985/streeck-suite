using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Menú de inicio (fase H, mockup H1): búsqueda, 12 apps ancladas, archivos recientes, cuenta y energía, con el
    /// cristal negro del panel. Vistas: inicio, todas las apps (alfabético) y resultados al escribir.
    /// Se abre con la tecla Windows (WinKeyHook) y se oculta con Esc o al hacer clic fuera.
    /// </summary>
    internal class StartMenuWindow : LayeredWindow
    {
        private enum View { Home, AllApps, Search }

        private const float CardWidth = 600, Pad = 24, BottomMargin = 12;
        private const float SearchHeight = 44, HeaderHeight = 22, CellHeight = 72, RecentRow = 46, FooterHeight = 62;
        private const float ListRow = 34, LetterRow = 26, ListHeight = 400;
        private const int RecentCount = 6;
        private static readonly Color Link = Color.FromArgb(125, 211, 252);
        private static readonly Color Violet = Color.FromArgb(196, 181, 253);

        private readonly float _scale;
        private readonly IslandPainter _painter;
        private readonly Font _inputFont;
        private View _view = View.Home;
        private string _query = "";
        private List<SearchResult> _results = new List<SearchResult>();
        private List<SearchResult> _files = new List<SearchResult>();
        private int _selected;
        private int _queryVersion;
        private float _listScroll;
        private bool _letterPicker;
        private bool _powerOpen;
        private StartMenuService.PowerAction? _powerConfirm;
        private bool _contextOpen;
        private List<RecentFile> _recent = new List<RecentFile>();
        private readonly System.Windows.Forms.Timer _fileTimer;
        private readonly List<KeyValuePair<RectangleF, Action>> _hits = new List<KeyValuePair<RectangleF, Action>>();
        private readonly List<KeyValuePair<RectangleF, ShellImage.App>> _appHits = new List<KeyValuePair<RectangleF, ShellImage.App>>();
        private PointF _mouse = new PointF(-1, -1);

        public StartMenuWindow()
        {
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) _scale = screen.DpiX / 96f;
            _painter = new IslandPainter(_scale, 1f);
            _inputFont = new Font("Segoe UI", 11f, FontStyle.Regular);
            TopMost = true;
            KeyPreview = true;
            Text = "Inicio · Streeck Suite";
            _fileTimer = new System.Windows.Forms.Timer { Interval = 150 };
            _fileTimer.Tick += (s, e) => { _fileTimer.Stop(); SearchFiles(); };
            MouseClick += OnCardClick;
            MouseWheel += OnCardWheel;
            MouseMove += (s, e) => { _mouse = new PointF(e.X - ShadowPad, e.Y - ShadowPad); RenderSurface(); };
            MouseLeave += (s, e) => { _mouse = new PointF(-1, -1); RenderSurface(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle &= ~0x08000000; // Puede activarse: se escribe para buscar
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        private float Px(float value) { return value * _scale; }
        private int ShadowPad { get { return (int)Px(24); } }

        #region Abrir y cerrar

        public void Toggle()
        {
            if (Visible) { HideMenu(); return; }
            Log.Info("[StartMenu] Abriendo el menú");
            _view = View.Home;
            _query = "";
            _powerOpen = false;
            _powerConfirm = null;
            _letterPicker = false;
            _listScroll = 0;
            SearchService.WarmUp();
            _recent = SuiteSettings.StartMenuShowRecent ? StartMenuService.Recent(RecentCount) : new List<RecentFile>();
            Relayout();
            Show();
            _shownAt = DateTime.Now;
            Activate();
            NativeMethods.SetForegroundWindow(Handle);
        }

        private DateTime _shownAt;

        private void HideMenu()
        {
            _fileTimer.Stop();
            Hide();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // Justo al abrirse, Windows puede quitarle el foco un instante: eso no cuenta como clic fuera
            if ((DateTime.Now - _shownAt).TotalMilliseconds < 400) { BeginInvoke((Action)(() => { Activate(); NativeMethods.SetForegroundWindow(Handle); })); return; }
            if (!_contextOpen) HideMenu();
        }

        #endregion

        #region Teclado

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                    if (_query.Length > 0) SetQuery("");
                    else if (_view == View.AllApps) { _view = View.Home; Relayout(); }
                    else HideMenu();
                    return true;
                case Keys.Enter:
                    if (_view == View.Search)
                    {
                        List<SearchResult> results = AllResults;
                        if (_selected < results.Count) Run(results[_selected]);
                    }
                    return true;
                case Keys.Down:
                    if (_view == View.Search && AllResults.Count > 0) { _selected = (_selected + 1) % AllResults.Count; RenderSurface(); }
                    return true;
                case Keys.Up:
                    if (_view == View.Search && AllResults.Count > 0) { _selected = (_selected - 1 + AllResults.Count) % AllResults.Count; RenderSurface(); }
                    return true;
                case Keys.Control | Keys.V:
                    if (Clipboard.ContainsText()) SetQuery(_query + Clipboard.GetText().Replace("\r", " ").Replace("\n", " "));
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (e.KeyChar == '\b') { if (_query.Length > 0) SetQuery(_query.Substring(0, _query.Length - 1)); }
            else if (!char.IsControl(e.KeyChar)) SetQuery(_query + e.KeyChar);
            e.Handled = true;
        }

        // Escribir en cualquier momento busca, como en el menú de Windows
        private void SetQuery(string query)
        {
            _query = query;
            _view = query.Trim().Length > 0 ? View.Search : View.Home;
            _results = SearchService.QuickResults(query);
            _files = new List<SearchResult>();
            _selected = 0;
            _queryVersion++;
            _fileTimer.Stop();
            if (_view == View.Search) _fileTimer.Start();
            Relayout();
        }

        private void SearchFiles()
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
                        if (version != _queryVersion || !Visible) return;
                        _files = files;
                        Relayout();
                    }));
                }
                catch (InvalidOperationException) { }
            });
        }

        // Mismo orden que la búsqueda rápida: calculadora y apps, archivos, IA y web
        private List<SearchResult> AllResults
        {
            get
            {
                List<SearchResult> all = new List<SearchResult>();
                all.AddRange(_results.FindAll(r => r.Type == SearchResult.Kind.Calculator || r.Type == SearchResult.Kind.App));
                all.AddRange(_files);
                all.AddRange(_results.FindAll(r => r.Type == SearchResult.Kind.Assistant));
                all.AddRange(_results.FindAll(r => r.Type == SearchResult.Kind.Web));
                return all;
            }
        }

        private void Run(SearchResult result)
        {
            HideMenu();
            SearchService.Run(result);
        }

        #endregion

        #region Ratón

        private void OnCardClick(object sender, MouseEventArgs e)
        {
            PointF point = new PointF(e.X - ShadowPad, e.Y - ShadowPad);
            if (e.Button == MouseButtons.Right)
            {
                foreach (KeyValuePair<RectangleF, ShellImage.App> hit in _appHits)
                {
                    if (hit.Key.Contains(point)) { ShowAppMenu(hit.Value, e.Location); return; }
                }
                return;
            }
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                if (_hits[i].Key.Contains(point)) { _hits[i].Value(); return; }
            }
            if (_powerOpen) { _powerOpen = false; _powerConfirm = null; RenderSurface(); }
        }

        private void OnCardWheel(object sender, MouseEventArgs e)
        {
            if (_view != View.AllApps) return;
            _listScroll = Math.Max(0, Math.Min(MaxListScroll(), _listScroll - e.Delta / 120f * Px(ListRow * 3)));
            RenderSurface();
        }

        // Clic derecho en una app: anclar, mover al principio, ejecutar como administrador
        private void ShowAppMenu(ShellImage.App app, Point location)
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            TrayMenuStyle.Apply(menu);
            bool pinned = StartMenuService.IsPinned(app.Id);
            menu.Items.Add(TrayMenuStyle.Item(pinned ? "Desanclar del inicio" : "Anclar al inicio", pinned ? "" : "", (s, e) =>
            {
                if (pinned) StartMenuService.Unpin(app.Id); else StartMenuService.Pin(app.Id);
                Relayout();
            }));
            if (pinned)
            {
                menu.Items.Add(TrayMenuStyle.Item("Mover al principio", "", (s, e) => { StartMenuService.MoveToFront(app.Id); Relayout(); }));
            }
            if (StartMenuService.ExePath(app.Id) != null)
            {
                menu.Items.Add(TrayMenuStyle.Item("Abrir ubicación del archivo", "", (s, e) =>
                {
                    HideMenu();
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + StartMenuService.ExePath(app.Id) + "\"");
                }));
                menu.Items.Add(TrayMenuStyle.Item("Ejecutar como administrador", "", (s, e) => { HideMenu(); StartMenuService.Launch(app, true); }));
            }
            _contextOpen = true;
            menu.Closed += (s, e) =>
            {
                _contextOpen = false;
                BeginInvoke((Action)(() => { if (Visible) { Activate(); RenderSurface(); } }));
            };
            menu.Show(this, location);
        }

        private bool Hover(RectangleF rect)
        {
            return rect.Contains(_mouse);
        }

        #endregion

        /// <summary>
        /// Herramienta de desarrollo (--preview-start): guarda en PNG el inicio, todas las apps y una búsqueda.
        /// </summary>
        internal void RenderPreviews(string folder)
        {
            _recent = SuiteSettings.StartMenuShowRecent ? StartMenuService.Recent(RecentCount) : new List<RecentFile>();
            SearchService.LoadAppsNow();
            string[] names = { "inicio", "todas", "busqueda", "energia" };
            for (int i = 0; i < names.Length; i++)
            {
                _view = i == 1 ? View.AllApps : View.Home;
                _powerOpen = i == 3;
                if (i == 2) { _query = "spot"; _view = View.Search; _results = SearchService.QuickResults(_query); _files = SearchService.FindFiles(_query); }
                int width = (int)Px(CardWidth) + 2 * ShadowPad, height = (int)Px(ContentHeight()) + 2 * ShadowPad;
                Size = new Size(width, height);
                using (Bitmap bitmap = new Bitmap(width, height))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(Color.FromArgb(40, 31, 61));
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        DrawSurface(g);
                    }
                    bitmap.Save(System.IO.Path.Combine(folder, "inicio-" + names[i] + ".png"));
                }
                _query = "";
            }
        }

        #region Medidas

        private float ContentHeight()
        {
            if (_view == View.AllApps) return Pad + HeaderHeight + 10 + ListHeight + 14 + FooterHeight;
            if (_view == View.Search) return Pad + SearchHeight + 10 + Math.Min(8, AllResults.Count) * 48 + 14 + FooterHeight;
            float height = Pad + SearchHeight + 14 + HeaderHeight + 8 + 2 * CellHeight;
            if (_recent.Count > 0) height += 14 + HeaderHeight + 8 + (float)Math.Ceiling(_recent.Count / 2f) * RecentRow;
            return height + 14 + FooterHeight;
        }

        private Screen _monitor; // Monitor donde se abrió (el del ratón); no cambia mientras está abierto

        private void Relayout()
        {
            if (!Visible || _monitor == null) _monitor = Screen.FromPoint(Cursor.Position);
            Rectangle area = _monitor.WorkingArea;
            int width = (int)Px(CardWidth), height = (int)Px(ContentHeight());
            Bounds = new Rectangle(area.X + (area.Width - width) / 2 - ShadowPad, area.Bottom - (int)Px(BottomMargin) - height - ShadowPad,
                width + 2 * ShadowPad, height + 2 * ShadowPad);
            RenderSurface();
        }

        #endregion

        #region Dibujo

        protected override void DrawSurface(Graphics g)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            RectangleF card = new RectangleF(ShadowPad, ShadowPad, Width - 2 * ShadowPad - 1, Height - 2 * ShadowPad - 1);
            Glass.DrawCard(g, card, Px(20), ShadowPad * 0.8f, Px(8), 225); // Estilo de cristal global (I2)
            g.TranslateTransform(ShadowPad, ShadowPad);
            _hits.Clear();
            _appHits.Clear();

            float x = Px(Pad), width = card.Width - 2 * Px(Pad), y = Px(Pad);
            switch (_view)
            {
                case View.AllApps: DrawAllApps(g, x, y, width); break;
                case View.Search: DrawSearchBox(g, x, y, width); DrawResults(g, x, y + Px(SearchHeight) + Px(10), width); break;
                default: DrawHome(g, x, y, width); break;
            }
            DrawFooter(g, card.Width, card.Height);
            if (_powerOpen) DrawPowerMenu(g, card.Width, card.Height);
        }

        private void DrawSearchBox(Graphics g, float x, float y, float width)
        {
            RectangleF box = new RectangleF(x, y, width, Px(SearchHeight));
            IslandPainter.FillRounded(g, box, Px(14), Color.FromArgb(15, 255, 255, 255));
            _painter.DrawText(g, "", _painter.Glyph16, Link, new RectangleF(box.X + Px(12), box.Y, Px(20), box.Height), StringAlignment.Center);
            RectangleF pill = new RectangleF(box.Right - Px(10) - Px(46), box.Y + (box.Height - Px(22)) / 2, Px(46), Px(22));
            IslandPainter.FillRounded(g, pill, Px(9), Color.FromArgb(46, 167, 139, 250));
            _painter.DrawText(g, "✦ IA", _painter.Bold12, Violet, pill, StringAlignment.Center);
            RectangleF text = new RectangleF(box.X + Px(42), box.Y, pill.X - Px(10) - box.X - Px(42), box.Height);
            if (_query.Length == 0)
            {
                _painter.DrawText(g, "Busca apps, archivos o pregunta a la IA", _inputFont, Color.FromArgb(139, 147, 163), text, StringAlignment.Near);
            }
            else
            {
                _painter.DrawText(g, _query, _inputFont, Color.White, text, StringAlignment.Near);
                float caretX = text.X + g.MeasureString(_query, _inputFont, PointF.Empty, StringFormat.GenericTypographic).Width + Px(1);
                using (Pen caret = new Pen(Color.White, Px(1.2f))) g.DrawLine(caret, Math.Min(caretX, text.Right), box.Y + Px(13), Math.Min(caretX, text.Right), box.Bottom - Px(13));
            }
        }

        private void DrawSectionHeader(Graphics g, float x, float y, float width, string title, string button, Action click)
        {
            _painter.DrawText(g, title, _painter.Bold15, Color.White, new RectangleF(x + Px(4), y, Px(200), Px(HeaderHeight)), StringAlignment.Near);
            if (button == null) return;
            float buttonWidth = g.MeasureString(button, _painter.Text12).Width + Px(20);
            RectangleF rect = new RectangleF(x + width - buttonWidth, y, buttonWidth, Px(HeaderHeight + 4) - Px(2));
            IslandPainter.FillRounded(g, rect, Px(8), Color.FromArgb(Hover(rect) ? 30 : 15, 255, 255, 255));
            _painter.DrawText(g, button, _painter.Text12, Color.FromArgb(195, 201, 212), rect, StringAlignment.Center);
            _hits.Add(new KeyValuePair<RectangleF, Action>(rect, click));
        }

        private void DrawHome(Graphics g, float x, float y, float width)
        {
            DrawSearchBox(g, x, y, width);
            y += Px(SearchHeight) + Px(14);
            DrawSectionHeader(g, x, y, width, "Anclados", "Todas las apps ›", () => { _view = View.AllApps; _listScroll = 0; _letterPicker = false; Relayout(); });
            y += Px(HeaderHeight) + Px(8);

            List<ShellImage.App> pinned = StartMenuService.Pinned;
            float cellWidth = width / 6;
            for (int i = 0; i < Math.Min(StartMenuService.PinnedCount, pinned.Count); i++)
            {
                ShellImage.App app = pinned[i];
                RectangleF cell = new RectangleF(x + (i % 6) * cellWidth, y + (i / 6) * Px(CellHeight), cellWidth, Px(CellHeight));
                if (Hover(cell)) IslandPainter.FillRounded(g, RectangleF.Inflate(cell, -Px(3), -Px(2)), Px(10), Color.FromArgb(15, 255, 255, 255));
                RectangleF icon = new RectangleF(cell.X + (cell.Width - Px(36)) / 2, cell.Y + Px(8), Px(36), Px(36));
                DrawAppIcon(g, app, icon);
                _painter.DrawText(g, ShortName(app.Name), _painter.Text12, Color.FromArgb(230, 233, 239), new RectangleF(cell.X + Px(2), icon.Bottom + Px(4), cell.Width - Px(4), Px(18)), StringAlignment.Center);
                ShellImage.App target = app;
                _hits.Add(new KeyValuePair<RectangleF, Action>(cell, () => { HideMenu(); StartMenuService.Launch(target, false); }));
                _appHits.Add(new KeyValuePair<RectangleF, ShellImage.App>(cell, app));
            }
            y += 2 * Px(CellHeight);

            if (_recent.Count == 0) return;
            y += Px(14);
            DrawSectionHeader(g, x, y, width, "Recientes", "Más ›", () => { HideMenu(); System.Diagnostics.Process.Start("explorer.exe", "shell:recent"); });
            y += Px(HeaderHeight) + Px(8);
            float columnWidth = width / 2;
            for (int i = 0; i < _recent.Count; i++)
            {
                RecentFile file = _recent[i];
                RectangleF row = new RectangleF(x + (i % 2) * columnWidth, y + (i / 2) * Px(RecentRow), columnWidth - Px(4), Px(RecentRow) - Px(4));
                if (Hover(row)) IslandPainter.FillRounded(g, row, Px(10), Color.FromArgb(15, 255, 255, 255));
                RectangleF icon = new RectangleF(row.X + Px(10), row.Y + (row.Height - Px(28)) / 2, Px(28), Px(28));
                Bitmap image = RecentIcon(file);
                if (image != null) _painter.DrawRoundedImage(g, image, icon, Px(6), 255);
                else IslandPainter.FillRounded(g, icon, Px(8), Color.FromArgb(15, 255, 255, 255));
                float textX = icon.Right + Px(10);
                _painter.DrawText(g, file.Name, _painter.Semibold13, Color.White, new RectangleF(textX, row.Y + Px(4), row.Right - textX - Px(6), Px(18)), StringAlignment.Near);
                _painter.DrawText(g, StartMenuService.WhenText(file.When) + " · " + file.Folder, _painter.Text11, Color.FromArgb(139, 147, 163),
                    new RectangleF(textX, row.Y + Px(22), row.Right - textX - Px(6), Px(16)), StringAlignment.Near);
                string link = file.LinkPath;
                _hits.Add(new KeyValuePair<RectangleF, Action>(row, () => { HideMenu(); try { System.Diagnostics.Process.Start(link); } catch (Exception ex) { Log.Error("[StartMenu] {0}", ex.Message); } }));
            }
        }

        private void DrawAllApps(Graphics g, float x, float y, float width)
        {
            DrawSectionHeader(g, x, y, width, "Todas las apps", "‹ Atrás", () => { _view = View.Home; Relayout(); });
            y += Px(HeaderHeight) + Px(10);
            RectangleF area = new RectangleF(x, y, width, Px(ListHeight));

            if (_letterPicker) { DrawLetterPicker(g, area); return; }

            GraphicsState state = g.Save();
            g.SetClip(area);
            float rowY = area.Y - _listScroll;
            char? letter = null;
            foreach (ShellImage.App app in StartMenuService.AllApps)
            {
                char first = GroupLetter(app.Name);
                if (letter != first)
                {
                    letter = first;
                    RectangleF header = new RectangleF(area.X + Px(8), rowY, Px(30), Px(LetterRow));
                    if (rowY + Px(LetterRow) > area.Y && rowY < area.Bottom)
                    {
                        _painter.DrawText(g, first.ToString(), _painter.Bold12, Link, header, StringAlignment.Near);
                        _hits.Add(new KeyValuePair<RectangleF, Action>(RectangleF.Intersect(header, area), () => { _letterPicker = true; RenderSurface(); }));
                    }
                    rowY += Px(LetterRow);
                }
                RectangleF row = new RectangleF(area.X, rowY, area.Width, Px(ListRow));
                if (rowY + Px(ListRow) > area.Y && rowY < area.Bottom)
                {
                    if (Hover(row)) IslandPainter.FillRounded(g, row, Px(9), Color.FromArgb(15, 255, 255, 255));
                    DrawAppIcon(g, app, new RectangleF(row.X + Px(8), row.Y + Px(5), Px(24), Px(24)));
                    _painter.DrawText(g, app.Name, _painter.Text13, Color.White, new RectangleF(row.X + Px(42), row.Y, row.Width - Px(50), row.Height), StringAlignment.Near);
                    ShellImage.App target = app;
                    RectangleF visible = RectangleF.Intersect(row, area);
                    _hits.Add(new KeyValuePair<RectangleF, Action>(visible, () => { HideMenu(); StartMenuService.Launch(target, false); }));
                    _appHits.Add(new KeyValuePair<RectangleF, ShellImage.App>(visible, app));
                }
                rowY += Px(ListRow);
            }
            g.Restore(state);
        }

        // Rejilla de letras para saltar en "Todas las apps"
        private void DrawLetterPicker(Graphics g, RectangleF area)
        {
            string letters = "#ABCDEFGHIJKLMNÑOPQRSTUVWXYZ";
            HashSet<char> present = new HashSet<char>();
            foreach (ShellImage.App app in StartMenuService.AllApps) present.Add(GroupLetter(app.Name));
            float cell = Px(56);
            int columns = (int)(area.Width / cell);
            float left = area.X + (area.Width - columns * cell) / 2;
            for (int i = 0; i < letters.Length; i++)
            {
                char letter = letters[i];
                RectangleF rect = new RectangleF(left + (i % columns) * cell, area.Y + Px(20) + (i / columns) * cell, cell - Px(6), cell - Px(6));
                bool enabled = present.Contains(letter);
                if (enabled && Hover(rect)) IslandPainter.FillRounded(g, rect, Px(10), Color.FromArgb(30, 255, 255, 255));
                _painter.DrawText(g, letter.ToString(), _painter.Bold18, enabled ? Color.White : Color.FromArgb(70, 255, 255, 255), rect, StringAlignment.Center);
                if (enabled) _hits.Add(new KeyValuePair<RectangleF, Action>(rect, () => JumpTo(letter)));
            }
        }

        private void JumpTo(char letter)
        {
            float offset = 0;
            char? current = null;
            foreach (ShellImage.App app in StartMenuService.AllApps)
            {
                char first = GroupLetter(app.Name);
                if (current != first)
                {
                    if (first == letter) break;
                    current = first;
                    offset += Px(LetterRow);
                }
                offset += Px(ListRow);
            }
            _letterPicker = false;
            _listScroll = Math.Min(offset, MaxListScroll());
            RenderSurface();
        }

        private float MaxListScroll()
        {
            float total = 0;
            char? current = null;
            foreach (ShellImage.App app in StartMenuService.AllApps)
            {
                char first = GroupLetter(app.Name);
                if (current != first) { current = first; total += Px(LetterRow); }
                total += Px(ListRow);
            }
            return Math.Max(0, total - Px(ListHeight));
        }

        private static char GroupLetter(string name)
        {
            char first = char.ToUpperInvariant(name.Length > 0 ? name[0] : '#');
            if ("ÁÀÄ".IndexOf(first) >= 0) return 'A';
            if ("ÉÈË".IndexOf(first) >= 0) return 'E';
            if ("ÍÌÏ".IndexOf(first) >= 0) return 'I';
            if ("ÓÒÖ".IndexOf(first) >= 0) return 'O';
            if ("ÚÙÜ".IndexOf(first) >= 0) return 'U';
            return char.IsLetter(first) ? first : '#';
        }

        private void DrawResults(Graphics g, float x, float y, float width)
        {
            List<SearchResult> results = AllResults;
            for (int i = 0; i < Math.Min(8, results.Count); i++)
            {
                SearchResult r = results[i];
                RectangleF row = new RectangleF(x, y + i * Px(48), width, Px(44));
                if (i == _selected) IslandPainter.FillRounded(g, row, Px(12), Color.FromArgb(36, IslandPainter.Sky));
                RectangleF icon = new RectangleF(row.X + Px(10), row.Y + (row.Height - Px(30)) / 2, Px(30), Px(30));
                if (r.Icon != null) _painter.DrawRoundedImage(g, r.Icon, icon, Px(8), 255);
                else
                {
                    IslandPainter.FillRounded(g, icon, Px(8), Color.FromArgb(30, 255, 255, 255));
                    string glyph = r.Type == SearchResult.Kind.Web ? "" : r.Type == SearchResult.Kind.Calculator ? "" : r.Type == SearchResult.Kind.Assistant ? "" : "";
                    _painter.DrawText(g, glyph, _painter.Glyph16, Color.FromArgb(195, 201, 212), icon, StringAlignment.Center);
                }
                float textX = icon.Right + Px(12);
                _painter.DrawText(g, r.Title, _painter.Semibold13, Color.White, new RectangleF(textX, row.Y + Px(3), row.Right - textX - Px(60), Px(19)), StringAlignment.Near);
                _painter.DrawText(g, r.Subtitle, _painter.Text12, IslandPainter.Gray, new RectangleF(textX, row.Y + Px(22), row.Right - textX - Px(60), Px(17)), StringAlignment.Near);
                if (i == _selected) _painter.DrawText(g, "Enter ↵", _painter.Bold12, Link, new RectangleF(row.Right - Px(64), row.Y, Px(54), row.Height), StringAlignment.Far);
                SearchResult target = r;
                _hits.Add(new KeyValuePair<RectangleF, Action>(row, () => Run(target)));
            }
        }

        private void DrawFooter(Graphics g, float cardWidth, float cardHeight)
        {
            RectangleF footer = new RectangleF(0, cardHeight - Px(FooterHeight), cardWidth, Px(FooterHeight));
            GraphicsState state = g.Save();
            using (GraphicsPath clip = RoundedRect(new RectangleF(0, 0, cardWidth, cardHeight), Px(20))) g.SetClip(clip);
            using (SolidBrush strip = new SolidBrush(Color.FromArgb(10, 255, 255, 255))) g.FillRectangle(strip, footer);
            using (Pen line = new Pen(Color.FromArgb(20, 255, 255, 255), 1)) g.DrawLine(line, footer.X, footer.Y, footer.Right, footer.Y);
            g.Restore(state);

            float x = Px(Pad);
            RectangleF avatar = new RectangleF(x, footer.Y + (footer.Height - Px(32)) / 2, Px(32), Px(32));
            Bitmap picture = StartMenuService.UserPicture;
            if (picture != null) _painter.DrawRoundedImage(g, picture, avatar, Px(16), 255);
            else
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(avatar, IslandPainter.Sky, Color.FromArgb(167, 139, 250), 45f)) g.FillEllipse(brush, avatar);
                _painter.DrawText(g, StartMenuService.UserName.Substring(0, 1).ToUpperInvariant(), _painter.Bold12, Color.White, avatar, StringAlignment.Center);
            }
            _painter.DrawText(g, StartMenuService.UserName, _painter.Semibold13, Color.White, new RectangleF(avatar.Right + Px(10), footer.Y, Px(300), footer.Height), StringAlignment.Near);

            RectangleF power = new RectangleF(cardWidth - Px(Pad) - Px(34), footer.Y + (footer.Height - Px(34)) / 2, Px(34), Px(34));
            IslandPainter.FillRounded(g, power, Px(10), Color.FromArgb(_powerOpen || Hover(power) ? 36 : 15, 255, 255, 255));
            _painter.DrawText(g, "", _painter.Glyph16, Color.White, power, StringAlignment.Center);
            _hits.Add(new KeyValuePair<RectangleF, Action>(power, () => { _powerOpen = !_powerOpen; _powerConfirm = null; RenderSurface(); }));
        }

        // Menú de energía sobre el botón ⏻; Reiniciar y Apagar piden un segundo clic
        private void DrawPowerMenu(Graphics g, float cardWidth, float cardHeight)
        {
            string[] labels = { "Bloquear", "Suspender", "Reiniciar", "Apagar" };
            string[] glyphs = { "", "", "", "" };
            StartMenuService.PowerAction[] actions = { StartMenuService.PowerAction.Lock, StartMenuService.PowerAction.Sleep, StartMenuService.PowerAction.Restart, StartMenuService.PowerAction.Shutdown };
            float width = Px(230), itemHeight = Px(38);
            RectangleF box = new RectangleF(cardWidth - Px(Pad) - width, cardHeight - Px(FooterHeight) - Px(8) - labels.Length * itemHeight - Px(12), width, labels.Length * itemHeight + Px(12));
            IslandPainter.FillRounded(g, box, Px(14), Color.FromArgb(22, 27, 38));
            using (GraphicsPath path = RoundedRect(box, Px(14)))
            using (Pen border = new Pen(Color.FromArgb(36, 255, 255, 255), 1)) g.DrawPath(border, path);
            for (int i = 0; i < labels.Length; i++)
            {
                StartMenuService.PowerAction action = actions[i];
                RectangleF item = new RectangleF(box.X + Px(6), box.Y + Px(6) + i * itemHeight, box.Width - Px(12), itemHeight);
                bool confirming = _powerConfirm == action;
                if (Hover(item) || confirming) IslandPainter.FillRounded(g, item, Px(9), confirming ? Color.FromArgb(60, 251, 146, 60) : Color.FromArgb(20, 255, 255, 255));
                _painter.DrawText(g, glyphs[i], _painter.Glyph16, confirming ? IslandPainter.Orange : Color.White, new RectangleF(item.X + Px(8), item.Y, Px(22), item.Height), StringAlignment.Center);
                _painter.DrawText(g, confirming ? "Clic otra vez para " + labels[i].ToLowerInvariant() : labels[i], _painter.Text13,
                    confirming ? IslandPainter.Orange : Color.White, new RectangleF(item.X + Px(38), item.Y, item.Width - Px(40), item.Height), StringAlignment.Near);
                _hits.Add(new KeyValuePair<RectangleF, Action>(item, () =>
                {
                    bool needsConfirm = action == StartMenuService.PowerAction.Restart || action == StartMenuService.PowerAction.Shutdown;
                    if (needsConfirm && _powerConfirm != action) { _powerConfirm = action; RenderSurface(); return; }
                    HideMenu();
                    StartMenuService.Power(action);
                }));
            }
        }

        #endregion

        #region Iconos

        private readonly Dictionary<string, Bitmap> _recentIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        private void DrawAppIcon(Graphics g, ShellImage.App app, RectangleF rect)
        {
            Bitmap icon = NotificationService.IconFor(app.Id);
            if (icon != null)
            {
                GraphicsState state = g.Save();
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(icon, rect);
                g.Restore(state);
            }
            else
            {
                IslandPainter.FillRounded(g, rect, Px(8), Color.FromArgb(40, IslandPainter.Sky));
                _painter.DrawText(g, app.Name.Substring(0, 1), _painter.Bold15, Color.White, rect, StringAlignment.Center);
            }
        }

        private Bitmap RecentIcon(RecentFile file)
        {
            Bitmap icon;
            if (!_recentIcons.TryGetValue(file.LinkPath, out icon))
            {
                icon = ShellImage.Get(file.LinkPath, 48, ShellImage.ThumbnailOrIcon);
                _recentIcons[file.LinkPath] = icon;
            }
            return icon;
        }

        // "Microsoft Edge" -> "Edge", nombres largos recortados para la rejilla
        private static string ShortName(string name)
        {
            if (name.StartsWith("Microsoft ", StringComparison.OrdinalIgnoreCase) && name.Length > 14) name = name.Substring(10);
            return name;
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _fileTimer.Dispose();
                _painter.Dispose();
                _inputFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Tecla Windows (fase H): si se pulsa y se suelta sola, abre el menú de la suite en vez del de Windows.
    /// Win + otra tecla (Win + E, Win + D...) sigue llegando a Windows igual. Para que Windows no abra su menú
    /// al soltar la tecla, se inyecta una pulsación de una tecla sin uso (Windows lo toma como combinación), igual
    /// que hacen Open-Shell o StartAllBack.
    /// </summary>
    internal class WinKeyHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
        private const int VkLeftWin = 0x5B, VkRightWin = 0x5C;
        private const byte VkUnassigned = 0xFF;
        private const uint LLKHF_INJECTED = 0x10;
        private const uint KeyUp = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct KBDLLHOOKSTRUCT
        {
            public uint vkCode, scanCode, flags, time;
            public IntPtr extraInfo;
        }

        private readonly NativeMethods.LowLevelMouseProc _proc; // Misma firma que el del ratón; referencia viva para el GC
        private readonly Action _pressed;
        private IntPtr _hook;
        private bool _winDown, _combo;

        public WinKeyHook(Action pressed)
        {
            _pressed = pressed;
            _proc = OnKey;
        }

        public void Apply()
        {
            bool wanted = SuiteSettings.StartMenuWinKey;
            if (wanted && _hook == IntPtr.Zero)
            {
                _hook = NativeMethods.SetWindowsHookEx(WH_KEYBOARD_LL, _proc, NativeMethods.GetModuleHandle(null), 0);
                if (_hook == IntPtr.Zero) Log.Error("[WinKeyHook] No se pudo instalar el gancho de teclado");
            }
            else if (!wanted && _hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }

        private IntPtr OnKey(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0)
            {
                KBDLLHOOKSTRUCT key = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(data, typeof(KBDLLHOOKSTRUCT));
                int msg = message.ToInt32();
                bool isWin = key.vkCode == VkLeftWin || key.vkCode == VkRightWin;
                bool injected = (key.flags & LLKHF_INJECTED) != 0;
                if (!injected)
                {
                    if (isWin && (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN))
                    {
                        if (!_winDown) { _winDown = true; _combo = false; }
                    }
                    else if (!isWin && (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN) && _winDown)
                    {
                        _combo = true; // Win + otra tecla: es de Windows
                    }
                    else if (isWin && (msg == WM_KEYUP || msg == WM_SYSKEYUP) && _winDown)
                    {
                        _winDown = false;
                        if (!_combo)
                        {
                            // Una tecla sin uso entre medias: Windows cree que fue una combinación y no abre su menú
                            NativeMethods.keybd_event(VkUnassigned, 0, 0, UIntPtr.Zero);
                            NativeMethods.keybd_event(VkUnassigned, 0, KeyUp, UIntPtr.Zero);
                            if (_pressed != null) _pressed();
                        }
                    }
                }
            }
            return NativeMethods.CallNextHookEx(_hook, code, message, data);
        }

        public void Dispose()
        {
            if (_hook != IntPtr.Zero) { NativeMethods.UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        }
    }
}
