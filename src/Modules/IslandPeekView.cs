using System;
using System.Drawing;
using System.Windows.Forms;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Aviso rápido (fase J5, idea de Nusa y Bloom): la píldora se ensancha un momento con un icono y una frase corta
    /// ("Bloq Mayús activado", "Teclado: ES → EN", "Sin internet", "Cargando · 64 %") y vuelve a su tamaño.
    /// </summary>
    internal class PeekView : IslandView
    {
        private static readonly Font Measure = new Font("Segoe UI", 10f);

        public string Glyph = "";
        public string Text = "";
        public Color GlyphColor = Color.White;

        public override SizeF BaseSize
        {
            get
            {
                int textWidth = TextRenderer.MeasureText(Text ?? "", Measure).Width;
                return new SizeF(Math.Max(160, 40 + textWidth), 38);
            }
        }

        // Solo el letrero, sin icono (el usuario lo prefiere así)
        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            p.DrawText(g, Text ?? "", p.Semibold13, Color.FromArgb(alpha, Color.White), new RectangleF(0, 0, size.Width, size.Height), StringAlignment.Center);
        }
    }
}
