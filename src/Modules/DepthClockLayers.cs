using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Qué se dibuja por delante de la hora.
    /// </summary>
    internal enum OcclusionMode
    {
        Auto,      // Sujeto principal si la IA encuentra uno claro; si no (paisajes), todo lo que esté cerca
        Subject,   // Solo el sujeto principal (auto, persona, animal...)
        Near       // Todo lo que esté más cerca que lo que hay detrás de la hora (árboles, edificios...)
    }

    /// <summary>
    /// Estilo visual del reloj en el escritorio:
    /// Apple (Clásico 3D detrás del sujeto) o Samsung (Adaptativo One UI con dígitos que se estiran contorneando la silueta).
    /// </summary>
    internal enum ClockStyle
    {
        Apple,
        Samsung
    }

    /// <summary>
    /// Ajustes del reloj, guardados en %LOCALAPPDATA%\StreeckSuite\reloj.ini. Posición y tamaño son fracciones
    /// de la pantalla, así valen para cualquier resolución. Los valores por defecto replican el mockup a 1080p.
    /// </summary>
    internal class ClockSettings
    {
        public const string DefaultFontFamily = "Segoe UI Variable Display";

        public ClockStyle Style = ClockStyle.Apple;
        public float CenterX = 0.5f;   // Centro horizontal de la hora (0 = borde izquierdo, 1 = derecho)
        public float Top = 0.30f;      // Parte superior visible de los dígitos, en fracción de la altura
        public float Size = 0.48f;     // Altura de la fuente de la hora, en fracción de la altura de pantalla
        public string FontFamily = DefaultFontFamily;
        public OcclusionMode Mode = OcclusionMode.Auto;
        public bool ShowDate = true;
        public bool ShowWeather = true;
        public bool Enabled = true;     // Interruptor de la página Reloj 3D del panel de ajustes
        public bool ShowSeconds = true; // 19:44:07 en lugar de 19:44
        // Reloj estirado (estilo Samsung, mockup R1)
        public string SamsungColor = "#ffffff";  // Color de los números
        public float SamsungMaxStretch = 0.65f;  // Altura máxima de un número estirado, en fracción de la pantalla
        public int SamsungWeight = 2;            // 0 fino, 1 normal, 2 grueso

        private static string FilePath
        {
            get { return Path.Combine(Path.GetDirectoryName(Log.FilePath), "reloj.ini"); }
        }

        public ClockSettings Clone()
        {
            return (ClockSettings)MemberwiseClone();
        }

        public static ClockSettings Load()
        {
            ClockSettings settings = new ClockSettings();
            try
            {
                if (!File.Exists(FilePath)) return settings;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                    float number;
                    bool isNumber = float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
                    // "arribaDigitos" sustituye al antiguo "arriba", que medía la caja de la fuente y no los dígitos
                    if (key == "estilo")
                    {
                        ClockStyle style;
                        if (Enum.TryParse(value, true, out style)) settings.Style = style;
                    }
                    else if (key == "centroX" && isNumber) settings.CenterX = number;
                    else if (key == "arribaDigitos" && isNumber) settings.Top = number;
                    else if (key == "tamano" && isNumber) settings.Size = number;
                    else if (key == "fuente" && value.Length > 0) settings.FontFamily = value;
                    else if (key == "fecha") settings.ShowDate = value != "0";
                    else if (key == "clima") settings.ShowWeather = value != "0";
                    else if (key == "activo") settings.Enabled = value != "0";
                    else if (key == "segundos") settings.ShowSeconds = value != "0";
                    else if (key == "colorSamsung" && value.Length > 0) settings.SamsungColor = value;
                    else if (key == "estiramientoSamsung" && isNumber) settings.SamsungMaxStretch = number;
                    else if (key == "grosorEstirado" && isNumber) settings.SamsungWeight = Math.Max(0, Math.Min(2, (int)number));
                    else if (key == "modo")
                    {
                        OcclusionMode mode;
                        if (Enum.TryParse(value, out mode)) settings.Mode = mode;
                    }
                }
            }
            catch (IOException ex)
            {
                Log.Error("[DepthClockModule] No se pudo leer {0}: {1}", FilePath, ex.Message);
            }
            return settings;
        }

        public void Save()
        {
            try
            {
                File.WriteAllLines(FilePath, new[]
                {
                    "estilo=" + Style,
                    "centroX=" + CenterX.ToString("0.###", CultureInfo.InvariantCulture),
                    "arribaDigitos=" + Top.ToString("0.###", CultureInfo.InvariantCulture),
                    "tamano=" + Size.ToString("0.###", CultureInfo.InvariantCulture),
                    "fuente=" + FontFamily,
                    "modo=" + Mode,
                    "fecha=" + (ShowDate ? "1" : "0"),
                    "clima=" + (ShowWeather ? "1" : "0"),
                    "activo=" + (Enabled ? "1" : "0"),
                    "segundos=" + (ShowSeconds ? "1" : "0"),
                    "colorSamsung=" + SamsungColor,
                    "estiramientoSamsung=" + SamsungMaxStretch.ToString("0.###", CultureInfo.InvariantCulture),
                    "grosorEstirado=" + SamsungWeight
                });
            }
            catch (IOException ex)
            {
                Log.Error("[DepthClockModule] No se pudo guardar {0}: {1}", FilePath, ex.Message);
            }
        }
    }

    /// <summary>
    /// Geometría del reloj en pantalla a partir de los ajustes. Se mide el contorno real de los dígitos (no la caja
    /// de la fuente, cuyo margen interno cambia mucho de una fuente a otra) para colocar la fecha justo encima,
    /// el clima justo debajo, y saber qué zona del fondo queda detrás de la hora.
    /// </summary>
    internal static class DepthClockLayout
    {
        public static float ClockFontPx(Size screen, ClockSettings s)
        {
            return screen.Height * s.Size;
        }

        /// <summary>
        /// Fecha y clima: proporcionales a la hora, pero sin hacerse diminutos ni gigantes.
        /// </summary>
        public static float CaptionFontPx(Size screen, ClockSettings s)
        {
            return Math.Max(screen.Height * 0.019f, Math.Min(screen.Height * 0.034f, ClockFontPx(screen, s) * 0.065f));
        }

        public static float CaptionGapPx(Size screen, ClockSettings s)
        {
            return CaptionFontPx(screen, s) * 0.9f;
        }

        public static FontFamily ResolveFamily(string name)
        {
            try
            {
                return new FontFamily(name);
            }
            catch (ArgumentException)
            {
                return new FontFamily("Segoe UI"); // La fuente elegida no está instalada
            }
        }

        public static string TimeFormat(ClockSettings s)
        {
            return s.ShowSeconds ? "HH:mm:ss" : "HH:mm";
        }

        // Hora de referencia para colocar y medir: tiene la anchura típica de cualquier hora
        private static string SampleText(ClockSettings s)
        {
            return s.ShowSeconds ? "00:00:00" : "00:00";
        }

        private static GraphicsPath RawPath(ClockSettings s, Size screen, string text)
        {
            GraphicsPath path = new GraphicsPath();
            using (FontFamily family = ResolveFamily(s.FontFamily))
            {
                int style = family.IsStyleAvailable(FontStyle.Bold) ? (int)FontStyle.Bold : (int)FontStyle.Regular;
                path.AddString(text, family, style, ClockFontPx(screen, s), PointF.Empty, StringFormat.GenericTypographic);
            }
            return path;
        }

        /// <summary>
        /// Contorno del texto de la hora ya colocado en pantalla: la hora de referencia queda centrada en CenterX y
        /// con la parte superior de los dígitos en Top; el texto real empieza donde ella, así los dígitos no bailan
        /// a izquierda y derecha cada segundo con las fuentes de anchura variable.
        /// Devuelve el contorno visible en <paramref name="ink"/>.
        /// </summary>
        public static GraphicsPath BuildDigitsPath(Size screen, ClockSettings s, string text, out RectangleF ink)
        {
            RectangleF sample;
            using (GraphicsPath reference = RawPath(s, screen, SampleText(s))) sample = reference.GetBounds();

            GraphicsPath path = RawPath(s, screen, text);
            RectangleF raw = path.GetBounds();
            float dx = s.CenterX * screen.Width - (sample.X + sample.Width / 2);
            float dy = s.Top * screen.Height - sample.Y;
            using (Matrix move = new Matrix())
            {
                move.Translate(dx, dy);
                path.Transform(move);
            }
            ink = new RectangleF(raw.X + dx, raw.Y + dy, raw.Width, raw.Height);
            return path;
        }

        /// <summary>
        /// Zona que ocupan los dígitos, en fracciones de la pantalla (0..1), medida con la hora de referencia.
        /// </summary>
        public static RectangleF DigitsArea(Size screen, ClockSettings s)
        {
            RectangleF ink;
            using (BuildDigitsPath(screen, s, SampleText(s), out ink)) { }

            float left = Clamp01(ink.Left / screen.Width), right = Clamp01(ink.Right / screen.Width);
            float top = Clamp01(ink.Top / screen.Height), bottom = Clamp01(ink.Bottom / screen.Height);
            return RectangleF.FromLTRB(left, top, Math.Max(right, left + 0.01f), Math.Max(bottom, top + 0.01f));
        }

        private static float Clamp01(float v)
        {
            return Math.Max(0f, Math.Min(1f, v));
        }
    }

    /// <summary>
    /// Capas del efecto: el fondo tal como lo muestra Windows y su primer plano recortado (la hora va entre ambas).
    /// El análisis con IA (profundidad y sujeto) se hace una vez por fondo y se guarda, así cambiar la posición
    /// o el modo solo recalcula el recorte.
    /// </summary>
    internal class DepthClockLayers : IDisposable
    {
        // Cuánto más cerca que lo que hay detrás de la hora debe estar algo para taparla (modo "todo lo cercano")
        private const float ForegroundMargin = 0.10f;
        // Ancho de la transición del borde del recorte, en unidades de profundidad
        private const float EdgeSoftness = 0.04f;
        // Refinado de bordes: resolución de trabajo del filtro guiado, cuánto debe respetar los bordes de la foto
        // (menor = más pegado a ellos) y contraste final de la máscara
        private const int GuidedFilterHeight = 540;
        private const float GuidedFilterEpsilon = 0.0008f;
        private const float EdgeContrast = 4f;

        public Bitmap Background;
        public Bitmap Foreground;
        public bool SubjectFound;

        private float[] _depth;
        private float[] _subject;
        // Copia de los píxeles del fondo: el recorte se recalcula en segundo plano mientras la ventana dibuja
        // Background, y GDI+ no permite usar el mismo Bitmap desde dos hilos a la vez
        private int[] _backgroundPixels;
        private Size _screen;

        /// <summary>
        /// Carga el fondo y lo analiza con las dos IA. Lento (~1 s): llamar desde un hilo en segundo plano.
        /// </summary>
        public static DepthClockLayers Analyze(string wallpaperPath, Size screen)
        {
            DepthClockLayers layers = new DepthClockLayers();
            using (Bitmap source = LoadWithoutLock(wallpaperPath))
            {
                layers.Background = RenderLikeWindows(source, screen);
            }
            layers._backgroundPixels = VisionModel.ReadPixels(layers.Background);
            layers._screen = screen;

            // Se analiza la imagen tal como se ve en pantalla, para que el recorte encaje
            layers._depth = VisionModel.Depth.Run(layers.Background);
            layers._subject = VisionModel.Subject.Run(layers.Background);
            layers.SubjectFound = LooksLikeASubject(layers._subject, VisionModel.Subject.Size);
            Log.Info("[DepthClockModule] Fondo analizado: {0} (sujeto principal: {1})", wallpaperPath, layers.SubjectFound ? "sí" : "no");
            return layers;
        }

        /// <summary>
        /// Calcula el primer plano para los ajustes dados (~0,3 s). Devuelve un bitmap nuevo; quien llama lo asigna.
        /// </summary>
        public Bitmap ComposeForeground(ClockSettings settings)
        {
            // Reloj estirado: "cercano" es lo que está más cerca que lo que hay detrás de la hora (igual que el recorte)
            if (_depth != null) _nearThreshold = BehindClockDepth(_depth, VisionModel.Depth.Size, DepthClockLayout.DigitsArea(_screen, settings)) + ForegroundMargin;

            OcclusionMode mode = settings.Mode;
            if (mode == OcclusionMode.Auto) mode = SubjectFound ? OcclusionMode.Subject : OcclusionMode.Near;

            Bitmap foreground;
            if (mode == OcclusionMode.Subject)
            {
                foreground = CutForeground(_subject, VisionModel.Subject.Size, 0.5f, 0.25f);
            }
            else
            {
                float threshold = BehindClockDepth(_depth, VisionModel.Depth.Size, DepthClockLayout.DigitsArea(_screen, settings)) + ForegroundMargin;
                foreground = CutForeground(_depth, VisionModel.Depth.Size, threshold, EdgeSoftness);
            }

            // El recorte deja decenas de MB en búferes temporales; compensa liberarlos ya
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            return foreground;
        }

        /// <summary>
        /// Reloj estirado: primera fila (fracción 0..1 de la pantalla) con sujeto en la columna colLeft..colRight,
        /// buscando desde fromY hacia abajo. Usa la silueta del sujeto y, si el fondo no tiene uno, lo cercano según
        /// la profundidad. Sin obstáculo devuelve 1 (la base de la pantalla).
        /// </summary>
        public float GetColumnObstacleTop(float colLeft, float colRight, float fromY)
        {
            // Obstáculo = el sujeto principal o cualquier cosa cercana (palmeras, casas, la isla): la hora estirada
            // los rodea todos. Se toma la primera fila con obstáculo en cualquiera de los dos mapas.
            float top = 1f;
            if (_subject != null && SubjectFound) top = Math.Min(top, FirstRow(_subject, VisionModel.Subject.Size, 0.35f, colLeft, colRight, fromY));
            if (_depth != null) top = Math.Min(top, FirstRow(_depth, VisionModel.Depth.Size, _nearThreshold, colLeft, colRight, fromY));
            return top;
        }

        private float _nearThreshold = 0.55f;

        private static float FirstRow(float[] map, int size, float threshold, float colLeft, float colRight, float fromY)
        {
            int xStart = Math.Max(0, (int)(colLeft * size));
            int xEnd = Math.Min(size - 1, (int)(colRight * size));
            if (xEnd < xStart) xEnd = xStart;
            for (int y = Math.Max(0, (int)(fromY * size)); y < size; y++)
            {
                int row = y * size;
                for (int x = xStart; x <= xEnd; x++)
                {
                    if (map[row + x] >= threshold) return (float)y / size;
                }
            }
            return 1f;
        }

        /// <summary>
        /// ¿La salida del detector describe un objeto concreto? En un paisaje marca zonas difusas y enormes (medio cielo)
        /// con poca confianza; con un sujeto real (un auto, una persona) marca una silueta nítida y despegada del borde superior.
        /// </summary>
        private static bool LooksLikeASubject(float[] subject, int size)
        {
            int inside = 0, topEdge = 0;
            double confidence = 0;
            for (int i = 0; i < subject.Length; i++)
            {
                if (subject[i] < 0.5f) continue;
                inside++;
                confidence += subject[i];
                if (i < size * 2) topEdge++;
            }
            if (inside == 0) return false;

            float area = (float)inside / subject.Length;
            float meanConfidence = (float)(confidence / inside);
            bool touchesTop = topEdge > size / 5; // Más de un 20% del borde superior: cielo o escena, no un objeto
            return area > 0.01f && area < 0.5f && meanConfidence > 0.75f && !touchesTop;
        }

        // Cargar a través de un MemoryStream deja libre el archivo para que Windows pueda reemplazarlo
        internal static Bitmap LoadWithoutLock(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (MemoryStream stream = new MemoryStream(bytes))
            using (Image image = Image.FromStream(stream))
            {
                return new Bitmap(image);
            }
        }

        /// <summary>
        /// Escala el fondo como lo hace Windows según el ajuste elegido (Rellenar por defecto).
        /// </summary>
        internal static Bitmap RenderLikeWindows(Bitmap source, Size screen)
        {
            Bitmap result = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                int style = WallpaperInfo.GetStyle();
                float scaleX = (float)screen.Width / source.Width;
                float scaleY = (float)screen.Height / source.Height;
                if (style == 2)
                {
                    g.DrawImage(source, new Rectangle(0, 0, screen.Width, screen.Height)); // Expandir
                }
                else if (style == 0)
                {
                    g.DrawImage(source, (screen.Width - source.Width) / 2, (screen.Height - source.Height) / 2, source.Width, source.Height); // Centrar
                }
                else
                {
                    float scale = style == 6 ? Math.Min(scaleX, scaleY) : Math.Max(scaleX, scaleY); // Ajustar / Rellenar
                    float w = source.Width * scale, h = source.Height * scale;
                    g.DrawImage(source, (screen.Width - w) / 2, (screen.Height - h) / 2, w, h);
                }
            }
            return result;
        }

        /// <summary>
        /// Profundidad de lo que queda detrás de los dígitos (percentil 25 de su zona): define el "plano" de la hora.
        /// Con un paisaje es el cielo; con un retrato, la pared que hay detrás de la persona.
        /// </summary>
        private static float BehindClockDepth(float[] depth, int size, RectangleF area)
        {
            List<float> values = new List<float>();
            for (int y = (int)(area.Top * size); y < (int)(area.Bottom * size); y++)
            {
                for (int x = (int)(area.Left * size); x < (int)(area.Right * size); x++)
                {
                    values.Add(depth[y * size + x]);
                }
            }
            if (values.Count == 0) return 0;
            values.Sort();
            return values[values.Count / 4];
        }

        /// <summary>
        /// Recorta el primer plano: los valores por encima de <paramref name="threshold"/> quedan delante de la hora.
        /// </summary>
        private Bitmap CutForeground(float[] values, int size, float threshold, float softness)
        {
            // Máscara a la resolución del modelo; al escalarla con bicúbico los bordes quedan suaves
            int[] maskPixels = new int[size * size];
            for (int i = 0; i < maskPixels.Length; i++)
            {
                float a = (values[i] - threshold) / softness + 0.5f;
                int alpha = (int)(255 * Math.Max(0f, Math.Min(1f, a)));
                maskPixels[i] = alpha << 24 | 0xFFFFFF;
            }

            int width = _screen.Width, height = _screen.Height;
            int[] alphaPixels;
            using (Bitmap mask = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            using (Bitmap bigMask = new Bitmap(width, height, PixelFormat.Format32bppArgb))
            {
                VisionModel.WritePixels(mask, maskPixels);
                using (Graphics g = Graphics.FromImage(bigMask))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (ImageAttributes clamp = new ImageAttributes())
                    {
                        clamp.SetWrapMode(WrapMode.TileFlipXY); // Evita un borde semitransparente en los extremos
                        g.DrawImage(mask, new Rectangle(0, 0, width, height), 0, 0, size, size, GraphicsUnit.Pixel, clamp);
                    }
                }
                alphaPixels = VisionModel.ReadPixels(bigMask);
            }

            int[] colorPixels = (int[])_backgroundPixels.Clone();
            float[] refined = RefineEdges(colorPixels, alphaPixels, width, height);
            for (int i = 0; i < colorPixels.Length; i++)
            {
                // Más contraste en la máscara: cada píxel queda claramente delante o detrás, sin "fantasmas" grises
                float a = (refined[i] - 0.5f) * EdgeContrast + 0.5f;
                int alpha = (int)(255 * Math.Max(0f, Math.Min(1f, a)));
                colorPixels[i] = (colorPixels[i] & 0x00FFFFFF) | (alpha << 24);
            }

            Bitmap foreground = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            VisionModel.WritePixels(foreground, colorPixels);
            return foreground;
        }

        /// <summary>
        /// Filtro guiado (He et al.): ajusta los bordes de la máscara, que viene ampliada desde la baja resolución
        /// del modelo, a los bordes reales de la foto (p. ej. la silueta de los árboles contra el cielo).
        /// Se calcula a ~540p y los coeficientes se amplían a resolución completa ("fast guided filter").
        /// </summary>
        private static float[] RefineEdges(int[] colorPixels, int[] alphaPixels, int width, int height)
        {
            int step = Math.Max(1, height / GuidedFilterHeight);
            int sw = width / step, sh = height / step;
            int radius = Math.Max(2, sh / 68);

            float[] guide = new float[sw * sh];
            float[] input = new float[sw * sh];
            for (int y = 0; y < sh; y++)
            {
                for (int x = 0; x < sw; x++)
                {
                    int src = (y * step) * width + x * step;
                    guide[y * sw + x] = Luminance(colorPixels[src]);
                    input[y * sw + x] = ((alphaPixels[src] >> 24) & 0xFF) / 255f;
                }
            }

            float[] meanI = BoxMean(guide, sw, sh, radius);
            float[] meanP = BoxMean(input, sw, sh, radius);
            float[] productIP = new float[guide.Length];
            float[] squareI = new float[guide.Length];
            for (int i = 0; i < guide.Length; i++)
            {
                productIP[i] = guide[i] * input[i];
                squareI[i] = guide[i] * guide[i];
            }
            float[] corrIP = BoxMean(productIP, sw, sh, radius);
            float[] corrI = BoxMean(squareI, sw, sh, radius);

            float[] a = new float[guide.Length];
            float[] b = new float[guide.Length];
            for (int i = 0; i < guide.Length; i++)
            {
                float variance = corrI[i] - meanI[i] * meanI[i];
                float covariance = corrIP[i] - meanI[i] * meanP[i];
                a[i] = covariance / (variance + GuidedFilterEpsilon);
                b[i] = meanP[i] - a[i] * meanI[i];
            }
            float[] meanA = BoxMean(a, sw, sh, radius);
            float[] meanB = BoxMean(b, sw, sh, radius);

            // q = A·I + B a resolución completa, con A y B interpolados bilinealmente
            float[] result = new float[width * height];
            for (int y = 0; y < height; y++)
            {
                float fy = Math.Max(0f, Math.Min(sh - 1.001f, (y + 0.5f) / step - 0.5f));
                int y0 = (int)fy;
                float ty = fy - y0;
                for (int x = 0; x < width; x++)
                {
                    float fx = Math.Max(0f, Math.Min(sw - 1.001f, (x + 0.5f) / step - 0.5f));
                    int x0 = (int)fx;
                    float tx = fx - x0;
                    int i00 = y0 * sw + x0, i01 = i00 + 1, i10 = i00 + sw, i11 = i10 + 1;

                    float aValue = (meanA[i00] * (1 - tx) + meanA[i01] * tx) * (1 - ty) + (meanA[i10] * (1 - tx) + meanA[i11] * tx) * ty;
                    float bValue = (meanB[i00] * (1 - tx) + meanB[i01] * tx) * (1 - ty) + (meanB[i10] * (1 - tx) + meanB[i11] * tx) * ty;
                    int index = y * width + x;
                    result[index] = aValue * Luminance(colorPixels[index]) + bValue;
                }
            }
            return result;
        }

        private static float Luminance(int argb)
        {
            return (0.299f * ((argb >> 16) & 0xFF) + 0.587f * ((argb >> 8) & 0xFF) + 0.114f * (argb & 0xFF)) / 255f;
        }

        /// <summary>
        /// Media en una ventana cuadrada de lado 2·radius+1 (recortada en los bordes), en O(n) con sumas acumuladas.
        /// </summary>
        private static float[] BoxMean(float[] source, int width, int height, int radius)
        {
            float[] horizontal = new float[source.Length];
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                float sum = 0;
                int count = 0;
                for (int x = 0; x <= Math.Min(radius, width - 1); x++) { sum += source[row + x]; count++; }
                for (int x = 0; x < width; x++)
                {
                    horizontal[row + x] = sum / count;
                    int add = x + radius + 1, remove = x - radius;
                    if (add < width) { sum += source[row + add]; count++; }
                    if (remove >= 0) { sum -= source[row + remove]; count--; }
                }
            }

            float[] result = new float[source.Length];
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                int count = 0;
                for (int y = 0; y <= Math.Min(radius, height - 1); y++) { sum += horizontal[y * width + x]; count++; }
                for (int y = 0; y < height; y++)
                {
                    result[y * width + x] = sum / count;
                    int add = y + radius + 1, remove = y - radius;
                    if (add < height) { sum += horizontal[add * width + x]; count++; }
                    if (remove >= 0) { sum -= horizontal[remove * width + x]; count--; }
                }
            }
            return result;
        }

        public void Dispose()
        {
            if (Background != null) Background.Dispose();
            if (Foreground != null) Foreground.Dispose();
        }
    }
}
