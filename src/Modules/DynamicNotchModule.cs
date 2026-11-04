using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Módulo Dynamic Notch: isla flotante en la parte superior. Compacta en reposo, se expande al pasar el ratón
    /// en un carrusel de vistas (música, clima, sistema...) que se recorre con la rueda, y muestra avisos pasajeros.
    /// </summary>
    public class DynamicNotchModule : IModule
    {
        public string Id { get { return "dynamic_notch"; } }
        public string Name { get { return "Dynamic Island / Notch Superior"; } }
        public string Version { get { return "3.0.0"; } }
        public bool IsEnabled { get; set; }

        private EventBus _eventBus;
        private NotchOverlayForm _overlayForm;
        private bool _isRunning;
        private SearchWindow _search;
        private SearchHotkey _searchHotkey;
        private SuiteSettings.SearchShortcut _registeredShortcut;
        private AssistantWindow _assistant;
        private AssistantHotkey _assistantHotkey;
        private bool _registeredAssistantHotkey;

        public DynamicNotchModule()
        {
            IsEnabled = true;
        }

        public void Initialize(EventBus eventBus)
        {
            _eventBus = eventBus;
            _eventBus.Subscribe(Events.NotchShowMessage, OnShowMessage);
            _eventBus.Subscribe(Events.GameModeActive, OnGameModeActive);
        }

        public void Start()
        {
            if (_overlayForm == null || _overlayForm.IsDisposed)
            {
                _overlayForm = new NotchOverlayForm(_eventBus);
            }
            _overlayForm.Show();
            _isRunning = true;
            StartSearch();
        }

        /// <summary>
        /// Búsqueda rápida (fase E): ventana propia y atajo global, que se vuelve a registrar si cambia en el panel.
        /// </summary>
        private void StartSearch()
        {
            if (_searchHotkey != null) return;
            _searchHotkey = new SearchHotkey(() =>
            {
                if (_search == null || _search.IsDisposed) _search = new SearchWindow();
                _search.Toggle();
            });
            // Búsqueda de la barra de Streeck
            _eventBus.Subscribe(Events.SearchOpen, payload => _overlayForm.BeginInvoke((Action)(() =>
            {
                if (_search == null || _search.IsDisposed) _search = new SearchWindow();
                if (!_search.Visible) _search.Toggle();
            })));
            _registeredShortcut = SuiteSettings.SearchHotkey;
            if (!_searchHotkey.Register()) _eventBus.Publish(Events.NotchShowMessage, "⚠️ El atajo de la búsqueda está ocupado; elige otro en Ajustes");
            // Asistente de IA (fase G): Ctrl + Alt + A y el resultado «Preguntar a la IA» de la búsqueda
            _assistantHotkey = new AssistantHotkey(() => Assistant.Toggle());
            _registeredAssistantHotkey = SuiteSettings.AssistantHotkey;
            if (!_assistantHotkey.Apply()) _eventBus.Publish(Events.NotchShowMessage, "⚠️ Ctrl + Alt + A ya lo usa otro programa");
            SearchService.AssistantRequested += question => Assistant.ShowAssistant(question);
            SuiteSettings.Changed += () =>
            {
                if (_assistantHotkey == null || SuiteSettings.AssistantHotkey == _registeredAssistantHotkey) return;
                _registeredAssistantHotkey = SuiteSettings.AssistantHotkey;
                _assistantHotkey.Apply();
            };

            SuiteSettings.Changed += () =>
            {
                if (_searchHotkey == null || SuiteSettings.SearchHotkey == _registeredShortcut) return;
                _registeredShortcut = SuiteSettings.SearchHotkey;
                if (!_searchHotkey.Register()) _eventBus.Publish(Events.NotchShowMessage, "⚠️ Ese atajo ya lo usa otro programa");
            };
        }

        private AssistantWindow Assistant
        {
            get
            {
                if (_assistant == null || _assistant.IsDisposed) _assistant = new AssistantWindow();
                return _assistant;
            }
        }

        public void Stop()
        {
            _isRunning = false;
            if (_assistantHotkey != null) { _assistantHotkey.Dispose(); _assistantHotkey = null; }
            if (_assistant != null && !_assistant.IsDisposed) _assistant.Hide();
            if (_searchHotkey != null) { _searchHotkey.Dispose(); _searchHotkey = null; }
            if (_search != null && !_search.IsDisposed) _search.Hide();
            if (_overlayForm != null && !_overlayForm.IsDisposed)
            {
                _overlayForm.Hide();
            }
        }

        private void OnShowMessage(object payload)
        {
            string message = payload as string;
            if (_overlayForm != null && !_overlayForm.IsDisposed && !string.IsNullOrEmpty(message))
            {
                _overlayForm.ShowAlert(message);
            }
        }

        private void OnGameModeActive(object payload)
        {
            bool isGame = payload is bool && (bool)payload;
            if (!_isRunning || _overlayForm == null || _overlayForm.IsDisposed) return;

            if (isGame)
            {
                _overlayForm.Hide(); // Suspender para 0% consumo en juegos
            }
            else
            {
                _overlayForm.Show(); // Restaurar al salir del juego
            }
        }
    }

    /// <summary>
    /// Ventana de la isla. Su tamaño sigue un muelle amortiguado hacia el de la vista activa.
    /// </summary>
    internal class NotchOverlayForm : LayeredWindow
    {
        // Tucked: rayita en el borde superior cuando una ventana ocupa la zona de la isla (p. ej. el navegador maximizado)
        private enum Mode { Tucked, Compact, Expanded, Alert }

        private const int TopMargin = 14;
        private const int TuckedTopMargin = 3;
        private const int TuckCheckMs = 250;
        private const int ExpandDelayMs = 200;   // Ratón encima este tiempo antes de expandir (como Bloom: no se abre al ir a las barras de título)
        // Con una ventana maximizada la isla está escondida sobre las pestañas: solo se asoma con el ratón pegado al borde
        // superior (2 px) durante medio segundo, para no abrirse al ir a hacer clic en una pestaña
        private const int TuckedEdgePx = 2;
        private const int TuckedDelayMs = 500;
        private const int AlertMs = 4000;
        private const int VolumeAlertMs = 1800;
        private const int MediaPollMs = 1000;
        // Cuánto sigue expandida tras sacar el ratón y la duración de una canción nueva: panel de ajustes (Isla)

        // Tras un clic o la rueda, margen para que no se cierre antes de que el ratón vuelva a contar como "encima"
        private static int InteractionPeekMs
        {
            get { return SuiteSettings.IslandCloseDelayMs + 300; }
        }

        // Muelles de Bloom (mockup N1): ancho, alto y esquinas se mueven cada uno con su propia rigidez y amortiguación
        private const float WidthStiffness = 400f, WidthDamping = 31f;
        private const float HeightStiffness = 450f, HeightDamping = 29f;
        private const float RadiusStiffness = 1000f, RadiusDamping = 40f;
        private const float FrameSeconds = 1f / 60f;

        private readonly EventBus _eventBus;
        private readonly float _dpiScale;
        private float _scale;                // PPP de la pantalla × tamaño elegido en el panel de ajustes
        private IslandPainter _painter;
        private readonly Action _settingsChanged;
        private readonly Timer _frameTimer;
        private readonly Timer _mediaTimer;

        private readonly CompactView _compact = new CompactView();
        private readonly NotificationView _alert = new NotificationView();
        private readonly VolumeView _volumeAlert = new VolumeView();
        private readonly NotificationAlertView _notificationAlert = new NotificationAlertView();
        private readonly CaptureAlertView _captureAlert = new CaptureAlertView();
        private readonly Queue<IslandNotification> _notificationQueue = new Queue<IslandNotification>();
        private readonly Action<IslandNotification> _notificationArrived;
        private readonly Action<CaptureMonitor.Device, string> _captureStarted;
        private readonly Action _captureChanged;
        private const int CaptureAlertMs = 3000;
        private const int EventAlertMs = 8000;
        private readonly TimerAlertView _timerAlert = new TimerAlertView();
        private readonly EventAlertView _eventAlert = new EventAlertView();
        private readonly HashSet<string> _alertedEvents = new HashSet<string>();
        private readonly Action _agendaUpdated;
        private readonly BluetoothAlertView _bluetoothAlert = new BluetoothAlertView();
        private readonly Action<BluetoothDevice> _bluetoothConnected, _bluetoothLow;
        private const int BluetoothAlertMs = 3000;
        private const int LowBatteryAlertMs = 5000;
        private bool _timerAlertPending;   // Terminó un tiempo y aún no se ha hecho clic en su aviso
        private readonly TrayView _tray = new TrayView();
        private readonly DropZoneView _dropZone = new DropZoneView();
        private Point? _dragStart;         // Donde se pulsó, para arrastrar un archivo fuera de la bandeja
        private bool _draggingOut;         // La isla es el origen del arrastre: no debe mostrar su propia zona de soltar
        private int _hoveredTab = -1;      // Pestaña bajo el ratón (se muestra su nombre)
        private readonly AudioService _audio;
        private IslandView _alertView;
        private readonly MusicView _music = new MusicView();
        private readonly List<IslandView> _views;
        private int _viewIndex;

        private Mode _mode = Mode.Compact;
        private IslandView _shownView;
        private float _width, _height, _widthVelocity, _heightVelocity, _radius, _radiusVelocity;
        private float _contentOpacity = 1f;
        private DateTime _alertUntil = DateTime.MinValue;
        private DateTime _peekUntil = DateTime.MinValue;
        private MediaInfo _lastMedia;
        private int _lastFullPaintSecond = -1;

        private bool _tuckRequested;
        private DateTime _lastTuckCheck = DateTime.MinValue;
        private DateTime? _hoverStart;
        private bool _hoverFromTucked;   // El ratón llegó con la isla escondida: se aplica la espera larga
        private DateTime _hoverUntil = DateTime.MinValue;

        private readonly NativeMethods.LowLevelMouseProc _wheelHookProc; // Referencia viva: si el GC la recoge, Windows llamaría a memoria liberada
        private IntPtr _wheelHook = IntPtr.Zero;

        public NotchOverlayForm(EventBus eventBus)
        {
            _eventBus = eventBus;

            using (Graphics screenGraphics = Graphics.FromHwnd(IntPtr.Zero))
            {
                _dpiScale = screenGraphics.DpiX / 96f;
            }
            _scale = _dpiScale * SuiteSettings.IslandScalePercent / 100f;
            _painter = new IslandPainter(_scale, SuiteSettings.IslandScalePercent / 100f);
            _settingsChanged = OnSettingsChanged;
            SuiteSettings.Changed += _settingsChanged;
            _audio = CreateAudioService();
            _views = new List<IslandView> { _music };
            if (_audio != null) _views.Add(new AudioView(_audio));
            _views.Add(new WeatherView());
            _views.Add(new SystemView());
            _views.Add(new TimerView());
            _views.Add(new AgendaView());
            _views.Add(_tray);
            _views.Add(new NotificationsView());

            // Bandeja: soltar archivos sobre la isla y arrastrarlos fuera
            AllowDrop = true;
            DragEnter += OnFilesDragEnter;
            DragOver += (s, e) => { if (_alertView == _dropZone && _mode == Mode.Alert) e.Effect = DragDropEffects.Copy; };
            DragLeave += (s, e) => EndDropZone();
            DragDrop += OnFilesDropped;
            MouseDown += (s, e) => { if (e.Button == MouseButtons.Left) _dragStart = e.Location; };
            MouseMove += OnMouseMoveDragOut;
            _bluetoothConnected = device => BeginInvokeSafe(() => ShowBluetoothAlert(device, false));
            _bluetoothLow = device => BeginInvokeSafe(() => ShowBluetoothAlert(device, true));
            BluetoothService.Connected += _bluetoothConnected;
            BluetoothService.LowBattery += _bluetoothLow;
            BluetoothService.EnsureStarted();
            _agendaUpdated = () => BeginInvokeSafe(RenderSurface);
            AgendaService.Updated += _agendaUpdated;
            AgendaService.EnsureStarted();

            // Avisos de las apps y uso del micrófono/cámara: llegan en hilos de segundo plano
            _notificationArrived = n => BeginInvokeSafe(() => EnqueueNotification(n));
            _captureStarted = (device, app) => BeginInvokeSafe(() => ShowCaptureAlert(device, app));
            _captureChanged = () => BeginInvokeSafe(RenderSurface);
            NotificationService.Arrived += _notificationArrived;
            CaptureMonitor.Started += _captureStarted;
            CaptureMonitor.Changed += _captureChanged;
            NotificationService.EnsureStarted();
            CaptureMonitor.EnsureStarted();
            _wheelHookProc = OnLowLevelMouse;

            TopMost = true;
            Cursor = Cursors.Hand;

            SizeF initial = TargetSize();
            _width = initial.Width;
            _height = initial.Height;
            ApplyBounds();

            // Un solo temporizador: 60 fps mientras la isla cambia de forma, ~10 fps para lo que se mueve en reposo
            _frameTimer = new Timer();
            _frameTimer.Interval = 16;
            _frameTimer.Tick += OnFrame;
            _frameTimer.Start();

            // Leer MediaService.Current es solo leer un campo: el trabajo lo hace el servicio en segundo plano
            _mediaTimer = new Timer();
            _mediaTimer.Interval = MediaPollMs;
            _mediaTimer.Tick += (s, e) => PollMedia();
            _mediaTimer.Start();
            PollMedia();

            MouseClick += OnIslandClick;
        }

        /// <summary>
        /// Audio: aviso al cambiar el volumen y al cambiar de altavoz/auriculares. Si Core Audio falla, la isla
        /// funciona igual pero sin la vista de sonido.
        /// </summary>
        private AudioService CreateAudioService()
        {
            try
            {
                AudioService audio = new AudioService();
                audio.VolumeChanged += (level, muted) => BeginInvokeSafe(() => ShowVolume(level, muted));
                audio.DefaultDeviceChanged += () => BeginInvokeSafe(() =>
                {
                    _audio.AttachDefaultDevice();
                    ShowAlert("🎧 Ahora suena por: " + _audio.DefaultDeviceName);
                });
                return audio;
            }
            catch (Exception ex)
            {
                Log.Error("[DynamicNotchModule] No se pudo acceder al audio de Windows: {0}", ex.Message);
                return null;
            }
        }

        // Los avisos de audio llegan en otro hilo; la interfaz solo se toca desde el suyo
        private void BeginInvokeSafe(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // La ventana se está cerrando
            }
        }

        #region Estado

        /// <summary>
        /// Cambio en el panel de ajustes: el tamaño necesita fuentes nuevas; el resto se lee en cada fotograma.
        /// </summary>
        private void OnSettingsChanged()
        {
            if (IsDisposed) return;
            float scale = _dpiScale * SuiteSettings.IslandScalePercent / 100f;
            if (scale != _scale)
            {
                _scale = scale;
                IslandPainter old = _painter;
                _painter = new IslandPainter(_scale, SuiteSettings.IslandScalePercent / 100f);
                old.Dispose();
                SizeF target = TargetSize();
                _width = target.Width;
                _height = target.Height;
                ApplyBounds();
            }
            UpdateMode();
        }

        // Pestañas que el usuario dejó visibles en el panel de ajustes
        private bool IsTabEnabled(IslandView view)
        {
            if (view == _music) return SuiteSettings.IslandShowMusic;
            if (view is AudioView) return SuiteSettings.IslandShowSound;
            if (view is WeatherView) return SuiteSettings.IslandShowWeather;
            if (view is SystemView) return SuiteSettings.IslandShowSystem;
            return true;
        }

        // "Solo avisos": con una ventana encima la isla desaparece del todo salvo para los avisos
        private bool HiddenExceptAlerts
        {
            get { return _tuckRequested && SuiteSettings.IslandMaximized == SuiteSettings.MaximizedBehavior.AlertsOnly; }
        }

        public void ShowAlert(string text)
        {
            _alert.Text = text;
            _alertView = _alert;
            _alertUntil = DateTime.Now.AddMilliseconds(AlertMs);
            UpdateMode();
        }

        /// <summary>
        /// Los avisos se muestran de uno en uno; mientras se ve uno, los demás esperan y se cuentan en «+N».
        /// </summary>
        private void EnqueueNotification(IslandNotification notification)
        {
            if (!SuiteSettings.NotificationsEnabled || !Visible) return;
            _notificationQueue.Enqueue(notification);
            if (_mode == Mode.Alert && _alertView == _notificationAlert && DateTime.Now < _alertUntil)
            {
                _notificationAlert.Pending = _notificationQueue.Count;
                RenderSurface();
                return;
            }
            ShowNextNotification();
        }

        private void ShowNextNotification()
        {
            if (_notificationQueue.Count == 0) return;
            _notificationAlert.Notification = _notificationQueue.Dequeue();
            _notificationAlert.Pending = _notificationQueue.Count;
            _alertView = _notificationAlert;
            _alertUntil = DateTime.Now.AddMilliseconds(SuiteSettings.NotificationDurationMs);
            _contentOpacity = 0f; // El aviso siguiente entra con el mismo fundido
            UpdateMode();
        }

        #region Bandeja de archivos

        private void OnFilesDragEnter(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetDataPresent(DataFormats.FileDrop) ? e.Data.GetData(DataFormats.FileDrop) as string[] : null;
            if (_draggingOut || !SuiteSettings.FileTrayEnabled || files == null || files.Length == 0)
            {
                e.Effect = DragDropEffects.None;
                return;
            }
            e.Effect = DragDropEffects.Copy;
            _dropZone.Count = files.Length;
            _alertView = _dropZone;
            _alertUntil = DateTime.MaxValue; // Abierta mientras dure el arrastre
            UpdateMode();
        }

        private void EndDropZone()
        {
            if (_alertView != _dropZone) return;
            _alertUntil = DateTime.MinValue;
            UpdateMode();
        }

        private void OnFilesDropped(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            EndDropZone();
            if (files == null || _draggingOut) return;
            FileTrayService.Add(files);
            // Enseñar la bandeja con lo recién guardado
            _viewIndex = _views.IndexOf(_tray);
            _peekUntil = DateTime.Now.AddMilliseconds(Math.Max(2500, SuiteSettings.IslandCloseDelayMs));
            UpdateMode();
        }

        /// <summary>
        /// Arrastrar un archivo de la bandeja a otra ventana: se copia (con Mayús se mueve), como desde el Explorador.
        /// </summary>
        private void OnMouseMoveDragOut(object sender, MouseEventArgs e)
        {
            if (_dragStart == null || (e.Button & MouseButtons.Left) == 0 || _mode != Mode.Expanded || ActiveView != _tray) return;
            if (Math.Abs(e.X - _dragStart.Value.X) < 6 && Math.Abs(e.Y - _dragStart.Value.Y) < 6) return;

            PointF content = new PointF(_dragStart.Value.X - ShadowPad, _dragStart.Value.Y - ShadowPad - Px(ContentTop));
            _dragStart = null;
            TrayFile file = _tray.FileAt(content);
            if (file == null) return;

            DataObject data = new DataObject(DataFormats.FileDrop, new[] { file.Path });
            // Sin esto el Explorador mueve por defecto dentro del mismo disco
            data.SetData("Preferred DropEffect", new System.IO.MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));
            _draggingOut = true;
            try
            {
                DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Move);
            }
            finally
            {
                _draggingOut = false;
            }
            RenderSurface(); // Si se movió, desaparece de la bandeja
        }

        #endregion

        /// <summary>
        /// Aviso unos minutos antes de cada evento de la agenda (una sola vez por evento).
        /// </summary>
        private void CheckEventAlerts(DateTime now)
        {
            int minutes = SuiteSettings.EventAlertMinutes;
            if (minutes <= 0 || !Visible || (_mode == Mode.Alert && now < _alertUntil)) return;
            foreach (AgendaEvent e in AgendaService.Events)
            {
                if (e.AllDay || _alertedEvents.Contains(e.Key)) continue;
                double until = (e.Start - now).TotalMinutes;
                if (until < 0 || until > minutes) continue;
                _alertedEvents.Add(e.Key);
                _eventAlert.Event = e;
                _alertView = _eventAlert;
                _alertUntil = now.AddMilliseconds(EventAlertMs);
                UpdateMode();
                return;
            }
        }

        /// <summary>
        /// Dispositivo Bluetooth conectado o con batería baja (fase F); sustituye al aviso que se esté viendo.
        /// </summary>
        private void ShowBluetoothAlert(BluetoothDevice device, bool lowBattery)
        {
            if (!Visible || (!lowBattery && !SuiteSettings.BluetoothConnectAlerts)) return;
            _bluetoothAlert.Device = device;
            _bluetoothAlert.LowBattery = lowBattery;
            _alertView = _bluetoothAlert;
            _alertUntil = DateTime.Now.AddMilliseconds(lowBattery ? LowBatteryAlertMs : BluetoothAlertMs);
            _contentOpacity = 0f;
            UpdateMode();
        }

        private void ShowCaptureAlert(CaptureMonitor.Device device, string app)
        {
            RenderSurface(); // Los puntos de la isla cerrada
            if (!SuiteSettings.CaptureAlerts || !Visible) return;
            _captureAlert.Device = device;
            _captureAlert.AppName = app;
            _alertView = _captureAlert;
            _alertUntil = DateTime.Now.AddMilliseconds(CaptureAlertMs);
            UpdateMode();
        }

        private void ShowVolume(float level, bool muted)
        {
            if (!Visible) return;
            _volumeAlert.Level = level;
            _volumeAlert.Muted = muted;
            _volumeAlert.DeviceName = _audio != null ? _audio.DefaultDeviceName : "";
            _alertView = _volumeAlert;
            _alertUntil = DateTime.Now.AddMilliseconds(VolumeAlertMs);
            UpdateMode();
        }

        private void PollMedia()
        {
            if (!Visible) return;

            MediaInfo media = MediaService.Current;
            if (media.IsPlaying && !media.SameTrackAs(_lastMedia) && SuiteSettings.IslandSongPeekMs > 0
                && SuiteSettings.IslandShowMusic && !HiddenExceptAlerts)
            {
                // Canción nueva: la isla se despliega unos segundos en la vista de música
                _viewIndex = _views.IndexOf(_music);
                _peekUntil = DateTime.Now.AddMilliseconds(SuiteSettings.IslandSongPeekMs);
            }
            _lastMedia = media;
            UpdateMode();
        }

        private bool IsHovered
        {
            get { return _hoverStart != null && (DateTime.Now - _hoverStart.Value).TotalMilliseconds >= (_hoverFromTucked ? TuckedDelayMs : ExpandDelayMs); }
        }

        /// <summary>
        /// Prioridad: aviso > expandida (ratón encima o canción nueva) > escondida (ventana encima) > compacta.
        /// </summary>
        private Mode DesiredMode()
        {
            DateTime now = DateTime.Now;
            if (now < _alertUntil) return Mode.Alert;
            // Sin "abrir al pasar el ratón", el ratón encima solo la mantiene abierta después de un clic
            bool hoverKeepsOpen = IsHovered && (SuiteSettings.IslandOpenOnHover || _mode == Mode.Expanded);
            if (hoverKeepsOpen || now < _peekUntil) return Mode.Expanded;
            if (_tuckRequested && !IsHovered) return Mode.Tucked; // Sigue escondida mientras se espera, sin tapar las pestañas
            return Mode.Compact;
        }

        private void UpdateMode()
        {
            Mode next = DesiredMode();
            if (next != _mode)
            {
                // Como Bloom: al abrirse, si suena algo, empieza en Música
                if (next == Mode.Expanded && _mode != Mode.Expanded && Attached && MediaService.Current.IsAvailable && MediaService.Current.IsPlaying)
                {
                    int music = _views.IndexOf(_music);
                    if (music >= 0 && IsTabEnabled(_music)) _viewIndex = music;
                }
                _mode = next;
                _contentOpacity = 0f; // El contenido nuevo aparece en cuanto la forma empieza a cambiar
                _frameTimer.Interval = 16;
                SetWheelHook(_mode == Mode.Expanded);
            }
            SyncShownView();
            RenderSurface();
        }

        /// <summary>
        /// La isla nunca tiene el foco (para no quitárselo a nadie), así que Windows no siempre le entrega la rueda.
        /// Mientras está expandida, un gancho de ratón de bajo nivel recoge la rueda sobre la isla y evita que
        /// se desplace la ventana de debajo. Fuera de ese estado el gancho no existe.
        /// </summary>
        private void SetWheelHook(bool enabled)
        {
            if (enabled && _wheelHook == IntPtr.Zero)
            {
                _wheelHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _wheelHookProc, NativeMethods.GetModuleHandle(null), 0);
                if (_wheelHook == IntPtr.Zero) Log.Error("[DynamicNotchModule] No se pudo instalar el gancho de la rueda del ratón");
            }
            else if (!enabled && _wheelHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_wheelHook);
                _wheelHook = IntPtr.Zero;
            }
        }

        private IntPtr OnLowLevelMouse(int code, IntPtr message, IntPtr data)
        {
            if (code >= 0 && message.ToInt32() == NativeMethods.WM_MOUSEWHEEL && _mode == Mode.Expanded)
            {
                NativeMethods.MSLLHOOKSTRUCT info = (NativeMethods.MSLLHOOKSTRUCT)System.Runtime.InteropServices.Marshal.PtrToStructure(data, typeof(NativeMethods.MSLLHOOKSTRUCT));
                Rectangle island = new Rectangle(Left + ShadowPad, Top + ShadowPad, PillW, PillH);
                if (island.Contains(info.pt.X, info.pt.Y))
                {
                    // Primero la vista (p. ej. rueda sobre una barra de volumen); si no la usa, cambia de pestaña
                    short delta = (short)(info.mouseData >> 16);
                    PointF point = new PointF(info.pt.X - island.X, info.pt.Y - island.Y - Px(ContentTop));
                    if (point.Y >= 0 && ActiveView.OnWheel(point, delta)) RenderSurface();
                    else SwitchView(delta < 0 ? 1 : -1);
                    _peekUntil = DateTime.Now.AddMilliseconds(InteractionPeekMs);
                    return new IntPtr(1); // Consumida: la ventana de debajo no se desplaza
                }
            }
            return NativeMethods.CallNextHookEx(_wheelHook, code, message, data);
        }

        /// <summary>
        /// Vistas que tienen algo que mostrar ahora; la actual se ajusta si dejó de estar disponible.
        /// </summary>
        private List<IslandView> AvailableViews()
        {
            List<IslandView> available = _views.FindAll(v => v.IsAvailable && IsTabEnabled(v));
            return available;
        }

        private IslandView CurrentExpandedView()
        {
            List<IslandView> available = AvailableViews();
            if (available.Count == 0) return _compact;
            IslandView preferred = _views[Math.Max(0, Math.Min(_viewIndex, _views.Count - 1))];
            return available.Contains(preferred) ? preferred : available[0];
        }

        private IslandView ActiveView
        {
            get
            {
                switch (_mode)
                {
                    case Mode.Alert: return _alertView ?? _alert;
                    case Mode.Expanded: return CurrentExpandedView();
                    default: return _compact;
                }
            }
        }

        /// <summary>
        /// Avisa a las vistas cuando empiezan o dejan de verse (la de sistema solo mide mientras se ve).
        /// </summary>
        private void SyncShownView()
        {
            IslandView active = ActiveView;
            if (active == _shownView) return;
            if (_shownView != null) _shownView.OnHidden();
            _shownView = active;
            _shownView.OnShown();
        }

        // Esquinas: panel y avisos grandes con radio fijo (22 px pegada arriba, como Bloom); compacta, píldora
        private float TargetRadius(SizeF target)
        {
            float radius;
            if (_mode == Mode.Expanded || (_mode == Mode.Alert && (ActiveView == _alert || ActiveView == _notificationAlert))) radius = Px(Attached ? 22 : PanelRadius);
            else if (_mode == Mode.Alert) radius = Px(22);
            else radius = target.Height / 2;
            return Math.Min(radius, target.Height / 2);
        }

        /// <summary>
        /// Forma del notch pegado arriba: borde superior recto contra la pantalla, esquinas de abajo redondeadas y dos
        /// "orejitas" cóncavas arriba que lo funden con el borde (como el notch de Bloom). closed=false deja abierto el
        /// lado de arriba, para el borde fino.
        /// </summary>
        private GraphicsPath NotchPath(RectangleF b, float radius, bool closed)
        {
            float ear = Math.Min(Px(8), b.Height / 2);
            float r = Math.Max(0.5f, Math.Min(radius, Math.Min(b.Width / 2, b.Height - ear)));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(b.X - 2 * ear, b.Y, 2 * ear, 2 * ear, 270, 90);
            path.AddLine(b.X, b.Y + ear, b.X, b.Bottom - r);
            path.AddArc(b.X, b.Bottom - 2 * r, 2 * r, 2 * r, 180, -90);
            path.AddLine(b.X + r, b.Bottom, b.Right - r, b.Bottom);
            path.AddArc(b.Right - 2 * r, b.Bottom - 2 * r, 2 * r, 2 * r, 90, -90);
            path.AddLine(b.Right, b.Bottom - r, b.Right, b.Y + ear);
            path.AddArc(b.Right, b.Y, 2 * ear, 2 * ear, 180, 90);
            if (closed) path.CloseFigure();
            return path;
        }

        private SizeF TargetSize()
        {
            if (_mode == Mode.Tucked) return new SizeF(Px(120), Px(6));
            SizeF size = ActiveView.BaseSize;
            if (_mode == Mode.Expanded) return new SizeF(Px(ActiveView.PanelWidth), Px(ContentTop + size.Height + PanelBottom));
            return new SizeF(Px(size.Width), Px(size.Height));
        }

        // Panel expandido (tablero 1): 440 px de ancho, pestañas a 12 px del borde, 38 px de alto,
        // 14 px hasta el contenido y 18 px de margen inferior
        private const float PanelWidth = 440;
        private const float TabsTop = 12;
        private const float TabsHeight = 38;
        private const float PanelRadius = 26;

        // Pegada arriba (notch de Bloom): sin barra de pestañas; los puntitos de las vistas van abajo
        private static bool Attached
        {
            get { return SuiteSettings.IslandStyle == SuiteSettings.IslandStyleKind.Attached; }
        }

        private static float ContentTop
        {
            get { return Attached ? 16 : TabsTop + TabsHeight + 14; }
        }

        private static float PanelBottom
        {
            get { return Attached ? 26 : 18; }
        }

        private readonly List<RectangleF> _tabRects = new List<RectangleF>();
        private readonly List<IslandView> _tabViews = new List<IslandView>();

        #endregion

        #region Animación y ratón

        private void OnFrame(object sender, EventArgs e)
        {
            if (!Visible) return;

            DateTime now = DateTime.Now;
            if ((now - _lastTuckCheck).TotalMilliseconds >= TuckCheckMs)
            {
                _lastTuckCheck = now;
                _tuckRequested = SuiteSettings.IslandMaximized != SuiteSettings.MaximizedBehavior.AlwaysShow && ForegroundCoversNotch();
            }

            // Ratón encima: cuenta el tiempo para expandir; al salir se mantiene un momento antes de plegar
            if (HoverZone().Contains(Cursor.Position))
            {
                if (_hoverStart == null)
                {
                    _hoverStart = now;
                    _hoverFromTucked = _mode == Mode.Tucked;
                }
                _hoverUntil = now.AddMilliseconds(SuiteSettings.IslandCloseDelayMs);
            }
            else if (_hoverStart != null && (now >= _hoverUntil || (_hoverFromTucked && !IsHovered)))
            {
                // Desde escondida, salir del borde antes de que se abra cancela la espera (p. ej. ir hacia una pestaña)
                _hoverStart = null;
            }

            // Terminó un aviso y quedan notificaciones en cola: la siguiente
            if (_notificationQueue.Count > 0 && now >= _alertUntil) ShowNextNotification();

            // Fin del temporizador: aviso que se queda hasta hacer clic (vuelve si otro aviso lo tapó)
            if (TimerService.Check()) _timerAlertPending = true;
            if (_timerAlertPending && _alertView != _timerAlert && now >= _alertUntil && _notificationQueue.Count == 0)
            {
                _alertView = _timerAlert;
                _alertUntil = DateTime.MaxValue;
                UpdateMode();
            }
            if (now.Second != _lastFullPaintSecond) CheckEventAlerts(now);

            // Nombre de la pestaña bajo el ratón (la barra solo muestra iconos)
            int hoveredTab = -1;
            if (_mode == Mode.Expanded)
            {
                Point cursor = Cursor.Position;
                PointF local = new PointF(cursor.X - Left - ShadowPad, cursor.Y - Top - ShadowPad);
                hoveredTab = _tabRects.FindIndex(r => r.Contains(local));
            }
            if (hoveredTab != _hoveredTab)
            {
                _hoveredTab = hoveredTab;
                RenderSurface();
            }

            if (DesiredMode() != _mode) UpdateMode();

            SizeF target = TargetSize();
            bool springing = StepSpring(ref _width, ref _widthVelocity, target.Width, WidthStiffness, WidthDamping)
                           | StepSpring(ref _height, ref _heightVelocity, target.Height, HeightStiffness, HeightDamping)
                           | StepSpring(ref _radius, ref _radiusVelocity, TargetRadius(target), RadiusStiffness, RadiusDamping);
            if (_contentOpacity < 1f) _contentOpacity = Math.Min(1f, _contentOpacity + FrameSeconds * 5f);

            if (springing) ApplyBounds();

            bool transitioning = springing || _contentOpacity < 1f;
            if (transitioning || now.Second != _lastFullPaintSecond || (_mode != Mode.Tucked && ActiveView.IsAnimated))
            {
                _lastFullPaintSecond = now.Second;
                RenderSurface();
            }

            // Fuera de las transiciones basta con ~10 fps para lo que se mueve y para seguir al ratón
            int interval = transitioning ? 16 : 100;
            if (_frameTimer.Interval != interval) _frameTimer.Interval = interval;
        }

        /// <summary>
        /// Avanza un paso del muelle amortiguado. Devuelve true mientras siga en movimiento.
        /// </summary>
        private static bool StepSpring(ref float value, ref float velocity, float target, float stiffness, float damping)
        {
            float acceleration = stiffness * (target - value) - damping * velocity;
            velocity += acceleration * FrameSeconds;
            value += velocity * FrameSeconds;

            if (Math.Abs(target - value) < 0.5f && Math.Abs(velocity) < 0.5f)
            {
                value = target;
                velocity = 0;
                return false;
            }
            return true;
        }

        private void OnIslandClick(object sender, MouseEventArgs e)
        {
            // Las vistas trabajan en coordenadas de la isla (sin el margen de la sombra)
            PointF point = new PointF(e.X - ShadowPad, e.Y - ShadowPad);

            if (_mode == Mode.Alert)
            {
                ActiveView.OnClick(point);       // Notificación: abre la app; fin de tiempo: siguiente fase
                if (ActiveView == _timerAlert) _timerAlertPending = false;
                _alertUntil = DateTime.MinValue; // Clic en un aviso: descartarlo
                UpdateMode();
                return;
            }
            if (_mode == Mode.Expanded)
            {
                // Clic en una pestaña: ir a esa vista
                for (int i = 0; i < _tabRects.Count; i++)
                {
                    if (_tabRects[i].Contains(point))
                    {
                        SelectView(_tabViews[i]);
                        return;
                    }
                }

                // Clic en el contenido: la vista trabaja en coordenadas de su zona (bajo las pestañas)
                PointF content = new PointF(point.X, point.Y - Px(ContentTop));
                if (content.Y >= 0 && ActiveView.OnClick(content))
                {
                    _peekUntil = DateTime.Now.AddMilliseconds(InteractionPeekMs); // Mantenerla abierta mientras se usa
                    RenderSurface();
                }
                return;
            }

            // Clic en la isla compacta o escondida: abrirla (luego el ratón encima la mantiene abierta)
            _peekUntil = DateTime.Now.AddMilliseconds(InteractionPeekMs);
            UpdateMode();
        }

        /// <summary>
        /// Pasa a la vista siguiente (+1) o anterior (-1) del carrusel.
        /// </summary>
        private void SwitchView(int step)
        {
            if (_mode != Mode.Expanded) return;

            List<IslandView> available = AvailableViews();
            if (available.Count < 2) return;

            int position = Math.Max(0, available.IndexOf(CurrentExpandedView()));
            position = (position + step + available.Count) % available.Count;
            SelectView(available[position]);
        }

        private void SelectView(IslandView view)
        {
            if (view == CurrentExpandedView()) return;
            _viewIndex = _views.IndexOf(view);
            _peekUntil = DateTime.Now.AddMilliseconds(InteractionPeekMs); // Recién elegida: que no se cierre al instante
            _contentOpacity = 0f;
            _frameTimer.Interval = 16;
            SyncShownView();
            RenderSurface();
        }

        /// <summary>
        /// ¿La ventana activa ocupa la zona de la isla? (navegador maximizado, editor pegado arriba...)
        /// </summary>
        private bool ForegroundCoversNotch()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero) return false;

            uint processId;
            NativeMethods.GetWindowThreadProcessId(foreground, out processId);
            if (processId == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return _tuckRequested;
            if (SystemMonitor.IsShellWindow(foreground)) return false;

            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(foreground, out rect)) return false;

            Rectangle screen = Screen.PrimaryScreen.Bounds;
            Rectangle zone = new Rectangle(screen.X + (screen.Width - (int)Px(440)) / 2, screen.Y, (int)Px(440), (int)Px(TopMargin + 38 + 10));
            return zone.IntersectsWith(Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom));
        }

        /// <summary>
        /// Zona donde el ratón cuenta como "encima de la isla": la propia isla con un pequeño margen,
        /// o una franja en el borde superior si está escondida.
        /// </summary>
        private Rectangle HoverZone()
        {
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            if (_mode == Mode.Tucked && HiddenExceptAlerts) return Rectangle.Empty; // Invisible: el ratón no la abre
            if (_mode == Mode.Tucked)
            {
                int stripWidth = (int)Px(260);
                return new Rectangle(screen.X + (screen.Width - stripWidth) / 2, screen.Y, stripWidth, Math.Max(1, (int)Px(TuckedEdgePx)));
            }
            int margin = (int)Px(8);
            return new Rectangle(Left + ShadowPad - margin, screen.Y, PillW + 2 * margin, Top + ShadowPad + PillH - screen.Y + margin);
        }

        private void ApplyBounds()
        {
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            int width = PillW;
            int height = PillH;

            // Al esconderse también sube hacia el borde: el margen superior acompaña a la altura durante la animación
            // Pegada arriba no hay margen: la isla nace del borde de la pantalla
            float openness = Math.Max(0f, Math.Min(1f, (height - Px(6)) / (Px(38) - Px(6))));
            int top = Attached ? 0 : (int)Math.Round(Px(TuckedTopMargin) + openness * (Px(TopMargin) - Px(TuckedTopMargin)));

            // La ventana es la isla más un margen alrededor para su sombra
            Bounds = new Rectangle(screen.X + (screen.Width - width) / 2 - ShadowPad, screen.Y + top - ShadowPad,
                width + 2 * ShadowPad, height + 2 * ShadowPad);
            RenderSurface();
        }

        private int ShadowPad
        {
            get { return (int)Px(20); }
        }

        // Tamaño actual de la isla (sin el margen de la sombra)
        private int PillW
        {
            get { return (int)Math.Round(_width); }
        }

        private int PillH
        {
            get { return (int)Math.Round(_height); }
        }

        #endregion

        #region Dibujo

        protected override byte SurfaceAlpha
        {
            get { return (byte)(SuiteSettings.IslandOpacityPercent * 255 / 100); }
        }

        protected override void DrawSurface(Graphics g)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            if (_mode == Mode.Tucked && HiddenExceptAlerts) return; // "Solo avisos": ni siquiera la rayita

            // Panel expandido y avisos: tarjeta con esquinas de 26 px (22 en el de volumen); compacta: píldora
            RectangleF bounds = new RectangleF(ShadowPad, ShadowPad, PillW - 1, PillH - 1);
            float radius = Math.Min(_radius > 0 ? _radius : TargetRadius(new SizeF(PillW, PillH)), bounds.Height / 2);
            DrawSoftShadow(g, bounds, radius, ShadowPad * 0.8f, Px(6), _mode == Mode.Tucked ? 90 : 200);

            // A partir de aquí todo se dibuja en coordenadas de la isla
            g.TranslateTransform(ShadowPad, ShadowPad);
            bounds.Offset(-ShadowPad, -ShadowPad);

            Color? viewBorder = _mode == Mode.Tucked ? null : ActiveView.BorderColor;
            // Pegada arriba: negro del notch de Bloom (86 %), salvo que el estilo de cristal pida más opacidad
            Color fillColor = Attached ? Color.FromArgb(Math.Max((int)Glass.Fill.A, 220), 0, 0, 0) : Glass.FillAtLeast(IslandPainter.Background.A);
            using (GraphicsPath path = Attached ? NotchPath(bounds, radius, true) : IslandPainter.RoundedRect(bounds, radius))
            using (GraphicsPath outline = Attached ? NotchPath(bounds, radius, false) : IslandPainter.RoundedRect(bounds, radius))
            using (SolidBrush fill = new SolidBrush(fillColor))
            using (Pen pen = new Pen(viewBorder ?? Glass.Border, 1))
            {
                g.FillPath(fill, path);
                // Sin contorno en ningún estilo (el usuario no lo quiere); solo el cristal Neón, que es justo ese halo
                if (SuiteSettings.GlassStyle == SuiteSettings.GlassStyleKind.Neon) g.DrawPath(pen, outline);
            }

            if (_mode == Mode.Tucked) return; // Solo la rayita, sin contenido

            // El contenido se dibuja a la escala de la vista destino y se funde mientras la forma cambia
            int alpha = (int)(255 * _contentOpacity);
            if (alpha <= 0) return;

            if (_mode != Mode.Expanded)
            {
                ActiveView.Draw(g, new SizeF(PillW, PillH), alpha, _painter);
                return;
            }

            if (Attached) DrawDots(g, alpha); else DrawTabs(g, alpha);
            g.TranslateTransform(0, Px(ContentTop));
            ActiveView.Draw(g, new SizeF(PillW, PillH - Px(ContentTop) - Px(PanelBottom)), alpha, _painter);
            g.TranslateTransform(0, -Px(ContentTop));
            if (!Attached) DrawTabTooltip(g, alpha);
        }

        /// <summary>
        /// Pegada arriba: un puntito por vista abajo del panel (el actual alargado), como en el notch de Bloom.
        /// Clic en un puntito va a esa vista; el nombre aparece junto a los puntitos al pasar el ratón.
        /// </summary>
        private void DrawDots(Graphics g, int alpha)
        {
            _tabRects.Clear();
            _tabViews.Clear();
            List<IslandView> available = AvailableViews();
            if (available.Count < 2) return;
            IslandView current = CurrentExpandedView();
            float dot = Px(6), activeDot = Px(16), gap = Px(5);
            float total = (available.Count - 1) * (dot + gap) + activeDot;
            float x = (PillW - total) / 2, y = PillH - Px(PanelBottom) / 2 - dot / 2;
            for (int i = 0; i < available.Count; i++)
            {
                bool active = available[i] == current;
                float w = active ? activeDot : dot;
                RectangleF shape = new RectangleF(x, y, w, dot);
                int dotAlpha = active ? alpha : i == _hoveredTab ? alpha * 180 / 255 : alpha * 80 / 255;
                IslandPainter.FillRounded(g, shape, dot / 2, Color.FromArgb(dotAlpha, Color.White));
                _tabRects.Add(RectangleF.Inflate(shape, gap / 2, Px(8))); // Zona de clic más generosa que el puntito
                _tabViews.Add(available[i]);
                x += w + gap;
            }
            if (_hoveredTab >= 0 && _hoveredTab < _tabViews.Count && _tabViews[_hoveredTab] != current)
            {
                string name = _tabViews[_hoveredTab].TabName;
                _painter.DrawText(g, name, _painter.Text11, IslandPainter.Fade(IslandPainter.Gray, alpha),
                    new RectangleF(x + Px(6), y - Px(6), Px(140), dot + Px(12)), StringAlignment.Near);
            }
        }

        /// <summary>
        /// Herramienta de desarrollo (--preview-island): dibuja cada estado de la isla a PNG sin mostrar la ventana,
        /// para revisar el diseño sin tener que mover el ratón sobre el escritorio.
        /// </summary>
        internal void RenderPreviews(string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            List<KeyValuePair<string, Action>> states = new List<KeyValuePair<string, Action>>();
            states.Add(new KeyValuePair<string, Action>("compacta", () => { _mode = Mode.Compact; }));
            // Música de ejemplo (con y sin letra), para revisar el diseño aunque no suene nada
            MediaInfo sample = new MediaInfo { Title = "Can't Stop", Artist = "Red Hot Chili Peppers", AppName = "Spotify", IsPlaying = true, IsAvailable = true,
                Accent = Color.FromArgb(120, 140, 230), Position = TimeSpan.FromSeconds(31), Duration = TimeSpan.FromSeconds(269), PositionSampledAt = DateTime.Now };
            const string sampleLrc = "[00:20.00] Can't stop, addicted to the shindig\n[00:23.00] Chop top, he says I'm gonna win big\n[00:26.00] Choose not a life of imitation\n" +
                "[00:29.00] Distant cousin to the reservation\n[00:32.00] Defunct the pistol that you pay for\n[00:35.00] This punk, the feeling that you stay for\n[00:38.00] In time I want to be your best friend";
            states.Add(new KeyValuePair<string, Action>("panel-musica-ejemplo", () => { MediaService.SetPreview(sample); LyricsService.SetPreview(sample, sampleLrc); _music.ShowLyrics = false; _mode = Mode.Expanded; _viewIndex = _views.IndexOf(_music); }));
            states.Add(new KeyValuePair<string, Action>("panel-musica-letra", () => { _music.ShowLyrics = true; _mode = Mode.Expanded; _viewIndex = _views.IndexOf(_music); }));
            foreach (IslandView view in AvailableViews())
            {
                IslandView v = view;
                states.Add(new KeyValuePair<string, Action>("panel-" + v.TabName.ToLowerInvariant(), () => { _mode = Mode.Expanded; _viewIndex = _views.IndexOf(v); }));
            }
            states.Add(new KeyValuePair<string, Action>("aviso-volumen", () =>
            {
                _mode = Mode.Alert; _alertView = _volumeAlert; _volumeAlert.Level = 0.82f; _volumeAlert.Muted = false;
                _volumeAlert.DeviceName = _audio != null ? _audio.DefaultDeviceName : "Altavoces";
            }));
            states.Add(new KeyValuePair<string, Action>("aviso-silencio", () => { _mode = Mode.Alert; _alertView = _volumeAlert; _volumeAlert.Muted = true; }));
            states.Add(new KeyValuePair<string, Action>("aviso-texto", () => { _mode = Mode.Alert; _alert.Text = "🛡️ Modo Privacidad Aero: ACTIVADO (Ctrl+Alt+P)"; _alertView = _alert; }));
            states.Add(new KeyValuePair<string, Action>("compacta-tiempo", () =>
            {
                TimerService.SetMode(TimerService.TimerMode.Pomodoro);
                TimerService.Toggle();
                TimerService.AddMinutes(-7);
                _mode = Mode.Compact;
            }));
            states.Add(new KeyValuePair<string, Action>("panel-tiempo-pomodoro", () => { _mode = Mode.Expanded; _viewIndex = _views.FindIndex(v => v is TimerView); }));
            states.Add(new KeyValuePair<string, Action>("aviso-tiempo", () => { _mode = Mode.Alert; _alertView = _timerAlert; }));
            states.Add(new KeyValuePair<string, Action>("aviso-bluetooth", () =>
            {
                _mode = Mode.Alert;
                _bluetoothAlert.Device = new BluetoothDevice { Name = "Galaxy Buds Pro", Battery = 92, Kind = BluetoothDevice.DeviceKind.Headphones, Connected = true };
                _bluetoothAlert.LowBattery = false;
                _alertView = _bluetoothAlert;
            }));
            states.Add(new KeyValuePair<string, Action>("aviso-bateria-baja", () =>
            {
                _mode = Mode.Alert;
                _bluetoothAlert.Device = new BluetoothDevice { Name = "MX Keys", Battery = 20, Kind = BluetoothDevice.DeviceKind.Keyboard, Connected = true };
                _bluetoothAlert.LowBattery = true;
                _alertView = _bluetoothAlert;
            }));
            states.Add(new KeyValuePair<string, Action>("aviso-evento", () =>
            {
                _mode = Mode.Alert;
                _eventAlert.Event = new AgendaEvent { Title = "Llamada con el equipo", Location = "Microsoft Teams", Start = DateTime.Now.AddMinutes(10),
                    End = DateTime.Now.AddMinutes(40), Color = IslandPainter.Sky, JoinUrl = "https://teams.microsoft.com/l/meetup-join/x" };
                _alertView = _eventAlert;
            }));
            foreach (string body in new[] { "", "Hola mi amor", "¿Vienes a cenar hoy? Ya está casi lista la comida, avísame si llegas tarde porque si no empezamos sin ti" })
            {
                string text = body;
                states.Add(new KeyValuePair<string, Action>("aviso-notificacion-" + (text.Length == 0 ? 0 : text.Length < 20 ? 1 : 2), () =>
                {
                    _mode = Mode.Alert;
                    _notificationAlert.Notification = new IslandNotification
                    {
                        AppId = "5319275A.51895FA4EA97F_cv1g1gvanyjgm!App", AppName = "WhatsApp Beta", Title = "Yesi", Body = text,
                        Time = DateTime.Now, Icon = NotificationService.IconFor("5319275A.51895FA4EA97F_cv1g1gvanyjgm!App"),
                        Accent = Color.FromArgb(37, 211, 102)
                    };
                    _notificationAlert.Pending = text.Length > 20 ? 2 : 0;
                    _alertView = _notificationAlert;
                }));
            }

            foreach (KeyValuePair<string, Action> state in states)
            {
                state.Value();
                SyncShownView();
                // Un primer dibujo de prueba carga los datos que deciden el tamaño (lista de audio, etc.)
                using (Bitmap warmUp = new Bitmap(1, 1))
                using (Graphics warmUpGraphics = Graphics.FromImage(warmUp))
                {
                    ActiveView.Draw(warmUpGraphics, new SizeF(Px(PanelWidth), Px(200)), 255, _painter);
                }
                SizeF size = TargetSize();
                _width = size.Width;
                _height = size.Height;
                _radius = TargetRadius(size);
                _contentOpacity = 1f;
                using (Bitmap image = new Bitmap(PillW + 2 * ShadowPad, PillH + 2 * ShadowPad))
                {
                    using (Graphics g = Graphics.FromImage(image))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.Clear(Color.FromArgb(28, 36, 51)); // Fondo de los mockups
                        DrawSurface(g);
                    }
                    image.Save(System.IO.Path.Combine(folder, state.Key + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
        }

        /// <summary>
        /// Barra de pestañas del panel (tablero 1): una por vista disponible, la actual resaltada.
        /// </summary>
        private void DrawTabs(Graphics g, int alpha)
        {
            _tabRects.Clear();
            _tabViews.Clear();
            List<IslandView> available = AvailableViews();
            IslandView current = CurrentExpandedView();

            RectangleF bar = new RectangleF(Px(18), Px(TabsTop), PillW - Px(36), Px(TabsHeight));
            IslandPainter.FillRounded(g, bar, Px(12), Color.FromArgb(alpha * 13 / 255, Color.White));

            // Opción A del tablero D4: iconos de 38 px y la pestaña activa ocupa el resto con icono y nombre
            float inner = Px(4);
            float iconWidth = Px(38);
            float activeWidth = bar.Width - 2 * inner - (available.Count - 1) * (iconWidth + inner);
            float x = bar.X + inner;
            for (int i = 0; i < available.Count; i++)
            {
                bool active = available[i] == current;
                RectangleF tab = new RectangleF(x, bar.Y + inner, active ? activeWidth : iconWidth, bar.Height - 2 * inner);
                x = tab.Right + inner;
                Color color = active ? Color.FromArgb(alpha, Color.White) : IslandPainter.Fade(IslandPainter.Gray, alpha);
                if (active)
                {
                    IslandPainter.FillRounded(g, tab, Px(9), Color.FromArgb(alpha * 36 / 255, Color.White));
                    // El ancho del icono se mide: algunos glifos (el monitor de Sistema) son más anchos que 16 px y se cortaban
                    SizeF name = g.MeasureString(available[i].TabName, _painter.Tab12);
                    float glyphWidth = g.MeasureString(available[i].TabGlyph, _painter.Glyph16).Width;
                    float contentWidth = glyphWidth + Px(4) + name.Width;
                    float left = tab.X + (tab.Width - contentWidth) / 2;
                    _painter.DrawText(g, available[i].TabGlyph, _painter.Glyph16, color, new RectangleF(left, tab.Y, glyphWidth, tab.Height), StringAlignment.Center);
                    _painter.DrawText(g, available[i].TabName, _painter.Tab12, color, new RectangleF(left + glyphWidth + Px(4), tab.Y, name.Width + Px(4), tab.Height), StringAlignment.Near);
                }
                else
                {
                    if (i == _hoveredTab) IslandPainter.FillRounded(g, tab, Px(9), Color.FromArgb(alpha * 15 / 255, Color.White));
                    _painter.DrawText(g, available[i].TabGlyph, _painter.Glyph16, color, tab, StringAlignment.Center);
                }
                _tabRects.Add(tab);
                _tabViews.Add(available[i]);
            }
        }

        /// <summary>
        /// Nombre de la pestaña bajo el ratón, en una etiqueta justo debajo de la barra (encima del contenido).
        /// </summary>
        private void DrawTabTooltip(Graphics g, int alpha)
        {
            RectangleF bar = new RectangleF(Px(18), Px(TabsTop), PillW - Px(36), Px(TabsHeight));
            if (_hoveredTab >= 0 && _hoveredTab < _tabRects.Count && _tabViews[_hoveredTab] != CurrentExpandedView())
            {
                RectangleF hovered = _tabRects[_hoveredTab];
                string name = _tabViews[_hoveredTab].TabName;
                SizeF size = g.MeasureString(name, _painter.Text12);
                RectangleF label = new RectangleF(hovered.X + hovered.Width / 2 - size.Width / 2 - Px(8), bar.Bottom + Px(2), size.Width + Px(16), Px(20));
                if (label.Right > bar.Right) label.X = bar.Right - label.Width;
                if (label.X < bar.X) label.X = bar.X;
                IslandPainter.FillRounded(g, label, Px(10), Color.FromArgb(alpha * 240 / 255, 42, 48, 64));
                _painter.DrawText(g, name, _painter.Text12, Color.FromArgb(alpha, Color.White), label, StringAlignment.Center);
            }
        }

        private float Px(float value)
        {
            return value * _scale;
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _frameTimer.Dispose();
                SetWheelHook(false);
                _mediaTimer.Dispose();
                if (_shownView != null) _shownView.OnHidden();
                _painter.Dispose();
                SuiteSettings.Changed -= _settingsChanged;
                NotificationService.Arrived -= _notificationArrived;
                CaptureMonitor.Started -= _captureStarted;
                CaptureMonitor.Changed -= _captureChanged;
                AgendaService.Updated -= _agendaUpdated;
                BluetoothService.Connected -= _bluetoothConnected;
                BluetoothService.LowBattery -= _bluetoothLow;
                if (_audio != null) _audio.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
