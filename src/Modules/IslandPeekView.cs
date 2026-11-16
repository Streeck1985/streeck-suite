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
        public Action Click; // Qué hace un clic en el aviso (p. ej. abrir Acerca de con la versión nueva)
        public string Detail; // Segunda línea opcional (p. ej. las primeras palabras del texto copiado)
        public string Chip;   // Etiqueta opcional a la derecha (p. ej. «Inglés»)

        public override bool OnClick(PointF point)
        {
            if (Click == null) return false;
            Click();
            return true;
        }

        public override SizeF BaseSize
        {
            get
            {
                int textWidth = TextRenderer.MeasureText(Text ?? "", Measure).Width;
                if (string.IsNullOrEmpty(Detail)) return new SizeF(Math.Max(160, 40 + textWidth), 38);
                return new SizeF(380, 50); // Dos líneas (mockup «Copiar texto»)
            }
        }

        // Solo el letrero, sin icono (el usuario lo prefiere así)
        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            if (string.IsNullOrEmpty(Detail))
            {
                p.DrawText(g, Text ?? "", p.Semibold13, Color.FromArgb(alpha, IslandPainter.InkBase), new RectangleF(0, 0, size.Width, size.Height), StringAlignment.Center);
                return;
            }
            float pad = p.Px(20), chipW = 0;
            if (!string.IsNullOrEmpty(Chip))
            {
                chipW = g.MeasureString(Chip, p.Text11).Width + p.Px(12);
                RectangleF chip = new RectangleF(size.Width - pad - chipW, (size.Height - p.Px(18)) / 2, chipW, p.Px(18));
                using (System.Drawing.Drawing2D.GraphicsPath path = IslandPainter.RoundedRect(chip, p.Px(6)))
                using (Pen line = new Pen(IslandPainter.Fade(IslandPainter.Gray, alpha / 2), 1)) g.DrawPath(line, path);
                p.DrawText(g, Chip, p.Text11, IslandPainter.Fade(IslandPainter.Gray, alpha), chip, StringAlignment.Center);
                chipW += p.Px(10);
            }
            float textW = size.Width - 2 * pad - chipW;
            p.DrawText(g, Text ?? "", p.Semibold13, Color.FromArgb(alpha, GlyphColor), new RectangleF(pad, p.Px(6), textW, size.Height / 2 - p.Px(4)), StringAlignment.Near);
            p.DrawText(g, Detail, p.Text11, IslandPainter.Fade(IslandPainter.Gray, alpha), new RectangleF(pad, size.Height / 2, textW, size.Height / 2 - p.Px(6)), StringAlignment.Near);
        }
    }
}
