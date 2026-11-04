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
                return new SizeF(Math.Max(180, 64 + textWidth), 38);
            }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            float glyphWidth = p.Px(22);
            float textWidth = g.MeasureString(Text ?? "", p.Semibold13).Width;
            float left = (size.Width - glyphWidth - p.Px(8) - textWidth) / 2;
            p.DrawText(g, Glyph, p.Glyph16, Color.FromArgb(alpha, GlyphColor), new RectangleF(left, 0, glyphWidth, size.Height), StringAlignment.Center);
            p.DrawText(g, Text ?? "", p.Semibold13, Color.FromArgb(alpha, Color.White),
                new RectangleF(left + glyphWidth + p.Px(8), 0, textWidth + p.Px(4), size.Height), StringAlignment.Near);
        }
    }
}
