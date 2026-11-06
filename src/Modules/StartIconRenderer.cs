using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Icono del botón de Inicio (mockup «Icono de Inicio» aprobado). Dibuja los iconos de la galería y prepara el
    /// PNG que la DLL de la barra pone en el botón. Los «Ojitos» los dibuja la propia DLL para que parpadeen.
    /// </summary>
    internal static class StartIconRenderer
    {
        public const int Original = 0, Streeck = 1, Orb = 2, Eyes = 3, Sparkle = 4, Moon = 5, Custom = 6;
        public static readonly string[] Names = { "Windows original", "Logo Streeck", "Orbe clásico", "Ojitos", "Destello", "Luna", "Tu imagen" };

        private static string Folder
        {
            get { return Path.GetDirectoryName(Log.FilePath); }
        }

        /// <summary>
        /// Copia de la imagen que eligió el usuario (así sigue funcionando aunque borre o mueva la original).
        /// </summary>
        public static string CustomCopy
        {
            get { return Path.Combine(Folder, "inicio_mi_imagen.png"); }
        }

        /// <summary>
        /// Guarda la imagen elegida como PNG cuadrado de 128 px. Devuelve false si el archivo no es una imagen.
        /// </summary>
        public static bool ImportCustom(string path)
        {
            try
            {
                using (Image source = Image.FromFile(path))
                using (Bitmap square = Fit(source, 128))
                {
                    square.Save(CustomCopy, ImageFormat.Png);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Error("[StartIcon] No se pudo usar la imagen {0}: {1}", path, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// PNG para la DLL (null si el icono es el original o los ojitos). El nombre cambia con el contenido: la
        /// barra puede tener abierto el anterior.
        /// </summary>
        /// <summary>
        /// Con «Ojitos»: imagen con los ojos cerrados (la DLL alterna las dos para parpadear). null en los demás.
        /// </summary>
        public static string ClosedEyesFile { get; private set; }

        public static string PrepareFile(int kind)
        {
            ClosedEyesFile = null;
            if (kind == Original) return null;
            // Ojitos: los mismos de la isla (estilo, color y extras), así que el nombre cambia con ellos
            string stamp = kind == Custom && File.Exists(CustomCopy) ? File.GetLastWriteTimeUtc(CustomCopy).Ticks.ToString()
                         : kind == Eyes ? string.Format("{0}_{1}{2}{3}{4}", SuiteSettings.EyesStyle, SuiteSettings.EyesColor.TrimStart('#'),
                               SuiteSettings.EyesMouth ? "b" : "", SuiteSettings.EyesBrows ? "c" : "", SuiteSettings.EyesBlush ? "r" : "")
                         : "1";
            string file = Path.Combine(Folder, "inicio_icono_" + kind + "_" + stamp + ".png");
            string closed = kind == Eyes ? Path.Combine(Folder, "inicio_icono_" + kind + "_" + stamp + "_cerrados.png") : null;
            try
            {
                if (!File.Exists(file))
                {
                    using (Bitmap bitmap = kind == Eyes ? IslandEyes.RenderStartIcon(128, false) : Render(kind, 128))
                    {
                        if (bitmap == null) return null;
                        bitmap.Save(file, ImageFormat.Png);
                    }
                }
                if (closed != null && !File.Exists(closed))
                {
                    using (Bitmap bitmap = IslandEyes.RenderStartIcon(128, true)) bitmap.Save(closed, ImageFormat.Png);
                }
                ClosedEyesFile = closed;
                foreach (string old in Directory.GetFiles(Folder, "inicio_icono_*.png"))
                {
                    if (!string.Equals(old, file, StringComparison.OrdinalIgnoreCase) && !string.Equals(old, closed, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(old); } catch (Exception) { } // La barra puede tenerlo abierto: se borra después
                    }
                }
                return file;
            }
            catch (Exception ex)
            {
                Log.Error("[StartIcon] No se pudo preparar el icono: {0}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Dibuja un icono de la galería (también las muestras del panel).
        /// </summary>
        public static Bitmap Render(int kind, int size)
        {
            if (kind == Eyes) return IslandEyes.RenderStartIcon(size, false); // Los mismos ojitos de la isla
            Bitmap bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float u = size / 24f; // Mismas coordenadas que los dibujos del mockup (caja de 24)
                switch (kind)
                {
                    case Original:
                        using (SolidBrush blue = new SolidBrush(Color.FromArgb(56, 189, 248)))
                        {
                            foreach (PointF corner in new[] { new PointF(2, 2), new PointF(12.5f, 2), new PointF(2, 12.5f), new PointF(12.5f, 12.5f) })
                                g.FillRectangle(blue, corner.X * u, corner.Y * u, 9.5f * u, 9.5f * u);
                        }
                        break;
                    case Streeck:
                        using (Stream stream = typeof(StartIconRenderer).Assembly.GetManifestResourceStream("StreeckSuite.logo.png"))
                        {
                            if (stream != null)
                            {
                                using (Image logo = Image.FromStream(stream)) g.DrawImage(logo, 0, 0, size, size);
                            }
                        }
                        break;
                    case Orb:
                        using (GraphicsPath circle = new GraphicsPath())
                        {
                            circle.AddEllipse(1 * u, 1 * u, 22 * u, 22 * u);
                            using (PathGradientBrush shade = new PathGradientBrush(circle))
                            {
                                shade.CenterPoint = new PointF(9.6f * u, 8.4f * u);
                                ColorBlend blend = new ColorBlend();
                                blend.Colors = new[] { Color.FromArgb(12, 74, 110), Color.FromArgb(2, 132, 199), Color.FromArgb(186, 230, 253) };
                                blend.Positions = new[] { 0f, 0.5f, 1f };
                                shade.InterpolationColors = blend;
                                g.FillPath(shade, circle);
                            }
                        }
                        using (SolidBrush shine = new SolidBrush(Color.FromArgb(90, 255, 255, 255))) g.FillEllipse(shine, 5 * u, 3.5f * u, 14 * u, 7 * u);
                        break;
                    case Eyes:
                        using (GraphicsPath pill = LayeredWindow.RoundedRect(new RectangleF(0, 3 * u, 24 * u, 18 * u), 9 * u))
                        using (SolidBrush black = new SolidBrush(Color.Black)) g.FillPath(black, pill);
                        using (GraphicsPath left = LayeredWindow.RoundedRect(new RectangleF(6 * u, 7.5f * u, 4.5f * u, 9 * u), 2.25f * u))
                        using (GraphicsPath right = LayeredWindow.RoundedRect(new RectangleF(13.5f * u, 7.5f * u, 4.5f * u, 9 * u), 2.25f * u))
                        using (SolidBrush white = new SolidBrush(Color.White))
                        {
                            g.FillPath(white, left);
                            g.FillPath(white, right);
                        }
                        break;
                    case Sparkle:
                        using (SolidBrush pink = new SolidBrush(Color.FromArgb(244, 114, 182)))
                        {
                            PointF[] star = { new PointF(12, 2), new PointF(14.4f, 8.6f), new PointF(21, 11), new PointF(14.4f, 13.4f), new PointF(12, 20),
                                              new PointF(9.6f, 13.4f), new PointF(3, 11), new PointF(9.6f, 8.6f) };
                            for (int i = 0; i < star.Length; i++) star[i] = new PointF(star[i].X * u, (star[i].Y + 1) * u);
                            g.FillPolygon(pink, star);
                        }
                        break;
                    case Moon:
                        using (GraphicsPath full = new GraphicsPath())
                        using (GraphicsPath bite = new GraphicsPath())
                        using (SolidBrush amber = new SolidBrush(Color.FromArgb(251, 191, 36)))
                        {
                            full.AddEllipse(3 * u, 3 * u, 18 * u, 18 * u);
                            bite.AddEllipse(9 * u, 0.5f * u, 15 * u, 15 * u);
                            using (Region crescent = new Region(full))
                            {
                                crescent.Exclude(bite);
                                g.FillRegion(amber, crescent);
                            }
                        }
                        break;
                    case Custom:
                        if (!File.Exists(CustomCopy))
                        {
                            // Sin imagen todavía: un recuadro con «+» (solo para la muestra del panel)
                            using (Pen dashed = new Pen(Color.FromArgb(154, 164, 181), 1.2f * u))
                            {
                                dashed.DashPattern = new[] { 2.5f, 1.7f };
                                using (GraphicsPath box = LayeredWindow.RoundedRect(new RectangleF(2 * u, 2 * u, 20 * u, 20 * u), 5 * u)) g.DrawPath(dashed, box);
                            }
                            using (Pen plus = new Pen(Color.FromArgb(154, 164, 181), 2 * u))
                            {
                                g.DrawLine(plus, 12 * u, 7 * u, 12 * u, 17 * u);
                                g.DrawLine(plus, 7 * u, 12 * u, 17 * u, 12 * u);
                            }
                            break;
                        }
                        using (Image mine = Image.FromFile(CustomCopy)) g.DrawImage(mine, 0, 0, size, size);
                        break;
                }
            }
            return bitmap;
        }

        // Cabe la imagen en un cuadrado sin deformarla (fondo transparente)
        private static Bitmap Fit(Image source, int size)
        {
            Bitmap square = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(square))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float scale = Math.Min(size / (float)source.Width, size / (float)source.Height);
                float w = source.Width * scale, h = source.Height * scale;
                g.DrawImage(source, (size - w) / 2, (size - h) / 2, w, h);
            }
            return square;
        }
    }
}
