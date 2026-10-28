using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Módulo Desktop Widgets: tarjetas de cristal independientes al nivel del escritorio (telemetría real,
    /// canción de Spotify y lista de tareas). Cada una se arrastra a donde se quiera y recuerda su posición.
    /// </summary>
    public class DesktopWidgetsModule : IModule
    {
        public string Id { get { return "desktop_widgets"; } }
        public string Name { get { return "Widgets de Escritorio"; } }
        public string Version { get { return "2.0.0"; } }
        public bool IsEnabled { get; set; }

        private const int RefreshIntervalMs = 2500;

        private EventBus _eventBus;
        private TelemetryWidget _telemetry;
        private MediaWidget _media;
        private NotesWidget _notes;
        private WidgetLayout _layout;
        private TelemetrySampler _sampler;
        private System.Threading.Timer _sampleTimer;
        private int _sampling; // 1 mientras hay una muestra en curso, para no solapar lecturas lentas
        private bool _isRunning;
        private bool _isGameModeActive;
        private bool _isHiddenByUser;

        public DesktopWidgetsModule()
        {
            IsEnabled = true;
        }

        public void Initialize(EventBus eventBus)
        {
            _eventBus = eventBus;
            _eventBus.Subscribe(Events.GameModeActive, OnGameModeActive);
            _eventBus.Subscribe(Events.WidgetsToggleVisibility, OnToggleVisibility);
            _eventBus.Subscribe(Events.WidgetToggle, OnToggleWidget);
            _eventBus.Subscribe(Events.WidgetsResetLayout, payload =>
            {
                if (_layout == null) return;
                _layout.Reset(_telemetry, _media, _notes);
                _eventBus.Publish(Events.NotchShowMessage, "🧩 Widgets recolocados");
            });
            // Acento y opacidad del cristal: se repintan al momento
            SuiteSettings.Changed += () =>
            {
                foreach (WidgetWindow widget in Widgets)
                {
                    if (widget != null && !widget.IsDisposed) widget.RenderSurface();
                }
            };
        }

        private void OnToggleWidget(object payload)
        {
            string id = payload as string;
            if (string.IsNullOrEmpty(id)) return;

            bool nowHidden = !WidgetVisibility.IsHidden(id);
            WidgetVisibility.SetHidden(id, nowHidden);
            UpdateVisibility();
            _eventBus.Publish(Events.NotchShowMessage, string.Format("🧩 {0}: {1}", WidgetVisibility.DisplayName(id), nowHidden ? "oculto" : "visible"));
        }

        public void Start()
        {
            if (_telemetry == null || _telemetry.IsDisposed)
            {
                _layout = new WidgetLayout();
                _telemetry = new TelemetryWidget(_layout, TelemetrySampler.GetCpuName(), TelemetrySampler.GetGpuName());
                _media = new MediaWidget(_layout);
                _notes = new NotesWidget(_layout);
                _layout.PlaceDefaults(_telemetry, _media, _notes);
            }
            _sampler = new TelemetrySampler();
            _isRunning = true;
            UpdateVisibility();
        }

        public void Stop()
        {
            _isRunning = false;
            UpdateVisibility();
            if (_sampler != null)
            {
                _sampler.Dispose();
                _sampler = null;
            }
        }

        private IEnumerable<WidgetWindow> Widgets
        {
            get { return new WidgetWindow[] { _telemetry, _media, _notes }; }
        }

        private void OnGameModeActive(object payload)
        {
            _isGameModeActive = payload is bool && (bool)payload;
            UpdateVisibility();
        }

        private void OnToggleVisibility(object payload)
        {
            _isHiddenByUser = !_isHiddenByUser;
            UpdateVisibility();
            _eventBus.Publish(Events.NotchShowMessage, _isHiddenByUser ? "🧩 Widgets ocultos" : "🧩 Widgets visibles");
        }

        /// <summary>
        /// Los widgets solo se muestran (y solo se mide la telemetría) si el módulo está activo,
        /// no hay un juego a pantalla completa y el usuario no los ha ocultado.
        /// </summary>
        private void UpdateVisibility()
        {
            if (_telemetry == null || _telemetry.IsDisposed) return;

            bool shouldShow = _isRunning && !_isGameModeActive && !_isHiddenByUser;
            foreach (WidgetWindow widget in Widgets)
            {
                if (shouldShow && !WidgetVisibility.IsHidden(widget.WidgetId)) widget.Show(); else widget.Hide();
            }

            if (shouldShow && _sampleTimer == null)
            {
                _sampleTimer = new System.Threading.Timer(OnSampleTimer, null, 0, RefreshIntervalMs);
            }
            else if (!shouldShow && _sampleTimer != null)
            {
                _sampleTimer.Dispose();
                _sampleTimer = null;
            }
        }

        // Se ejecuta en un hilo del pool: aquí se hacen las lecturas lentas y luego se pasa el resultado a la UI
        private void OnSampleTimer(object state)
        {
            if (Interlocked.Exchange(ref _sampling, 1) == 1) return;
            try
            {
                TelemetrySampler sampler = _sampler;
                TelemetryWidget telemetry = _telemetry;
                MediaWidget media = _media;
                NotesWidget notes = _notes;
                if (sampler == null || telemetry == null || telemetry.IsDisposed || !telemetry.IsHandleCreated) return;

                TelemetrySampler.Snapshot snapshot = sampler.Sample();
                MediaInfo info = MediaService.Current;
                telemetry.BeginInvoke((Action)(() =>
                {
                    telemetry.UpdateData(snapshot);
                    media.UpdateData(info);
                    notes.RenderSurface(); // Por si cambió el fondo de escritorio que se ve detrás
                }));
            }
            catch (Exception ex)
            {
                Log.Error("[DesktopWidgetsModule] Error al actualizar los widgets: {0}", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _sampling, 0);
            }
        }
    }

    /// <summary>
    /// Qué widgets eligió ocultar el usuario (%LOCALAPPDATA%\StreeckSuite\widgets_ocultos.txt, un id por línea).
    /// </summary>
    internal static class WidgetVisibility
    {
        public static readonly string[] AllIds = { "telemetria", "reproductor", "tareas" };

        private static readonly string FilePath = Path.Combine(Path.GetDirectoryName(Log.FilePath), "widgets_ocultos.txt");
        private static HashSet<string> _hidden;

        public static string DisplayName(string id)
        {
            switch (id)
            {
                case "telemetria": return "Telemetría";
                case "reproductor": return "Reproductor";
                case "tareas": return "Tareas pendientes";
                default: return id;
            }
        }

        public static bool IsHidden(string id)
        {
            return Hidden.Contains(id);
        }

        public static void SetHidden(string id, bool hidden)
        {
            if (hidden) Hidden.Add(id); else Hidden.Remove(id);
            try
            {
                File.WriteAllLines(FilePath, new List<string>(Hidden).ToArray());
            }
            catch (IOException ex)
            {
                Log.Error("[DesktopWidgetsModule] No se pudo guardar {0}: {1}", FilePath, ex.Message);
            }
        }

        private static HashSet<string> Hidden
        {
            get
            {
                if (_hidden != null) return _hidden;
                _hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    if (File.Exists(FilePath))
                    {
                        foreach (string line in File.ReadAllLines(FilePath))
                        {
                            if (line.Trim().Length > 0) _hidden.Add(line.Trim());
                        }
                    }
                }
                catch (IOException ex)
                {
                    Log.Error("[DesktopWidgetsModule] No se pudo leer {0}: {1}", FilePath, ex.Message);
                }
                return _hidden;
            }
        }
    }

    /// <summary>
    /// Posiciones guardadas de los widgets (%LOCALAPPDATA%\StreeckSuite\widgets.ini, una línea "id=x,y" por widget).
    /// </summary>
    internal class WidgetLayout
    {
        // Columna por defecto arriba a la derecha (el mockup la pone a la izquierda, pero ahí están los iconos)
        private const int DefaultMargin = 45;
        private const int DefaultTop = 75;
        private const int DefaultGap = 16;

        private readonly string _path = Path.Combine(Path.GetDirectoryName(Log.FilePath), "widgets.ini");
        private readonly Dictionary<string, Point> _saved = new Dictionary<string, Point>();

        public WidgetLayout()
        {
            try
            {
                if (!File.Exists(_path)) return;
                foreach (string line in File.ReadAllLines(_path))
                {
                    string[] parts = line.Split('=', ',');
                    int x, y;
                    if (parts.Length == 3 && int.TryParse(parts[1], out x) && int.TryParse(parts[2], out y))
                    {
                        _saved[parts[0].Trim()] = new Point(x, y);
                    }
                }
            }
            catch (IOException ex)
            {
                Log.Error("[DesktopWidgetsModule] No se pudo leer {0}: {1}", _path, ex.Message);
            }
        }

        /// <summary>
        /// Coloca cada widget en su posición guardada; si no tiene, o quedaría fuera de todas las pantallas
        /// (p. ej. se desconectó un monitor), lo apila en la columna por defecto.
        /// </summary>
        public void PlaceDefaults(params WidgetWindow[] widgets)
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int y = area.Top + widgets[0].Px(DefaultTop);
            foreach (WidgetWindow widget in widgets)
            {
                Point saved;
                if (_saved.TryGetValue(widget.WidgetId, out saved) && IsVisibleOnSomeScreen(new Rectangle(saved, widget.Size)))
                {
                    widget.Location = saved;
                }
                else
                {
                    widget.Location = new Point(area.Right - widget.Px(DefaultMargin) - widget.Width, y);
                }
                y += widget.Height + widget.Px(DefaultGap);
            }
        }

        /// <summary>
        /// Olvida las posiciones guardadas y vuelve a apilar los widgets en la columna por defecto.
        /// </summary>
        public void Reset(params WidgetWindow[] widgets)
        {
            _saved.Clear();
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            catch (IOException ex)
            {
                Log.Error("[DesktopWidgetsModule] No se pudo borrar {0}: {1}", _path, ex.Message);
            }
            PlaceDefaults(widgets);
        }

        public void Save(WidgetWindow widget)
        {
            _saved[widget.WidgetId] = widget.Location;
            try
            {
                List<string> lines = new List<string>();
                foreach (KeyValuePair<string, Point> entry in _saved)
                {
                    lines.Add(string.Format("{0}={1},{2}", entry.Key, entry.Value.X, entry.Value.Y));
                }
                File.WriteAllLines(_path, lines.ToArray());
            }
            catch (IOException ex)
            {
                Log.Error("[DesktopWidgetsModule] No se pudo guardar {0}: {1}", _path, ex.Message);
            }
        }

        private static bool IsVisibleOnSomeScreen(Rectangle bounds)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle overlap = Rectangle.Intersect(screen.WorkingArea, bounds);
                if (overlap.Width >= 40 && overlap.Height >= 40) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Base de cada widget: una tarjeta de cristal que vive al fondo del orden Z (justo encima del escritorio)
    /// y que se arrastra con el ratón.
    /// </summary>
    internal abstract class WidgetWindow : LayeredWindow
    {
        // Diseño a 96 PPP, igual que el mockup (.widget-card: 250px de ancho, 16px de padding, radio 18)
        protected const int BaseCardWidth = 250;
        protected const int BasePadding = 16;
        private const int BaseRadius = 18;
        private const int BaseShadow = 14;   // Margen alrededor de la tarjeta para su sombra difusa
        private const int DragThreshold = 4;

        // Color de acento elegido en el panel de ajustes (Widgets)
        protected static Color Accent
        {
            get { return SuiteSettings.WidgetAccent; }
        }

        // Variante clara del acento para el inicio de los degradados
        protected static Color AccentLight
        {
            get
            {
                Color c = Accent;
                return Color.FromArgb(c.R + (255 - c.R) * 3 / 10, c.G + (255 - c.G) * 3 / 10, c.B + (255 - c.B) * 3 / 10);
            }
        }
        protected static readonly Color Muted = Color.FromArgb(115, 255, 255, 255);
        protected static readonly Color Secondary = Color.FromArgb(184, 255, 255, 255);
        private static readonly Color BadgeBack = Color.FromArgb(51, 59, 130, 246);
        private static readonly Color BadgeText = Color.FromArgb(147, 197, 253);

        protected readonly Font TitleFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        protected readonly Font LabelFont = new Font("Segoe UI", 9f, FontStyle.Regular);
        protected readonly Font ValueFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        protected readonly Font SmallFont = new Font("Segoe UI", 7.5f, FontStyle.Regular);

        private readonly WidgetLayout _layout;
        private readonly float _scale;
        private Point _dragStartCursor, _dragStartLocation;
        private bool _mouseDown, _dragging;

        protected WidgetWindow(WidgetLayout layout, string widgetId)
        {
            _layout = layout;
            WidgetId = widgetId;

            using (Graphics screenGraphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                _scale = screenGraphics.DpiX / 96f;
            }

            Cursor = Cursors.SizeAll;
        }

        public string WidgetId { get; private set; }

        public int Px(float value)
        {
            return (int)Math.Round(value * _scale);
        }

        protected int HeaderHeight
        {
            get { return Px(16) + Px(12); }
        }

        private int ShadowSize
        {
            get { return Px(BaseShadow); }
        }

        /// <summary>
        /// Ajusta el alto de la tarjeta según su contenido. La ventana es algo mayor que la tarjeta para la sombra.
        /// </summary>
        protected void SetCardHeight(int height)
        {
            Size = new Size(Px(BaseCardWidth) + 2 * ShadowSize, height + 2 * ShadowSize);
            RenderSurface();
        }

        /// <summary>
        /// Clic sin arrastrar sobre la tarjeta.
        /// </summary>
        protected virtual void OnCardClick(Point location)
        {
        }

        #region Ventana al nivel del escritorio

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            RenderSurface(); // El trozo de fondo desenfocado que se ve detrás depende de la posición
        }

        protected override void WndProc(ref Message m)
        {
            // Cualquier intento de reordenar la ventana la devuelve al fondo, detrás de todas las aplicaciones
            if (m.Msg == NativeMethods.WM_WINDOWPOSCHANGING)
            {
                NativeMethods.WINDOWPOS pos = (NativeMethods.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.WINDOWPOS));
                if ((pos.flags & NativeMethods.SWP_NOZORDER) == 0)
                {
                    pos.hwndInsertAfter = NativeMethods.HWND_BOTTOM;
                    Marshal.StructureToPtr(pos, m.LParam, false);
                }
            }
            base.WndProc(ref m);
        }

        #endregion

        #region Arrastrar

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            _mouseDown = true;
            _dragging = false;
            _dragStartCursor = Cursor.Position;
            _dragStartLocation = Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_mouseDown) return;

            Point cursor = Cursor.Position;
            int dx = cursor.X - _dragStartCursor.X, dy = cursor.Y - _dragStartCursor.Y;
            if (!_dragging && (Math.Abs(dx) > DragThreshold || Math.Abs(dy) > DragThreshold)) _dragging = true;
            if (_dragging) Location = new Point(_dragStartLocation.X + dx, _dragStartLocation.Y + dy);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!_mouseDown) return;
            _mouseDown = false;

            if (_dragging)
            {
                _layout.Save(this);
            }
            else
            {
                OnCardClick(e.Location);
            }
            _dragging = false;
        }

        #endregion

        #region Dibujo común

        protected override void DrawSurface(Graphics g)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            int shadow = ShadowSize;
            Rectangle card = new Rectangle(shadow, shadow, Width - 2 * shadow - 1, Height - 2 * shadow - 1);
            float radius = Px(BaseRadius);
            DrawSoftShadow(g, card, radius, shadow * 0.9f, Px(4), 140);

            using (GraphicsPath path = LayeredWindow.RoundedRect(card, radius))
            {
                // Cristal: el trozo del fondo de escritorio desenfocado que queda detrás de la tarjeta
                Bitmap backdrop = DesktopBackdrop.Get();
                Rectangle screen = Screen.PrimaryScreen.Bounds;
                if (backdrop != null)
                {
                    Region oldClip = g.Clip;
                    g.SetClip(path);
                    g.DrawImage(backdrop, card, new Rectangle(Left + card.X - screen.X, Top + card.Y - screen.Y, card.Width, card.Height), GraphicsUnit.Pixel);
                    g.Clip = oldClip;
                }

                // Opacidad del cristal (panel de ajustes): cuánto tapa el tinte oscuro al fondo desenfocado
                int glass = backdrop != null ? SuiteSettings.WidgetGlassPercent * 255 / 100 : 225;
                using (SolidBrush tint = new SolidBrush(Color.FromArgb(glass, 12, 16, 26)))
                using (Pen border = new Pen(Color.FromArgb(45, 255, 255, 255), 1))
                {
                    g.FillPath(tint, path);
                    g.DrawPath(border, path);
                }
            }
            PaintContent(g, card);
        }

        protected abstract void PaintContent(Graphics g, Rectangle card);

        protected Rectangle DrawHeader(Graphics g, Rectangle card, string title)
        {
            int padding = Px(BasePadding);
            Rectangle header = new Rectangle(card.X + padding, card.Y + padding, card.Width - 2 * padding, Px(16));
            DrawText(g, title, TitleFont, Muted, header, StringAlignment.Near);
            return header;
        }

        protected void DrawBadge(Graphics g, Rectangle header, string text)
        {
            SizeF size = g.MeasureString(text, SmallFont);
            int width = (int)size.Width + Px(10);
            Rectangle badge = new Rectangle(header.Right - width, header.Y, width, header.Height);
            using (GraphicsPath path = RoundedRect(badge, badge.Height / 2))
            using (SolidBrush back = new SolidBrush(BadgeBack))
            {
                g.FillPath(back, path);
            }
            DrawText(g, text, SmallFont, BadgeText, badge, StringAlignment.Center);
        }

        // GDI+ (no TextRenderer): GDI no escribe el canal alfa y el texto saldría translúcido sobre el cristal
        protected static void DrawText(Graphics g, string text, Font font, Color color, Rectangle rect, StringAlignment align)
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

        protected static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            int diameter = radius * 2;
            GraphicsPath path = new GraphicsPath();
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                TitleFont.Dispose();
                LabelFont.Dispose();
                ValueFont.Dispose();
                SmallFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Widget de telemetría: CPU, RAM y GPU reales, y lo que consume la propia suite.
    /// </summary>
    internal class TelemetryWidget : WidgetWindow
    {
        private readonly string _cpuName;
        private readonly string _gpuName;
        private TelemetrySampler.Snapshot _snapshot;

        public TelemetryWidget(WidgetLayout layout, string cpuName, string gpuName) : base(layout, "telemetria")
        {
            _cpuName = cpuName;
            _gpuName = gpuName;
            int padding = Px(BasePadding);
            SetCardHeight(padding + HeaderHeight + 3 * (Px(16) + Px(3) + Px(6)) + 2 * Px(10) + Px(12) + Px(16) + padding);
        }

        public void UpdateData(TelemetrySampler.Snapshot snapshot)
        {
            _snapshot = snapshot;
            RenderSurface();
        }

        protected override void PaintContent(Graphics g, Rectangle card)
        {
            Rectangle header = DrawHeader(g, card, "TELEMETRÍA");

            // Punto de "en vivo" con halo
            int dot = Px(8);
            int dotX = header.Right - dot;
            int dotY = header.Y + (header.Height - dot) / 2;
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(60, Accent)))
            using (SolidBrush fill = new SolidBrush(Accent))
            {
                g.FillEllipse(glow, dotX - Px(3), dotY - Px(3), dot + Px(6), dot + Px(6));
                g.FillEllipse(fill, dotX, dotY, dot, dot);
            }

            TelemetrySampler.Snapshot s = _snapshot;
            int y = header.Bottom + Px(12);
            string ramLabel = s != null ? string.Format("RAM ({0:0.0}/{1:0} GB)", s.RamUsedGb, s.RamTotalGb) : "RAM";
            y = DrawBar(g, card, y, "CPU (" + _cpuName + ")", s != null ? s.CpuPercent : 0);
            y = DrawBar(g, card, y + Px(10), ramLabel, s != null ? s.RamPercent : 0);
            y = DrawBar(g, card, y + Px(10), "GPU (" + _gpuName + ")", s != null ? s.GpuPercent : 0);

            int padding = Px(BasePadding);
            Rectangle footer = new Rectangle(card.X + padding, y + Px(12), card.Width - 2 * padding, Px(16));
            string footprint = s != null
                ? string.Format("Consumo Suite: {0:0} MB RAM • {1:0.0}% CPU", s.SuiteRamMb, s.SuiteCpuPercent)
                : "Consumo Suite: midiendo…";
            DrawText(g, footprint, SmallFont, Muted, footer, StringAlignment.Near);
        }

        /// <summary>
        /// Dibuja una fila "etiqueta ... valor%" con su barra de progreso. Devuelve la coordenada Y final.
        /// </summary>
        private int DrawBar(Graphics g, Rectangle card, int y, string label, float percent)
        {
            int padding = Px(BasePadding);
            Rectangle row = new Rectangle(card.X + padding, y, card.Width - 2 * padding, Px(16));
            string value = percent < 0 ? "N/D" : string.Format("{0:0}%", percent);
            DrawText(g, label, LabelFont, Secondary, new Rectangle(row.X, row.Y, row.Width - Px(44), row.Height), StringAlignment.Near);
            DrawText(g, value, ValueFont, Color.White, row, StringAlignment.Far);

            Rectangle track = new Rectangle(row.X, row.Bottom + Px(3), row.Width, Px(6));
            using (GraphicsPath trackPath = RoundedRect(track, track.Height / 2))
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
            {
                g.FillPath(trackBrush, trackPath);
            }

            int fillWidth = (int)(track.Width * Math.Max(0, percent) / 100f);
            if (fillWidth >= track.Height)
            {
                Rectangle fill = new Rectangle(track.X, track.Y, fillWidth, track.Height);
                using (GraphicsPath fillPath = RoundedRect(fill, track.Height / 2))
                using (LinearGradientBrush gradient = new LinearGradientBrush(track, AccentLight, Accent, LinearGradientMode.Horizontal))
                {
                    g.FillPath(gradient, fillPath);
                }
            }
            return track.Bottom;
        }
    }

    /// <summary>
    /// Widget del reproductor: la canción que suena en Spotify.
    /// </summary>
    internal class MediaWidget : WidgetWindow
    {
        private readonly Font _mediaTitleFont = new Font("Segoe UI", 9.75f, FontStyle.Bold);
        private readonly Font _symbolFont = new Font("Segoe UI Symbol", 14f, FontStyle.Regular);
        private MediaInfo _media = MediaInfo.Nothing;

        public MediaWidget(WidgetLayout layout) : base(layout, "reproductor")
        {
            int padding = Px(BasePadding);
            SetCardHeight(padding + HeaderHeight + Px(36) + padding);
        }

        public void UpdateData(MediaInfo media)
        {
            _media = media;
            RenderSurface();
        }

        protected override void PaintContent(Graphics g, Rectangle card)
        {
            Rectangle header = DrawHeader(g, card, "REPRODUCTOR");
            DrawBadge(g, header, _media.IsAvailable ? _media.AppName : "Música");

            int rowY = header.Bottom + Px(12);
            int thumb = Px(36);
            Rectangle thumbRect = new Rectangle(header.X, rowY, thumb, thumb);
            using (GraphicsPath thumbPath = RoundedRect(thumbRect, Px(8)))
            {
                Bitmap art = _media.Art;
                if (art != null)
                {
                    // Carátula real recortada con esquinas redondeadas
                    Region oldClip = g.Clip;
                    g.SetClip(thumbPath);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(art, thumbRect);
                    g.Clip = oldClip;
                }
                else
                {
                    using (SolidBrush thumbBrush = new SolidBrush(Color.FromArgb(40, Accent)))
                    {
                        g.FillPath(thumbBrush, thumbPath);
                    }
                    DrawText(g, "♪", _symbolFont, Accent, thumbRect, StringAlignment.Center);
                }
            }

            int textX = thumbRect.Right + Px(12);
            int textWidth = header.Right - textX;
            DrawText(g, _media.Title, _mediaTitleFont, Color.White, new Rectangle(textX, rowY, textWidth, thumb / 2), StringAlignment.Near);
            DrawText(g, _media.Subtitle, SmallFont, Muted, new Rectangle(textX, rowY + thumb / 2, textWidth, thumb / 2), StringAlignment.Near);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _mediaTitleFont.Dispose();
                _symbolFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Widget de tareas: lee notas.txt (una tarea por línea, "[x]" = hecha) y se actualiza al guardarlo.
    /// </summary>
    internal class NotesWidget : WidgetWindow
    {
        private const int MaxVisibleTasks = 6;

        private readonly string _notesPath = Path.Combine(Path.GetDirectoryName(Log.FilePath), "notas.txt");
        private readonly System.Windows.Forms.Timer _notesWatcher;
        private List<TaskItem> _tasks = new List<TaskItem>();
        private DateTime _notesLastWrite;

        private class TaskItem
        {
            public string Text;
            public bool Done;
        }

        public NotesWidget(WidgetLayout layout) : base(layout, "tareas")
        {
            EnsureNotesFile();
            LoadTasks();
            UpdateHeight();

            // Recargar las tareas al guardar el archivo (comprobar la fecha es más barato que un FileSystemWatcher)
            _notesWatcher = new System.Windows.Forms.Timer();
            _notesWatcher.Interval = 2000;
            _notesWatcher.Tick += (s, e) =>
            {
                if (File.Exists(_notesPath) && File.GetLastWriteTimeUtc(_notesPath) != _notesLastWrite)
                {
                    LoadTasks();
                    UpdateHeight();
                }
            };
            _notesWatcher.Start();
        }

        private void UpdateHeight()
        {
            int padding = Px(BasePadding);
            int taskCount = Math.Max(1, Math.Min(_tasks.Count, MaxVisibleTasks));
            SetCardHeight(padding + HeaderHeight + taskCount * Px(18) + (taskCount - 1) * Px(8) + padding);
        }

        protected override void OnCardClick(Point location)
        {
            try
            {
                Process.Start("notepad.exe", "\"" + _notesPath + "\"");
            }
            catch (Exception ex)
            {
                Log.Error("[DesktopWidgetsModule] No se pudo abrir el archivo de notas: {0}", ex);
            }
        }

        protected override void PaintContent(Graphics g, Rectangle card)
        {
            Rectangle header = DrawHeader(g, card, "TAREAS PENDIENTES");
            int pending = _tasks.FindAll(t => !t.Done).Count;
            DrawBadge(g, header, pending == 1 ? "1 pendiente" : pending + " pendientes");

            int y = header.Bottom + Px(12);
            if (_tasks.Count == 0)
            {
                DrawText(g, "Haz clic para añadir tareas", LabelFont, Muted, new Rectangle(header.X, y, header.Width, Px(18)), StringAlignment.Near);
                return;
            }

            int shown = Math.Min(_tasks.Count, MaxVisibleTasks);
            for (int i = 0; i < shown; i++)
            {
                TaskItem task = _tasks[i];
                int mark = Px(10);
                Rectangle markRect = new Rectangle(header.X, y + (Px(18) - mark) / 2, mark, mark);
                if (task.Done)
                {
                    using (SolidBrush done = new SolidBrush(Accent)) g.FillEllipse(done, markRect);
                }
                else
                {
                    using (Pen open = new Pen(Accent, Px(1.5f))) g.DrawEllipse(open, markRect);
                }

                Rectangle textRect = new Rectangle(markRect.Right + Px(8), y, header.Right - markRect.Right - Px(8), Px(18));
                DrawText(g, task.Text, LabelFont, task.Done ? Muted : Secondary, textRect, StringAlignment.Near);
                y += Px(18) + Px(8);
            }
        }

        private void EnsureNotesFile()
        {
            if (File.Exists(_notesPath)) return;
            try
            {
                File.WriteAllLines(_notesPath, new[]
                {
                    "# Una tarea por línea. Empieza con [x] para marcarla como hecha.",
                    "[x] Modo privacidad Aero",
                    "[x] Widgets de escritorio",
                    "[ ] Reloj 3D en el escritorio"
                });
            }
            catch (Exception ex)
            {
                Log.Error("[DesktopWidgetsModule] No se pudo crear {0}: {1}", _notesPath, ex.Message);
            }
        }

        private void LoadTasks()
        {
            List<TaskItem> tasks = new List<TaskItem>();
            try
            {
                if (File.Exists(_notesPath))
                {
                    _notesLastWrite = File.GetLastWriteTimeUtc(_notesPath);
                    foreach (string rawLine in File.ReadAllLines(_notesPath))
                    {
                        string line = rawLine.Trim();
                        if (line.Length == 0 || line.StartsWith("#")) continue;

                        TaskItem task = new TaskItem();
                        if (line.StartsWith("[x]", StringComparison.OrdinalIgnoreCase))
                        {
                            task.Done = true;
                            line = line.Substring(3);
                        }
                        else if (line.StartsWith("[ ]"))
                        {
                            line = line.Substring(3);
                        }
                        task.Text = line.TrimStart('-', ' ');
                        tasks.Add(task);
                    }
                }
            }
            catch (IOException)
            {
                return; // El editor aún está guardando; se reintenta en la siguiente comprobación
            }
            _tasks = tasks;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _notesWatcher.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
