using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Módulo Depth Clock: dibuja la hora sobre el fondo de escritorio actual, detrás del sujeto principal o de lo que
    /// esté en primer plano. El fondo se analiza con IA (profundidad y sujeto) una sola vez por imagen.
    /// </summary>
    public class DepthClockModule : IModule
    {
        public string Id { get { return "depth_clock"; } }
        public string Name { get { return "Reloj con Profundidad 3D"; } }
        public string Version { get { return "3.0.0"; } }
        public bool IsEnabled { get; set; }

        private const int WallpaperCheckIntervalMs = 5000;
        // Espera tras el último movimiento de un deslizador antes de recalcular el recorte
        private const int RecomposeDelayMs = 150;

        private EventBus _eventBus;
        private ClockSettings _settings;
        private DesktopClockForm _form;
        private System.Windows.Forms.Timer _watchTimer;
        private System.Windows.Forms.Timer _recomposeTimer;
        private SynchronizationContext _ui;
        private string _wallpaperKey;
        private DepthClockLayers _layers;
        private int _processing; // 1 mientras se analiza un fondo en segundo plano
        private int _composeVersion;
        private bool _isRunning;
        private WeatherService _weather;

        /// <summary>
        /// Instancia en marcha, para la página Reloj 3D del panel de ajustes.
        /// </summary>
        internal static DepthClockModule Current { get; private set; }

        /// <summary>
        /// Se analizó un fondo nuevo (cambia si la IA encontró sujeto).
        /// </summary>
        internal event Action LayersChanged;

        public DepthClockModule()
        {
            IsEnabled = true;
        }

        public void Initialize(EventBus eventBus)
        {
            Current = this;
            _eventBus = eventBus;
            _settings = ClockSettings.Load();
            _eventBus.Subscribe(Events.ClockSetFont, OnSetFont);
            _eventBus.Subscribe(Events.ClockOpenSettings, payload => SettingsWindow.Open(SettingsWindow.Page.Clock));
            _eventBus.Subscribe(Events.GameModeActive, OnGameModeActive);
        }

        public void Start()
        {
            _isRunning = true;
            _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();

            _recomposeTimer = new System.Windows.Forms.Timer();
            _recomposeTimer.Interval = RecomposeDelayMs;
            _recomposeTimer.Tick += (s, e) => { _recomposeTimer.Stop(); Recompose(); };

            _watchTimer = new System.Windows.Forms.Timer();
            _watchTimer.Interval = WallpaperCheckIntervalMs;
            _watchTimer.Tick += (s, e) => CheckWallpaper();
            _watchTimer.Start();
            CheckWallpaper();
            UpdateWeatherService();
        }

        /// <summary>
        /// El servicio del clima (consultas a internet) solo existe mientras el clima está activado en los ajustes.
        /// </summary>
        private void UpdateWeatherService()
        {
            bool wanted = _isRunning && _settings.ShowWeather;
            if (wanted && _weather == null)
            {
                _weather = WeatherService.Shared;
                _weather.Updated += OnWeatherUpdated;
                if (_form != null && !_form.IsDisposed) _form.SetWeather(_weather.Current);
            }
            else if (!wanted && _weather != null)
            {
                _weather.Updated -= OnWeatherUpdated;
                _weather = null;
            }
        }

        // Llega desde un hilo en segundo plano
        private void OnWeatherUpdated()
        {
            WeatherService service = _weather;
            if (service == null) return;
            _ui.Post(state =>
            {
                if (_form != null && !_form.IsDisposed) _form.SetWeather(service.Current);
            }, null);
        }

        public void Stop()
        {
            _isRunning = false;
            if (_watchTimer != null) { _watchTimer.Dispose(); _watchTimer = null; }
            if (_recomposeTimer != null) { _recomposeTimer.Dispose(); _recomposeTimer = null; }
            UpdateWeatherService();
            DestroyForm();
            if (_layers != null)
            {
                _layers.Dispose();
                _layers = null;
            }
            _wallpaperKey = null;
            Log.Info("[DepthClockModule] Detenido");
        }

        private void OnSetFont(object payload)
        {
            string fontName = payload as string;
            if (string.IsNullOrEmpty(fontName)) return;

            ClockSettings settings = _settings.Clone();
            settings.FontFamily = fontName;
            ApplySettings(settings);
            _eventBus.Publish(Events.NotchShowMessage, string.Format("🔤 Tipografía del reloj: {0}", fontName));
        }

        private void OnGameModeActive(object payload)
        {
            bool isGame = payload is bool && (bool)payload;
            if (_form != null && !_form.IsDisposed) _form.SetPaused(isGame);
        }

        #region Ajustes

        /// <summary>
        /// Copia de los ajustes actuales (la página del panel trabaja sobre copias).
        /// </summary>
        internal ClockSettings CurrentSettings
        {
            get { return _settings.Clone(); }
        }

        /// <summary>
        /// Fondo analizado (null si aún no hay) y clima actual, para la ventana de «Mover reloj».
        /// </summary>
        internal DepthClockLayers Layers
        {
            get { return _layers; }
        }

        internal WeatherService.Conditions Weather
        {
            get { return _weather != null ? _weather.Current : null; }
        }

        /// <summary>
        /// null mientras no se haya analizado ningún fondo.
        /// </summary>
        internal bool? SubjectFound
        {
            get { return _layers != null ? _layers.SubjectFound : (bool?)null; }
        }

        /// <summary>
        /// La hora se mueve al instante; el recorte (que depende de dónde está la hora) se recalcula en segundo plano
        /// cuando el usuario deja de mover el deslizador.
        /// </summary>
        internal void ApplySettings(ClockSettings settings)
        {
            // La zona de los dígitos (y por tanto el recorte) depende de la posición, el tamaño, la fuente y los segundos
            bool cutChanged = settings.Mode != _settings.Mode || settings.CenterX != _settings.CenterX
                              || settings.Top != _settings.Top || settings.Size != _settings.Size
                              || settings.FontFamily != _settings.FontFamily || settings.ShowSeconds != _settings.ShowSeconds
                              || settings.Style != _settings.Style;
            bool enabledChanged = settings.Enabled != _settings.Enabled;
            _settings = settings;
            _settings.Save();
            UpdateWeatherService();

            if (enabledChanged)
            {
                // Al apagarlo se quita la ventana pero se conserva el análisis: al volver a encenderlo aparece al instante
                if (!settings.Enabled) DestroyForm();
                else if (_layers != null) ShowLayers(_layers);
                else CheckWallpaper();
            }

            if (_form != null && !_form.IsDisposed) _form.SetSettings(settings);
            if (cutChanged && _recomposeTimer != null)
            {
                _recomposeTimer.Stop();
                _recomposeTimer.Start();
            }
        }

        private void Recompose()
        {
            DepthClockLayers layers = _layers;
            if (layers == null) return;

            int version = Interlocked.Increment(ref _composeVersion);
            ClockSettings settings = _settings.Clone();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    Bitmap foreground = layers.ComposeForeground(settings);
                    _ui.Post(state =>
                    {
                        // Descartar resultados de un ajuste anterior o de un fondo que ya cambió
                        if (version != _composeVersion || layers != _layers) { foreground.Dispose(); return; }
                        Bitmap old = layers.Foreground;
                        layers.Foreground = foreground;
                        if (old != null) old.Dispose();
                        if (_form != null && !_form.IsDisposed) _form.Invalidate();
                    }, null);
                }
                catch (Exception ex)
                {
                    Log.Error("[DepthClockModule] No se pudo recalcular el recorte: {0}", ex);
                }
            });
        }

        #endregion

        #region Fondo de escritorio

        /// <summary>
        /// Comprueba cada pocos segundos si el fondo cambió (o si Explorer se reinició y se llevó la ventana)
        /// y vuelve a preparar el reloj cuando hace falta.
        /// </summary>
        private void CheckWallpaper()
        {
            if (!_isRunning || !_settings.Enabled) return; // Apagado desde el panel: ni ventana ni análisis

            if (_form != null && !_form.IsEmbedded && _layers != null)
            {
                Log.Info("[DepthClockModule] La capa del escritorio desapareció (¿reinicio de Explorer?); se vuelve a incrustar");
                DestroyForm();
                ShowLayers(_layers);
            }

            string path = WallpaperInfo.GetCurrentPath();
            string key = WallpaperInfo.GetChangeKey(path);
            if (key == _wallpaperKey) return;
            if (Interlocked.CompareExchange(ref _processing, 1, 0) != 0) return;

            _wallpaperKey = key;
            Size screen = Screen.PrimaryScreen.Bounds.Size;
            ClockSettings settings = _settings.Clone();

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (path == null)
                    {
                        // Fondo de color sólido o presentación sin imagen: no hay nada sobre lo que dibujar
                        _ui.Post(state => { DestroyForm(); _layers = null; }, null);
                        return;
                    }

                    if (!VisionModel.Depth.IsAvailable || !VisionModel.Subject.IsAvailable)
                    {
                        PostMessage("🧠 Descargando IA del reloj 3D (100 MB)...");
                        VisionModel.Depth.EnsureDownloaded();
                        VisionModel.Subject.EnsureDownloaded();
                    }

                    DepthClockLayers layers = DepthClockLayers.Analyze(path, screen);
                    layers.Foreground = layers.ComposeForeground(settings);
                    _ui.Post(state =>
                    {
                        if (!_isRunning) { layers.Dispose(); return; }
                        if (_settings.Enabled)
                        {
                            ShowLayers(layers);
                            _eventBus.Publish(Events.NotchShowMessage, "🕒 Reloj 3D adaptado a tu fondo");
                        }
                        else
                        {
                            if (_layers != null) _layers.Dispose();
                            _layers = layers;
                        }
                        Action changed = LayersChanged;
                        if (changed != null) changed();
                    }, null);
                }
                catch (Exception ex)
                {
                    Log.Error("[DepthClockModule] No se pudo preparar el reloj para {0}: {1}", path, ex);
                    PostMessage("⚠️ No se pudo preparar el reloj 3D (ver registro)");
                }
                finally
                {
                    Interlocked.Exchange(ref _processing, 0);
                }
            });
        }

        private void ShowLayers(DepthClockLayers layers)
        {
            if (_layers != null && _layers != layers) _layers.Dispose();
            _layers = layers;

            if (_form == null || _form.IsDisposed)
            {
                _form = new DesktopClockForm(_settings);
                if (_weather != null) _form.SetWeather(_weather.Current);
            }
            _form.SetLayers(layers);
            _form.EmbedInDesktop();
        }

        private void DestroyForm()
        {
            if (_form != null && !_form.IsDisposed)
            {
                _form.Dispose();
            }
            _form = null;
        }

        private void PostMessage(string message)
        {
            _ui.Post(state => _eventBus.Publish(Events.NotchShowMessage, message), null);
        }

        #endregion

        /// <summary>
        /// Escanea el catálogo completo de fuentes TTF/OTF instaladas en Windows.
        /// </summary>
        public List<string> GetInstalledWindowsFonts()
        {
            List<string> fonts = new List<string>();
            try
            {
                using (InstalledFontCollection installedFonts = new InstalledFontCollection())
                {
                    foreach (FontFamily family in installedFonts.Families)
                    {
                        fonts.Add(family.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[DepthClockModule] Error al leer fuentes: {0}", ex);
            }
            return fonts;
        }
    }

    /// <summary>
    /// Dónde está el fondo de escritorio actual y cómo saber si cambió.
    /// </summary>
    internal static class WallpaperInfo
    {
        // Copia que Windows guarda del fondo en uso; cambia también con presentaciones y Windows Spotlight
        private static readonly string TranscodedPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");

        public static string GetCurrentPath()
        {
            string configured = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallPaper", null) as string;
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;
            return File.Exists(TranscodedPath) ? TranscodedPath : null;
        }

        public static string GetChangeKey(string path)
        {
            if (path == null) return "none";
            DateTime stamp = File.Exists(TranscodedPath) ? File.GetLastWriteTimeUtc(TranscodedPath) : File.GetLastWriteTimeUtc(path);
            return path + "|" + stamp.Ticks + "|" + Screen.PrimaryScreen.Bounds.Size;
        }

        /// <summary>
        /// Valor de "Elegir un ajuste" de Configuración: 10 Rellenar, 6 Ajustar, 2 Expandir, 0 Centrar/Mosaico, 22 Extender.
        /// </summary>
        public static int GetStyle()
        {
            string style = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "WallpaperStyle", "10") as string;
            int value;
            return int.TryParse(style, out value) ? value : 10;
        }
    }
}
