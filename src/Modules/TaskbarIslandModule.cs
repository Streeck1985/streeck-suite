using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Barra de tareas de Streeck (fase I): muestra la barra de islas, le reserva su franja a las ventanas maximizadas
    /// (AppBar) y oculta la barra de Windows mientras está activa. Al salir, al desactivarla en Ajustes o si la suite
    /// falla, la barra de Windows vuelve tal como estaba (el estado original se guarda en barra_estado.txt).
    /// </summary>
    public class TaskbarIslandModule : IModule
    {
        public string Id { get { return "taskbar_islands"; } }
        public string Name { get { return "Barra de tareas en islas"; } }
        public string Version { get { return "2.0.0"; } }
        public bool IsEnabled { get; set; }

        private static readonly string StateFile = Path.Combine(Path.GetDirectoryName(Log.FilePath), "barra_estado.txt");

        private EventBus _eventBus;
        private TaskbarWindow _bar;
        private Timer _guard;
        private Action _settingsChanged;
        private bool _active, _gameMode, _fullscreen;
        private DateTime _windowsTrayUntil = DateTime.MinValue;

        public TaskbarIslandModule()
        {
            IsEnabled = true;
        }

        public void Initialize(EventBus eventBus)
        {
            _eventBus = eventBus;
            _eventBus.Subscribe(Events.GameModeActive, payload => OnUi(() => { _gameMode = payload is bool && (bool)payload; UpdateVisibility(); }));
            _eventBus.Subscribe(Events.TaskbarShowWindowsTray, payload => OnUi(ShowWindowsTray));
        }

        public void Start()
        {
            _bar = new TaskbarWindow(_eventBus);
            IntPtr handle = _bar.Handle; // Manejador listo para recibir los avisos de la AppBar aunque esté oculta
            _bar.AppBarNotify += OnAppBarNotify;
            _bar.ExplorerRestarted += () => { if (_active) { HideWindowsTaskbar(); RegisterAppBar(); } };

            // Si la suite falla o el sistema se apaga, la barra de Windows no se queda escondida
            AppDomain.CurrentDomain.UnhandledException += (s, e) => RestoreWindowsTaskbar();
            AppDomain.CurrentDomain.ProcessExit += (s, e) => RestoreWindowsTaskbar();

            // Explorer a veces vuelve a mostrar su barra (cambios de pantalla, tecla Windows): se vuelve a ocultar
            _guard = new Timer { Interval = 1000 };
            _guard.Tick += (s, e) =>
            {
                if (_active && DateTime.Now > _windowsTrayUntil) HideWindowsTaskbar();
            };
            _guard.Start();

            _settingsChanged = () => OnUi(Apply);
            SuiteSettings.Changed += _settingsChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
            Apply();
        }

        public void Stop()
        {
            SuiteSettings.Changed -= _settingsChanged;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            if (_guard != null) { _guard.Dispose(); _guard = null; }
            Deactivate();
            if (_bar != null) { _bar.Dispose(); _bar = null; }
        }

        private void OnDisplayChanged(object sender, EventArgs e) { OnUi(Apply); }

        private void OnUi(Action action)
        {
            if (_bar != null && !_bar.IsDisposed && _bar.IsHandleCreated) _bar.BeginInvoke(action);
        }

        // Activa o desactiva la barra según el interruptor y vuelve a colocarla (formato, separación, cristal)
        private void Apply()
        {
            if (_bar == null) return;
            if (SuiteSettings.TaskbarEnabled && !_active) Activate();
            else if (!SuiteSettings.TaskbarEnabled && _active) Deactivate();
            if (_active)
            {
                RegisterAppBar();
                _bar.Relayout();
            }
            UpdateVisibility();
        }

        private void Activate()
        {
            SaveOriginalState();
            HideWindowsTaskbar();
            _active = true;
            RegisterAppBar();
            Log.Info("[Taskbar] Barra de Streeck activa");
        }

        private void Deactivate()
        {
            if (!_active) return;
            _active = false;
            UnregisterAppBar();
            if (_bar != null) _bar.Hide();
            RestoreWindowsTaskbar();
            Log.Info("[Taskbar] Barra de Windows restaurada");
        }

        private void UpdateVisibility()
        {
            if (_bar == null) return;
            bool show = _active && !_gameMode && !_fullscreen;
            if (show && !_bar.Visible) { _bar.Show(); _bar.Relayout(); }
            else if (!show && _bar.Visible) _bar.Hide();
        }

        private void OnAppBarNotify(int code, IntPtr lParam)
        {
            if (code == AbnFullscreenApp)
            {
                _fullscreen = lParam != IntPtr.Zero;
                UpdateVisibility();
            }
            else if (code == AbnPosChanged && _active)
            {
                RegisterAppBar();
            }
        }

        // ⌃: no se pueden copiar los iconos de otros programas, así que se muestra un momento la barra de Windows
        // con su bandeja de iconos ocultos abierta (Win + B selecciona la flecha y Enter la abre)
        private void ShowWindowsTray()
        {
            if (!_active) return;
            _windowsTrayUntil = DateTime.Now.AddSeconds(12);
            foreach (IntPtr tray in TrayWindows()) ShowWindow(tray, SwShowNa);
            Timer later = new Timer { Interval = 250 };
            later.Tick += (s, e) =>
            {
                later.Dispose();
                NativeMethods.keybd_event(0x5B, 0, 0, UIntPtr.Zero);
                NativeMethods.keybd_event(0x42, 0, 0, UIntPtr.Zero);
                NativeMethods.keybd_event(0x42, 0, 2, UIntPtr.Zero);
                NativeMethods.keybd_event(0x5B, 0, 2, UIntPtr.Zero);
                NativeMethods.keybd_event(0x0D, 0, 0, UIntPtr.Zero);
                NativeMethods.keybd_event(0x0D, 0, 2, UIntPtr.Zero);
            };
            later.Start();
        }

        #region Barra de Windows

        private static IntPtr[] TrayWindows()
        {
            System.Collections.Generic.List<IntPtr> list = new System.Collections.Generic.List<IntPtr>();
            IntPtr main = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (main != IntPtr.Zero) list.Add(main);
            IntPtr secondary = IntPtr.Zero;
            while ((secondary = NativeMethods.FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) list.Add(secondary);
            return list.ToArray();
        }

        // Se guarda solo la primera vez: si la suite se cerró a la fuerza, el archivo conserva el estado verdadero
        private static void SaveOriginalState()
        {
            if (File.Exists(StateFile)) return;
            try
            {
                APPBARDATA data = NewData(NativeMethods.FindWindow("Shell_TrayWnd", null));
                int state = (int)SHAppBarMessage(AbmGetState, ref data);
                File.WriteAllText(StateFile, state.ToString());
            }
            catch (Exception ex)
            {
                Log.Error("[Taskbar] No se pudo guardar el estado de la barra de Windows: {0}", ex.Message);
            }
        }

        // Ocultar automáticamente hace que Windows deje de reservar su franja; además se esconde para que no asome
        private static void HideWindowsTaskbar()
        {
            IntPtr main = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (main == IntPtr.Zero) return;
            APPBARDATA data = NewData(main);
            if (((int)SHAppBarMessage(AbmGetState, ref data) & AbsAutoHide) == 0)
            {
                data.lParam = (IntPtr)AbsAutoHide;
                SHAppBarMessage(AbmSetState, ref data);
            }
            foreach (IntPtr tray in TrayWindows())
            {
                if (IsWindowVisible(tray)) ShowWindow(tray, SwHide);
            }
        }

        private static readonly object RestoreLock = new object();

        internal static void RestoreWindowsTaskbar()
        {
            lock (RestoreLock)
            {
                if (!File.Exists(StateFile)) return;
                try
                {
                    int state;
                    if (!int.TryParse(File.ReadAllText(StateFile).Trim(), out state)) state = AbsAlwaysOnTop;
                    IntPtr main = NativeMethods.FindWindow("Shell_TrayWnd", null);
                    if (main != IntPtr.Zero)
                    {
                        APPBARDATA data = NewData(main);
                        data.lParam = (IntPtr)state;
                        SHAppBarMessage(AbmSetState, ref data);
                    }
                    foreach (IntPtr tray in TrayWindows()) ShowWindow(tray, SwShowNa);
                    File.Delete(StateFile);
                }
                catch (Exception ex)
                {
                    Log.Error("[Taskbar] No se pudo restaurar la barra de Windows: {0}", ex.Message);
                }
            }
        }

        #endregion

        #region AppBar propia

        private bool _registered;

        private void RegisterAppBar()
        {
            APPBARDATA data = NewData(_bar.Handle);
            if (!_registered)
            {
                data.uCallbackMessage = TaskbarWindow.AppBarCallback;
                if (SHAppBarMessage(AbmNew, ref data) == IntPtr.Zero) { Log.Error("[Taskbar] No se pudo registrar la AppBar"); return; }
                _registered = true;
            }
            Screen screen = Screen.PrimaryScreen;
            data.uEdge = AbeBottom;
            data.rc.Left = screen.Bounds.Left;
            data.rc.Right = screen.Bounds.Right;
            data.rc.Bottom = screen.Bounds.Bottom;
            data.rc.Top = screen.Bounds.Bottom - _bar.ReservedHeight;
            SHAppBarMessage(AbmQueryPos, ref data);
            data.rc.Top = data.rc.Bottom - _bar.ReservedHeight;
            SHAppBarMessage(AbmSetPos, ref data);
        }

        private void UnregisterAppBar()
        {
            if (!_registered || _bar == null) return;
            APPBARDATA data = NewData(_bar.Handle);
            SHAppBarMessage(AbmRemove, ref data);
            _registered = false;
        }

        #endregion

        #region Win32

        private const int AbmNew = 0, AbmRemove = 1, AbmQueryPos = 2, AbmSetPos = 3, AbmGetState = 4, AbmSetState = 10;
        private const int AbnPosChanged = 1, AbnFullscreenApp = 2;
        private const int AbsAutoHide = 1, AbsAlwaysOnTop = 2, AbeBottom = 3;
        private const int SwHide = 0, SwShowNa = 8;

        [StructLayout(LayoutKind.Sequential)]
        private struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uCallbackMessage;
            public int uEdge;
            public NativeMethods.RECT rc;
            public IntPtr lParam;
        }

        private static APPBARDATA NewData(IntPtr hwnd)
        {
            APPBARDATA data = new APPBARDATA();
            data.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            data.hWnd = hwnd;
            return data;
        }

        [DllImport("shell32.dll")]
        private static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        #endregion
    }
}
