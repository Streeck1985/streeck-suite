using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Reloj estirado, como el "Stretch Font" de One UI 8 (mockup R1): números rellenos de la fuente elegida; cada uno
    /// se estira hacia abajo hasta quedar a 12 px de la silueta que la IA detecta en el fondo. Si ni a tamaño normal
    /// cabe, no se encoge: el sujeto pasa por delante. Los dos puntos quedan centrados en la altura normal de los números.
    /// </summary>
    internal static class SamsungClockRenderer
    {
        private const float Margin = 12f;      // Distancia mínima entre un número y la silueta
        private const float MinScale = 0.45f;  // Lo más que se acorta un número para no tocar la silueta
        /// <param name="blocked">Recibe las celdas de los números que no cupieron sobre el sujeto: ahí se dibuja
        /// el primer plano encima, como en el reloj clásico.</param>
        public static void Draw(Graphics g, Size screen, ClockSettings settings, DepthClockLayers layers, DateTime now, CultureInfo spanish, WeatherService.Conditions weather, System.Collections.Generic.List<RectangleF> blocked)
        {
            if (settings == null) return;
            string time = now.ToString(DepthClockLayout.TimeFormat(settings));
            float fontPx = DepthClockLayout.ClockFontPx(screen, settings);
            float top = settings.Top * screen.Height;
            Color color = ParseColor(settings.SamsungColor);

            using (FontFamily family = ResolveWeightedFamily(settings.FontFamily, settings.SamsungWeight))
            {
                FontStyle style = settings.SamsungWeight == 2 && family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;

                // Celdas de ancho fijo (las del "0"), así los números no bailan cada segundo con fuentes de ancho variable
                RectangleF zero = GlyphBounds(family, style, fontPx, '0');
                RectangleF colon = GlyphBounds(family, style, fontPx, ':');
                float gap = fontPx * 0.06f;
                float digitH = zero.Height;
                float total = 0;
                foreach (char c in time) total += (c == ':' ? colon.Width : zero.Width) + gap;
                total -= gap;

                float maxH = Math.Max(digitH, screen.Height * settings.SamsungMaxStretch);
                float x = settings.CenterX * screen.Width - total / 2;

                // Si algún número no cabe a tamaño normal sobre la silueta (el techo de un auto justo debajo), el reloj
                // sube lo necesario para pararse encima en vez de aplastarse. Solo si subir lo resuelve (una torre que
                // llega hasta arriba no se arregla subiendo) y sin pasar de un 20 % de la pantalla
                if (layers != null)
                {
                    float limit = settings.ShowDate
                        ? DepthClockLayout.CaptionFontPx(screen, settings) * 1.6f + DepthClockLayout.CaptionGapPx(screen, settings)
                        : screen.Height * 0.02f;
                    float minRoom = MinRoom(layers, screen, time, x, top, zero.Width, colon.Width, gap);
                    float shift = digitH - minRoom;
                    if (shift > 0 && shift <= screen.Height * 0.2f && top - shift >= limit)
                    {
                        float raised = top - shift;
                        if (MinRoom(layers, screen, time, x, raised, zero.Width, colon.Width, gap) >= digitH * 0.95f) top = raised;
                    }
                }
                using (SolidBrush brush = new SolidBrush(color))
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                {
                    foreach (char c in time)
                    {
                        bool isColon = c == ':';
                        float cell = isColon ? colon.Width : zero.Width;
                        using (GraphicsPath glyph = new GraphicsPath())
                        {
                            glyph.AddString(c.ToString(), family, (int)style, fontPx, PointF.Empty, StringFormat.GenericTypographic);
                            RectangleF bounds = glyph.GetBounds();
                            float height = digitH;
                            if (!isColon && bounds.Height > 0)
                            {
                                // Espacio libre bajo esta columna hasta la silueta del sujeto
                                float obstacle = layers != null
                                    ? layers.GetColumnObstacleTop(x / screen.Width, (x + cell) / screen.Width, top / screen.Height) * screen.Height
                                    : screen.Height;
                                // Se para sobre la silueta (más corto si hace falta, sin quedar como una rayita);
                                // si ni así cabe, el sujeto le pasa por delante
                                float room = obstacle - Margin - top;
                                height = Math.Max(digitH * MinScale, Math.Min(maxH, room));
                                if (room < digitH * MinScale && blocked != null) blocked.Add(new RectangleF(x - gap, top, cell + 2 * gap, height + Margin));
                            }
                            using (Matrix place = new Matrix())
                            {
                                float glyphTop = isColon ? top + (digitH - bounds.Height) / 2 : top;
                                place.Translate(x + (cell - bounds.Width) / 2, glyphTop);
                                if (!isColon) place.Scale(1, height / bounds.Height);
                                place.Translate(-bounds.X, -bounds.Y);
                                glyph.Transform(place);
                            }
                            // Sombra suave para que se lea sobre fondos claros
                            using (GraphicsPath dropped = (GraphicsPath)glyph.Clone())
                            using (Matrix down = new Matrix())
                            {
                                down.Translate(0, Math.Max(1f, fontPx * 0.012f));
                                dropped.Transform(down);
                                g.FillPath(shadow, dropped);
                            }
                            g.FillPath(brush, glyph);
                        }
                        x += cell + gap;
                    }
                }

                // Fecha encima, en el color del reloj, y clima debajo de la altura normal de los números
                float captionFont = DepthClockLayout.CaptionFontPx(screen, settings);
                float captionGap = DepthClockLayout.CaptionGapPx(screen, settings);
                float centerX = settings.CenterX * screen.Width;
                if (settings.ShowDate)
                {
                    string date = now.ToString("dddd, d 'de' MMMM", spanish).ToUpper(spanish);
                    DrawCaption(g, date, captionFont, centerX, top - captionGap, color);
                }
                if (settings.ShowWeather && weather != null)
                {
                    DesktopClockForm.DrawWeatherPill(g, weather, captionFont, centerX, top + digitH + captionGap);
                }
            }
        }

        // El espacio libre más chico bajo los números (de la hora hasta la silueta), con la hora arriba en "top"
        private static float MinRoom(DepthClockLayers layers, Size screen, string time, float x, float top, float digitW, float colonW, float gap)
        {
            float min = float.MaxValue;
            foreach (char c in time)
            {
                float cell = c == ':' ? colonW : digitW;
                if (c != ':')
                {
                    float obstacle = layers.GetColumnObstacleTop(x / screen.Width, (x + cell) / screen.Width, top / screen.Height) * screen.Height;
                    min = Math.Min(min, obstacle - Margin - top);
                }
                x += cell + gap;
            }
            return min;
        }

        // Fino usa la variante "Light" de la fuente si existe; normal y grueso usan la fuente tal cual
        private static FontFamily ResolveWeightedFamily(string name, int weight)
        {
            if (weight == 0)
            {
                foreach (string suffix in new[] { " Light", " Semilight" })
                {
                    try { return new FontFamily(name + suffix); }
                    catch (ArgumentException) { }
                }
            }
            return DepthClockLayout.ResolveFamily(name);
        }

        private static RectangleF GlyphBounds(FontFamily family, FontStyle style, float fontPx, char c)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddString(c.ToString(), family, (int)style, fontPx, PointF.Empty, StringFormat.GenericTypographic);
                return path.GetBounds();
            }
        }

        private static Color ParseColor(string hex)
        {
            try
            {
                return ColorTranslator.FromHtml(string.IsNullOrEmpty(hex) ? "#ffffff" : hex);
            }
            catch (Exception)
            {
                return Color.White;
            }
        }

        private static void DrawCaption(Graphics g, string text, float fontPx, float centerX, float bottom, Color color)
        {
            float spacing = fontPx * 0.16f;
            using (FontFamily family = DepthClockLayout.ResolveFamily(ClockSettings.DefaultFontFamily))
            using (Font font = new Font(family, fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat measure = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (GraphicsPath path = new GraphicsPath())
            {
                measure.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                float x = 0;
                foreach (char c in text)
                {
                    string s = c.ToString();
                    path.AddString(s, family, (int)FontStyle.Bold, fontPx, new PointF(x, 0), StringFormat.GenericTypographic);
                    x += g.MeasureString(s, font, PointF.Empty, measure).Width + spacing;
                }

                RectangleF bounds = path.GetBounds();
                using (Matrix move = new Matrix())
                {
                    move.Translate(centerX - (bounds.X + bounds.Width / 2f), bottom - bounds.Bottom);
                    path.Transform(move);
                }

                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                using (SolidBrush brush = new SolidBrush(color))
                {
                    using (Matrix down = new Matrix())
                    using (GraphicsPath shadowPath = (GraphicsPath)path.Clone())
                    {
                        down.Translate(0, fontPx * 0.05f);
                        shadowPath.Transform(down);
                        g.FillPath(shadow, shadowPath);
                    }
                    g.FillPath(brush, path);
                }
            }
        }
    }
}
