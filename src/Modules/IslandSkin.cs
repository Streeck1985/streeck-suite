using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// La isla de arriba usa el mismo skin que la barra de tareas (Ajustes → Barra de tareas → Skin): Cristal, Notch,
    /// Dock, Neón, Fantasma, Línea fina, Aurora, Aero 7, Cristal líquido o Gamer RGB. Con la barra apagada, la isla usa
    /// su propio color y opacidad (Ajustes → Isla), como el skin Notch.
    /// </summary>
    internal static class IslandSkin
    {
        public const int Glass = 0, Notch = 1, Dock = 2, Neon = 3, Ghost = 4, Line = 5, Aurora = 6, Aero7 = 7, Liquid = 8, Rgb = 9;
        private static readonly Color Sky = Color.FromArgb(56, 189, 248);

        public static int Current
        {
            get { return SuiteSettings.TaskbarEnabled ? SuiteSettings.TaskbarSkin : Notch; }
        }

        /// <summary>
        /// Gamer RGB gira: la isla se redibuja aunque no haya nada más en movimiento.
        /// </summary>
        public static bool IsAnimated
        {
            get { return Current == Rgb; }
        }

        /// <summary>
        /// Pinta el fondo de la isla con el skin. <paramref name="userFill"/> es el color y opacidad de Ajustes → Isla.
        /// </summary>
        public static void Paint(Graphics g, GraphicsPath shape, GraphicsPath outline, RectangleF bounds, Color userFill)
        {
            switch (Current)
            {
                case Notch:
                    Fill(g, shape, userFill);
                    break;
                case Dock:
                    Fill(g, shape, Color.FromArgb(205, 28, 30, 38));
                    Stroke(g, outline, Color.FromArgb(50, 255, 255, 255), 1);
                    break;
                case Neon:
                    Fill(g, shape, Color.FromArgb(230, 8, 10, 20));
                    Stroke(g, outline, Color.FromArgb(40, Sky), 6);   // Halo
                    Stroke(g, outline, Color.FromArgb(70, Sky), 3);
                    Stroke(g, outline, Color.FromArgb(220, Sky), 1.5f);
                    break;
                case Ghost:
                    Fill(g, shape, Color.FromArgb(90, 0, 0, 0)); // Casi invisible, pero el texto se sigue leyendo
                    break;
                case Line:
                    Fill(g, shape, Color.FromArgb(228, 10, 12, 18));
                    Stroke(g, outline, Color.FromArgb(30, 255, 255, 255), 1);
                    break;
                case Aurora:
                    using (LinearGradientBrush brush = new LinearGradientBrush(bounds, Color.FromArgb(225, 124, 58, 237), Color.FromArgb(225, 236, 72, 153), 35f))
                    {
                        g.FillPath(brush, shape);
                    }
                    break;
                case Aero7:
                    Fill(g, shape, Color.FromArgb(195, 20, 40, 80));
                    // Brillo de cristal en la mitad de arriba (máximo 24 px, como la barra de Windows 7)
                    GraphicsState state = g.Save();
                    g.SetClip(shape);
                    RectangleF gloss = new RectangleF(bounds.X, bounds.Y, bounds.Width, Math.Max(2, Math.Min(bounds.Height / 2, 24)));
                    using (LinearGradientBrush brush = new LinearGradientBrush(new RectangleF(gloss.X, gloss.Y - 1, gloss.Width, gloss.Height + 2),
                        Color.FromArgb(100, 255, 255, 255), Color.FromArgb(18, 255, 255, 255), 90f))
                    {
                        g.FillRectangle(brush, gloss);
                    }
                    g.Restore(state);
                    Stroke(g, outline, Color.FromArgb(140, 255, 255, 255), 1);
                    break;
                case Liquid:
                    Fill(g, shape, Color.FromArgb(70, 22, 24, 34));
                    Stroke(g, outline, Color.FromArgb(80, 255, 255, 255), 1);
                    using (Pen shine = new Pen(Color.FromArgb(150, 255, 255, 255), 1.2f))
                    {
                        float inset = Math.Min(bounds.Width / 4, 18);
                        g.DrawLine(shine, bounds.X + inset, bounds.Bottom - 1.5f, bounds.Right - inset, bounds.Bottom - 1.5f); // Reflejo fino
                    }
                    break;
                case Rgb:
                    Fill(g, shape, Color.FromArgb(230, 8, 10, 20));
                    using (LinearGradientBrush rainbow = new LinearGradientBrush(bounds, Color.Red, Color.Blue, (float)(DateTime.Now.TimeOfDay.TotalMilliseconds / 4000.0 * 360 % 360)))
                    {
                        ColorBlend blend = new ColorBlend();
                        blend.Colors = new[] { Color.FromArgb(255, 0, 76), Color.FromArgb(255, 184, 0), Color.FromArgb(61, 255, 110),
                            Color.FromArgb(0, 194, 255), Color.FromArgb(157, 77, 255), Color.FromArgb(255, 0, 76) };
                        blend.Positions = new[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f };
                        rainbow.InterpolationColors = blend;
                        using (Pen pen = new Pen(rainbow, 2)) g.DrawPath(pen, outline);
                    }
                    break;
                default: // Cristal
                    Fill(g, shape, Color.FromArgb(215, 14, 18, 28));
                    Stroke(g, outline, Color.FromArgb(41, 255, 255, 255), 1);
                    break;
            }
        }

        private static void Fill(Graphics g, GraphicsPath shape, Color color)
        {
            using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, shape);
        }

        private static void Stroke(Graphics g, GraphicsPath outline, Color color, float width)
        {
            using (Pen pen = new Pen(color, width)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, outline); }
        }
    }
}
