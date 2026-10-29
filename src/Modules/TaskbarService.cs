using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Botón de la isla de apps (fase I): una app anclada y/o con ventanas abiertas, con su icono original.
    /// </summary>
    internal class TaskbarApp
    {
        public string Key;              // Id del menú Inicio (anclada) o AUMID/ruta del .exe (solo abierta)
        public string Name;
        public bool Pinned;
        public ShellImage.App StartApp;  // Para abrirla si no tiene ventanas
        public string ExePath;
        public Bitmap Icon;
        public List<IntPtr> Windows = new List<IntPtr>();
    }

    /// <summary>
    /// Apps de la barra de tareas (fase I): las ancladas (al principio, las de la barra de Windows) y las que tienen
    /// ventanas abiertas, agrupadas por app como en Windows (por AUMID de la ventana o por el .exe). Iconos
    /// originales: los del menú Inicio o los del propio ejecutable.
    /// </summary>
    internal static class TaskbarService
    {
        private static readonly string PinnedFile = Path.Combine(Path.GetDirectoryName(Log.FilePath), "barra_anclados.txt");
        private static readonly Dictionary<string, Bitmap> ExeIcons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, string> ProcessPaths = new Dictionary<int, string>();
        private static List<string> _pinned;

        #region Win32

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int cmd);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, int id);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(IntPtr hwnd, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out CoreAudio.IPropertyStore store);

        private const uint GW_OWNER = 4;
        private const int WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_NOACTIVATE = 0x08000000;
        private const int DWMWA_CLOAKED = 14;
        private const int SW_RESTORE = 9, SW_MINIMIZE = 6;
        private const uint WM_CLOSE = 0x10;
        private static readonly CoreAudio.PROPERTYKEY AppUserModelId = new CoreAudio.PROPERTYKEY(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

        #endregion

        /// <summary>
        /// Lista actual: primero las ancladas (con o sin ventanas), luego las demás abiertas en orden de aparición.
        /// </summary>
        public static List<TaskbarApp> Snapshot()
        {
            LoadPinned();
            List<ShellImage.App> all = StartMenuService.AllApps;
            List<TaskbarApp> apps = new List<TaskbarApp>();
            foreach (string id in _pinned)
            {
                ShellImage.App start = all.Find(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
                if (start == null) continue;
                apps.Add(new TaskbarApp { Key = start.Id, Name = start.Name, Pinned = true, StartApp = start, ExePath = StartMenuService.ExePath(start.Id), Icon = NotificationService.IconFor(start.Id) });
            }

            int ownProcess = Process.GetCurrentProcess().Id;
            List<IntPtr> windows = new List<IntPtr>();
            EnumWindows((hwnd, lParam) => { if (IsTaskbarWindow(hwnd)) windows.Add(hwnd); return true; }, IntPtr.Zero);
            foreach (IntPtr hwnd in windows)
            {
                uint processId;
                NativeMethods.GetWindowThreadProcessId(hwnd, out processId);
                if (processId == ownProcess) continue;
                string exe = ProcessPath((int)processId);
                string aumid = WindowAppId(hwnd);

                TaskbarApp app = apps.Find(a =>
                    (aumid != null && string.Equals(a.Key, aumid, StringComparison.OrdinalIgnoreCase)) ||
                    (exe != null && a.ExePath != null && string.Equals(a.ExePath, exe, StringComparison.OrdinalIgnoreCase) && aumid == null) ||
                    (exe != null && !a.Pinned && string.Equals(a.Key, exe, StringComparison.OrdinalIgnoreCase)));
                if (app == null)
                {
                    ShellImage.App start = aumid != null ? all.Find(a => string.Equals(a.Id, aumid, StringComparison.OrdinalIgnoreCase)) : null;
                    if (start == null && exe != null) start = all.Find(a => string.Equals(StartMenuService.ExePath(a.Id), exe, StringComparison.OrdinalIgnoreCase));
                    app = new TaskbarApp
                    {
                        Key = start != null ? start.Id : (aumid ?? exe ?? hwnd.ToString()),
                        Name = start != null ? start.Name : WindowTitle(hwnd),
                        StartApp = start,
                        ExePath = exe,
                        Icon = start != null ? NotificationService.IconFor(start.Id) : ExeIcon(exe)
                    };
                    apps.Add(app);
                }
                app.Windows.Add(hwnd);
            }
            return apps;
        }

        // Las mismas reglas que usa la barra de Windows para decidir qué ventanas tienen botón
        private static bool IsTaskbarWindow(IntPtr hwnd)
        {
            if (!IsWindowVisible(hwnd) || GetWindowTextLength(hwnd) == 0) return false;
            int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
            bool appWindow = (ex & WS_EX_APPWINDOW) != 0;
            if ((ex & WS_EX_TOOLWINDOW) != 0 && !appWindow) return false;
            if ((ex & WS_EX_NOACTIVATE) != 0 && !appWindow) return false;
            if (GetWindow(hwnd, GW_OWNER) != IntPtr.Zero && !appWindow) return false;
            int cloaked;
            if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int)) == 0 && cloaked != 0) return false; // Otro escritorio virtual o app suspendida
            return true;
        }

        public static string WindowTitle(IntPtr hwnd)
        {
            StringBuilder text = new StringBuilder(GetWindowTextLength(hwnd) + 1);
            GetWindowText(hwnd, text, text.Capacity);
            return text.ToString();
        }

        private static string WindowAppId(IntPtr hwnd)
        {
            CoreAudio.IPropertyStore store = null;
            try
            {
                Guid iid = typeof(CoreAudio.IPropertyStore).GUID;
                if (SHGetPropertyStoreForWindow(hwnd, ref iid, out store) != 0 || store == null) return null;
                CoreAudio.PROPERTYKEY key = AppUserModelId;
                CoreAudio.PROPVARIANT value;
                if (store.GetValue(ref key, out value) != 0) return null;
                string id = value.AsString();
                CoreAudio.PropVariantClear(ref value);
                return string.IsNullOrEmpty(id) ? null : id;
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                if (store != null) Marshal.ReleaseComObject(store);
            }
        }

        private static string ProcessPath(int processId)
        {
            string path;
            if (ProcessPaths.TryGetValue(processId, out path)) return path;
            IntPtr process = OpenProcess(0x1000, false, processId); // PROCESS_QUERY_LIMITED_INFORMATION
            if (process != IntPtr.Zero)
            {
                StringBuilder name = new StringBuilder(1024);
                int size = name.Capacity;
                if (QueryFullProcessImageName(process, 0, name, ref size)) path = name.ToString();
                CloseHandle(process);
            }
            ProcessPaths[processId] = path;
            return path;
        }

        // Icono original del ejecutable (el mismo que enseña el Explorador)
        private static Bitmap ExeIcon(string exe)
        {
            if (exe == null) return null;
            Bitmap icon;
            if (!ExeIcons.TryGetValue(exe, out icon))
            {
                icon = ShellImage.Get(exe, 64, ShellImage.IconOnly);
                ExeIcons[exe] = icon;
            }
            return icon;
        }

        #region Acciones

        /// <summary>
        /// Clic en una app: abrirla si no tiene ventanas; si su ventana está delante, minimizarla; si no, traerla.
        /// Con varias ventanas, cada clic pasa a la siguiente.
        /// </summary>
        public static void Activate(TaskbarApp app)
        {
            if (app.Windows.Count == 0)
            {
                Launch(app);
                return;
            }
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            int index = app.Windows.IndexOf(foreground);
            if (index >= 0 && app.Windows.Count == 1)
            {
                ShowWindow(foreground, SW_MINIMIZE);
                return;
            }
            IntPtr target = app.Windows[index >= 0 ? (index + 1) % app.Windows.Count : 0];
            Focus(target);
        }

        public static void Focus(IntPtr hwnd)
        {
            if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
            NativeMethods.SetForegroundWindow(hwnd);
        }

        public static void Launch(TaskbarApp app)
        {
            try
            {
                if (app.StartApp != null) StartMenuService.Launch(app.StartApp, false);
                else if (app.ExePath != null) Process.Start(app.ExePath);
            }
            catch (Exception ex)
            {
                Log.Error("[TaskbarService] No se pudo abrir {0}: {1}", app.Name, ex.Message);
            }
        }

        public static void CloseAll(TaskbarApp app)
        {
            foreach (IntPtr hwnd in app.Windows) PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        #endregion

        #region Anclados de la barra

        public static void Pin(string id) { LoadPinned(); if (!_pinned.Contains(id)) { _pinned.Add(id); SavePinned(); } }
        public static void Unpin(string id) { LoadPinned(); _pinned.Remove(id); SavePinned(); }

        private static void LoadPinned()
        {
            if (_pinned != null) return;
            if (File.Exists(PinnedFile))
            {
                _pinned = new List<string>(File.ReadAllLines(PinnedFile));
                _pinned.RemoveAll(l => l.Trim().Length == 0);
                return;
            }
            // La primera vez: las ancladas en la barra de Windows (sus accesos directos), buscadas por nombre en el menú Inicio
            _pinned = new List<string>();
            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            List<ShellImage.App> all = StartMenuService.AllApps;
            if (Directory.Exists(folder))
            {
                foreach (string link in Directory.GetFiles(folder, "*.lnk"))
                {
                    string name = Path.GetFileNameWithoutExtension(link);
                    ShellImage.App app = all.Find(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase))
                                         ?? (name == "File Explorer" ? all.Find(a => a.Id == "Microsoft.Windows.Explorer") : null);
                    if (app != null && !_pinned.Contains(app.Id)) _pinned.Add(app.Id);
                }
            }
            SavePinned();
        }

        private static void SavePinned()
        {
            try { File.WriteAllLines(PinnedFile, _pinned.ToArray()); }
            catch (IOException ex) { Log.Error("[TaskbarService] No se pudieron guardar los anclados: {0}", ex.Message); }
        }

        #endregion
    }
}
