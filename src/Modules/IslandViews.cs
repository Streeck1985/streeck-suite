using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Una "pantalla" de la isla: su tamaño, cómo se dibuja y qué hace con un clic o la rueda.
    /// Las vistas con pestaña (Música, Sonido, Clima, Sistema) forman el panel expandido; la isla dibuja la barra
    /// de pestañas encima y les pasa solo su zona de contenido. Diseño: mockups aprobados (Isla dinámica: propuestas).
    /// </summary>
    internal abstract class IslandView
    {
        /// <summary>
        /// Nombre de la pestaña, o null si la vista no va en el panel (isla compacta, avisos).
        /// </summary>
        public virtual string TabName
        {
            get { return null; }
        }

        /// <summary>
        /// Icono de la pestaña (glifo de Segoe Fluent Icons): la barra muestra iconos y solo la activa lleva nombre.
        /// </summary>
        public virtual string TabGlyph
        {
            get { return ""; }
        }

        /// <summary>
        /// Si la vista tiene algo que mostrar ahora (p. ej. la de música solo si hay algo sonando o en pausa).
        /// </summary>
        public virtual bool IsAvailable
        {
            get { return true; }
        }

        /// <summary>
        /// Tamaño en píxeles a 96 PPP: el de la isla entera (compacta, avisos) o el de la zona de contenido (pestañas).
        /// </summary>
        public abstract SizeF BaseSize { get; }

        /// <summary>
        /// Ancho del panel abierto con esta vista, a 96 PPP (la de música se ensancha al mostrar la letra).
        /// </summary>
        public virtual float PanelWidth
        {
            get { return 440; }
        }

        /// <summary>
        /// Borde de la isla mientras se ve esta vista (null = el gris normal).
        /// </summary>
        public virtual Color? BorderColor
        {
            get { return null; }
        }

        /// <summary>
        /// Si tiene algo en movimiento y hay que redibujarla unas 10 veces por segundo.
        /// </summary>
        public virtual bool IsAnimated
        {
            get { return false; }
        }

        public abstract void Draw(Graphics g, SizeF size, int alpha, IslandPainter p);

        /// <summary>
        /// Clic en coordenadas de la vista. Devuelve true si la vista lo usó.
        /// </summary>
        public virtual bool OnClick(PointF point)
        {
            return false;
        }

        /// <summary>
        /// Rueda sobre la vista. Devuelve true si la usó; si no, la rueda cambia de pestaña.
        /// </summary>
        public virtual bool OnWheel(PointF point, int delta)
        {
            return false;
        }

        public virtual void OnShown() { }
        public virtual void OnHidden() { }

        // Margen lateral del contenido del panel (18 px en los mockups)
        protected const float PadX = 18;
    }

    /// <summary>
    /// Isla compacta (reposo): "cámara", hora, punto de estado (o ecualizador si suena música), red y batería.
    /// </summary>
    internal class CompactView : IslandView
    {
        // Red y batería se consultan cada pocos segundos, no en cada fotograma (enumerar las interfaces de red es caro)
        private string _networkGlyph = IslandPainter.GlyphOffline;
        private string _batteryText;   // null si el equipo no tiene batería
        private DateTime _statusRefreshedAt = DateTime.MinValue;

        public override SizeF BaseSize
        {
            get { return new SizeF(220, 38); }
        }

        public override bool IsAnimated
        {
            get { return true; } // Punto pulsante o ecualizador
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            if ((DateTime.Now - _statusRefreshedAt).TotalSeconds >= 5) RefreshSystemStatus();

            float midY = size.Height / 2f;
            float x = p.Px(16);

            float camera = p.Px(9);
            using (SolidBrush lens = new SolidBrush(Color.FromArgb(alpha, 15, 23, 42)))
            using (Pen ring = new Pen(Color.FromArgb(alpha * 160 / 255, IslandPainter.Sky), p.Px(1.2f)))
            {
                g.FillEllipse(lens, x, midY - camera / 2, camera, camera);
                g.DrawEllipse(ring, x, midY - camera / 2, camera, camera);
            }

            p.DrawText(g, DateTime.Now.ToString("HH:mm"), p.TimeFont, IslandPainter.Fade(IslandPainter.Secondary, alpha),
                new RectangleF(x + camera + p.Px(8), 0, p.Px(60), size.Height), StringAlignment.Near);

            MediaInfo media = MediaService.Current;
            if (TimerService.IsActive)
            {
                // Tiempo en marcha (mockup D1): mini anillo y cuenta atrás en el centro
                string remaining = TimerService.Format(TimerService.Remaining);
                float textWidth = g.MeasureString(remaining, p.TimeFont).Width;
                float ringSize = p.Px(16);
                float left = size.Width / 2f - (ringSize + p.Px(6) + textWidth) / 2;
                p.DrawRing(g, new RectangleF(left, midY - ringSize / 2, ringSize, ringSize), TimerService.Progress, TimeColors.Current, alpha, p.Px(3));
                p.DrawText(g, remaining, p.TimeFont, IslandPainter.Fade(TimeColors.CurrentLight, alpha),
                    new RectangleF(left + ringSize + p.Px(6), 0, textWidth + p.Px(4), size.Height), StringAlignment.Near);
            }
            else if (media.IsPlaying)
            {
                p.DrawBars(g, new RectangleF(size.Width / 2f - p.Px(14), midY - p.Px(8), p.Px(28), p.Px(16)), alpha, media.Accent, true);
            }
            else
            {
                // Punto de estado pulsante (ciclo de 2 s)
                int pulse = (int)(alpha * (0.4 + 0.6 * (0.5 + 0.5 * Math.Sin(p.Seconds * Math.PI))));
                float orb = p.Px(7);
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(pulse / 4, IslandPainter.Sky)))
                using (SolidBrush dot = new SolidBrush(Color.FromArgb(pulse, IslandPainter.Sky)))
                {
                    g.FillEllipse(glow, size.Width / 2f - orb, midY - orb, orb * 2, orb * 2);
                    g.FillEllipse(dot, size.Width / 2f - orb / 2, midY - orb / 2, orb, orb);
                }
            }

            Color status = IslandPainter.Fade(IslandPainter.Secondary, alpha);
            float right = size.Width - p.Px(16);
            if (_batteryText != null)
            {
                p.DrawText(g, _batteryText, p.SmallFont, status, new RectangleF(right - p.Px(30), 0, p.Px(30), size.Height), StringAlignment.Far);
                right -= p.Px(32);
                p.DrawText(g, IslandPainter.GlyphBattery, p.IconFont, status, new RectangleF(right - p.Px(16), 0, p.Px(16), size.Height), StringAlignment.Far);
                right -= p.Px(22);
            }
            p.DrawText(g, _networkGlyph, p.IconFont, status, new RectangleF(right - p.Px(16), 0, p.Px(16), size.Height), StringAlignment.Far);
            right -= p.Px(24);

            // Puntos de micrófono (naranja) y cámara (verde) mientras alguna app los usa (mockup C2)
            if (!SuiteSettings.CaptureDots) return;
            float dotSize = p.Px(7);
            Color[] dots =
            {
                CaptureMonitor.CameraApps.Count > 0 ? Color.FromArgb(34, 197, 94) : Color.Empty,
                CaptureMonitor.MicrophoneApps.Count > 0 ? Color.FromArgb(251, 146, 60) : Color.Empty
            };
            foreach (Color color in dots)
            {
                if (color.IsEmpty) continue;
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(alpha / 4, color)))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(alpha, color)))
                {
                    g.FillEllipse(glow, right - dotSize * 1.5f, midY - dotSize, dotSize * 2, dotSize * 2);
                    g.FillEllipse(fill, right - dotSize, midY - dotSize / 2, dotSize, dotSize);
                }
                right -= dotSize + p.Px(6);
            }
        }

        private void RefreshSystemStatus()
        {
            _statusRefreshedAt = DateTime.Now;
            _networkGlyph = NetworkGlyph();

            PowerStatus power = SystemInformation.PowerStatus;
            bool hasBattery = (power.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0;
            _batteryText = hasBattery ? string.Format("{0:0}%", power.BatteryLifePercent * 100) : null;
        }

        private static string NetworkGlyph()
        {
            try
            {
                NetworkInterface active = NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    n.GetIPProperties().GatewayAddresses.Count > 0);
                if (active == null) return IslandPainter.GlyphOffline;
                return active.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? IslandPainter.GlyphWifi : IslandPainter.GlyphEthernet;
            }
            catch (NetworkInformationException)
            {
                return IslandPainter.GlyphOffline;
            }
        }
    }

    /// <summary>
    /// Música (tablero 2): carátula, título, artista, etiqueta de la app, ecualizador, progreso con tiempos
    /// (clic para saltar) y controles grandes centrados. Borde y colores con el tono de la carátula.
    /// </summary>
    internal class MusicView : IslandView
    {
        private RectangleF _prevButton, _playButton, _nextButton, _progressBar, _lyricsButton;
        private bool? _optimisticPlaying; // Respuesta inmediata al pulsar ⏯ hasta que el reproductor lo confirme
        private DateTime _optimisticUntil;

        // Letra sincronizada (fase J2): la isla se parte en dos; la línea cantada se ilumina y un clic salta a ella
        private const float PlayerWidth = 440, LyricsWidth = 260, LineHeight = 30;
        private bool _showLyrics;
        private float _lyricsScroll = -1;  // Desplazamiento suave de la lista (en líneas)
        private readonly List<KeyValuePair<RectangleF, TimeSpan>> _lineHits = new List<KeyValuePair<RectangleF, TimeSpan>>();

        public override float PanelWidth
        {
            get { return _showLyrics ? PlayerWidth + LyricsWidth : PlayerWidth; }
        }

        public override string TabName
        {
            get { return "Música"; }
        }

        public override string TabGlyph
        {
            get { return ""; }
        }

        public override bool IsAvailable
        {
            get { return MediaService.Current.IsAvailable; }
        }

        // Carátula 72 + 14 + progreso 24 + 14 + controles 52
        public override SizeF BaseSize
        {
            get { return new SizeF(440, 176); }
        }

        public override Color? BorderColor
        {
            get { return Color.FromArgb(115, MediaService.Current.Accent); }
        }

        public override bool IsAnimated
        {
            get { return true; } // Ecualizador y progreso
        }

        private bool IsPlaying
        {
            get
            {
                if (_optimisticPlaying != null && DateTime.Now < _optimisticUntil) return _optimisticPlaying.Value;
                return MediaService.Current.IsPlaying;
            }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            MediaInfo media = MediaService.Current;
            LyricsService.Request(media);
            if (_showLyrics && LyricsService.CurrentState == LyricsService.State.NotFound) _showLyrics = false;

            float playerWidth = Math.Min(size.Width, p.Px(PlayerWidth));
            DrawPlayer(g, new SizeF(playerWidth, size.Height), alpha, p, media);
            _lineHits.Clear();
            if (_showLyrics && size.Width > playerWidth + p.Px(40))
            {
                using (Pen divider = new Pen(Color.FromArgb(alpha * 26 / 255, Color.White), 1))
                {
                    g.DrawLine(divider, playerWidth, p.Px(4), playerWidth, size.Height - p.Px(4));
                }
                DrawLyrics(g, new RectangleF(playerWidth + p.Px(18), 0, size.Width - playerWidth - p.Px(18) - p.Px(PadX), size.Height), alpha, p, media);
            }
        }

        // Posición actual de la canción, avanzando sola entre las lecturas del reproductor
        private static TimeSpan LivePosition(MediaInfo media)
        {
            TimeSpan position = media.Position;
            if (media.IsPlaying) position += DateTime.Now - media.PositionSampledAt;
            return position;
        }

        /// <summary>
        /// Cinco líneas centradas en la que se canta (blanca y más grande); la lista se desliza suave al cambiar.
        /// </summary>
        private void DrawLyrics(Graphics g, RectangleF area, int alpha, IslandPainter p, MediaInfo media)
        {
            List<LyricsService.Line> lines = LyricsService.Lines;
            if (lines.Count == 0)
            {
                p.DrawText(g, "Buscando la letra…", p.Text13, IslandPainter.Fade(IslandPainter.Gray, alpha), area, StringAlignment.Center);
                return;
            }
            int current = Math.Max(0, LyricsService.IndexAt(LivePosition(media)));
            if (_lyricsScroll < 0) _lyricsScroll = current;
            _lyricsScroll += (current - _lyricsScroll) * 0.35f; // Se redibuja ~10 veces por segundo: llega en medio segundo

            float lineHeight = p.Px(LineHeight);
            float centerY = area.Y + area.Height / 2;
            Region clip = g.Clip;
            g.SetClip(area);
            for (int i = Math.Max(0, current - 4); i < Math.Min(lines.Count, current + 5); i++)
            {
                float y = centerY + (i - _lyricsScroll) * lineHeight - lineHeight / 2;
                if (y < area.Y - lineHeight || y > area.Bottom) continue;
                bool active = i == current;
                int distance = Math.Abs(i - current);
                int lineAlpha = active ? alpha : alpha * Math.Max(50, 150 - distance * 40) / 255;
                RectangleF row = new RectangleF(area.X, y, area.Width, lineHeight);
                p.DrawText(g, lines[i].Text, active ? p.Bold15 : p.Text13, Color.FromArgb(lineAlpha, Color.White), row, StringAlignment.Near);
                _lineHits.Add(new KeyValuePair<RectangleF, TimeSpan>(row, lines[i].Time));
            }
            g.Clip = clip;
        }

        private void DrawPlayer(Graphics g, SizeF size, int alpha, IslandPainter p, MediaInfo media)
        {
            Color accent = media.Accent;
            float x = p.Px(PadX), width = size.Width - 2 * p.Px(PadX);
            Color white = Color.FromArgb(alpha, Color.White);

            // Brillo ambiental del color de la portada detrás de ella (como Bloom)
            RectangleF glow = new RectangleF(x - p.Px(26), -p.Px(26), p.Px(124), p.Px(124));
            using (GraphicsPath glowPath = new GraphicsPath())
            {
                glowPath.AddEllipse(glow);
                using (PathGradientBrush brush = new PathGradientBrush(glowPath))
                {
                    brush.CenterColor = Color.FromArgb(alpha * 120 / 255, accent);
                    brush.SurroundColors = new[] { Color.FromArgb(0, accent) };
                    g.FillEllipse(brush, glow);
                }
            }

            // Carátula 72 px
            RectangleF art = new RectangleF(x, 0, p.Px(72), p.Px(72));
            if (media.Art != null)
            {
                p.DrawRoundedImage(g, media.Art, art, p.Px(16), alpha);
            }
            else
            {
                IslandPainter.FillRounded(g, art, p.Px(16), Color.FromArgb(alpha, 60, 68, 88));
                p.DrawText(g, IslandPainter.GlyphMusic, p.ThumbFont, white, art, StringAlignment.Center);
            }

            // Ecualizador a la derecha
            float barsWidth = p.Px(34);
            p.DrawBars(g, new RectangleF(x + width - barsWidth, art.Y + (art.Height - p.Px(28)) / 2, barsWidth, p.Px(28)), alpha, accent, IsPlaying);

            // Título, artista y etiqueta de la app
            float textX = art.Right + p.Px(14);
            float textWidth = x + width - barsWidth - p.Px(12) - textX;
            p.DrawText(g, media.Title, p.Bold17, white, new RectangleF(textX, p.Px(2), textWidth, p.Px(24)), StringAlignment.Near);
            p.DrawText(g, string.IsNullOrEmpty(media.Artist) ? " " : media.Artist, p.Text13, IslandPainter.Fade(IslandPainter.Gray, alpha),
                new RectangleF(textX, p.Px(26), textWidth, p.Px(20)), StringAlignment.Near);
            float chipRight = textX;
            if (!string.IsNullOrEmpty(media.AppName))
            {
                SizeF chipText = g.MeasureString(media.AppName, p.Bold12);
                RectangleF chip = new RectangleF(textX, p.Px(50), chipText.Width + p.Px(10), p.Px(19));
                IslandPainter.FillRounded(g, chip, p.Px(8), Color.FromArgb(alpha * 40 / 255, accent));
                p.DrawText(g, media.AppName, p.Bold12, IslandPainter.Fade(Lighten(accent), alpha), chip, StringAlignment.Center);
                chipRight = chip.Right + p.Px(6);
            }

            // «♪ Letra»: solo si LRCLIB tiene la letra sincronizada de esta canción
            _lyricsButton = RectangleF.Empty;
            if (LyricsService.CurrentState == LyricsService.State.Ready)
            {
                string label = _showLyrics ? "Ocultar letra" : "♪ Letra";
                SizeF labelSize = g.MeasureString(label, p.Bold12);
                _lyricsButton = new RectangleF(chipRight, p.Px(50), labelSize.Width + p.Px(12), p.Px(19));
                IslandPainter.FillRounded(g, _lyricsButton, p.Px(9), _showLyrics ? Color.FromArgb(alpha * 40 / 255, Color.White) : Color.FromArgb(alpha, Color.White));
                p.DrawText(g, label, p.Bold12, _showLyrics ? white : Color.FromArgb(alpha, Color.Black), _lyricsButton, StringAlignment.Center);
            }

            // Progreso con tiempos
            float progress = media.Progress;
            _progressBar = new RectangleF(x, p.Px(86) - p.Px(6), width, p.Px(17)); // Zona de clic más alta que la barra
            IslandPainter.DrawBar(g, new RectangleF(x, p.Px(86), width, p.Px(5)), Math.Max(0, progress),
                IslandPainter.Fade(IslandPainter.Track, alpha), Color.FromArgb(alpha, accent));
            if (progress >= 0)
            {
                TimeSpan position = TimeSpan.FromSeconds(media.Duration.TotalSeconds * progress);
                p.DrawText(g, FormatTime(position), p.Text11, IslandPainter.Fade(IslandPainter.Gray, alpha),
                    new RectangleF(x, p.Px(95), p.Px(60), p.Px(15)), StringAlignment.Near);
                p.DrawText(g, FormatTime(media.Duration), p.Text11, IslandPainter.Fade(IslandPainter.Gray, alpha),
                    new RectangleF(x + width - p.Px(60), p.Px(95), p.Px(60), p.Px(15)), StringAlignment.Far);
            }

            // Controles: ⏮ 44 px, ⏯ 52 px en un círculo solo de contorno (sin relleno), ⏭ 44 px, separados 26 px
            float controlsY = p.Px(124);
            float center = size.Width / 2;
            _playButton = new RectangleF(center - p.Px(26), controlsY, p.Px(52), p.Px(52));
            _prevButton = new RectangleF(_playButton.X - p.Px(26) - p.Px(44), controlsY + p.Px(4), p.Px(44), p.Px(44));
            _nextButton = new RectangleF(_playButton.Right + p.Px(26), controlsY + p.Px(4), p.Px(44), p.Px(44));
            float ringWidth = p.Px(1.6f);
            using (Pen ring = new Pen(white, ringWidth))
            {
                g.DrawEllipse(ring, RectangleF.Inflate(_playButton, -ringWidth / 2, -ringWidth / 2));
            }
            p.DrawText(g, IsPlaying ? IslandPainter.GlyphPause : IslandPainter.GlyphPlay, p.Glyph20, white, _playButton, StringAlignment.Center);
            p.DrawText(g, IslandPainter.GlyphPrevious, p.Glyph20, white, _prevButton, StringAlignment.Center);
            p.DrawText(g, IslandPainter.GlyphNext, p.Glyph20, white, _nextButton, StringAlignment.Center);
        }

        public override bool OnClick(PointF point)
        {
            MediaInfo media = MediaService.Current;
            if (_lyricsButton.Contains(point)) { _showLyrics = !_showLyrics; _lyricsScroll = -1; return true; }
            foreach (KeyValuePair<RectangleF, TimeSpan> line in _lineHits)
            {
                if (line.Key.Contains(point)) { MediaService.Seek(line.Value); return true; } // Clic en una línea: saltar ahí
            }
            if (_prevButton.Contains(point)) { MediaService.Previous(); return true; }
            if (_nextButton.Contains(point)) { MediaService.Next(); return true; }
            if (_playButton.Contains(point))
            {
                _optimisticPlaying = !IsPlaying;
                _optimisticUntil = DateTime.Now.AddSeconds(2);
                MediaService.TogglePlayPause();
                return true;
            }
            if (_progressBar.Contains(point) && media.Duration.TotalSeconds > 1)
            {
                float fraction = Math.Max(0, Math.Min(1, (point.X - _progressBar.X) / _progressBar.Width));
                MediaService.Seek(TimeSpan.FromSeconds(media.Duration.TotalSeconds * fraction));
                return true;
            }
            return false;
        }

        private static string FormatTime(TimeSpan time)
        {
            return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : string.Format("{0}:{1:00}", (int)time.TotalMinutes, time.Seconds);
        }

        // Versión clara del color de la carátula para el texto de la etiqueta (legible sobre fondo oscuro)
        private static Color Lighten(Color color)
        {
            return Color.FromArgb((color.R + 255 * 2) / 3, (color.G + 255 * 2) / 3, (color.B + 255 * 2) / 3);
        }
    }

    /// <summary>
    /// Clima (tablero 5): icono, temperatura grande, descripción, ciudad, máxima/mínima y las próximas 4 horas.
    /// </summary>
    internal class WeatherView : IslandView
    {
        public override string TabName
        {
            get { return "Clima"; }
        }

        public override string TabGlyph
        {
            get { return ""; }
        }

        // Fila principal 56 + 16 + horas 50
        public override SizeF BaseSize
        {
            get { return new SizeF(440, 122); }
        }

        public override void OnShown()
        {
            WeatherService.Start(); // Empieza a consultar el clima si nadie lo había pedido aún
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            WeatherService.Conditions weather = WeatherService.Shared.Current;
            float x = p.Px(PadX), width = size.Width - 2 * p.Px(PadX);
            Color white = Color.FromArgb(alpha, Color.White);
            Color gray = IslandPainter.Fade(IslandPainter.Gray, alpha);

            if (weather == null)
            {
                p.DrawText(g, "Consultando el clima…", p.Text13, gray, new RectangleF(x, 0, width, size.Height), StringAlignment.Center);
                return;
            }

            float icon = p.Px(56);
            DrawFadedIcon(g, p, new RectangleF(x, 0, icon, icon), weather.Code, weather.IsDay, alpha);

            string temperature = string.Format("{0:0}°", weather.TemperatureC);
            SizeF tempSize = g.MeasureString(temperature, p.Big44);
            float tempX = x + icon + p.Px(16);
            p.DrawText(g, temperature, p.Big44, white, new RectangleF(tempX, 0, tempSize.Width, icon), StringAlignment.Near);

            float textX = tempX + tempSize.Width + p.Px(4);
            float textWidth = x + width - textX;
            p.DrawText(g, weather.Description, p.Bold15, white, new RectangleF(textX, p.Px(8), textWidth, p.Px(22)), StringAlignment.Near);
            string detail = string.Format("Máx {0:0}° · Mín {1:0}°", weather.MaxC, weather.MinC);
            if (!string.IsNullOrEmpty(weather.City)) detail = weather.City + " · " + detail;
            p.DrawText(g, detail, p.Text13, gray, new RectangleF(textX, p.Px(30), textWidth, p.Px(20)), StringAlignment.Near);

            // Próximas horas: 4 celdas iguales separadas 8 px
            int count = Math.Min(4, weather.NextHours.Count);
            float gap = p.Px(8);
            float cellWidth = (width - gap * 3) / 4;
            for (int i = 0; i < count; i++)
            {
                WeatherService.HourForecast hour = weather.NextHours[i];
                RectangleF cell = new RectangleF(x + i * (cellWidth + gap), p.Px(72), cellWidth, p.Px(50));
                IslandPainter.FillRounded(g, cell, p.Px(12), Color.FromArgb(alpha * 13 / 255, Color.White));
                p.DrawText(g, hour.Time, p.Text12, gray, new RectangleF(cell.X, cell.Y + p.Px(7), cell.Width, p.Px(16)), StringAlignment.Center);
                p.DrawText(g, string.Format("{0:0}°", hour.TemperatureC), p.Bold12, white, new RectangleF(cell.X, cell.Y + p.Px(26), cell.Width, p.Px(16)), StringAlignment.Center);
            }
        }

        // El icono vectorial se dibuja en una capa aparte para poder fundirlo con el resto del contenido
        private static void DrawFadedIcon(Graphics g, IslandPainter p, RectangleF rect, int code, bool isDay, int alpha)
        {
            using (Bitmap layer = new Bitmap((int)Math.Ceiling(rect.Width), (int)Math.Ceiling(rect.Height)))
            {
                using (Graphics ig = Graphics.FromImage(layer))
                {
                    WeatherIcons.Draw(ig, new RectangleF(0, 0, rect.Width, rect.Height), code, isDay);
                }
                p.DrawRoundedImage(g, layer, rect, 0, alpha);
            }
        }
    }

    /// <summary>
    /// Sistema (tablero 6): tres anillos grandes (CPU, RAM, GPU) con el número dentro y el nombre debajo.
    /// Solo mide mientras se ve.
    /// </summary>
    internal class SystemView : IslandView
    {
        private readonly string _cpuName = TelemetrySampler.GetCpuName();
        private readonly string _gpuName = TelemetrySampler.GetGpuName();
        private TelemetrySampler _sampler;
        private System.Threading.Timer _timer;
        private volatile TelemetrySampler.Snapshot _snapshot;
        private int _sampling;

        public override string TabName
        {
            get { return "Sistema"; }
        }

        public override string TabGlyph
        {
            get { return ""; }
        }

        // Anillo 76 + 8 + nombre 18 + 4 + detalle 14; + 58 con dispositivos Bluetooth conectados (fase F)
        // y + 28 con la línea de micrófono y cámara
        public override SizeF BaseSize
        {
            get { return new SizeF(440, 120 + (BluetoothService.ConnectedDevices.Count > 0 ? 58 : 0) + (HasCaptureLine ? 28 : 0)); }
        }

        private static bool HasCaptureLine
        {
            get { return SuiteSettings.CaptureDots && (CaptureMonitor.MicrophoneApps.Count > 0 || CaptureMonitor.CameraApps.Count > 0); }
        }

        public override bool IsAnimated
        {
            get { return true; } // Los valores cambian cada segundo
        }

        public override void OnShown()
        {
            if (_timer != null) return;
            _sampler = new TelemetrySampler();
            _timer = new System.Threading.Timer(state =>
            {
                if (Interlocked.Exchange(ref _sampling, 1) == 1) return;
                try
                {
                    TelemetrySampler sampler = _sampler;
                    if (sampler != null) _snapshot = sampler.Sample();
                }
                catch (Exception ex)
                {
                    Log.Error("[DynamicNotchModule] No se pudo medir el sistema: {0}", ex.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref _sampling, 0);
                }
            }, null, 0, 1000);
        }

        public override void OnHidden()
        {
            if (_timer != null) { _timer.Dispose(); _timer = null; }
            if (_sampler != null) { _sampler.Dispose(); _sampler = null; }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            TelemetrySampler.Snapshot s = _snapshot;
            float x = p.Px(PadX), width = size.Width - 2 * p.Px(PadX);
            float column = width / 3;

            DrawGauge(g, p, alpha, x, column, "CPU", _cpuName, s != null ? s.CpuPercent : 0, IslandPainter.Sky);
            DrawGauge(g, p, alpha, x + column, column, "RAM",
                s != null ? string.Format("{0:0.0} / {1:0} GB", s.RamUsedGb, s.RamTotalGb) : "", s != null ? s.RamPercent : 0, Color.FromArgb(167, 139, 250));
            DrawGauge(g, p, alpha, x + 2 * column, column, "GPU", _gpuName, s != null ? s.GpuPercent : 0, Color.FromArgb(74, 222, 128));

            // Dispositivos Bluetooth conectados con su batería (mockup F1), hasta 3 en una fila
            float lineY = p.Px(126);
            List<BluetoothDevice> devices = BluetoothService.ConnectedDevices;
            if (devices.Count > 0)
            {
                DrawDevices(g, p, alpha, devices, x, lineY + p.Px(4), width);
                lineY += p.Px(58);
            }

            // Qué apps usan el micrófono o la cámara (los puntos de la isla cerrada)
            if (!HasCaptureLine) return;
            List<string> microphone = CaptureMonitor.MicrophoneApps, camera = CaptureMonitor.CameraApps;
            float cursor = x;
            foreach (KeyValuePair<Color, string> entry in new[]
            {
                new KeyValuePair<Color, string>(Color.FromArgb(251, 146, 60), microphone.Count > 0 ? "Micrófono: " + string.Join(", ", microphone.ToArray()) : null),
                new KeyValuePair<Color, string>(Color.FromArgb(34, 197, 94), camera.Count > 0 ? "Cámara: " + string.Join(", ", camera.ToArray()) : null)
            })
            {
                if (entry.Value == null) continue;
                float dot = p.Px(8);
                using (SolidBrush brush = new SolidBrush(IslandPainter.Fade(entry.Key, alpha)))
                {
                    g.FillEllipse(brush, cursor, lineY + (p.Px(18) - dot) / 2, dot, dot);
                }
                float textWidth = Math.Min(g.MeasureString(entry.Value, p.Text12).Width + p.Px(4), x + width - cursor - dot - p.Px(6));
                p.DrawText(g, entry.Value, p.Text12, IslandPainter.Fade(IslandPainter.Gray, alpha),
                    new RectangleF(cursor + dot + p.Px(6), lineY, textWidth, p.Px(18)), StringAlignment.Near);
                cursor += dot + p.Px(6) + textWidth + p.Px(16);
            }
        }

        private static void DrawDevices(Graphics g, IslandPainter p, int alpha, List<BluetoothDevice> devices, float x, float y, float width)
        {
            int count = Math.Min(3, devices.Count);
            float gap = p.Px(8);
            float chipWidth = (width - gap * (count - 1)) / count;
            for (int i = 0; i < count; i++)
            {
                BluetoothDevice device = devices[i];
                RectangleF chip = new RectangleF(x + i * (chipWidth + gap), y, chipWidth, p.Px(46));
                IslandPainter.FillRounded(g, chip, p.Px(12), Color.FromArgb(alpha * 13 / 255, Color.White));
                RectangleF icon = new RectangleF(chip.X + p.Px(8), chip.Y, p.Px(22), chip.Height);
                p.DrawText(g, BluetoothService.Glyph(device.Kind), p.Glyph16, IslandPainter.Fade(Color.FromArgb(195, 201, 212), alpha), icon, StringAlignment.Center);

                float textX = icon.Right + p.Px(8);
                float textWidth = chip.Right - p.Px(8) - textX;
                p.DrawText(g, device.Name, p.Bold12, Color.FromArgb(alpha, Color.White), new RectangleF(textX, chip.Y + p.Px(5), textWidth, p.Px(18)), StringAlignment.Near);
                Color color = BatteryDrawing.ColorFor(device.Battery);
                string percent = device.Battery + " %";
                float percentWidth = g.MeasureString(percent, p.Bold12).Width;
                p.DrawText(g, percent, p.Bold12, IslandPainter.Fade(color, alpha), new RectangleF(textX, chip.Y + p.Px(23), percentWidth + p.Px(2), p.Px(18)), StringAlignment.Near);
                BatteryDrawing.Draw(g, p, new PointF(textX + percentWidth + p.Px(4), chip.Y + p.Px(26)), device.Battery, color, alpha);
            }
        }

        private static void DrawGauge(Graphics g, IslandPainter p, int alpha, float x, float width,
            string label, string detail, float percent, Color color)
        {
            float ring = p.Px(76);
            RectangleF ringRect = new RectangleF(x + (width - ring) / 2, 0, ring, ring);
            p.DrawRing(g, ringRect, Math.Max(0, percent) / 100f, color, alpha, p.Px(7));
            p.DrawText(g, percent < 0 ? "N/D" : string.Format("{0:0}%", percent), p.Bold17, Color.FromArgb(alpha, Color.White), ringRect, StringAlignment.Center);
            p.DrawText(g, label, p.Bold15, Color.FromArgb(alpha, Color.White), new RectangleF(x, ring + p.Px(6), width, p.Px(20)), StringAlignment.Center);
            p.DrawText(g, detail, p.Text11, IslandPainter.Fade(IslandPainter.Gray, alpha), new RectangleF(x, ring + p.Px(26), width, p.Px(16)), StringAlignment.Center);
        }
    }

    /// <summary>
    /// Aviso pasajero de la suite ("🛡️ Modo Privacidad Aero: ACTIVADO"): el emoji inicial hace de icono.
    /// </summary>
    internal class NotificationView : IslandView
    {
        public string Title = "Streeck Suite";
        public string Text = "";

        public override SizeF BaseSize
        {
            get { return new SizeF(440, 84); }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            float padX = p.Px(20);
            string text = Text ?? "";
            string emoji = GetLeadingEmoji(text);
            if (emoji != null) text = text.Substring(emoji.Length).TrimStart();

            RectangleF badge = new RectangleF(padX, 0, p.Px(36), size.Height);
            p.DrawText(g, emoji ?? "✨", p.EmojiFont, Color.FromArgb(alpha, Color.White), badge, StringAlignment.Center);

            float textX = badge.Right + p.Px(12);
            float textWidth = size.Width - textX - padX;
            p.DrawText(g, Title, p.Bold15, Color.FromArgb(alpha, Color.White), new RectangleF(textX, p.Px(14), textWidth, p.Px(22)), StringAlignment.Near);

            using (StringFormat wrap = new StringFormat())
            using (SolidBrush brush = new SolidBrush(IslandPainter.Fade(IslandPainter.Secondary, alpha)))
            {
                wrap.Trimming = StringTrimming.EllipsisWord;
                g.DrawString(text, p.Text13, brush, new RectangleF(textX, p.Px(37), textWidth, size.Height - p.Px(42)), wrap);
            }
        }

        private static string GetLeadingEmoji(string text)
        {
            if (string.IsNullOrEmpty(text) || char.IsLetterOrDigit(text[0])) return null;
            int space = text.IndexOf(' ');
            return space > 0 ? text.Substring(0, space) : null;
        }
    }
}
