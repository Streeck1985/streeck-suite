using System;
using System.Collections.Generic;
using System.Drawing;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Iconos de Segoe Fluent Icons para el audio.
    /// </summary>
    internal static class VolumeGlyphs
    {
        public const string Muted = "";
        public const string Speakers = "";
        public const string Headphones = "";
        public const string Check = "";

        public static string ForLevel(float level, bool muted)
        {
            if (muted || level <= 0.001f) return Muted;
            if (level < 0.34f) return "";
            if (level < 0.67f) return "";
            return "";
        }

        // Los auriculares se reconocen por el nombre que les da Windows
        public static string ForDevice(string name)
        {
            string lower = (name ?? "").ToLowerInvariant();
            return lower.Contains("auricular") || lower.Contains("headphone") || lower.Contains("headset") || lower.Contains("buds")
                ? Headphones : Speakers;
        }
    }

    /// <summary>
    /// Aviso al cambiar el volumen (tablero 4): número grande; en silencio, borde e icono naranjas y la palabra «Silenciado».
    /// </summary>
    internal class VolumeView : IslandView
    {
        public float Level;
        public bool Muted;
        public string DeviceName = "";

        public override SizeF BaseSize
        {
            get { return new SizeF(360, 62); }
        }

        public override Color? BorderColor
        {
            get { return Muted ? Color.FromArgb(128, 251, 146, 60) : (Color?)null; }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            float x = p.Px(PadX);
            Color white = Color.FromArgb(alpha, Color.White);
            Color gray = IslandPainter.Fade(IslandPainter.Gray, alpha);

            if (Muted)
            {
                RectangleF badge = new RectangleF(x, (size.Height - p.Px(34)) / 2, p.Px(34), p.Px(34));
                IslandPainter.FillRounded(g, badge, p.Px(10), IslandPainter.Fade(IslandPainter.OrangeBack, alpha));
                p.DrawText(g, VolumeGlyphs.Muted, p.Glyph16, IslandPainter.Fade(IslandPainter.Orange, alpha), badge, StringAlignment.Center);
                float textX = badge.Right + p.Px(14);
                p.DrawText(g, "Silenciado", p.Bold15, IslandPainter.Fade(IslandPainter.Orange, alpha),
                    new RectangleF(textX, p.Px(10), size.Width - textX - x, p.Px(22)), StringAlignment.Near);
                p.DrawText(g, DeviceName, p.Text12, gray, new RectangleF(textX, p.Px(32), size.Width - textX - x, p.Px(18)), StringAlignment.Near);
                return;
            }

            RectangleF icon = new RectangleF(x, 0, p.Px(26), size.Height);
            p.DrawText(g, VolumeGlyphs.ForLevel(Level, false), p.Glyph20, white, icon, StringAlignment.Center);

            float numberWidth = p.Px(48);
            float barX = icon.Right + p.Px(14);
            float barWidth = size.Width - x - numberWidth - p.Px(10) - barX;
            p.DrawText(g, DeviceName, p.Text12, gray, new RectangleF(barX, p.Px(12), barWidth, p.Px(18)), StringAlignment.Near);
            IslandSkin.DrawLevelBar(g, new RectangleF(barX, p.Px(35), barWidth, p.Px(6)), Level, alpha); // Con el color del skin
            p.DrawText(g, string.Format("{0:0}", Level * 100), p.Bold18, white,
                new RectangleF(size.Width - x - numberWidth, 0, numberWidth, size.Height), StringAlignment.Far);
        }
    }

    /// <summary>
    /// Sonido (tablero 3): lista de salidas (clic para elegir, la actual con ✓), volumen general con botón de silencio
    /// (naranja y «Silenciado» cuando está mudo) y volumen de cada app con su icono real.
    /// Clic en una barra fija el volumen; la rueda sobre una barra lo sube o baja de 5 en 5.
    /// </summary>
    internal class AudioView : IslandView
    {
        private const int MaxApps = 4;
        private const float WheelStep = 0.05f;

        private static readonly Color[] TileColors =
        {
            Color.FromArgb(37, 99, 235), Color.FromArgb(29, 185, 84), Color.FromArgb(234, 88, 12),
            Color.FromArgb(168, 85, 247), Color.FromArgb(14, 165, 233), Color.FromArgb(219, 39, 119)
        };

        private readonly AudioService _audio;
        private readonly Dictionary<string, Bitmap> _appIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private List<AudioService.AppVolume> _apps = new List<AudioService.AppVolume>();
        private List<AudioService.Device> _devices = new List<AudioService.Device>();
        private DateTime _refreshedAt = DateTime.MinValue;

        // Zonas interactivas calculadas al dibujar (coordenadas de la vista)
        private readonly List<RectangleF> _deviceRows = new List<RectangleF>();
        private RectangleF _muteButton, _masterRow;
        private readonly List<RectangleF> _appIconRects = new List<RectangleF>();
        private readonly List<RectangleF> _appRows = new List<RectangleF>();
        private readonly List<RectangleF> _appBars = new List<RectangleF>();
        private RectangleF _masterBar;

        public AudioView(AudioService audio)
        {
            _audio = audio;
        }

        public override string TabName
        {
            get { return "Sonido"; }
        }

        public override string TabGlyph
        {
            get { return ""; }
        }

        // Altura según cuántas salidas y apps hay (en px a 96 PPP, como en el tablero 3)
        public override SizeF BaseSize
        {
            get
            {
                int devices = Math.Max(1, _devices.Count);
                int apps = Math.Min(_apps.Count, MaxApps);
                float height = 20 + devices * 40 - 4 + 10 + 1 + 14 + 40;
                if (apps > 0) height += 14 + 16 + 10 + apps * 28 + (apps - 1) * 10;
                return new SizeF(440, height);
            }
        }

        public override bool IsAnimated
        {
            get { return true; } // Niveles en vivo (otra app puede cambiar su volumen)
        }

        public override void OnShown()
        {
            Refresh(true);
        }

        private void Refresh(bool force)
        {
            if (!force && (DateTime.Now - _refreshedAt).TotalMilliseconds < 1000) return;
            _refreshedAt = DateTime.Now;
            try
            {
                _apps = _audio.GetAppVolumes();
                _devices = _audio.GetOutputDevices();
            }
            catch (Exception ex)
            {
                Log.Error("[DynamicNotchModule] No se pudo leer el audio: {0}", ex.Message);
            }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            Refresh(false);
            float x = p.Px(PadX), width = size.Width - 2 * p.Px(PadX);
            Color white = Color.FromArgb(alpha, Color.White);
            Color gray = IslandPainter.Fade(IslandPainter.Gray, alpha);
            Color label = IslandPainter.Fade(Color.FromArgb(139, 147, 163), alpha);
            float y = 0;

            // SALIDA: una fila por dispositivo
            p.DrawText(g, "SALIDA", p.Label11, label, new RectangleF(x + p.Px(4), y, width, p.Px(16)), StringAlignment.Near);
            y += p.Px(20);
            _deviceRows.Clear();
            foreach (AudioService.Device device in _devices)
            {
                RectangleF row = new RectangleF(x, y, width, p.Px(36));
                if (device.IsDefault) IslandPainter.FillRounded(g, row, p.Px(10), Color.FromArgb(alpha * 36 / 255, IslandPainter.Sky));
                p.DrawText(g, VolumeGlyphs.ForDevice(device.Name), p.Glyph16, device.IsDefault ? white : IslandPainter.Fade(IslandPainter.Secondary, alpha),
                    new RectangleF(row.X + p.Px(10), row.Y, p.Px(20), row.Height), StringAlignment.Center);
                p.DrawText(g, device.Name, device.IsDefault ? p.Semibold13 : p.Text13, device.IsDefault ? white : IslandPainter.Fade(IslandPainter.Secondary, alpha),
                    new RectangleF(row.X + p.Px(40), row.Y, row.Width - p.Px(76), row.Height), StringAlignment.Near);
                if (device.IsDefault)
                {
                    p.DrawText(g, VolumeGlyphs.Check, p.Glyph16, IslandPainter.Fade(Color.FromArgb(125, 211, 252), alpha),
                        new RectangleF(row.Right - p.Px(32), row.Y, p.Px(22), row.Height), StringAlignment.Center);
                }
                _deviceRows.Add(row);
                y += p.Px(40);
            }
            if (_devices.Count == 0) y += p.Px(40);
            y += p.Px(6);

            // Separador
            IslandPainter.FillRounded(g, new RectangleF(x, y, width, Math.Max(1, p.Px(1))), 0, Color.FromArgb(alpha * 20 / 255, Color.White));
            y += p.Px(15);

            // Volumen general
            float level = _audio.Level;
            bool muted = _audio.Muted;
            _muteButton = new RectangleF(x, y, p.Px(40), p.Px(40));
            _masterRow = new RectangleF(x, y, width, p.Px(40));
            IslandPainter.FillRounded(g, _muteButton, p.Px(12), muted ? IslandPainter.Fade(IslandPainter.OrangeBack, alpha) : Color.FromArgb(alpha * 20 / 255, Color.White));
            p.DrawText(g, VolumeGlyphs.ForLevel(level, muted), p.Glyph16, muted ? IslandPainter.Fade(IslandPainter.Orange, alpha) : white, _muteButton, StringAlignment.Center);

            float masterX = _muteButton.Right + p.Px(12);
            float masterWidth = x + width - masterX;
            p.DrawText(g, "Volumen general", p.Semibold13, white, new RectangleF(masterX, y + p.Px(2), masterWidth, p.Px(18)), StringAlignment.Near);
            p.DrawText(g, muted ? "Silenciado" : string.Format("{0:0} %", level * 100), p.Bold12, muted ? IslandPainter.Fade(IslandPainter.Orange, alpha) : white,
                new RectangleF(masterX, y + p.Px(2), masterWidth, p.Px(18)), StringAlignment.Far);
            _masterBar = new RectangleF(masterX, y + p.Px(28), masterWidth, p.Px(6));
            IslandPainter.DrawBar(g, _masterBar, level, IslandPainter.Fade(IslandPainter.Track, alpha),
                muted ? IslandPainter.Fade(IslandPainter.MutedFill, alpha) : IslandPainter.Fade(IslandPainter.Sky, alpha));
            y += p.Px(40);

            // APPS
            _appIconRects.Clear();
            _appRows.Clear();
            _appBars.Clear();
            int apps = Math.Min(_apps.Count, MaxApps);
            if (apps == 0) return;
            y += p.Px(14);
            p.DrawText(g, "APPS", p.Label11, label, new RectangleF(x + p.Px(4), y, width, p.Px(16)), StringAlignment.Near);
            y += p.Px(26);
            for (int i = 0; i < apps; i++)
            {
                AudioService.AppVolume app = _apps[i];
                RectangleF iconRect = new RectangleF(x, y, p.Px(28), p.Px(28));
                DrawAppIcon(g, p, app, iconRect, alpha);

                RectangleF nameRect = new RectangleF(iconRect.Right + p.Px(12), y, p.Px(96), p.Px(28));
                using (Font nameFont = app.Muted ? new Font(p.Text13, FontStyle.Strikeout) : null)
                {
                    p.DrawText(g, app.Name, nameFont ?? p.Text13, app.Muted ? IslandPainter.Fade(Color.FromArgb(139, 147, 163), alpha) : white, nameRect, StringAlignment.Near);
                }

                float valueWidth = p.Px(40);
                RectangleF bar = new RectangleF(nameRect.Right + p.Px(12), y + p.Px(11.5f), x + width - valueWidth - p.Px(12) - nameRect.Right - p.Px(12), p.Px(5));
                IslandPainter.DrawBar(g, bar, app.Level, IslandPainter.Fade(IslandPainter.Track, alpha),
                    app.Muted ? IslandPainter.Fade(IslandPainter.MutedFill, alpha) : IslandPainter.Fade(Color.FromArgb(167, 139, 250), alpha));
                p.DrawText(g, app.Muted ? "Mudo" : string.Format("{0:0} %", app.Level * 100), p.Text12,
                    app.Muted ? IslandPainter.Fade(IslandPainter.Orange, alpha) : gray,
                    new RectangleF(x + width - valueWidth, y, valueWidth, p.Px(28)), StringAlignment.Far);

                _appIconRects.Add(RectangleF.Union(iconRect, nameRect));
                _appRows.Add(new RectangleF(x, y, width, p.Px(28)));
                _appBars.Add(new RectangleF(bar.X, y, bar.Width, p.Px(28)));
                y += p.Px(38);
            }
        }

        /// <summary>
        /// Icono real del ejecutable de la app; si no se puede leer (apps de la Store, procesos protegidos),
        /// un cuadrado de color con su inicial.
        /// </summary>
        private void DrawAppIcon(Graphics g, IslandPainter p, AudioService.AppVolume app, RectangleF rect, int alpha)
        {
            Bitmap icon;
            if (!_appIcons.TryGetValue(app.Name, out icon))
            {
                icon = null;
                try
                {
                    if (!string.IsNullOrEmpty(app.ExePath))
                    {
                        using (Icon extracted = Icon.ExtractAssociatedIcon(app.ExePath))
                        {
                            if (extracted != null) icon = extracted.ToBitmap();
                        }
                    }
                }
                catch (Exception)
                {
                    icon = null;
                }
                _appIcons[app.Name] = icon;
            }

            if (icon != null)
            {
                p.DrawRoundedImage(g, icon, rect, p.Px(8), alpha);
                return;
            }
            Color tile = TileColors[Math.Abs(app.Name.GetHashCode()) % TileColors.Length];
            IslandPainter.FillRounded(g, rect, p.Px(8), Color.FromArgb(alpha, tile));
            p.DrawText(g, app.Name.Substring(0, 1).ToUpperInvariant(), p.Bold12, Color.FromArgb(alpha, Color.White), rect, StringAlignment.Center);
        }

        public override bool OnClick(PointF point)
        {
            for (int i = 0; i < _deviceRows.Count && i < _devices.Count; i++)
            {
                if (_deviceRows[i].Contains(point))
                {
                    if (!_devices[i].IsDefault) _audio.SetDefaultDevice(_devices[i].Id);
                    Refresh(true);
                    return true;
                }
            }
            if (_muteButton.Contains(point)) { _audio.ToggleMute(); return true; }
            if (_masterRow.Contains(point) && point.X >= _masterBar.X)
            {
                _audio.SetLevel(LevelAt(_masterBar, point));
                return true;
            }

            for (int i = 0; i < _appRows.Count && i < _apps.Count; i++)
            {
                if (_appBars[i].Contains(point)) { _audio.SetAppLevel(_apps[i], LevelAt(_appBars[i], point)); Refresh(true); return true; }
                if (_appIconRects[i].Contains(point)) { _audio.ToggleAppMute(_apps[i]); Refresh(true); return true; }
            }
            return false;
        }

        public override bool OnWheel(PointF point, int delta)
        {
            float step = delta > 0 ? WheelStep : -WheelStep;
            if (_masterRow.Contains(point))
            {
                _audio.SetLevel(_audio.Level + step);
                return true;
            }
            for (int i = 0; i < _appRows.Count && i < _apps.Count; i++)
            {
                if (_appRows[i].Contains(point))
                {
                    _audio.SetAppLevel(_apps[i], _apps[i].Level + step);
                    Refresh(true);
                    return true;
                }
            }
            return false;
        }

        private static float LevelAt(RectangleF bar, PointF point)
        {
            return Math.Max(0f, Math.Min(1f, (point.X - bar.X) / bar.Width));
        }
    }
}
