using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Iconos del clima dibujados como vectores (GDI+ no sabe pintar emojis a color).
    /// </summary>
    internal static class WeatherIcons
    {
        private static readonly Color SunColor = Color.FromArgb(253, 184, 19);
        private static readonly Color MoonColor = Color.FromArgb(254, 240, 180);
        private static readonly Color CloudColor = Color.FromArgb(236, 240, 248);
        private static readonly Color DarkCloudColor = Color.FromArgb(160, 170, 190);
        private static readonly Color RainColor = Color.FromArgb(96, 165, 250);
        private static readonly Color BoltColor = Color.FromArgb(250, 204, 21);

        public static void Draw(Graphics g, RectangleF r, int code, bool isDay)
        {
            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (code == 0)
            {
                DrawSunOrMoon(g, r, isDay);
            }
            else if (code <= 2)
            {
                // Parcialmente nublado: astro arriba a la izquierda y nube delante
                DrawSunOrMoon(g, new RectangleF(r.X, r.Y, r.Width * 0.7f, r.Height * 0.7f), isDay);
                DrawCloud(g, new RectangleF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.35f, r.Width * 0.8f, r.Height * 0.55f), CloudColor);
            }
            else if (code == 45 || code == 48)
            {
                using (Pen fog = new Pen(DarkCloudColor, r.Height * 0.09f))
                {
                    fog.StartCap = fog.EndCap = LineCap.Round;
                    for (int i = 0; i < 3; i++)
                    {
                        float y = r.Y + r.Height * (0.3f + i * 0.2f);
                        float inset = i == 1 ? 0 : r.Width * 0.12f;
                        g.DrawLine(fog, r.X + inset, y, r.Right - inset, y);
                    }
                }
            }
            else
            {
                bool heavy = code >= 61;
                RectangleF cloud = new RectangleF(r.X, r.Y + r.Height * 0.05f, r.Width, r.Height * 0.6f);
                DrawCloud(g, cloud, code == 3 ? CloudColor : DarkCloudColor);

                if (code >= 95)
                {
                    DrawBolt(g, new RectangleF(r.X + r.Width * 0.38f, r.Y + r.Height * 0.5f, r.Width * 0.3f, r.Height * 0.5f));
                }
                else if ((code >= 71 && code <= 77) || code == 85 || code == 86)
                {
                    using (SolidBrush snow = new SolidBrush(CloudColor))
                    {
                        float d = r.Width * 0.11f;
                        for (int i = 0; i < 3; i++)
                        {
                            g.FillEllipse(snow, r.X + r.Width * (0.22f + i * 0.25f), r.Y + r.Height * (i == 1 ? 0.82f : 0.72f), d, d);
                        }
                    }
                }
                else if (code > 3)
                {
                    using (Pen rain = new Pen(RainColor, r.Width * (heavy ? 0.08f : 0.06f)))
                    {
                        rain.StartCap = rain.EndCap = LineCap.Round;
                        for (int i = 0; i < 3; i++)
                        {
                            float x = r.X + r.Width * (0.28f + i * 0.22f);
                            g.DrawLine(rain, x, r.Y + r.Height * 0.7f, x - r.Width * 0.08f, r.Bottom - r.Height * 0.02f);
                        }
                    }
                }
            }

            g.SmoothingMode = previous;
        }

        private static void DrawSunOrMoon(Graphics g, RectangleF r, bool isDay)
        {
            float size = Math.Min(r.Width, r.Height);
            float cx = r.X + size / 2, cy = r.Y + size / 2;

            if (isDay)
            {
                using (Pen rays = new Pen(SunColor, size * 0.08f))
                using (SolidBrush disc = new SolidBrush(SunColor))
                {
                    rays.StartCap = rays.EndCap = LineCap.Round;
                    for (int i = 0; i < 8; i++)
                    {
                        double angle = i * Math.PI / 4;
                        float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                        g.DrawLine(rays, cx + cos * size * 0.36f, cy + sin * size * 0.36f, cx + cos * size * 0.48f, cy + sin * size * 0.48f);
                    }
                    float d = size * 0.5f;
                    g.FillEllipse(disc, cx - d / 2, cy - d / 2, d, d);
                }
            }
            else
            {
                // Luna creciente: un círculo menos otro desplazado
                float d = size * 0.8f;
                using (GraphicsPath moon = new GraphicsPath())
                using (GraphicsPath bite = new GraphicsPath())
                using (SolidBrush brush = new SolidBrush(MoonColor))
                {
                    moon.AddEllipse(cx - d / 2, cy - d / 2, d, d);
                    bite.AddEllipse(cx - d / 2 + d * 0.35f, cy - d / 2 - d * 0.15f, d, d);
                    using (Region crescent = new Region(moon))
                    {
                        crescent.Exclude(bite);
                        g.FillRegion(brush, crescent);
                    }
                }
            }
        }

        private static void DrawCloud(Graphics g, RectangleF r, Color color)
        {
            using (GraphicsPath cloud = new GraphicsPath())
            using (SolidBrush brush = new SolidBrush(color))
            {
                cloud.FillMode = FillMode.Winding; // Une los círculos en una sola silueta
                cloud.AddEllipse(r.X, r.Y + r.Height * 0.35f, r.Width * 0.45f, r.Height * 0.65f);
                cloud.AddEllipse(r.X + r.Width * 0.2f, r.Y, r.Width * 0.55f, r.Height * 0.85f);
                cloud.AddEllipse(r.X + r.Width * 0.5f, r.Y + r.Height * 0.25f, r.Width * 0.5f, r.Height * 0.75f);
                cloud.AddRectangle(new RectangleF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.6f, r.Width * 0.6f, r.Height * 0.4f));
                g.FillPath(brush, cloud);
            }
        }

        private static void DrawBolt(Graphics g, RectangleF r)
        {
            PointF[] bolt =
            {
                new PointF(r.X + r.Width * 0.6f, r.Y),
                new PointF(r.X, r.Y + r.Height * 0.55f),
                new PointF(r.X + r.Width * 0.45f, r.Y + r.Height * 0.55f),
                new PointF(r.X + r.Width * 0.3f, r.Bottom),
                new PointF(r.Right, r.Y + r.Height * 0.4f),
                new PointF(r.X + r.Width * 0.55f, r.Y + r.Height * 0.4f)
            };
            using (SolidBrush brush = new SolidBrush(BoltColor))
            {
                g.FillPolygon(brush, bolt);
            }
        }
    }
}
