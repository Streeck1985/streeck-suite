using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using StreeckSuite.Core;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Lectura y control de la música a través del control multimedia global de Windows
    /// (el mismo que aparece al cambiar el volumen). Funciona con cualquier app que lo use.
    /// Compartido por el notch y el widget del reproductor; trabaja en un hilo en segundo plano.
    /// </summary>
    internal static class MediaService
    {
        private const int PollMs = 1000;
        private const int ArtSize = 128;
        // Aunque Windows avisa de los cambios de canción, se relee cada cierto tiempo por si algún reproductor no avisa
        private const int PropertiesRefreshMs = 10000;

        private static readonly object _gate = new object();
        private static Timer _timer;
        private static int _polling;
        private static GlobalSystemMediaTransportControlsSessionManager _manager;
        private static GlobalSystemMediaTransportControlsSession _session;
        private static string _sessionId;
        private static volatile bool _propertiesDirty = true;
        private static DateTime _propertiesReadAt = DateTime.MinValue;
        private static MediaInfo _current = MediaInfo.Nothing;
        private static string _lastError;

        /// <summary>
        /// Se lanza (en un hilo en segundo plano) cuando cambia la canción, el estado o la carátula.
        /// </summary>
        public static event Action Changed;

        public static MediaInfo Current
        {
            get
            {
                EnsureStarted();
                return _current;
            }
        }

        public static void EnsureStarted()
        {
            lock (_gate)
            {
                if (_timer == null) _timer = new Timer(Poll, null, 0, PollMs);
            }
        }

        #region Controles

        public static void TogglePlayPause() { Control(s => WinRtAsync.Wait(s.TryTogglePlayPauseAsync())); }
        public static void Next() { Control(s => WinRtAsync.Wait(s.TrySkipNextAsync())); }
        public static void Previous() { Control(s => WinRtAsync.Wait(s.TrySkipPreviousAsync())); }

        /// <summary>
        /// Salta a ese momento de la canción (clic en la barra de progreso).
        /// </summary>
        public static void Seek(TimeSpan position)
        {
            long ticks = Math.Max(0, position.Ticks);
            Control(s => WinRtAsync.Wait(s.TryChangePlaybackPositionAsync(ticks)));
        }

        private static void Control(Action<GlobalSystemMediaTransportControlsSession> action)
        {
            GlobalSystemMediaTransportControlsSession session = _session;
            if (session == null) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    action(session);
                    _propertiesDirty = true;
                    Poll(null); // Reflejar el cambio sin esperar al siguiente sondeo
                }
                catch (Exception ex)
                {
                    Log.Error("[MediaService] El reproductor no aceptó la orden: {0}", ex.Message);
                }
            });
        }

        #endregion

        #region Lectura

        private static void Poll(object state)
        {
            if (Interlocked.Exchange(ref _polling, 1) == 1) return;
            try
            {
                if (_manager == null)
                {
                    _manager = WinRtAsync.Wait(GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
                    _manager.CurrentSessionChanged += (s, e) => _propertiesDirty = true;
                }

                // Windows puede devolver un objeto nuevo para la misma sesión en cada llamada: se compara por app
                GlobalSystemMediaTransportControlsSession session = _manager.GetCurrentSession();
                string sessionId = session != null ? session.SourceAppUserModelId : null;
                if (sessionId != _sessionId)
                {
                    _sessionId = sessionId;
                    _propertiesDirty = true;
                    if (session != null) session.MediaPropertiesChanged += (s, e) => _propertiesDirty = true;
                }
                _session = session;

                MediaInfo next = session == null ? MediaInfo.Nothing : Read(session);
                MediaInfo previous = _current;
                _current = next;
                _lastError = null;

                if (!next.SameTrackAs(previous))
                {
                    Log.Info("[MediaService] Sonando: {0} - {1} ({2}, carátula: {3})", next.Artist, next.Title, next.AppName, next.Art != null ? "sí" : "no");
                }

                bool changed = !next.SameTrackAs(previous) || next.IsPlaying != previous.IsPlaying
                               || next.IsAvailable != previous.IsAvailable || next.Art != previous.Art;
                Action handler = Changed;
                if (changed && handler != null) handler();
            }
            catch (Exception ex)
            {
                // Se registra una sola vez por tipo de error para no llenar el registro cada segundo
                string error = ex.GetBaseException().Message;
                if (error != _lastError) Log.Error("[MediaService] No se pudo leer el control multimedia: {0}", error);
                _lastError = error;
            }
            finally
            {
                Interlocked.Exchange(ref _polling, 0);
            }
        }

        private static MediaInfo Read(GlobalSystemMediaTransportControlsSession session)
        {
            MediaInfo previous = _current;
            MediaInfo info = new MediaInfo();
            info.IsAvailable = true;
            info.AppName = FriendlyAppName(session.SourceAppUserModelId);

            GlobalSystemMediaTransportControlsSessionPlaybackInfo playback = session.GetPlaybackInfo();
            info.IsPlaying = playback != null && playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            GlobalSystemMediaTransportControlsSessionTimelineProperties timeline = session.GetTimelineProperties();
            if (timeline != null)
            {
                info.Position = timeline.Position;
                info.Duration = timeline.EndTime - timeline.StartTime;
                info.PositionSampledAt = timeline.LastUpdatedTime.LocalDateTime;
            }

            // Título, artista y carátula solo cuando Windows avisa de un cambio (leerlos cuesta más)
            bool refresh = _propertiesDirty || (DateTime.Now - _propertiesReadAt).TotalMilliseconds > PropertiesRefreshMs
                           || previous.AppName != info.AppName;
            if (!refresh && previous.IsAvailable)
            {
                info.Title = previous.Title;
                info.Artist = previous.Artist;
                info.Art = previous.Art;
                info.Accent = previous.Accent;
                return info;
            }

            _propertiesDirty = false;
            _propertiesReadAt = DateTime.Now;
            GlobalSystemMediaTransportControlsSessionMediaProperties properties = WinRtAsync.Wait(session.TryGetMediaPropertiesAsync());
            info.Title = string.IsNullOrEmpty(properties.Title) ? info.AppName : properties.Title;
            info.Artist = properties.Artist ?? "";

            if (info.SameTrackAs(previous) && previous.Art != null)
            {
                info.Art = previous.Art;
                info.Accent = previous.Accent;
            }
            else
            {
                // La carátula anterior no se libera aquí: puede estar dibujándose en la interfaz; la recoge el GC
                info.Art = LoadArt(properties.Thumbnail);
                if (info.Art != null) info.Accent = DominantColor(info.Art);
            }
            return info;
        }

        private static Bitmap LoadArt(IRandomAccessStreamReference thumbnail)
        {
            if (thumbnail == null) return null;
            try
            {
                byte[] bytes;
                using (IRandomAccessStreamWithContentType stream = WinRtAsync.Wait(thumbnail.OpenReadAsync()))
                using (DataReader reader = new DataReader(stream.GetInputStreamAt(0)))
                {
                    uint size = (uint)stream.Size;
                    WinRtAsync.Wait(reader.LoadAsync(size));
                    bytes = new byte[size];
                    reader.ReadBytes(bytes);
                }

                using (MemoryStream copy = new MemoryStream(bytes))
                {
                    using (Image image = Image.FromStream(copy))
                    {
                        // Recorte cuadrado centrado (YouTube da miniaturas 16:9) y reducción a un tamaño fijo
                        int side = Math.Min(image.Width, image.Height);
                        Rectangle source = new Rectangle((image.Width - side) / 2, (image.Height - side) / 2, side, side);
                        Bitmap art = new Bitmap(ArtSize, ArtSize, PixelFormat.Format32bppArgb);
                        using (Graphics g = Graphics.FromImage(art))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(image, new Rectangle(0, 0, ArtSize, ArtSize), source, GraphicsUnit.Pixel);
                        }
                        return art;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[MediaService] No se pudo leer la carátula: {0}", ex.GetBaseException().Message);
                return null;
            }
        }

        /// <summary>
        /// Color más vivo y representativo de la carátula: media de los píxeles ponderada por saturación,
        /// llevada a un brillo legible sobre fondo oscuro.
        /// </summary>
        internal static Color DominantColor(Bitmap art)
        {
            double r = 0, g = 0, b = 0, weight = 0;
            int[] pixels = VisionModel.ReadPixels(art);
            for (int i = 0; i < pixels.Length; i += 3)
            {
                Color c = Color.FromArgb(pixels[i]);
                float saturation = c.GetSaturation();
                float brightness = c.GetBrightness();
                if (brightness < 0.15f || brightness > 0.95f) continue; // Negros y blancos no definen el color
                double w = saturation * saturation + 0.02;
                r += c.R * w;
                g += c.G * w;
                b += c.B * w;
                weight += w;
            }
            if (weight <= 0) return MediaInfo.Nothing.Accent;

            Color average = Color.FromArgb((int)(r / weight), (int)(g / weight), (int)(b / weight));
            return FromHsl(average.GetHue(), Math.Max(0.55f, average.GetSaturation()), 0.6f);
        }

        private static Color FromHsl(float hue, float saturation, float lightness)
        {
            float c = (1 - Math.Abs(2 * lightness - 1)) * saturation;
            float x = c * (1 - Math.Abs(hue / 60f % 2 - 1));
            float m = lightness - c / 2;
            float r, g, b;
            if (hue < 60) { r = c; g = x; b = 0; }
            else if (hue < 120) { r = x; g = c; b = 0; }
            else if (hue < 180) { r = 0; g = c; b = x; }
            else if (hue < 240) { r = 0; g = x; b = c; }
            else if (hue < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
        }

        /// <summary>
        /// Nombre legible de la app a partir de su identificador ("Spotify.exe", "MSEdge", "Chrome"...).
        /// </summary>
        private static string FriendlyAppName(string appId)
        {
            if (string.IsNullOrEmpty(appId)) return "Multimedia";
            string id = appId.ToLowerInvariant();
            if (id.Contains("spotify")) return "Spotify";
            if (id.Contains("msedge")) return "Edge";
            if (id.Contains("chrome")) return "Chrome";
            if (id.Contains("firefox") || id.Contains("308046b0af4a39cb")) return "Firefox";
            if (id.Contains("opera")) return "Opera";
            if (id.Contains("brave")) return "Brave";
            if (id.Contains("applemusic")) return "Apple Music";
            if (id.Contains("zunemusic")) return "Reproductor multimedia";
            if (id.Contains("vlc")) return "VLC";
            if (id.Contains("discord")) return "Discord";
            if (id.Contains("whatsapp")) return "WhatsApp";

            string name = appId;
            int bang = name.LastIndexOf('!');
            if (bang >= 0)
            {
                // Apps de la Store: "Editor.NombreApp_hash!App"; el nombre útil está antes del "!", no después
                string package = name.Substring(0, bang);
                int underscore = package.IndexOf('_');
                if (underscore > 0) package = package.Substring(0, underscore);
                int dot = package.LastIndexOf('.');
                name = dot >= 0 ? package.Substring(dot + 1) : package;
            }
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
            return name;
        }

        #endregion
    }
}
