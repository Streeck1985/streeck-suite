using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Centro de control (fase J3, mockup interactivo): Wi-Fi, Bluetooth y No molestar en tres botones. El botón
    /// enciende o apaga y su flechita › abre la lista para elegir red o dispositivo. Debajo, volumen y micrófono
    /// (clic para fijar, rueda para subir o bajar). Es la primera vista del carrusel, como en Bloom.
    /// </summary>
    internal class ControlCenterView : IslandView
    {
        private enum Page { Main, Wifi, Bluetooth }

        private const float WheelStep = 0.05f;
        private const int MaxRows = 6;
        private const float RowHeight = 32;
        private static readonly Color Blue = Color.FromArgb(10, 132, 255);

        private readonly AudioService _audio;
        private Page _page = Page.Main;
        private RectangleF _wifiTile, _wifiChevron, _bluetoothTile, _bluetoothChevron, _quietTile, _micTile, _nightTile, _powerTile;
        private SystemTweaks.PowerMode _powerMode = SystemTweaks.CurrentPowerMode;
        private readonly RectangleF[] _toolButtons = new RectangleF[3];
        private RectangleF _volumeBar, _volumeRow, _micBar, _micRow, _micButton, _backButton;
        private readonly List<KeyValuePair<RectangleF, object>> _rows = new List<KeyValuePair<RectangleF, object>>();

        private List<SystemControls.PairedDevice> _paired = new List<SystemControls.PairedDevice>();
        private DateTime _pairedReadAt = DateTime.MinValue;
        private bool _pairedLoading;

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

        // Principal: botones 58 + 14 + volumen 26 + 10 + micrófono 26. Listas: cabecera 30 + 6 filas de 32
        public override SizeF BaseSize
        {
            get
            {
                if (_page == Page.Main) return new SizeF(380, (TrayIconsService.Apps.Count > 0 ? 182 : 134) + 66 + 38); // + segunda fila de botones y herramientas
                int rows = _page == Page.Wifi ? SystemControls.Networks.Count : _paired.Count;
                return new SizeF(380, 34 + Math.Max(1, Math.Min(MaxRows, rows)) * RowHeight);
            }
        }

        public override float PanelWidth
        {
            get { return 380; }
        }

        public override bool IsAnimated
        {
            get { return true; } // Radios, volumen y listas cambian por su cuenta
        }

        public override void OnHidden()
        {
            _page = Page.Main; // Al volver a abrir la isla, empieza en la página principal
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            _rows.Clear();
            if (_page == Page.Main) DrawMain(g, size, alpha, p);
            else DrawList(g, size, alpha, p);
        }

        #region Página principal

        private void DrawMain(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            float x = p.Px(PadX), width = size.Width - 2 * p.Px(PadX);
            float gap = p.Px(8), tileWidth = (width - 2 * gap) / 3, tileHeight = p.Px(58);

            bool? wifi = SystemControls.WifiOn, bluetooth = SystemControls.BluetoothOn;
            int devices = BluetoothService.ConnectedDevices.Count;
            string wifiState = wifi == null ? "No disponible" : !wifi.Value ? "Apagado"
                : SystemControls.OnEthernet ? "Usas cable" : (SystemControls.NetworkName ?? "Sin conexión");
            _wifiTile = new RectangleF(x, 0, tileWidth, tileHeight);
            _bluetoothTile = new RectangleF(_wifiTile.Right + gap, 0, tileWidth, tileHeight);
            _quietTile = new RectangleF(_bluetoothTile.Right + gap, 0, tileWidth, tileHeight);
            _wifiChevron = DrawTile(g, p, alpha, _wifiTile, "", "Wi-Fi", wifiState, wifi == true, wifi != null);
            _bluetoothChevron = DrawTile(g, p, alpha, _bluetoothTile, "", "Bluetooth",
                bluetooth == null ? "No disponible" : !bluetooth.Value ? "Apagado" : devices == 1 ? "1 dispositivo" : devices + " dispositivos", bluetooth == true, bluetooth != null);
            DrawTile(g, p, alpha, _quietTile, "", "No molestar", SuiteSettings.DoNotDisturb ? "Activado" : "Apagado", SuiteSettings.DoNotDisturb, false);

            // Segunda fila: micrófono (silenciar), luz nocturna y modo de energía
            float row2 = tileHeight + gap;
            _micTile = new RectangleF(x, row2, tileWidth, tileHeight);
            _nightTile = new RectangleF(_micTile.Right + gap, row2, tileWidth, tileHeight);
            _powerTile = new RectangleF(_nightTile.Right + gap, row2, tileWidth, tileHeight);
            bool hasMic = _audio != null && _audio.HasMic, micOff = hasMic && _audio.MicMuted;
            DrawTile(g, p, alpha, _micTile, "", "Micrófono", !hasMic ? "No hay" : micOff ? "Silenciado" : "Activo", micOff, false, micOff ? MutedRed : (Color?)null);
            DrawTile(g, p, alpha, _nightTile, "", "Luz nocturna", SystemTweaks.NightLight ? "Encendida" : "Apagada", SystemTweaks.NightLight, false, SystemTweaks.NightLight ? NightAmber : (Color?)null);
            DrawTile(g, p, alpha, _powerTile, "", "Energía", SystemTweaks.PowerModeName(_powerMode), _powerMode != SystemTweaks.PowerMode.Balanced, false);

            // Volumen
            float y = row2 + tileHeight + p.Px(14);
            float level = _audio != null ? _audio.Level : 0;
            bool muted = _audio != null && _audio.Muted;
            _volumeRow = new RectangleF(x, y, width, p.Px(26));
            _volumeBar = DrawSlider(g, p, alpha, _volumeRow, VolumeGlyphs.ForLevel(level, muted), muted ? 0 : level, muted ? "Silencio" : Percent(level));

            // Micrófono (en una PC de escritorio sirve más que el brillo): el icono lo silencia
            y += p.Px(36);
            _micRow = new RectangleF(x, y, width, p.Px(26));
            _micButton = new RectangleF(x - p.Px(4), y - p.Px(2), p.Px(30), p.Px(30));
            if (_audio != null && _audio.HasMic)
            {
                bool micMuted = _audio.MicMuted;
                float micLevel = _audio.MicLevel;
                if (micMuted) IslandPainter.FillRounded(g, _micButton, p.Px(8), IslandPainter.Fade(IslandPainter.OrangeBack, alpha));
                _micBar = DrawSlider(g, p, alpha, _micRow, micMuted ? "" : "", micMuted ? 0 : micLevel, micMuted ? "Silenciado" : Percent(micLevel));
            }
            else
            {
                _micBar = RectangleF.Empty;
                p.DrawText(g, "", p.Glyph16, IslandPainter.Fade(IslandPainter.Gray, alpha), new RectangleF(x, y, p.Px(22), _micRow.Height), StringAlignment.Center);
                p.DrawText(g, "No hay micrófono conectado", p.Text11, IslandPainter.Fade(IslandPainter.Gray, alpha),
                    new RectangleF(x + p.Px(30), y, width - p.Px(30), _micRow.Height), StringAlignment.Near);
            }

            // Herramientas: Claude, portapapeles y celular (antes solo con atajo)
            y += p.Px(36);
            string[] toolNames = { "Claude", "Portapapeles", "Celular" };
            string[] toolGlyphs = { "✳", "", "" };
            float toolGap = p.Px(8), toolW = (width - 2 * toolGap) / 3;
            for (int i = 0; i < 3; i++)
            {
                _toolButtons[i] = new RectangleF(x + i * (toolW + toolGap), y, toolW, p.Px(30));
                IslandPainter.FillRounded(g, _toolButtons[i], p.Px(10), Color.FromArgb(alpha * 22 / 255, IslandPainter.InkBase));
                p.DrawText(g, toolGlyphs[i], i == 0 ? p.Text13 : p.IconFont, Color.FromArgb(alpha, IslandPainter.InkBase),
                    new RectangleF(_toolButtons[i].X + p.Px(10), y, p.Px(18), _toolButtons[i].Height), StringAlignment.Center);
                p.DrawText(g, toolNames[i], p.Text12, Color.FromArgb(alpha, IslandPainter.InkBase),
                    new RectangleF(_toolButtons[i].X + p.Px(30), y, toolW - p.Px(34), _toolButtons[i].Height), StringAlignment.Near);
            }

            // Iconos de la bandeja de Windows (fase J3b): clic abre la app, clic derecho muestra su menú
            _trayHits.Clear();
            List<TrayIconsService.TrayApp> apps = TrayIconsService.Apps;
            if (apps.Count == 0) return;
            y += p.Px(36);
            using (Pen line = new Pen(Color.FromArgb(alpha * 20 / 255, IslandPainter.InkBase), 1)) g.DrawLine(line, x, y, x + width, y);
            y += p.Px(10);
            float icon = p.Px(30), iconGap = p.Px(6);
            int fit = Math.Max(1, (int)((width + iconGap) / (icon + iconGap)));
            for (int i = 0; i < Math.Min(fit, apps.Count); i++)
            {
                RectangleF cell = new RectangleF(x + i * (icon + iconGap), y, icon, icon);
                IslandPainter.FillRounded(g, cell, p.Px(9), Color.FromArgb(alpha * 22 / 255, IslandPainter.InkBase));
                if (apps[i].Icon != null) g.DrawImage(apps[i].Icon, RectangleF.Inflate(cell, -p.Px(6), -p.Px(6)));
                else p.DrawText(g, apps[i].Name.Substring(0, 1), p.Bold12, Color.FromArgb(alpha, IslandPainter.InkBase), cell, StringAlignment.Center);
                _trayHits.Add(new KeyValuePair<RectangleF, TrayIconsService.TrayApp>(cell, apps[i]));
            }
        }

        private readonly List<KeyValuePair<RectangleF, TrayIconsService.TrayApp>> _trayHits = new List<KeyValuePair<RectangleF, TrayIconsService.TrayApp>>();

        public override bool OnRightClick(PointF point)
        {
            if (_page != Page.Main) return false;
            foreach (KeyValuePair<RectangleF, TrayIconsService.TrayApp> hit in _trayHits)
            {
                if (hit.Key.Contains(point)) { TrayIconsService.Click(hit.Value, true); return true; }
            }
            return false;
        }

        // Botón con icono, nombre y estado; con flechita a la derecha si se puede elegir. Devuelve la zona de la flechita.
        private static readonly Color MutedRed = Color.FromArgb(220, 60, 75);
        private static readonly Color NightAmber = Color.FromArgb(214, 140, 40);

        private static RectangleF DrawTile(Graphics g, IslandPainter p, int alpha, RectangleF tile, string glyph, string name, string state, bool on, bool chevron, Color? onColor = null)
        {
            IslandPainter.FillRounded(g, tile, p.Px(14), on ? Color.FromArgb(alpha, onColor ?? Blue) : Color.FromArgb(alpha * 22 / 255, IslandPainter.InkBase));
            Color text = on ? Color.FromArgb(alpha, Color.White) : Color.FromArgb(alpha, IslandPainter.InkBase); // Sobre el botón encendido (azul), siempre blanco
            float textWidth = tile.Width - p.Px(14) - (chevron ? p.Px(22) : 0);
            p.DrawText(g, glyph, p.Glyph16, text, new RectangleF(tile.X + p.Px(10), tile.Y + p.Px(7), p.Px(20), p.Px(20)), StringAlignment.Near);
            p.DrawText(g, name, p.Bold12, text, new RectangleF(tile.X + p.Px(10), tile.Y + p.Px(26), textWidth, p.Px(16)), StringAlignment.Near);
            p.DrawText(g, state, p.Text11, (on ? Color.FromArgb(alpha * 210 / 255, Color.White) : Color.FromArgb(alpha * 150 / 255, IslandPainter.InkBase)),
                new RectangleF(tile.X + p.Px(10), tile.Y + p.Px(40), textWidth, p.Px(15)), StringAlignment.Near);
            if (!chevron) return RectangleF.Empty;
            RectangleF zone = new RectangleF(tile.Right - p.Px(26), tile.Y, p.Px(26), tile.Height);
            IslandPainter.FillRounded(g, RectangleF.Inflate(zone, -p.Px(3), -p.Px(8)), p.Px(8), Color.FromArgb(alpha * 40 / 255, Color.Black));
            p.DrawText(g, "", p.Glyph16, text, zone, StringAlignment.Center); // ›
            return zone;
        }

        // Icono, barra y porcentaje; devuelve la zona de la barra para los clics
        private static RectangleF DrawSlider(Graphics g, IslandPainter p, int alpha, RectangleF row, string glyph, float fraction, string value)
        {
            p.DrawText(g, glyph, p.Glyph16, Color.FromArgb(alpha, IslandPainter.InkBase), new RectangleF(row.X, row.Y, p.Px(22), row.Height), StringAlignment.Center);
            RectangleF bar = new RectangleF(row.X + p.Px(30), row.Y + (row.Height - p.Px(6)) / 2, row.Width - p.Px(30) - p.Px(70), p.Px(6));
            IslandPainter.DrawBar(g, bar, Math.Max(0, Math.Min(1, fraction)), Color.FromArgb(alpha * 38 / 255, IslandPainter.InkBase), Color.FromArgb(alpha, IslandPainter.InkBase));
            p.DrawText(g, value, p.Text12, IslandPainter.Fade(IslandPainter.Gray, alpha), new RectangleF(bar.Right + p.Px(6), row.Y, p.Px(64), row.Height), StringAlignment.Far);
            return new RectangleF(bar.X, row.Y, bar.Width, row.Height); // Zona de clic tan alta como la fila
        }

        #endregion

        #region Listas de redes y de dispositivos

        private void DrawList(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            float x = p.Px(PadX), width = size.Width - 2 * p.Px(PadX);
            Color white = Color.FromArgb(alpha, IslandPainter.InkBase), gray = IslandPainter.Fade(IslandPainter.Gray, alpha);
            bool wifi = _page == Page.Wifi;

            _backButton = new RectangleF(x - p.Px(4), 0, p.Px(28), p.Px(28));
            p.DrawText(g, "", p.Glyph16, white, _backButton, StringAlignment.Center); // ‹
            p.DrawText(g, wifi ? "Wi-Fi" : "Bluetooth", p.Bold15, white, new RectangleF(x + p.Px(28), 0, p.Px(140), p.Px(28)), StringAlignment.Near);
            string status = wifi ? (SystemControls.Scanning ? "Buscando redes…" : SystemControls.WifiProblem ?? SystemControls.Networks.Count + " redes")
                                 : (_pairedLoading && _paired.Count == 0 ? "Buscando…" : "Emparejados");
            p.DrawText(g, status, p.Text11, gray, new RectangleF(x + p.Px(150), 0, width - p.Px(150), p.Px(28)), StringAlignment.Far);

            float y = p.Px(34);
            if (wifi)
            {
                List<SystemControls.WifiNetwork> networks = SystemControls.Networks;
                for (int i = 0; i < Math.Min(MaxRows, networks.Count); i++, y += p.Px(RowHeight))
                {
                    SystemControls.WifiNetwork n = networks[i];
                    RectangleF row = DrawRow(g, p, alpha, x, y, width, n.Connected);
                    DrawSignal(g, p, alpha, new RectangleF(row.X + p.Px(10), row.Y, p.Px(16), row.Height), n.Bars);
                    p.DrawText(g, n.Name, n.Connected ? p.Semibold13 : p.Text13, white, new RectangleF(row.X + p.Px(34), row.Y, row.Width - p.Px(120), row.Height), StringAlignment.Near);
                    p.DrawText(g, n.Connected ? "Conectado" : "", p.Text11, gray, new RectangleF(row.Right - p.Px(90), row.Y, p.Px(80), row.Height), StringAlignment.Far);
                    if (n.Secured) p.DrawText(g, "", p.Glyph16, gray, new RectangleF(row.Right - (n.Connected ? p.Px(98) : p.Px(30)), row.Y, p.Px(20), row.Height), StringAlignment.Center);
                    _rows.Add(new KeyValuePair<RectangleF, object>(row, n));
                }
            }
            else
            {
                RefreshPaired(false);
                for (int i = 0; i < Math.Min(MaxRows, _paired.Count); i++, y += p.Px(RowHeight))
                {
                    SystemControls.PairedDevice d = _paired[i];
                    RectangleF row = DrawRow(g, p, alpha, x, y, width, d.Connected);
                    p.DrawText(g, d.IsAudio ? "" : "", p.Glyph16, white, new RectangleF(row.X + p.Px(8), row.Y, p.Px(20), row.Height), StringAlignment.Center);
                    p.DrawText(g, d.Name, d.Connected ? p.Semibold13 : p.Text13, white, new RectangleF(row.X + p.Px(34), row.Y, row.Width - p.Px(150), row.Height), StringAlignment.Near);
                    string right = d.Connected ? (d.Battery >= 0 ? d.Battery + " % · " : "") + "Conectado" : d.IsAudio ? "Conectar" : "Sin conexión";
                    p.DrawText(g, right, p.Text11, gray, new RectangleF(row.Right - p.Px(140), row.Y, p.Px(130), row.Height), StringAlignment.Far);
                    _rows.Add(new KeyValuePair<RectangleF, object>(row, d));
                }
                if (_paired.Count == 0 && !_pairedLoading)
                {
                    p.DrawText(g, "No hay dispositivos emparejados", p.Text12, gray, new RectangleF(x, y, width, p.Px(RowHeight)), StringAlignment.Center);
                }
            }
        }

        private static RectangleF DrawRow(Graphics g, IslandPainter p, int alpha, float x, float y, float width, bool current)
        {
            RectangleF row = new RectangleF(x, y, width, p.Px(RowHeight - 4));
            IslandPainter.FillRounded(g, row, p.Px(10), current ? Color.FromArgb(alpha * 90 / 255, Blue) : Color.FromArgb(alpha * 13 / 255, IslandPainter.InkBase));
            return row;
        }

        // Rayitas de señal (0..4)
        private static void DrawSignal(Graphics g, IslandPainter p, int alpha, RectangleF area, int bars)
        {
            float barWidth = p.Px(3), step = p.Px(4);
            for (int i = 0; i < 4; i++)
            {
                float height = p.Px(4 + i * 2.5f);
                RectangleF bar = new RectangleF(area.X + i * step, area.Y + area.Height / 2 + p.Px(6) - height, barWidth, height);
                IslandPainter.FillRounded(g, bar, p.Px(1), Color.FromArgb(alpha * (i < bars ? 255 : 60) / 255, IslandPainter.InkBase));
            }
        }

        private void RefreshPaired(bool force)
        {
            if (_pairedLoading || (!force && (DateTime.Now - _pairedReadAt).TotalSeconds < 3)) return;
            _pairedLoading = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { _paired = SystemControls.PairedDevices(); }
                catch (Exception ex) { Log.Error("[ControlCenter] No se pudieron leer los dispositivos Bluetooth: {0}", ex.Message); }
                _pairedReadAt = DateTime.Now;
                _pairedLoading = false;
            });
        }

        #endregion

        private static string Percent(float level)
        {
            return (int)Math.Round(level * 100) + " %";
        }

        public override bool OnClick(PointF point)
        {
            if (_page != Page.Main)
            {
                if (_backButton.Contains(point)) { _page = Page.Main; return true; }
                foreach (KeyValuePair<RectangleF, object> row in _rows)
                {
                    if (!row.Key.Contains(point)) continue;
                    SystemControls.WifiNetwork network = row.Value as SystemControls.WifiNetwork;
                    if (network != null) SystemControls.ConnectWifi(network);
                    SystemControls.PairedDevice device = row.Value as SystemControls.PairedDevice;
                    if (device != null && device.IsAudio)
                    {
                        SystemControls.ToggleAudioDevice(device);
                        device.Connected = !device.Connected; // Respuesta inmediata; la lista se vuelve a leer en 3 s
                    }
                    return true;
                }
                return true;
            }

            foreach (KeyValuePair<RectangleF, TrayIconsService.TrayApp> hit in _trayHits)
            {
                if (hit.Key.Contains(point)) { TrayIconsService.Click(hit.Value, false); return true; }
            }
            if (_wifiChevron.Contains(point)) { _page = Page.Wifi; SystemControls.ScanWifi(); return true; }
            if (_bluetoothChevron.Contains(point)) { _page = Page.Bluetooth; RefreshPaired(true); return true; }
            if (_wifiTile.Contains(point)) { SystemControls.ToggleWifi(); return true; }
            if (_bluetoothTile.Contains(point)) { SystemControls.ToggleBluetooth(); return true; }
            if (_quietTile.Contains(point)) { NotificationService.ToggleDoNotDisturb(); return true; }
            if (_toolButtons[0].Contains(point)) { IslandTools.Run(IslandTools.OpenClaude); return true; }
            if (_toolButtons[1].Contains(point)) { IslandTools.Run(IslandTools.OpenClipboard); return true; }
            if (_toolButtons[2].Contains(point)) { IslandTools.Run(IslandTools.OpenPhone); return true; }
            if (_micTile.Contains(point) && _audio != null && _audio.HasMic) { _audio.ToggleMicMute(); return true; }
            if (_nightTile.Contains(point)) { SystemTweaks.ToggleNightLight(); return true; }
            if (_powerTile.Contains(point)) { _powerMode = SystemTweaks.CyclePowerMode(); return true; }
            if (_volumeBar.Contains(point) && _audio != null) { _audio.SetLevel(Fraction(point, _volumeBar)); return true; }
            if (_micButton.Contains(point) && _audio != null) { _audio.ToggleMicMute(); return true; }
            if (_micBar.Contains(point) && _audio != null) { _audio.SetMicLevel(Fraction(point, _micBar)); return true; }
            return false;
        }

        public override bool OnWheel(PointF point, int delta)
        {
            if (_page != Page.Main) return true; // En las listas la rueda no cambia de vista
            float step = delta > 0 ? WheelStep : -WheelStep;
            if (_volumeRow.Contains(point) && _audio != null) { _audio.SetLevel(Math.Max(0, Math.Min(1, _audio.Level + step))); return true; }
            if (_micRow.Contains(point) && _audio != null && _audio.HasMic) { _audio.SetMicLevel(Math.Max(0, Math.Min(1, _audio.MicLevel + step))); return true; }
            return false;
        }

        private static float Fraction(PointF point, RectangleF bar)
        {
            return Math.Max(0, Math.Min(1, (point.X - bar.X) / bar.Width));
        }
    }
}
