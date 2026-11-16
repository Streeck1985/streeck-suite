using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using StreeckSuite.Core;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// «Copiar texto de la pantalla» (mockup aprobado, sustituye al extractor de PowerToys): se congela la pantalla,
    /// arrastras un recuadro y el lector de texto de Windows (OCR, sin internet) deja copiado lo que diga, en español o
    /// inglés. El resultado va al portapapeles y al historial marcado «De la pantalla».
    /// </summary>
    internal static class ScreenText
    {
        /// <summary>Texto leído, número de líneas e idioma («Español» o «Inglés»).</summary>
        public static event Action<string, int, string> Copied;
        public static event Action NotFound;

        private static ScreenTextWindow _open;
        private static SynchronizationContext _ui;

        public static void Start()
        {
            if (_open != null && !_open.IsDisposed) return;
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            Bitmap shot = new Bitmap(virtualScreen.Width, virtualScreen.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(shot)) g.CopyFromScreen(virtualScreen.Location, Point.Empty, virtualScreen.Size);
            _open = new ScreenTextWindow(shot, virtualScreen);
            _open.Show();
            _open.Activate();
        }

        internal static void Recognize(Bitmap crop)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string text = null, language = null;
                int lines = 0;
                try
                {
                    text = ReadBest(crop, out lines, out language);
                }
                catch (Exception ex)
                {
                    Log.Error("[ScreenText] No se pudo leer el texto: {0}", ex.Message);
                }
                finally
                {
                    crop.Dispose();
                }
                _ui.Post(state =>
                {
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        Action notFound = NotFound;
                        if (notFound != null) notFound();
                        return;
                    }
                    try
                    {
                        ClipboardHistory.MarkNextFromScreen();
                        Clipboard.SetText(text);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("[ScreenText] No se pudo copiar: {0}", ex.Message);
                        return;
                    }
                    Action<string, int, string> copied = Copied;
                    if (copied != null) copied(text, lines, language);
                }, null);
            });
        }

        /// <summary>Herramienta de desarrollo (--test-ocr): lee una imagen como si se hubiera recortado de la pantalla.</summary>
        internal static string TestRead(string path, out int lines, out string language)
        {
            using (Bitmap image = new Bitmap(path)) return ReadBest(image, out lines, out language);
        }

        // Automático: lee primero en español; si el texto parece inglés, lo vuelve a leer con el lector de inglés
        private static string ReadBest(Bitmap crop, out int lines, out string language)
        {
            using (Bitmap prepared = Prepare(crop))
            {
                SuiteSettings.OcrLanguageKind choice = SuiteSettings.OcrLanguage;
                string first = choice == SuiteSettings.OcrLanguageKind.English ? "en" : "es";
                string text = Read(prepared, first, out lines);
                if (text == null && choice == SuiteSettings.OcrLanguageKind.Auto) { first = "en"; text = Read(prepared, "en", out lines); }
                language = first == "en" ? "Inglés" : "Español";
                if (choice == SuiteSettings.OcrLanguageKind.Auto && first == "es" && LooksEnglish(text))
                {
                    int englishLines;
                    string english = Read(prepared, "en", out englishLines);
                    if (!string.IsNullOrWhiteSpace(english)) { text = english; lines = englishLines; language = "Inglés"; }
                    else language = "Inglés";
                }
                return text;
            }
        }

        private static readonly Regex Words = new Regex(@"[A-Za-zÁÉÍÓÚÜÑáéíóúüñ']+");
        private static readonly string[] Spanish = { "de", "la", "el", "que", "y", "en", "los", "las", "por", "con", "para", "un", "una", "es", "no", "se", "del", "al", "lo", "su" };
        private static readonly string[] English = { "the", "and", "of", "to", "is", "in", "for", "with", "on", "it", "this", "that", "you", "not", "be", "are", "from", "at", "or", "an" };

        private static bool LooksEnglish(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            if (text.IndexOfAny("áéíóúñÁÉÍÓÚÑ¿¡".ToCharArray()) >= 0) return false;
            int es = 0, en = 0;
            foreach (Match m in Words.Matches(text.ToLowerInvariant()))
            {
                if (Spanish.Contains(m.Value)) es++;
                if (English.Contains(m.Value)) en++;
            }
            return en > es;
        }

        // Texto chico se lee mejor ampliado; el lector de Windows tiene un tamaño máximo
        private static Bitmap Prepare(Bitmap crop)
        {
            float scale = crop.Height < 120 ? 3f : crop.Height < 400 ? 2f : 1f;
            float max = OcrEngine.MaxImageDimension;
            scale = Math.Min(scale, Math.Min(max / crop.Width, max / crop.Height));
            int w = Math.Max(1, (int)(crop.Width * scale)), h = Math.Max(1, (int)(crop.Height * scale));
            Bitmap result = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(crop, new Rectangle(0, 0, w, h));
            }
            return result;
        }

        private static string Read(Bitmap bitmap, string languageTag, out int lines)
        {
            lines = 0;
            OcrEngine engine = null;
            foreach (Language available in OcrEngine.AvailableRecognizerLanguages)
            {
                if (available.LanguageTag.StartsWith(languageTag, StringComparison.OrdinalIgnoreCase)) { engine = OcrEngine.TryCreateFromLanguage(available); break; }
            }
            if (engine == null) return null; // Ese idioma no está instalado en Windows

            using (SoftwareBitmap software = ToSoftwareBitmap(bitmap))
            {
                OcrResult result = WinRtAsync.Wait(engine.RecognizeAsync(software));
                string[] text = result.Lines.Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
                lines = text.Length;
                return string.Join(Environment.NewLine, text);
            }
        }

        private static SoftwareBitmap ToSoftwareBitmap(Bitmap bitmap)
        {
            byte[] png;
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, ImageFormat.Png);
                png = memory.ToArray();
            }
            using (InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream())
            {
                using (DataWriter writer = new DataWriter(stream.GetOutputStreamAt(0)))
                {
                    writer.WriteBytes(png);
                    WinRtAsync.Wait(writer.StoreAsync());
                    WinRtAsync.Wait(writer.FlushAsync());
                    writer.DetachStream();
                }
                stream.Seek(0);
                BitmapDecoder decoder = WinRtAsync.Wait(BitmapDecoder.CreateAsync(stream));
                return WinRtAsync.Wait(decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied));
            }
        }
    }

    /// <summary>
    /// La pantalla congelada y oscurecida: arrastras el recuadro sobre el texto. Esc o clic derecho cancelan.
    /// </summary>
    internal class ScreenTextWindow : Form
    {
        private readonly Bitmap _shot;
        private readonly Rectangle _bounds;
        private Point _start;
        private bool _dragging;
        private Rectangle _selection;
        private readonly Font _hintFont = new Font("Segoe UI Semibold", 10f);
        private readonly Font _hintSmall = new Font("Segoe UI", 10f);
        private static readonly Color Accent = Color.FromArgb(56, 189, 248);

        public ScreenTextWindow(Bitmap shot, Rectangle bounds)
        {
            _shot = shot;
            _bounds = bounds;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            BackColor = Color.Black;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = _bounds; // Por si Windows la acomodó a un solo monitor
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { Close(); return; }
            if (e.Button != MouseButtons.Left) return;
            _start = e.Location;
            _dragging = true;
            _selection = Rectangle.Empty;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!_dragging) { Invalidate(HintRect()); return; }
            Rectangle old = _selection;
            _selection = Rectangle.FromLTRB(Math.Min(_start.X, e.X), Math.Min(_start.Y, e.Y), Math.Max(_start.X, e.X), Math.Max(_start.Y, e.Y));
            Rectangle dirty = Rectangle.Union(old, _selection);
            dirty.Inflate(8, 8);
            Invalidate(dirty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!_dragging || e.Button != MouseButtons.Left) return;
            _dragging = false;
            Rectangle area = Rectangle.Intersect(_selection, new Rectangle(Point.Empty, _shot.Size));
            if (area.Width < 6 || area.Height < 6) { _selection = Rectangle.Empty; Invalidate(); return; } // Un clic sin arrastrar
            Bitmap crop = _shot.Clone(area, PixelFormat.Format32bppArgb);
            Close();
            ScreenText.Recognize(crop);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawImageUnscaled(_shot, 0, 0);
            using (Region dim = new Region(ClientRectangle))
            {
                if (_selection.Width > 0 && _selection.Height > 0) dim.Exclude(_selection);
                using (SolidBrush shade = new SolidBrush(Color.FromArgb(140, 0, 0, 0))) g.FillRegion(shade, dim);
            }
            if (_selection.Width > 0 && _selection.Height > 0)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen border = new Pen(Accent, 2)) g.DrawRectangle(border, _selection);
                using (SolidBrush handle = new SolidBrush(Accent))
                {
                    foreach (Point p in new[] { _selection.Location, new Point(_selection.Right, _selection.Top), new Point(_selection.Left, _selection.Bottom), new Point(_selection.Right, _selection.Bottom) })
                        g.FillRectangle(handle, p.X - 4, p.Y - 4, 8, 8);
                }
            }
            DrawHint(g);
        }

        // Letrero abajo, centrado en el monitor donde está el ratón
        private Rectangle HintRect()
        {
            Rectangle screen = Screen.FromPoint(Cursor.Position).Bounds;
            screen.Offset(-_bounds.X, -_bounds.Y);
            return new Rectangle(screen.X + screen.Width / 2 - 200, screen.Bottom - 90, 400, 50);
        }

        private void DrawHint(Graphics g)
        {
            const string bold = "Arrastra sobre el texto", light = " · Esc para cancelar";
            Size a = TextRenderer.MeasureText(bold, _hintFont), b = TextRenderer.MeasureText(light, _hintSmall);
            Rectangle area = HintRect();
            int w = a.Width + b.Width + 20, h = Math.Max(a.Height, b.Height) + 12;
            Rectangle pill = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = IslandPainter.RoundedRect(pill, h / 2f))
            using (SolidBrush black = new SolidBrush(Color.FromArgb(235, 0, 0, 0)))
                g.FillPath(black, path);
            TextRenderer.DrawText(g, bold, _hintFont, new Point(pill.X + 10, pill.Y + (h - a.Height) / 2), Color.White);
            TextRenderer.DrawText(g, light, _hintSmall, new Point(pill.X + 10 + a.Width - 4, pill.Y + (h - b.Height) / 2), Color.FromArgb(154, 161, 178));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _shot.Dispose();
                _hintFont.Dispose();
                _hintSmall.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
