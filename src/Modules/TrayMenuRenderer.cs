using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Aspecto de los menús contextuales de Windows 11 en modo oscuro para el menú de la bandeja:
    /// esquinas redondeadas (las pone DWM), resaltado redondeado, iconos de Segoe Fluent Icons y filas espaciosas.
    /// </summary>
    internal static class TrayMenuStyle
    {
        public static readonly Color Back = Color.FromArgb(44, 44, 44);
        public static readonly Color Hover = Color.FromArgb(61, 61, 61);
        public static readonly Color Text = Color.FromArgb(255, 255, 255);
        public static readonly Color Disabled = Color.FromArgb(120, 120, 120);
        public static readonly Color Line = Color.FromArgb(64, 64, 64);
        public static readonly Color Accent = Color.FromArgb(76, 194, 255);

        // Borde de 1 px que dibuja DWM alrededor del menú (COLORREF: 0x00BBGGRR)
        private const int BorderColorRef = 0x00444444;

        public static void Apply(ToolStripDropDown menu)
        {
            menu.Renderer = new Win11MenuRenderer();
            menu.BackColor = Back;
            menu.ForeColor = Text;
            menu.Font = new Font("Segoe UI", 9.75f);
            menu.Padding = new Padding(4, 6, 4, 6);
            menu.DropShadowEnabled = true;
            menu.Opened += (s, e) => StyleWindow(menu.Handle);
        }

        /// <summary>
        /// Esquinas redondeadas, modo oscuro y borde fino de Windows 11 para la ventana del menú.
        /// </summary>
        private static void StyleWindow(IntPtr handle)
        {
            int round = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            int dark = 1;
            NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
            int border = BorderColorRef;
            NativeMethods.DwmSetWindowAttribute(handle, NativeMethods.DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }

        /// <summary>
        /// Elemento de menú con un icono de Segoe Fluent Icons y altura cómoda.
        /// </summary>
        public static ToolStripMenuItem Item(string text, string glyph, EventHandler onClick)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text, glyph != null ? Glyph(glyph) : null, onClick);
            item.Padding = new Padding(4, 5, 4, 5);
            item.ForeColor = Text;
            return item;
        }

        private static Image Glyph(string glyph)
        {
            float scale;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) scale = screen.DpiX / 96f;

            int size = (int)Math.Round(16 * scale);
            Bitmap image = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(image))
            using (Font font = new Font("Segoe Fluent Icons", 12f * scale, GraphicsUnit.Pixel))
            using (SolidBrush brush = new SolidBrush(Text))
            using (StringFormat center = new StringFormat())
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                center.Alignment = StringAlignment.Center;
                center.LineAlignment = StringAlignment.Center;
                g.DrawString(glyph, font, brush, new RectangleF(0, 0, size, size), center);
            }
            return image;
        }
    }

    internal class Win11MenuRenderer : ToolStripProfessionalRenderer
    {
        public Win11MenuRenderer() : base(new Win11ColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (SolidBrush back = new SolidBrush(TrayMenuStyle.Back))
            {
                e.Graphics.FillRectangle(back, e.AffectedBounds);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // Sin borde propio: lo dibuja DWM, redondeado
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
        {
            // Mismo fondo que el resto del menú (sin la franja gris clásica)
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;

            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF highlight = new RectangleF(2, 1, e.Item.Width - 4, e.Item.Height - 2);
            using (GraphicsPath path = LayeredWindow.RoundedRect(highlight, 4))
            using (SolidBrush brush = new SolidBrush(TrayMenuStyle.Hover))
            {
                g.FillPath(brush, path);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? TrayMenuStyle.Text : TrayMenuStyle.Disabled;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen pen = new Pen(TrayMenuStyle.Line))
            {
                e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            // Flecha de submenú como en Windows 11 (chevron fino)
            Graphics g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (Font font = new Font("Segoe Fluent Icons", 7.5f))
            using (SolidBrush brush = new SolidBrush(TrayMenuStyle.Text))
            using (StringFormat center = new StringFormat())
            {
                center.Alignment = StringAlignment.Center;
                center.LineAlignment = StringAlignment.Center;
                g.DrawString("", font, brush, e.ArrowRectangle, center);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Marca de verificación en el color de acento, sin el recuadro clásico
            Graphics g = e.Graphics;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (Font font = new Font("Segoe Fluent Icons", 9.5f))
            using (SolidBrush brush = new SolidBrush(TrayMenuStyle.Accent))
            using (StringFormat center = new StringFormat())
            {
                center.Alignment = StringAlignment.Center;
                center.LineAlignment = StringAlignment.Center;
                g.DrawString("", font, brush, e.ImageRectangle, center);
            }
        }
    }

    internal class Win11ColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return TrayMenuStyle.Back; } }
        public override Color ImageMarginGradientBegin { get { return TrayMenuStyle.Back; } }
        public override Color ImageMarginGradientMiddle { get { return TrayMenuStyle.Back; } }
        public override Color ImageMarginGradientEnd { get { return TrayMenuStyle.Back; } }
        public override Color MenuBorder { get { return TrayMenuStyle.Back; } }
        public override Color MenuItemBorder { get { return Color.Transparent; } }
        public override Color MenuItemSelected { get { return TrayMenuStyle.Hover; } }
        public override Color SeparatorDark { get { return TrayMenuStyle.Line; } }
        public override Color SeparatorLight { get { return TrayMenuStyle.Line; } }
    }
}
