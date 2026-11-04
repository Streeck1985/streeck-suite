using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Islas sobre la barra de tareas REAL de Windows (mockup I3). La suite mete streeck_taskbar_*.dll en el Explorador
    /// (ver native\StreeckTaskbar\tap.cpp) y le pasa los ajustes del panel por barra_skin.ini y un evento con nombre.
    /// Al apagarlo o al salir, la DLL devuelve la barra normal. Si el Explorador se reinicia, se vuelve a meter.
    /// </summary>
    public class TaskbarIslandModule : IModule
    {
        public string Id { get { return "taskbar_islands"; } }
        public string Name { get { return "Islas en la barra de Windows"; } }
        public string Version { get { return "3.0.0"; } }
        public bool IsEnabled { get; set; }

        private const string ReloadEventName = @"Local\StreeckSuite.TaskbarSkin";
        private static readonly string IniFile = Path.Combine(Path.GetDirectoryName(Log.FilePath), "barra_skin.ini");

        private ExplorerWatcher _watcher;
        private Action _settingsChanged;
        private SuiteSettings.TaskbarSearchKind _appliedSearch;

        public TaskbarIslandModule()
        {
            IsEnabled = true;
        }

        public void Initialize(EventBus eventBus)
        {
        }

        public void Start()
        {
            _appliedSearch = SuiteSettings.TaskbarSearch;
            _watcher = new ExplorerWatcher();
            _watcher.ExplorerRestarted += () =>
            {
                // El Explorador nuevo tarda en tener su barra lista
                System.Windows.Forms.Timer later = new System.Windows.Forms.Timer { Interval = 4000 };
                later.Tick += (s, e) => { later.Dispose(); Apply(); };
                later.Start();
            };
            _settingsChanged = () =>
            {
                if (SuiteSettings.TaskbarSearch != _appliedSearch)
                {
                    _appliedSearch = SuiteSettings.TaskbarSearch;
                    ApplySearchMode(_appliedSearch);
                }
                Apply();
            };
            SuiteSettings.Changed += _settingsChanged;
            Apply();
        }

        public void Stop()
        {
            SuiteSettings.Changed -= _settingsChanged;
            WriteSettings(false);
            Signal();
            if (_watcher != null) { _watcher.Dispose(); _watcher = null; }
            Log.Info("[Taskbar] Barra de Windows restaurada");
        }

        // Escribe los ajustes, mete la DLL si aún no está y le avisa
        private void Apply()
        {
            WriteSettings(SuiteSettings.TaskbarEnabled);
            if (SuiteSettings.TaskbarEnabled)
            {
                string result = Inject();
                if (result != null) Log.Error("[Taskbar] No se pudo entrar a la barra: {0}", result);
            }
            // La DLL tarda un momento en conectarse la primera vez: se avisa ahora y otra vez en unos segundos
            Signal();
            ThreadPool.QueueUserWorkItem(_ => { Thread.Sleep(3000); Signal(); });
        }

        private static void WriteSettings(bool active)
        {
            try
            {
                File.WriteAllLines(IniFile, new[]
                {
                    "activo=" + (active ? 1 : 0),
                    "formato=" + (int)SuiteSettings.TaskbarFormat,
                    "separacion=" + SuiteSettings.TaskbarGap,
                    "extremos=" + (SuiteSettings.TaskbarEdges ? 1 : 0),
                    "cristal=" + (int)SuiteSettings.GlassStyle,
                    "miniplayer=" + (SuiteSettings.TaskbarMiniPlayer ? 1 : 0),
                    "skin=" + SuiteSettings.TaskbarSkin,
                    // Las islas de la barra imitan al notch: su color y su opacidad
                    "color=" + SuiteSettings.IslandColor,
                    "opacidad=" + SuiteSettings.IslandOpacityPercent,
                    // Icono del botón de Inicio (la imagen la prepara StartIconRenderer; los ojitos los dibuja la DLL)
                    "inicio_icono=" + SuiteSettings.StartIcon,
                    "inicio_tamano=" + SuiteSettings.StartIconSize,
                    "inicio_efecto=" + SuiteSettings.StartIconHover,
                    "inicio_clic=" + (SuiteSettings.StartButtonOpensSuite ? 1 : 0),
                    "inicio_archivo=" + (StartIconRenderer.PrepareFile(SuiteSettings.StartIcon) ?? ""),
                });
            }
            catch (Exception ex)
            {
                Log.Error("[Taskbar] No se pudieron guardar los ajustes de la barra: {0}", ex.Message);
            }
        }

        private static void Signal()
        {
            try
            {
                using (EventWaitHandle reload = new EventWaitHandle(false, EventResetMode.AutoReset, ReloadEventName)) reload.Set();
            }
            catch (Exception ex)
            {
                Log.Error("[Taskbar] No se pudo avisar a la barra: {0}", ex.Message);
            }
        }

        // Búsqueda de la barra: es el ajuste propio de Windows (0 oculta, 1 icono, 2 cuadro completo)
        private static void ApplySearchMode(SuiteSettings.TaskbarSearchKind kind)
        {
            int mode = kind == SuiteSettings.TaskbarSearchKind.Hidden ? 0 : kind == SuiteSettings.TaskbarSearchKind.Icon ? 1 : 2;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Search"))
                {
                    key.SetValue("SearchboxTaskbarMode", mode, RegistryValueKind.DWord);
                }
                IntPtr result;
                SendMessageTimeout((IntPtr)0xFFFF, 0x001A, IntPtr.Zero, "TraySettings", 2, 1000, out result);
            }
            catch (Exception ex)
            {
                Log.Error("[Taskbar] No se pudo cambiar la búsqueda de la barra: {0}", ex.Message);
            }
        }

        #region Entrar al Explorador

        // Windows carga la DLL en el Explorador al poner un gancho de ventana en el hilo de su barra; la DLL se fija
        // sola y el gancho se quita enseguida. Si ya estaba dentro, no hace nada (lo controla la propia DLL).
        private static string Inject()
        {
            string folder = Path.GetDirectoryName(Application.ExecutablePath);
            string dll = Directory.GetFiles(folder, "streeck_taskbar_*.dll").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).LastOrDefault();
            if (dll == null) return "falta streeck_taskbar_*.dll junto a la suite (compílala con native\\build_tap.ps1)";

            IntPtr module = LoadLibrary(dll);
            if (module == IntPtr.Zero) return "LoadLibrary falló (" + Marshal.GetLastWin32Error() + ")";
            IntPtr hookProc = GetProcAddress(module, "StreeckHookProc");
            if (hookProc == IntPtr.Zero) return "la DLL no tiene StreeckHookProc";
            IntPtr tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return "no se encontró la barra de Windows";
            uint pid;
            uint thread = GetWindowThreadProcessId(tray, out pid);
            IntPtr hook = SetWindowsHookEx(WhCallWndProc, hookProc, module, thread);
            if (hook == IntPtr.Zero) return "SetWindowsHookEx falló (" + Marshal.GetLastWin32Error() + ")";
            IntPtr ignored;
            SendMessageTimeout(tray, 0, IntPtr.Zero, IntPtr.Zero, 2, 3000, out ignored); // WM_NULL: obliga a cargarla ya
            Thread.Sleep(300);
            UnhookWindowsHookEx(hook);
            return null;
        }

        private const int WhCallWndProc = 4;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibrary(string path);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, IntPtr proc, IntPtr module, uint threadId);

        [DllImport("user32.dll")]
        private static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

        /// <summary>
        /// Ventana invisible que recibe "TaskbarCreated" cuando el Explorador se reinicia.
        /// </summary>
        private sealed class ExplorerWatcher : NativeWindow, IDisposable
        {
            private static readonly int TaskbarCreated = RegisterWindowMessage("TaskbarCreated");
            public event Action ExplorerRestarted;

            public ExplorerWatcher()
            {
                CreateHandle(new CreateParams { Caption = "StreeckSuite.ExplorerWatcher" });
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == TaskbarCreated && ExplorerRestarted != null) ExplorerRestarted();
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                DestroyHandle();
            }

            [DllImport("user32.dll", CharSet = CharSet.Unicode)]
            private static extern int RegisterWindowMessage(string message);
        }

        #endregion
    }
}
