using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Fuentes, colores y utilidades de dibujo compartidos por todas las vistas de la isla.
    /// Todo se dibuja con GDI+ (no TextRenderer): GDI no escribe el canal alfa de la ventana transparente.
    /// </summary>
    internal class IslandPainter : IDisposable
    {
        // Glifos de la fuente Segoe Fluent Icons (Windows 11)
        public const string GlyphPrevious = "";
        public const string GlyphNext = "";
        public const string GlyphPlay = "";
        public const string GlyphPause = "";
        public const string GlyphWifi = "";
        public const string GlyphEthernet = "";
        public const string GlyphOffline = "";
        public const string GlyphBattery = "";
        public const string GlyphMusic = "";

        public static readonly Color Background = Color.FromArgb(235, 10, 14, 22);
        public static readonly Color Border = Color.FromArgb(41, 255, 255, 255);
        public static readonly Color Secondary = Color.FromArgb(184, 255, 255, 255);
        public static readonly Color Muted = Color.FromArgb(115, 255, 255, 255);
        public static readonly Color Sky = Color.FromArgb(56, 189, 248);
        public static readonly Color Green = Color.FromArgb(34, 197, 94);
        public static readonly Color Violet = Color.FromArgb(168, 85, 247);
        public static readonly Color Amber = Color.FromArgb(251, 191, 36);
        // Colores de los mockups: texto secundario, pistas de las barras, silencio en naranja
        public static readonly Color Gray = Color.FromArgb(154, 164, 181);
        public static readonly Color Track = Color.FromArgb(42, 48, 64);
        public static readonly Color MutedFill = Color.FromArgb(75, 83, 99);
        public static readonly Color Orange = Color.FromArgb(253, 186, 116);
        public static readonly Color OrangeBack = Color.FromArgb(46, 251, 146, 60);

        /// <summary>
        /// Rectángulo redondeado relleno (fondos de pestañas, filas, botones).
        /// </summary>
        public static void FillRounded(Graphics g, RectangleF rect, float radius, Color color)
        {
            using (System.Drawing.Drawing2D.GraphicsPath path = RoundedRect(rect, radius))
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        /// <summary>
        /// Barra simple: pista y relleno con esquinas redondeadas (colores ya con su opacidad).
        /// </summary>
        public static void DrawBar(Graphics g, RectangleF track, float value, Color trackColor, Color fillColor)
        {
            FillRounded(g, track, track.Height / 2, trackColor);
            if (value <= 0.005f) return;
            FillRounded(g, new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * Math.Min(1f, value)), track.Height), track.Height / 2, fillColor);
        }

        // Tamaños de los mockups aprobados (en px a 96 PPP; 1 pt = 4/3 px): 11, 12, 13, 16-17 y 44 px
        public readonly Font Label11;
        public readonly Font Text11;
        public readonly Font Tab12;
        public readonly Font Text12;
        public readonly Font Bold12;
        public readonly Font Text13;
        public readonly Font Semibold13;
        public readonly Font Bold15;
        public readonly Font Bold17;
        public readonly Font Bold18;
        public readonly Font Big44;
        public readonly Font Glyph16;
        public readonly Font Glyph20;

        public readonly Font TimeFont;
        public readonly Font TitleFont;
        public readonly Font BodyFont;
        public readonly Font SmallFont;
        public readonly Font BigFont;
        public readonly Font IconFont;
        public readonly Font ControlFont;
        public readonly Font EmojiFont;
        public readonly Font ThumbFont;

        private readonly float _scale;
        private readonly DateTime _startTime = DateTime.Now;

        /// <summary>
        /// scale: píxeles por unidad de diseño (PPP × tamaño de la isla); fontScale: solo el tamaño de la isla elegido
        /// en el panel, porque las fuentes en puntos ya siguen los PPP de la pantalla.
        /// </summary>
        public IslandPainter(float scale, float fontScale)
        {
            _scale = scale;
            Label11 = new Font("Segoe UI", 8.25f * fontScale, FontStyle.Bold);
            Text11 = new Font("Segoe UI", 8.25f * fontScale, FontStyle.Regular);
            Tab12 = new Font("Segoe UI Semibold", 9f * fontScale, FontStyle.Regular);
            Text12 = new Font("Segoe UI", 9f * fontScale, FontStyle.Regular);
            Bold12 = new Font("Segoe UI", 9f * fontScale, FontStyle.Bold);
            Text13 = new Font("Segoe UI", 9.75f * fontScale, FontStyle.Regular);
            Semibold13 = new Font("Segoe UI Semibold", 9.75f * fontScale, FontStyle.Regular);
            Bold15 = new Font("Segoe UI", 11.25f * fontScale, FontStyle.Bold);
            Bold17 = new Font("Segoe UI", 12.75f * fontScale, FontStyle.Bold);
            Bold18 = new Font("Segoe UI", 13.5f * fontScale, FontStyle.Bold);
            Big44 = new Font("Segoe UI", 33f * fontScale, FontStyle.Bold);
            Glyph16 = new Font("Segoe Fluent Icons", 12f * fontScale, FontStyle.Regular);
            Glyph20 = new Font("Segoe Fluent Icons", 15f * fontScale, FontStyle.Regular);
            TimeFont = new Font("Segoe UI", 9.75f * fontScale, FontStyle.Bold);
            TitleFont = new Font("Segoe UI", 10.5f * fontScale, FontStyle.Bold);
            BodyFont = new Font("Segoe UI", 9f * fontScale, FontStyle.Regular);
            SmallFont = new Font("Segoe UI", 8.25f * fontScale, FontStyle.Regular);
            BigFont = new Font("Segoe UI", 22f * fontScale, FontStyle.Bold);
            IconFont = new Font("Segoe Fluent Icons", 9f * fontScale, FontStyle.Regular);
            ControlFont = new Font("Segoe Fluent Icons", 11f * fontScale, FontStyle.Regular);
            EmojiFont = new Font("Segoe UI Emoji", 17f * fontScale, FontStyle.Regular);
            ThumbFont = new Font("Segoe Fluent Icons", 18f * fontScale, FontStyle.Regular);
        }

        public float Px(float value)
        {
            return value * _scale;
        }

        /// <summary>
        /// Segundos desde que arrancó la isla, para las animaciones cíclicas (punto pulsante, barras).
        /// </summary>
        public double Seconds
        {
            get { return (DateTime.Now - _startTime).TotalSeconds; }
        }

        public static Color Fade(Color color, int alpha)
        {
            return Color.FromArgb(color.A * alpha / 255, color);
        }

        public void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect, StringAlignment align)
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

        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            return LayeredWindow.RoundedRect(rect, radius);
        }

        /// <summary>
        /// Imagen recortada con esquinas redondeadas y con opacidad (para fundirla con el resto del contenido).
        /// </summary>
        public void DrawRoundedImage(Graphics g, Image image, RectangleF rect, float radius, int alpha)
        {
            using (GraphicsPath path = RoundedRect(rect, radius))
            using (ImageAttributes fade = new ImageAttributes())
            {
                ColorMatrix matrix = new ColorMatrix();
                matrix.Matrix33 = alpha / 255f;
                fade.SetColorMatrix(matrix);

                Region oldClip = g.Clip;
                InterpolationMode oldInterpolation = g.InterpolationMode;
                g.SetClip(path);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(image, Rectangle.Round(rect), 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, fade);
                g.InterpolationMode = oldInterpolation;
                g.Clip = oldClip;
            }
        }

        /// <summary>
        /// Ecualizador de 5 barras (graves a la izquierda, agudos a la derecha) al ritmo real de lo que suena;
        /// si Windows no deja escuchar la salida, ondas desfasadas de reserva. Quietas y bajas si no suena nada.
        /// </summary>
        public void DrawBars(Graphics g, RectangleF area, int alpha, Color color, bool moving)
        {
            int count = AudioSpectrum.Bands;
            float barWidth = area.Width / (count + (count - 1) * 0.6f);
            float gap = barWidth * 0.6f;
            float[] levels = moving ? AudioSpectrum.Levels : null;
            bool real = levels != null && AudioSpectrum.IsCapturing;
            double t = Seconds;
            using (SolidBrush brush = new SolidBrush(Fade(color, alpha)))
            {
                for (int i = 0; i < count; i++)
                {
                    double level = !moving ? 0.2 : real ? levels[i] : 0.6 + 0.4 * Math.Sin(t * 6 + i * 1.7);
                    float h = (float)(area.Height * Math.Max(0.2, level));
                    using (GraphicsPath bar = RoundedRect(new RectangleF(area.X + i * (barWidth + gap), area.Bottom - h, barWidth, h), barWidth / 2))
                    {
                        g.FillPath(brush, bar);
                    }
                }
            }
        }

        /// <summary>
        /// Barra de progreso horizontal redondeada (valor de 0 a 1).
        /// </summary>
        public void DrawProgress(Graphics g, RectangleF track, float value, Color color, int alpha)
        {
            using (GraphicsPath trackPath = RoundedRect(track, track.Height / 2))
            using (SolidBrush trackBrush = new SolidBrush(Color.FromArgb(alpha * 45 / 255, Color.White)))
            {
                g.FillPath(trackBrush, trackPath);
            }
            if (value <= 0.01f) return;

            RectangleF fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * Math.Min(1f, value)), track.Height);
            using (GraphicsPath fillPath = RoundedRect(fill, track.Height / 2))
            using (SolidBrush fillBrush = new SolidBrush(Fade(color, alpha)))
            {
                g.FillPath(fillBrush, fillPath);
            }
        }

        /// <summary>
        /// Anillo de progreso (indicadores de CPU/RAM/GPU, temporizador).
        /// </summary>
        public void DrawRing(Graphics g, RectangleF bounds, float value, Color color, int alpha, float thickness)
        {
            RectangleF ring = RectangleF.Inflate(bounds, -thickness / 2, -thickness / 2);
            using (Pen track = new Pen(Color.FromArgb(alpha * 40 / 255, Color.White), thickness))
            {
                g.DrawEllipse(track, ring);
            }
            if (value <= 0.005f) return;

            using (Pen arc = new Pen(Fade(color, alpha), thickness))
            {
                arc.StartCap = arc.EndCap = LineCap.Round;
                g.DrawArc(arc, ring, -90, 360 * Math.Min(1f, value));
            }
        }

        public void Dispose()
        {
            foreach (Font font in new[] { Label11, Text11, Tab12, Text12, Bold12, Text13, Semibold13, Bold15, Bold17, Bold18, Big44, Glyph16, Glyph20,
                                          TimeFont, TitleFont, BodyFont, SmallFont, BigFont, IconFont, ControlFont, EmojiFont, ThumbFont })
            {
                font.Dispose();
            }
        }
    }
}
