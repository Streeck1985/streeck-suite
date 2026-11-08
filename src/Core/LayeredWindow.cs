using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace StreeckSuite.Core
{
    /// <summary>
    /// Ventana con transparencia por píxel (UpdateLayeredWindow). A diferencia de recortar con Region, los bordes
    /// redondeados quedan suavizados y se pueden dibujar sombras difusas. Las subclases dibujan en DrawSurface y
    /// llaman a RenderSurface cuando cambia el contenido (WM_PAINT no se usa en este tipo de ventanas).
    /// Los píxeles totalmente transparentes dejan pasar los clics a lo que haya debajo.
    /// </summary>
    public abstract class LayeredWindow : Form
    {
        protected LayeredWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; } // Aparecer sin quitarle el foco a la ventana en uso
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW: Ocultar de Alt+Tab
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE: No robar el foco de otras ventanas
                return cp;
            }
        }

        /// <summary>
        /// Dibuja el contenido completo de la ventana sobre un lienzo transparente del tamaño de la ventana.
        /// </summary>
        protected abstract void DrawSurface(Graphics g);

        /// <summary>
        /// Opacidad de toda la ventana (0..255) por encima de la transparencia de cada píxel.
        /// </summary>
        protected virtual byte SurfaceAlpha
        {
            get { return 255; }
        }

        // Posición y tamaño que se aplican junto con el próximo dibujo (ver RenderSurface(Rectangle))
        private Rectangle? _nextBounds;
        private bool _pushing;

        /// <summary>
        /// Mueve/redimensiona la ventana y entrega su contenido nuevo en un solo paso (UpdateLayeredWindow):
        /// así no hay un cuadro intermedio con la imagen vieja en el tamaño nuevo, que en las animaciones se ve
        /// como un temblor.
        /// </summary>
        public void RenderSurface(Rectangle bounds)
        {
            _nextBounds = bounds;
            RenderSurface();
        }

        public void RenderSurface()
        {
            if (!IsHandleCreated || IsDisposed) return;
            Rectangle target = _nextBounds ?? new Rectangle(Left, Top, Width, Height);
            _nextBounds = null;
            if (target.Width <= 0 || target.Height <= 0) return;

            using (Bitmap surface = new Bitmap(target.Width, target.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(surface))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawSurface(g);
                }
                Push(surface, target.Location);
            }
        }

        private void Push(Bitmap surface, Point location)
        {
            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            IntPtr memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            IntPtr bitmap = IntPtr.Zero, previous = IntPtr.Zero;
            try
            {
                // GetHbitmap entrega los píxeles premultiplicados, que es lo que espera UpdateLayeredWindow
                bitmap = surface.GetHbitmap(Color.FromArgb(0));
                previous = NativeMethods.SelectObject(memoryDc, bitmap);

                NativeMethods.POINT destination = new NativeMethods.POINT(location.X, location.Y);
                NativeMethods.SIZE size = new NativeMethods.SIZE(surface.Width, surface.Height);
                NativeMethods.POINT source = new NativeMethods.POINT(0, 0);
                NativeMethods.BLENDFUNCTION blend = new NativeMethods.BLENDFUNCTION();
                blend.BlendOp = NativeMethods.AC_SRC_OVER;
                blend.SourceConstantAlpha = SurfaceAlpha;
                blend.AlphaFormat = NativeMethods.AC_SRC_ALPHA;

                _pushing = true; // Windows avisa del cambio de tamaño aquí mismo: no hay que volver a dibujar
                try { NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size, memoryDc, ref source, 0, ref blend, NativeMethods.ULW_ALPHA); }
                finally { _pushing = false; }
            }
            finally
            {
                if (previous != IntPtr.Zero) NativeMethods.SelectObject(memoryDc, previous);
                if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
                NativeMethods.DeleteDC(memoryDc);
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            RenderSurface();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (!_pushing) RenderSurface(); // Cambio de tamaño hecho por RenderSurface: ya trae su contenido
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Sin uso: el contenido se entrega con UpdateLayeredWindow
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Sin uso: el contenido se entrega con UpdateLayeredWindow
        }

        /// <summary>
        /// Sombra difusa alrededor de una forma redondeada: varias capas crecientes muy transparentes.
        /// </summary>
        public static void DrawSoftShadow(Graphics g, RectangleF shape, float radius, float spread, float offsetY, int strength)
        {
            const int layers = 8;
            for (int i = layers; i >= 1; i--)
            {
                float grow = spread * i / layers;
                RectangleF r = new RectangleF(shape.X - grow, shape.Y - grow + offsetY, shape.Width + 2 * grow, shape.Height + 2 * grow);
                int alpha = strength * (layers - i + 1) / (layers * layers);
                using (GraphicsPath path = RoundedRect(r, radius + grow))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(Math.Max(1, alpha), 0, 0, 0)))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            GraphicsPath path = new GraphicsPath();
            if (d <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
