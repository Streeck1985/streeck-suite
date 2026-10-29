using System.Drawing;
using System.Drawing.Drawing2D;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Estilo de cristal global del simulador ("Aplica globalmente"): Aero acrílico, 100 % transparente, Obsidiana
    /// frosted y Resplandor neón. Lo usan la barra de tareas, el menú de inicio y la isla.
    /// </summary>
    internal static class Glass
    {
        private static readonly Color Neon = Color.FromArgb(56, 189, 248);

        /// <summary>
        /// Relleno de las tarjetas. Nunca totalmente transparente: un píxel con alfa 0 dejaría pasar los clics.
        /// </summary>
        public static Color Fill
        {
            get
            {
                switch (SuiteSettings.GlassStyle)
                {
                    case SuiteSettings.GlassStyleKind.Transparent: return Color.FromArgb(70, 18, 22, 32);
                    case SuiteSettings.GlassStyleKind.Obsidian: return Color.FromArgb(240, 6, 8, 12);
                    case SuiteSettings.GlassStyleKind.Neon: return Color.FromArgb(215, 14, 18, 28);
                    default: return Color.FromArgb(225, 14, 18, 28);
                }
            }
        }

        public static Color Border
        {
            get
            {
                switch (SuiteSettings.GlassStyle)
                {
                    case SuiteSettings.GlassStyleKind.Transparent: return Color.FromArgb(25, 255, 255, 255);
                    case SuiteSettings.GlassStyleKind.Obsidian: return Color.FromArgb(22, 255, 255, 255);
                    case SuiteSettings.GlassStyleKind.Neon: return Color.FromArgb(210, Neon);
                    default: return Color.FromArgb(41, 255, 255, 255);
                }
            }
        }

        /// <summary>
        /// Intensidad de la sombra (en Transparente casi no hay, para que parezca que los iconos flotan).
        /// </summary>
        public static int ShadowStrength
        {
            get { return SuiteSettings.GlassStyle == SuiteSettings.GlassStyleKind.Transparent ? 40 : SuiteSettings.GlassStyle == SuiteSettings.GlassStyleKind.Obsidian ? 230 : 190; }
        }

        /// <summary>
        /// Tarjeta completa: sombra (o halo azul en Neón), relleno y borde.
        /// </summary>
        public static void DrawCard(Graphics g, RectangleF rect, float radius, float shadowSpread, float shadowOffset)
        {
            DrawCard(g, rect, radius, shadowSpread, shadowOffset, 0);
        }

        /// <summary>
        /// Relleno con una opacidad mínima: el menú de inicio y la isla tienen texto que debe leerse en cualquier fondo.
        /// </summary>
        public static Color FillAtLeast(int minAlpha)
        {
            Color fill = Fill;
            return fill.A >= minAlpha ? fill : Color.FromArgb(minAlpha, fill);
        }

        public static void DrawCard(Graphics g, RectangleF rect, float radius, float shadowSpread, float shadowOffset, int minAlpha)
        {
            if (SuiteSettings.GlassStyle == SuiteSettings.GlassStyleKind.Neon)
            {
                // Resplandor neón: varias capas azules difusas alrededor del borde
                for (int i = 6; i >= 1; i--)
                {
                    float grow = shadowSpread * 0.7f * i / 6;
                    using (GraphicsPath glow = LayeredWindow.RoundedRect(RectangleF.Inflate(rect, grow, grow), radius + grow))
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(10 + (6 - i) * 6, Neon)))
                    {
                        g.FillPath(brush, glow);
                    }
                }
            }
            else
            {
                LayeredWindow.DrawSoftShadow(g, rect, radius, shadowSpread, shadowOffset, ShadowStrength);
            }
            using (GraphicsPath path = LayeredWindow.RoundedRect(rect, radius))
            using (SolidBrush fill = new SolidBrush(FillAtLeast(minAlpha)))
            using (Pen border = new Pen(Border, SuiteSettings.GlassStyle == SuiteSettings.GlassStyleKind.Neon ? 1.5f : 1f))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
        }
    }
}
