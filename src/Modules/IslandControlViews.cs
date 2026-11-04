using System;
using System.Drawing;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Centro de control (fase J3, mockup N2, como Bloom): Wi-Fi, Bluetooth y No molestar en tres botones, y el volumen
    /// y el brillo en barras (clic para fijar, rueda para subir o bajar). Es la primera vista del carrusel.
    /// </summary>
    internal class ControlCenterView : IslandView
    {
        private const float WheelStep = 0.05f;
        private static readonly Color Blue = Color.FromArgb(10, 132, 255);

        private readonly AudioService _audio;
        private RectangleF _wifiTile, _bluetoothTile, _quietTile, _volumeBar, _volumeRow, _brightnessBar, _brightnessRow;

        public ControlCenterView(AudioService audio)
        {
            _audio = audio;
        }

        public override string TabName
        {
            get { return "Control"; }
        }

        public override string TabGlyph
        {
            get { return ""; }
        }

        // Botones 58 + 14 + volumen 26 + 10 + brillo 26
        public override SizeF BaseSize
        {
            get { return new SizeF(380, 134); }
        }

        public override float PanelWidth
        {
            get { return 380; }
        }

        public override bool IsAnimated
        {
            get { return true; } // El estado de las radios y el volumen pueden cambiar desde otro sitio
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            float x = p.Px(PadX), width = size.Width - 2 * p.Px(PadX);
            float gap = p.Px(8), tileWidth = (width - 2 * gap) / 3, tileHeight = p.Px(58);

            bool? wifi = SystemControls.WifiOn, bluetooth = SystemControls.BluetoothOn;
            int devices = BluetoothService.ConnectedDevices.Count;
            _wifiTile = new RectangleF(x, 0, tileWidth, tileHeight);
            _bluetoothTile = new RectangleF(_wifiTile.Right + gap, 0, tileWidth, tileHeight);
            _quietTile = new RectangleF(_bluetoothTile.Right + gap, 0, tileWidth, tileHeight);
            DrawTile(g, p, alpha, _wifiTile, "", "Wi-Fi", wifi == null ? "No disponible" : wifi.Value ? (SystemControls.NetworkName ?? "Sin conexión") : "Apagado", wifi == true);
            DrawTile(g, p, alpha, _bluetoothTile, "", "Bluetooth", bluetooth == null ? "No disponible" : !bluetooth.Value ? "Apagado" : devices == 1 ? "1 dispositivo" : devices + " dispositivos", bluetooth == true);
            DrawTile(g, p, alpha, _quietTile, "", "No molestar", SuiteSettings.DoNotDisturb ? "Activado" : "Apagado", SuiteSettings.DoNotDisturb);

            // Volumen
            float y = tileHeight + p.Px(14);
            float level = _audio != null ? _audio.Level : 0;
            bool muted = _audio != null && _audio.Muted;
            _volumeRow = new RectangleF(x, y, width, p.Px(26));
            _volumeBar = DrawSlider(g, p, alpha, _volumeRow, VolumeGlyphs.ForLevel(level, muted), muted ? 0 : level, muted ? "Silencio" : Percent(level));

            // Brillo: por DDC/CI; muchos monitores lo traen apagado en su menú
            y += p.Px(36);
            _brightnessRow = new RectangleF(x, y, width, p.Px(26));
            if (SystemControls.BrightnessSupported)
            {
                int brightness = SystemControls.Brightness;
                _brightnessBar = DrawSlider(g, p, alpha, _brightnessRow, "", brightness / 100f, brightness + " %");
            }
            else
            {
                _brightnessBar = RectangleF.Empty;
                string text = SystemControls.BrightnessLoaded ? "Tu monitor no deja cambiar el brillo (activa DDC/CI en su menú)" : "Leyendo el brillo…";
                p.DrawText(g, "", p.Glyph16, IslandPainter.Fade(IslandPainter.Gray, alpha), new RectangleF(x, y, p.Px(22), _brightnessRow.Height), StringAlignment.Center);
                p.DrawText(g, text, p.Text11, IslandPainter.Fade(IslandPainter.Gray, alpha), new RectangleF(x + p.Px(30), y, width - p.Px(30), _brightnessRow.Height), StringAlignment.Near);
            }
        }

        private static void DrawTile(Graphics g, IslandPainter p, int alpha, RectangleF tile, string glyph, string name, string state, bool on)
        {
            IslandPainter.FillRounded(g, tile, p.Px(14), on ? Color.FromArgb(alpha, Blue) : Color.FromArgb(alpha * 22 / 255, Color.White));
            Color text = Color.FromArgb(alpha, Color.White);
            p.DrawText(g, glyph, p.Glyph16, text, new RectangleF(tile.X + p.Px(10), tile.Y + p.Px(7), p.Px(20), p.Px(20)), StringAlignment.Near);
            p.DrawText(g, name, p.Bold12, text, new RectangleF(tile.X + p.Px(10), tile.Y + p.Px(26), tile.Width - p.Px(14), p.Px(16)), StringAlignment.Near);
            p.DrawText(g, state, p.Text11, Color.FromArgb(alpha * (on ? 210 : 150) / 255, Color.White),
                new RectangleF(tile.X + p.Px(10), tile.Y + p.Px(40), tile.Width - p.Px(14), p.Px(15)), StringAlignment.Near);
        }

        // Icono, barra y porcentaje; devuelve la zona de la barra para los clics
        private static RectangleF DrawSlider(Graphics g, IslandPainter p, int alpha, RectangleF row, string glyph, float fraction, string value)
        {
            p.DrawText(g, glyph, p.Glyph16, Color.FromArgb(alpha, Color.White), new RectangleF(row.X, row.Y, p.Px(22), row.Height), StringAlignment.Center);
            RectangleF bar = new RectangleF(row.X + p.Px(30), row.Y + (row.Height - p.Px(6)) / 2, row.Width - p.Px(30) - p.Px(52), p.Px(6));
            IslandPainter.DrawBar(g, bar, Math.Max(0, Math.Min(1, fraction)), Color.FromArgb(alpha * 38 / 255, Color.White), Color.FromArgb(alpha, Color.White));
            p.DrawText(g, value, p.Text12, IslandPainter.Fade(IslandPainter.Gray, alpha), new RectangleF(bar.Right + p.Px(8), row.Y, p.Px(46), row.Height), StringAlignment.Far);
            return new RectangleF(bar.X, row.Y, bar.Width, row.Height); // Zona de clic tan alta como la fila
        }

        private static string Percent(float level)
        {
            return (int)Math.Round(level * 100) + " %";
        }

        public override bool OnClick(PointF point)
        {
            if (_wifiTile.Contains(point)) { SystemControls.ToggleWifi(); return true; }
            if (_bluetoothTile.Contains(point)) { SystemControls.ToggleBluetooth(); return true; }
            if (_quietTile.Contains(point)) { SuiteSettings.DoNotDisturb = !SuiteSettings.DoNotDisturb; SuiteSettings.Save(); return true; }
            if (_volumeBar.Contains(point) && _audio != null) { _audio.SetLevel(Fraction(point, _volumeBar)); return true; }
            if (_brightnessBar.Contains(point)) { SystemControls.SetBrightness((int)Math.Round(Fraction(point, _brightnessBar) * 100)); return true; }
            return false;
        }

        public override bool OnWheel(PointF point, int delta)
        {
            float step = delta > 0 ? WheelStep : -WheelStep;
            if (_volumeRow.Contains(point) && _audio != null) { _audio.SetLevel(Math.Max(0, Math.Min(1, _audio.Level + step))); return true; }
            if (_brightnessRow.Contains(point) && SystemControls.BrightnessSupported)
            {
                SystemControls.SetBrightness(SystemControls.Brightness + (delta > 0 ? 5 : -5));
                return true;
            }
            return false;
        }

        private static float Fraction(PointF point, RectangleF bar)
        {
            return Math.Max(0, Math.Min(1, (point.X - bar.X) / bar.Width));
        }
    }
}
