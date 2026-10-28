using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Fondo de escritorio desenfocado para el efecto cristal de los widgets. Como los widgets viven sobre el escritorio,
    /// lo que hay detrás de ellos es siempre el fondo: basta con desenfocarlo una vez (por cada fondo) y recortar la
    /// zona de cada tarjeta, en lugar de usar el acrílico de Windows, que obliga a recortar las ventanas sin suavizado.
    /// </summary>
    internal static class DesktopBackdrop
    {
        // Reducir a 1/24 y volver a ampliar con bicúbico equivale a un desenfoque gaussiano amplio y es muy barato
        private const int BlurFactor = 24;
        private const int CheckIntervalMs = 5000;

        private static Bitmap _blurred;
        private static string _key;
        private static DateTime _lastCheck = DateTime.MinValue;

        /// <summary>
        /// Fondo desenfocado de la pantalla principal, o null si no hay imagen de fondo.
        /// </summary>
        public static Bitmap Get()
        {
            if ((DateTime.Now - _lastCheck).TotalMilliseconds < CheckIntervalMs) return _blurred;
            _lastCheck = DateTime.Now;

            string path = WallpaperInfo.GetCurrentPath();
            string key = WallpaperInfo.GetChangeKey(path);
            if (key == _key) return _blurred;
            _key = key;

            Bitmap old = _blurred;
            _blurred = path == null ? null : Build(path);
            if (old != null) old.Dispose();
            return _blurred;
        }

        /// <summary>
        /// Se volvió a leer el fondo (cambió): los widgets deben redibujarse.
        /// </summary>
        public static string Key
        {
            get { return _key; }
        }

        private static Bitmap Build(string path)
        {
            try
            {
                Size screen = Screen.PrimaryScreen.Bounds.Size;
                using (Bitmap source = DepthClockLayers.LoadWithoutLock(path))
                using (Bitmap full = DepthClockLayers.RenderLikeWindows(source, screen))
                using (Bitmap small = new Bitmap(Math.Max(1, screen.Width / BlurFactor), Math.Max(1, screen.Height / BlurFactor), PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(small))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(full, new Rectangle(0, 0, small.Width, small.Height));
                    }

                    Bitmap blurred = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppPArgb);
                    using (Graphics g = Graphics.FromImage(blurred))
                    using (ImageAttributes clamp = new ImageAttributes())
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        clamp.SetWrapMode(WrapMode.TileFlipXY); // Sin halo oscuro en los bordes de la pantalla
                        g.DrawImage(small, new Rectangle(0, 0, screen.Width, screen.Height), 0, 0, small.Width, small.Height, GraphicsUnit.Pixel, clamp);
                    }
                    return blurred;
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DesktopWidgetsModule] No se pudo preparar el fondo desenfocado: {0}", ex.Message);
                return null;
            }
        }
    }
}
