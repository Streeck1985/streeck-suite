using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Una cosa copiada: texto, enlace, color, imagen o archivos.
    /// </summary>
    internal class ClipItem
    {
        public enum ItemKind { Text, Link, Color, Image, Files }
        public ItemKind Kind;
        public string Text;          // Texto, enlace, color (#RRGGBB) o rutas (una por línea)
        public Bitmap Image;         // Solo imágenes
        public Bitmap Thumb;         // Miniatura para la lista
        public bool Pinned;
        public DateTime Time = DateTime.Now;
    }

    /// <summary>
    /// Historial del portapapeles (mockup aprobado): escucha cada vez que se copia algo y guarda los últimos 50.
    /// Los fijados (★) se guardan en portapapeles_fijados.txt y sobreviven al reiniciar; el resto solo vive en memoria.
    /// No guarda lo que los administradores de contraseñas marcan como privado.
    /// </summary>
    internal static class ClipboardHistory
    {
        private const int MaxItems = 50;
        private const int MaxImages = 10;
        private static readonly string PinnedPath = Path.Combine(Path.GetDirectoryName(Log.FilePath), "portapapeles_fijados.txt");
        private static readonly Regex LinkPattern = new Regex(@"^https?://\S+$", RegexOptions.IgnoreCase);
        private static readonly Regex ColorPattern = new Regex(@"^#([0-9a-f]{6}|[0-9a-f]{3})$", RegexOptions.IgnoreCase);

        private static readonly List<ClipItem> _items = new List<ClipItem>();
        private static Listener _listener;
        internal static bool IgnoreNext; // Lo que copia la propia lista al pegar no se vuelve a anotar

        public static event Action Changed;

        public static List<ClipItem> Items
        {
            get { return new List<ClipItem>(_items); }
        }

        public static void Start()
        {
            if (_listener != null) return;
            LoadPinned();
            _listener = new Listener();
        }

        public static void Stop()
        {
            if (_listener != null) { _listener.Dispose(); _listener = null; }
        }

        private static void Capture()
        {
            if (IgnoreNext) { IgnoreNext = false; return; }
            try
            {
                IDataObject data = Clipboard.GetDataObject();
                if (data == null) return;
                // Contraseñas y datos que la app de origen pide no guardar en ningún historial
                if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore")) return;
                if (data.GetDataPresent("CanIncludeInClipboardHistory"))
                {
                    MemoryStream flag = data.GetData("CanIncludeInClipboardHistory") as MemoryStream;
                    if (flag != null && flag.Length >= 4 && flag.ToArray()[0] == 0) return;
                }

                ClipItem item = null;
                if (data.GetDataPresent(DataFormats.FileDrop))
                {
                    string[] files = data.GetData(DataFormats.FileDrop) as string[];
                    if (files != null && files.Length > 0) item = new ClipItem { Kind = ClipItem.ItemKind.Files, Text = string.Join("\n", files) };
                }
                else if (data.GetDataPresent(DataFormats.UnicodeText))
                {
                    string text = (data.GetData(DataFormats.UnicodeText) as string ?? "").Trim();
                    if (text.Length == 0 || text.Length > 100000) return;
                    ClipItem.ItemKind kind = LinkPattern.IsMatch(text) ? ClipItem.ItemKind.Link
                                           : ColorPattern.IsMatch(text) ? ClipItem.ItemKind.Color : ClipItem.ItemKind.Text;
                    item = new ClipItem { Kind = kind, Text = text };
                }
                else if (Clipboard.ContainsImage())
                {
                    Image image = Clipboard.GetImage();
                    if (image == null) return;
                    Bitmap copy = new Bitmap(image);
                    image.Dispose();
                    item = new ClipItem { Kind = ClipItem.ItemKind.Image, Image = copy, Thumb = Thumbnail(copy), Text = string.Format("Imagen {0}×{1}", copy.Width, copy.Height) };
                }
                if (item != null) Add(item);
            }
            catch (Exception ex)
            {
                // Otra app puede tener el portapapeles abierto en ese instante
                Log.Error("[ClipboardHistory] No se pudo leer el portapapeles: {0}", ex.Message);
            }
        }

        private static void Add(ClipItem item)
        {
            // Lo repetido sube arriba en vez de duplicarse (conserva su ★)
            if (item.Kind != ClipItem.ItemKind.Image)
            {
                ClipItem same = _items.Find(i => i.Kind == item.Kind && i.Text == item.Text);
                if (same != null) { _items.Remove(same); item.Pinned = same.Pinned; }
            }
            _items.Insert(0, item);

            int images = 0;
            for (int i = 0; i < _items.Count; i++)
            {
                ClipItem it = _items[i];
                if (it.Kind == ClipItem.ItemKind.Image && ++images > MaxImages && !it.Pinned) { Dispose(it); _items.RemoveAt(i--); }
            }
            while (_items.Count > MaxItems)
            {
                int last = _items.FindLastIndex(i => !i.Pinned);
                if (last < 0) break;
                Dispose(_items[last]);
                _items.RemoveAt(last);
            }
            Raise();
        }

        public static void TogglePin(ClipItem item)
        {
            item.Pinned = !item.Pinned;
            SavePinned();
            Raise();
        }

        public static void Remove(ClipItem item)
        {
            _items.Remove(item);
            if (item.Pinned) SavePinned();
            Dispose(item);
            Raise();
        }

        /// <summary>
        /// Lo pone en el portapapeles (sin anotarlo otra vez) y lo sube arriba.
        /// </summary>
        public static void CopyBack(ClipItem item)
        {
            IgnoreNext = true;
            try
            {
                switch (item.Kind)
                {
                    case ClipItem.ItemKind.Image: Clipboard.SetImage(item.Image); break;
                    case ClipItem.ItemKind.Files:
                        System.Collections.Specialized.StringCollection files = new System.Collections.Specialized.StringCollection();
                        files.AddRange(item.Text.Split('\n'));
                        Clipboard.SetFileDropList(files);
                        break;
                    default: Clipboard.SetText(item.Text); break;
                }
            }
            catch (Exception ex)
            {
                IgnoreNext = false;
                Log.Error("[ClipboardHistory] No se pudo copiar: {0}", ex.Message);
                return;
            }
            _items.Remove(item);
            _items.Insert(0, item);
            item.Time = DateTime.Now;
            Raise();
        }

        private static void Raise()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        private static void Dispose(ClipItem item)
        {
            if (item.Image != null) item.Image.Dispose();
            if (item.Thumb != null) item.Thumb.Dispose();
        }

        private static Bitmap Thumbnail(Bitmap source)
        {
            const int h = 48;
            int w = Math.Max(1, Math.Min(96, source.Width * h / Math.Max(1, source.Height)));
            Bitmap thumb = new Bitmap(w, h);
            using (Graphics g = Graphics.FromImage(thumb))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, w, h);
            }
            return thumb;
        }

        // Fijados: una línea por elemento, con \n escapado (solo texto, enlaces y colores)
        private static void SavePinned()
        {
            try
            {
                List<string> lines = new List<string>();
                foreach (ClipItem i in _items)
                {
                    if (i.Pinned && i.Kind != ClipItem.ItemKind.Image && i.Kind != ClipItem.ItemKind.Files)
                        lines.Add((int)i.Kind + "|" + i.Text.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"));
                }
                File.WriteAllLines(PinnedPath, lines.ToArray());
            }
            catch (IOException ex)
            {
                Log.Error("[ClipboardHistory] No se pudieron guardar los fijados: {0}", ex.Message);
            }
        }

        private static void LoadPinned()
        {
            try
            {
                if (!File.Exists(PinnedPath)) return;
                foreach (string line in File.ReadAllLines(PinnedPath))
                {
                    int bar = line.IndexOf('|');
                    int kind;
                    if (bar <= 0 || !int.TryParse(line.Substring(0, bar), out kind)) continue;
                    string text = Regex.Replace(line.Substring(bar + 1), @"\\(\\|n)", m => m.Groups[1].Value == "n" ? "\n" : "\\");
                    _items.Add(new ClipItem { Kind = (ClipItem.ItemKind)kind, Text = text, Pinned = true, Time = DateTime.MinValue });
                }
            }
            catch (IOException) { }
        }

        // Ventana oculta que recibe WM_CLIPBOARDUPDATE
        private class Listener : NativeWindow, IDisposable
        {
            private const int WM_CLIPBOARDUPDATE = 0x031D;

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool AddClipboardFormatListener(IntPtr hwnd);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

            public Listener()
            {
                CreateHandle(new CreateParams());
                if (!AddClipboardFormatListener(Handle)) Log.Error("[ClipboardHistory] No se pudo escuchar el portapapeles");
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_CLIPBOARDUPDATE) Capture();
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                if (Handle == IntPtr.Zero) return;
                RemoveClipboardFormatListener(Handle);
                DestroyHandle();
            }
        }
    }

    /// <summary>
    /// Ventana del portapapeles: baja desde la isla como el cajón de Claude, con buscador y la lista.
    /// Clic o Enter: lo pega donde estabas. ★ fija, ✕ borra. Esc o clic afuera la esconden.
    /// </summary>
    internal class ClipboardWindow : Form
    {
        private const float LogicalW = 440, LogicalH = 460, HeaderH = 52, RowH = 42, Pad = 10, Radius = 26;

        private readonly float _scale;
        private readonly TextBox _search;
        private List<ClipItem> _shown = new List<ClipItem>();
        private int _selected, _scroll, _hover = -1;
        private IntPtr _previous;
        private DateTime _shownAt;
        private readonly List<KeyValuePair<RectangleF, Action>> _hits = new List<KeyValuePair<RectangleF, Action>>();
        private readonly Font _rowFont = new Font("Segoe UI", 9.5f);
        private readonly Font _smallFont = new Font("Segoe UI", 8f);
        private readonly Font _glyphFont = new Font("Segoe Fluent Icons", 10f);

        /// <summary>
        /// Lo pone la isla: abre la ventana del celular con este elemento.
        /// </summary>
        public static Action<ClipItem> SendToPhone;

        public ClipboardWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            DoubleBuffered = true;
            KeyPreview = true;
            Text = "Streeck Suite · Portapapeles";
            using (Graphics g = CreateGraphics()) _scale = g.DpiX / 96f;

            _search = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Color.FromArgb(22, 24, 29),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10.5f)
            };
            _search.TextChanged += (s, e) => { _selected = 0; _scroll = 0; Filter(); };
            Controls.Add(_search);
            ClipboardHistory.Changed += () => { if (Visible && IsHandleCreated) BeginInvoke((Action)Filter); };
        }

        private float Px(float v) { return v * _scale; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x80; // Fuera de Alt+Tab
                return cp;
            }
        }

        public void Toggle()
        {
            if (Visible) { Hide(); return; }
            _previous = NativeMethods.GetForegroundWindow();
            Rectangle island = IslandEyes.IslandRect;
            Screen screen = island.Width > 0 ? Screen.FromRectangle(island) : Screen.PrimaryScreen;
            int w = (int)Px(LogicalW), h = (int)Px(LogicalH);
            int centerX = island.Width > 0 ? island.Left + island.Width / 2 : screen.Bounds.Left + screen.Bounds.Width / 2;
            Bounds = new Rectangle(Math.Max(screen.Bounds.Left, Math.Min(screen.Bounds.Right - w, centerX - w / 2)), screen.Bounds.Top, w, h);
            using (GraphicsPath shape = BottomRounded(new RectangleF(0, 0, w, h), Px(Radius))) Region = new Region(shape);
            _search.Bounds = new Rectangle((int)Px(64), (int)Px(17), w - (int)Px(64 + 2 * Pad + 8), (int)Px(22));
            _search.Text = "";
            _selected = 0; _scroll = 0;
            Filter();
            Show();
            SendMessage(_search.Handle, 0x1501, (IntPtr)1, "Buscar en lo que copiaste…"); // Texto de ayuda dentro del buscador
            Activate();
            _search.Focus();
            _shownAt = DateTime.Now;
        }

        private void Filter()
        {
            string q = _search.Text.Trim();
            _shown = ClipboardHistory.Items;
            if (q.Length > 0) _shown = _shown.FindAll(i => i.Text != null && i.Text.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
            // Fijados primero
            List<ClipItem> pinned = _shown.FindAll(i => i.Pinned), rest = _shown.FindAll(i => !i.Pinned);
            pinned.AddRange(rest);
            _shown = pinned;
            _selected = Math.Max(0, Math.Min(_selected, _shown.Count - 1));
            Invalidate();
        }

        private int VisibleRows
        {
            get { return (int)((Height - Px(HeaderH) - Px(Pad)) / Px(RowH)); }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Visible && (DateTime.Now - _shownAt).TotalMilliseconds > 300) Hide();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Hide(); return true;
                case Keys.Down: MoveSelection(1); return true;
                case Keys.Up: MoveSelection(-1); return true;
                case Keys.Enter: if (_selected < _shown.Count) Paste(_shown[_selected]); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void MoveSelection(int delta)
        {
            if (_shown.Count == 0) return;
            _selected = Math.Max(0, Math.Min(_shown.Count - 1, _selected + delta));
            if (_selected < _scroll) _scroll = _selected;
            if (_selected >= _scroll + VisibleRows) _scroll = _selected - VisibleRows + 1;
            Invalidate();
        }

        // Lo copia, vuelve a la ventana donde estabas y pega con Ctrl+V
        private void Paste(ClipItem item)
        {
            ClipboardHistory.CopyBack(item);
            Hide();
            if (_previous == IntPtr.Zero) return;
            NativeMethods.SetForegroundWindow(_previous);
            System.Threading.Thread.Sleep(60);
            const byte VK_CONTROL = 0x11, VK_V = 0x56;
            const uint KEYUP = 2;
            NativeMethods.keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(VK_V, 0, 0, UIntPtr.Zero);
            NativeMethods.keybd_event(VK_V, 0, KEYUP, UIntPtr.Zero);
            NativeMethods.keybd_event(VK_CONTROL, 0, KEYUP, UIntPtr.Zero);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _scroll = Math.Max(0, Math.Min(Math.Max(0, _shown.Count - VisibleRows), _scroll - Math.Sign(e.Delta)));
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row = RowAt(e.Location);
            if (row != _hover) { _hover = row; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            Invalidate();
        }

        private int RowAt(Point p)
        {
            float top = Px(HeaderH);
            if (p.Y < top) return -1;
            int row = _scroll + (int)((p.Y - top) / Px(RowH));
            return row < _shown.Count ? row : -1;
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            foreach (KeyValuePair<RectangleF, Action> hit in _hits)
            {
                if (hit.Key.Contains(e.Location)) { hit.Value(); return; }
            }
            int row = RowAt(e.Location);
            if (row >= 0) Paste(_shown[row]);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Black);
            _hits.Clear();
            Color gray = Color.FromArgb(139, 146, 163), soft = Color.FromArgb(22, 24, 29);

            // Ojitos y buscador
            IslandEyes.DrawPreview(g, new PointF(Px(34), Px(28)), 0.55f * _scale, IslandEyes.Mood.Idle);
            RectangleF box = new RectangleF(Px(56), Px(12), Width - Px(56 + Pad), Px(32));
            IslandPainter.FillRounded(g, box, Px(10), soft);

            float y = Px(HeaderH), x = Px(Pad), w = Width - 2 * Px(Pad);
            if (_shown.Count == 0)
            {
                TextRenderer.DrawText(g, _search.Text.Length > 0 ? "Nada coincide" : "Copia algo y aparecerá aquí", _rowFont,
                    Rectangle.Round(new RectangleF(x, y, w, Px(60))), gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            for (int i = _scroll; i < _shown.Count && i < _scroll + VisibleRows; i++)
            {
                ClipItem item = _shown[i];
                RectangleF row = new RectangleF(x, y, w, Px(RowH) - Px(4));
                bool active = i == _selected || i == _hover;
                IslandPainter.FillRounded(g, row, Px(10), active ? Color.FromArgb(32, 35, 43) : soft);

                // Icono o miniatura
                RectangleF icon = new RectangleF(row.X + Px(8), row.Y + (row.Height - Px(24)) / 2, Px(34), Px(24));
                if (item.Kind == ClipItem.ItemKind.Image && item.Thumb != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(item.Thumb, icon);
                }
                else if (item.Kind == ClipItem.ItemKind.Color)
                {
                    Color c;
                    try { c = ColorTranslator.FromHtml(item.Text); } catch (Exception) { c = Color.Gray; }
                    IslandPainter.FillRounded(g, new RectangleF(icon.X + Px(5), icon.Y, Px(24), Px(24)), Px(6), c);
                }
                else
                {
                    string glyph = item.Kind == ClipItem.ItemKind.Link ? "" : item.Kind == ClipItem.ItemKind.Files ? "" : "";
                    TextRenderer.DrawText(g, glyph, _glyphFont, Rectangle.Round(icon), Color.FromArgb(205, 208, 216), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }

                // Acciones a la derecha: ★ fijar y ✕ borrar
                RectangleF del = new RectangleF(row.Right - Px(30), row.Y + (row.Height - Px(24)) / 2, Px(24), Px(24));
                RectangleF pin = new RectangleF(del.X - Px(28), del.Y, Px(24), Px(24));
                ClipItem captured = item;
                _hits.Add(new KeyValuePair<RectangleF, Action>(pin, () => ClipboardHistory.TogglePin(captured)));
                _hits.Add(new KeyValuePair<RectangleF, Action>(del, () => ClipboardHistory.Remove(captured)));
                // Pasar al celular (QR)
                RectangleF phone = new RectangleF(pin.X - Px(28), del.Y, Px(24), Px(24));
                if (active && SendToPhone != null)
                {
                    _hits.Add(new KeyValuePair<RectangleF, Action>(phone, () => { Hide(); SendToPhone(captured); }));
                    TextRenderer.DrawText(g, "\uE8EA", _glyphFont, Rectangle.Round(phone), gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                TextRenderer.DrawText(g, item.Pinned ? "" : "", _glyphFont, Rectangle.Round(pin),
                    item.Pinned ? Color.FromArgb(255, 181, 71) : (active ? gray : Color.FromArgb(70, 74, 86)), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                if (active) TextRenderer.DrawText(g, "", _glyphFont, Rectangle.Round(del), gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

                // Texto (una línea) y cuándo
                string text = item.Kind == ClipItem.ItemKind.Files ? FilesLabel(item.Text) : item.Text.Replace("\r", " ").Replace("\n", " ");
                string when = item.Pinned ? "fijado" : Ago(item.Time);
                Size whenSize = TextRenderer.MeasureText(when, _smallFont);
                RectangleF textRect = new RectangleF(icon.Right + Px(8), row.Y, phone.X - icon.Right - Px(10) - whenSize.Width, row.Height);
                TextRenderer.DrawText(g, text, _rowFont, Rectangle.Round(textRect), Color.White,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, when, _smallFont, Rectangle.Round(new RectangleF(textRect.Right + Px(4), row.Y, whenSize.Width + Px(2), row.Height)), gray,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
                y += Px(RowH);
            }
        }

        private static string FilesLabel(string text)
        {
            string[] files = text.Split('\n');
            return files.Length == 1 ? Path.GetFileName(files[0]) : files.Length + " archivos: " + Path.GetFileName(files[0]) + "…";
        }

        private static string Ago(DateTime time)
        {
            TimeSpan span = DateTime.Now - time;
            if (span.TotalMinutes < 1) return "ahora";
            if (span.TotalMinutes < 60) return "hace " + (int)span.TotalMinutes + " min";
            if (span.TotalHours < 24) return "hace " + (int)span.TotalHours + " h";
            return time.ToString("d MMM");
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
    /// Atajo global genérico (Ctrl + Alt + tecla) para las ventanas de la isla.
    /// </summary>
    internal class IslandHotkey : NativeWindow, IDisposable
    {
        private readonly int _id;
        private readonly uint _key;
        private readonly Action _pressed;
        private bool _registered;

        public IslandHotkey(int id, Keys key, Action pressed)
        {
            _id = id;
            _key = (uint)key;
            _pressed = pressed;
            CreateHandle(new CreateParams());
        }

        public bool Apply()
        {
            if (_registered) { NativeMethods.UnregisterHotKey(Handle, _id); _registered = false; }
            _registered = NativeMethods.RegisterHotKey(Handle, _id, NativeMethods.MOD_NOREPEAT | NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, _key);
            if (!_registered) Log.Error("[IslandHotkey] Ctrl+Alt+{0} está ocupado por otro programa", (Keys)_key);
            return _registered;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == _id && _pressed != null) _pressed();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            if (_registered) NativeMethods.UnregisterHotKey(Handle, _id);
            DestroyHandle();
        }
    }
}
