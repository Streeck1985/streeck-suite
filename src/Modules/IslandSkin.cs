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
        public const int Glass = 0, Notch = 1, Dock = 2, Neon = 3, Ghost = 4, Line = 5, Aurora = 6, Aero7 = 7, Liquid = 8, Rgb = 9,
                         Graphite = 10, Paper = 11, Sunset = 12, Terminal = 13, Ice = 14, Gold = 15;
        public const int Count = 16;
        public static readonly string[] Names = { "Cristal", "Notch", "Dock", "Neón", "Fantasma", "Línea fina", "Aurora", "Aero 7", "Cristal líquido", "Gamer RGB",
                                                   "Grafito", "Papel", "Atardecer", "Terminal", "Hielo", "Oro" };
        private static readonly Color GoldColor = Color.FromArgb(232, 196, 106);
        private static readonly Color TerminalGreen = Color.FromArgb(77, 255, 122);

        /// <summary>
        /// Para la galería de Ajustes: pinta con este skin en lugar del elegido.
        /// </summary>
        internal static int? PreviewOverride;

        /// <summary>
        /// Papel y Hielo son claros: la isla escribe en oscuro.
        /// </summary>
        public static bool IsLight
        {
            get { int s = Current; return s == Paper || s == Ice; }
        }

        /// <summary>
        /// Color de acento del skin (barras, botones encendidos); null = el de siempre.
        /// </summary>
        public static Color? AccentColor
        {
            get
            {
                switch (Current)
                {
                    case Terminal: return TerminalGreen;
                    case Gold: return GoldColor;
                    case Paper: return Color.FromArgb(37, 99, 235);
                    case Ice: return Color.FromArgb(14, 165, 233);
                    case Graphite: return Color.FromArgb(161, 161, 170);
                    default: return null;
                }
            }
        }
        private static readonly Color Sky = Color.FromArgb(56, 189, 248);

        public static int Current
        {
            get { return PreviewOverride ?? (SuiteSettings.TaskbarEnabled ? SuiteSettings.TaskbarSkin : Notch); }
        }

        /// <summary>
        /// Gamer RGB gira: la isla se redibuja aunque no haya nada más en movimiento.
        /// </summary>
        public static bool IsAnimated
        {
            get { return Current == Rgb || SuiteSettings.IslandBorder == SuiteSettings.BorderKind.Glow || (SuiteSettings.IslandBorder == SuiteSettings.BorderKind.Skin && Current == Gold); }
        }

        /// <summary>
        /// Pinta el fondo de la isla con el skin. <paramref name="userFill"/> es el color y opacidad de Ajustes → Isla.
        /// </summary>
        public static void Paint(Graphics g, GraphicsPath shape, GraphicsPath outline, RectangleF bounds, Color userFill)
        {
            // Fondo cristal: el mismo skin, más transparente
            float glassFactor = SuiteSettings.IslandGlass ? 0.62f : 1f;
            _glass = glassFactor;
            PaintFill(g, shape, outline, bounds, userFill, glassFactor);
            PaintBorder(g, outline, bounds);
        }

        // Fill ya aplica el cristal a todos los rellenos sólidos
        private static float _glass = 1f;

        private static Color A(Color c, float factor)
        {
            return c;
        }

        // Borde elegido en Ajustes › Apariencia (o el propio del skin)
        private static void PaintBorder(Graphics g, GraphicsPath outline, RectangleF bounds)
        {
            SuiteSettings.BorderKind border = SuiteSettings.IslandBorder;
            int skin = Current;
            if (border == SuiteSettings.BorderKind.Thin)
            {
                Stroke(g, outline, IsLight ? Color.FromArgb(40, 0, 0, 0) : Color.FromArgb(46, 255, 255, 255), 1);
            }
            else if (border == SuiteSettings.BorderKind.Glow || (border == SuiteSettings.BorderKind.Skin && skin == Gold))
            {
                // Brillo que recorre el contorno con el color del skin
                Color main = skin == Gold ? GoldColor : AccentColor ?? Sky;
                Color light = Color.FromArgb(255, Math.Min(255, main.R + 90), Math.Min(255, main.G + 90), Math.Min(255, main.B + 90));
                float angle = (float)(DateTime.Now.TimeOfDay.TotalMilliseconds / 5000.0 * 360 % 360);
                using (LinearGradientBrush brush = new LinearGradientBrush(bounds, main, light, angle))
                {
                    ColorBlend blend = new ColorBlend();
                    blend.Colors = new[] { Color.FromArgb(90, main), light, main, Color.FromArgb(90, main) };
                    blend.Positions = new[] { 0f, 0.35f, 0.65f, 1f };
                    brush.InterpolationColors = blend;
                    using (Pen halo = new Pen(Color.FromArgb(50, main), 4)) { halo.LineJoin = LineJoin.Round; g.DrawPath(halo, outline); }
                    using (Pen pen = new Pen(brush, 1.6f)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, outline); }
                }
            }
        }

        private static void PaintFill(Graphics g, GraphicsPath shape, GraphicsPath outline, RectangleF bounds, Color userFill, float k)
        {
            bool skinBorder = SuiteSettings.IslandBorder == SuiteSettings.BorderKind.Skin;
            switch (Current)
            {
                case Graphite:
                    Fill(g, shape, A(Color.FromArgb(240, 42, 44, 49), k));
                    if (skinBorder) Stroke(g, outline, Color.FromArgb(26, 255, 255, 255), 1);
                    return;
                case Paper:
                    Fill(g, shape, A(Color.FromArgb(245, 250, 250, 248), k));
                    if (skinBorder) Stroke(g, outline, Color.FromArgb(30, 0, 0, 0), 1);
                    return;
                case Sunset:
                    using (LinearGradientBrush brush = new LinearGradientBrush(bounds, Color.FromArgb((int)(240 * k), 255, 122, 69), Color.FromArgb((int)(240 * k), 122, 44, 255), 20f))
                    {
                        ColorBlend blend = new ColorBlend();
                        blend.Colors = new[] { Color.FromArgb((int)(240 * k), 255, 122, 69), Color.FromArgb((int)(240 * k), 255, 61, 119), Color.FromArgb((int)(240 * k), 122, 44, 255) };
                        blend.Positions = new[] { 0f, 0.55f, 1f };
                        brush.InterpolationColors = blend;
                        g.FillPath(brush, shape);
                    }
                    return;
                case Terminal:
                    Fill(g, shape, A(Color.FromArgb(245, 2, 7, 2), k));
                    if (skinBorder) Stroke(g, outline, Color.FromArgb(90, TerminalGreen), 1);
                    return;
                case Ice:
                    Fill(g, shape, A(Color.FromArgb(200, 214, 236, 255), k));
                    if (skinBorder) Stroke(g, outline, Color.FromArgb(150, 255, 255, 255), 1);
                    return;
                case Gold:
                    Fill(g, shape, A(Color.FromArgb(245, 10, 10, 10), k));
                    return;
            }
            if (!skinBorder) { PaintClassicFillOnly(g, shape, bounds, userFill, k); return; }
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

        // Los skins de siempre sin su borde (cuando el usuario elige otro borde)
        private static void PaintClassicFillOnly(Graphics g, GraphicsPath shape, RectangleF bounds, Color userFill, float k)
        {
            switch (Current)
            {
                case Notch: Fill(g, shape, userFill); break;
                case Dock: Fill(g, shape, A(Color.FromArgb(205, 28, 30, 38), k)); break;
                case Neon: case Rgb: Fill(g, shape, A(Color.FromArgb(230, 8, 10, 20), k)); break;
                case Ghost: Fill(g, shape, Color.FromArgb(90, 0, 0, 0)); break;
                case Line: Fill(g, shape, A(Color.FromArgb(228, 10, 12, 18), k)); break;
                case Aurora:
                    using (LinearGradientBrush brush = new LinearGradientBrush(bounds, Color.FromArgb((int)(225 * k), 124, 58, 237), Color.FromArgb((int)(225 * k), 236, 72, 153), 35f))
                        g.FillPath(brush, shape);
                    break;
                case Aero7: Fill(g, shape, A(Color.FromArgb(195, 20, 40, 80), k)); break;
                case Liquid: Fill(g, shape, Color.FromArgb(70, 22, 24, 34)); break;
                default: Fill(g, shape, A(Color.FromArgb(215, 14, 18, 28), k)); break;
            }
        }

        /// <summary>
        /// El fondo es casi sólido: las letras se pueden dibujar con ClearType (nítidas). En los skins muy
        /// transparentes (Fantasma, Cristal líquido) ClearType dejaría bordes de colores: van suavizadas en gris.
        /// </summary>
        public static bool IsSolid(Color userFill)
        {
            switch (Current)
            {
                case Notch: return userFill.A >= 190;
                case Ghost: case Liquid: return false;
                case Paper: case Ice: return false; // Letra oscura sobre claro: suavizado en gris
                default: if (SuiteSettings.IslandGlass) return false; return true;
            }
        }

        /// <summary>
        /// Barra de nivel (volumen) con el color del skin: blanca en Notch, cian con brillo en Neón, degradado en
        /// Aurora, cristal en Aero 7, arcoíris que gira en Gamer RGB...
        /// </summary>
        public static void DrawLevelBar(Graphics g, RectangleF track, float value, int alpha)
        {
            IslandPainter.FillRounded(g, track, track.Height / 2, IslandPainter.Fade(IslandPainter.Track, alpha));
            if (value <= 0.005f) return;
            RectangleF fill = new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * Math.Min(1f, value)), track.Height);
            int skin = Current;
            if (skin == Neon)
            {
                RectangleF glow = RectangleF.Inflate(fill, 2, 2);
                IslandPainter.FillRounded(g, glow, glow.Height / 2, Color.FromArgb(70 * alpha / 255, Sky));
            }
            using (GraphicsPath path = IslandPainter.RoundedRect(fill, fill.Height / 2))
            {
                if (skin == Aurora || skin == Rgb || skin == Aero7)
                {
                    RectangleF area = new RectangleF(track.X, track.Y - 1, track.Width, track.Height + 2); // El degradado va a lo largo de toda la pista
                    float angle = skin == Rgb ? (float)(DateTime.Now.TimeOfDay.TotalMilliseconds / 4000.0 * 360 % 360) : skin == Aero7 ? 90f : 0f;
                    using (LinearGradientBrush brush = new LinearGradientBrush(area, Color.White, Color.White, angle))
                    {
                        ColorBlend blend = new ColorBlend();
                        if (skin == Aurora)
                        {
                            blend.Colors = new[] { Color.FromArgb(alpha, 167, 139, 250), Color.FromArgb(alpha, 236, 72, 153) };
                            blend.Positions = new[] { 0f, 1f };
                        }
                        else if (skin == Aero7)
                        {
                            blend.Colors = new[] { Color.FromArgb(alpha, 186, 230, 253), Color.FromArgb(alpha, 56, 150, 230), Color.FromArgb(alpha, 20, 90, 180) };
                            blend.Positions = new[] { 0f, 0.5f, 1f };
                        }
                        else
                        {
                            blend.Colors = new[] { Color.FromArgb(alpha, 255, 0, 76), Color.FromArgb(alpha, 255, 184, 0), Color.FromArgb(alpha, 61, 255, 110),
                                Color.FromArgb(alpha, 0, 194, 255), Color.FromArgb(alpha, 157, 77, 255), Color.FromArgb(alpha, 255, 0, 76) };
                            blend.Positions = new[] { 0f, 0.2f, 0.4f, 0.6f, 0.8f, 1f };
                        }
                        brush.InterpolationColors = blend;
                        g.FillPath(brush, path);
                    }
                    return;
                }
                Color color = skin == Glass ? Sky
                            : skin == Neon ? Color.FromArgb(125, 211, 252)
                            : skin == Liquid ? Color.FromArgb(200, 255, 255, 255)
                            : AccentColor.HasValue ? AccentColor.Value
                            : Color.White; // Notch, Dock, Fantasma, Línea fina
                using (SolidBrush brush = new SolidBrush(IslandPainter.Fade(color, alpha))) g.FillPath(brush, path);
            }
        }

        private static void Fill(Graphics g, GraphicsPath shape, Color color)
        {
            using (SolidBrush brush = new SolidBrush(Color.FromArgb((int)(color.A * _glass), color))) g.FillPath(brush, shape);
        }

        private static void Stroke(Graphics g, GraphicsPath outline, Color color, float width)
        {
            using (Pen pen = new Pen(color, width)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, outline); }
        }
    }
}
