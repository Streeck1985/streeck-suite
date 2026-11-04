using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using Microsoft.Win32;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Iconos de la bandeja de Windows en el Centro de control (fase J3b, como en el Bloom del usuario):
    /// la lista sale del registro (HKCU\Control Panel\NotifyIconSettings) filtrada a las apps que están abiertas, con el
    /// icono de su ejecutable. Al hacer clic se busca el icono real con UI Automation (en la barra o, si está oculto,
    /// abriendo la flecha ⌃) y se le pasa el clic: izquierdo con Invoke, derecho con un clic de ratón real.
    /// </summary>
    internal static class TrayIconsService
    {
        internal class TrayApp
        {
            public string Name;
            public string ExePath;
            public Bitmap Icon;
            public readonly List<string> Hints = new List<string>(); // Textos para encontrar su icono por el nombre
        }

        private static readonly object Sync = new object();
        private static List<TrayApp> _apps = new List<TrayApp>();
        private static DateTime _readAt = DateTime.MinValue;
        private static bool _reading;
        private static readonly Dictionary<string, Bitmap> Icons = new Dictionary<string, Bitmap>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Apps con icono en la bandeja (se vuelve a leer en segundo plano cada 5 s mientras se mira).
        /// </summary>
        public static List<TrayApp> Apps
        {
            get
            {
                if (!_reading && (DateTime.Now - _readAt).TotalSeconds >= 5)
                {
                    _reading = true;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try { List<TrayApp> apps = Read(); lock (Sync) _apps = apps; }
                        catch (Exception ex) { Log.Error("[TrayIcons] No se pudo leer la bandeja: {0}", ex.Message); }
                        _readAt = DateTime.Now;
                        _reading = false;
                    });
                }
                lock (Sync) return _apps;
            }
        }

        private static List<TrayApp> Read()
        {
            HashSet<string> running = RunningExecutables();
            Dictionary<string, TrayApp> byExe = new Dictionary<string, TrayApp>(StringComparer.OrdinalIgnoreCase);
            using (RegistryKey root = Registry.CurrentUser.OpenSubKey(@"Control Panel\NotifyIconSettings"))
            {
                if (root == null) return new List<TrayApp>();
                foreach (string id in root.GetSubKeyNames())
                {
                    using (RegistryKey key = root.OpenSubKey(id))
                    {
                        string exe = ExpandKnownFolder(key.GetValue("ExecutablePath") as string);
                        if (string.IsNullOrEmpty(exe) || !running.Contains(exe)) continue;
                        if (Path.GetFileName(exe).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)) continue; // Red, sonido... de Windows
                        TrayApp app;
                        if (!byExe.TryGetValue(exe, out app))
                        {
                            app = new TrayApp { ExePath = exe };
                            string description = null;
                            try { description = FileVersionInfo.GetVersionInfo(exe).FileDescription; } catch (Exception) { }
                            app.Name = !string.IsNullOrEmpty(description) ? description.Trim() : Path.GetFileNameWithoutExtension(exe);
                            app.Hints.Add(app.Name);
                            app.Hints.Add(Path.GetFileNameWithoutExtension(exe).Replace(".Root", ""));
                            app.Icon = IconFor(exe);
                            byExe[exe] = app;
                        }
                        string tooltip = key.GetValue("InitialTooltip") as string;
                        if (!string.IsNullOrEmpty(tooltip))
                        {
                            tooltip = tooltip.Replace("\u202A", "").Replace("\u202C", "").Split('\n')[0].Trim();
                            if (tooltip.Length > 0 && !app.Hints.Contains(tooltip)) app.Hints.Insert(0, tooltip);
                        }
                    }
                }
            }
            List<TrayApp> list = new List<TrayApp>(byExe.Values);
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        private static Bitmap IconFor(string exe)
        {
            Bitmap icon;
            if (Icons.TryGetValue(exe, out icon)) return icon;
            icon = ShellImage.Get(exe, 32, ShellImage.IconOnly);
            Icons[exe] = icon;
            return icon;
        }

        // "{F38BF404-1D43-42F2-9305-67DE0B28FC23}\explorer.exe" → "C:\Windows\explorer.exe"
        private static string ExpandKnownFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("{")) return path;
            int close = path.IndexOf('}');
            if (close < 0) return path;
            try
            {
                Guid folder = new Guid(path.Substring(0, close + 1));
                IntPtr buffer;
                if (SHGetKnownFolderPath(ref folder, 0, IntPtr.Zero, out buffer) != 0) return path;
                string root = Marshal.PtrToStringUni(buffer);
                Marshal.FreeCoTaskMem(buffer);
                return root + path.Substring(close + 1);
            }
            catch (Exception)
            {
                return path;
            }
        }

        private static HashSet<string> RunningExecutables()
        {
            HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Process process in Process.GetProcesses())
            {
                IntPtr handle = OpenProcess(0x1000, false, process.Id); // PROCESS_QUERY_LIMITED_INFORMATION
                if (handle == IntPtr.Zero) continue;
                StringBuilder path = new StringBuilder(1024);
                int size = path.Capacity;
                if (QueryFullProcessImageName(handle, 0, path, ref size)) paths.Add(path.ToString());
                CloseHandle(handle);
                process.Dispose();
            }
            return paths;
        }

        #region Clic en el icono real

        /// <summary>
        /// Pasa el clic al icono real de la bandeja (en segundo plano: UI Automation tarda unos cientos de ms).
        /// </summary>
        public static void Click(TrayApp app, bool right)
        {
            if (app == null) return;
            Thread thread = new Thread(() =>
            {
                try
                {
                    AutomationElement icon = FindVisibleIcon(app) ?? FindHiddenIcon(app);
                    if (icon == null)
                    {
                        Log.Info("[TrayIcons] No se encontró el icono de {0}; se abre la app", app.Name);
                        if (!right) Process.Start(app.ExePath);
                        return;
                    }
                    object pattern;
                    if (!right && icon.TryGetCurrentPattern(InvokePattern.Pattern, out pattern))
                    {
                        ((InvokePattern)pattern).Invoke();
                        return;
                    }
                    ClickAt(icon.Current.BoundingRectangle, right);
                }
                catch (Exception ex)
                {
                    Log.Error("[TrayIcons] Clic en {0}: {1}", app.Name, ex.Message);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();
        }

        // Iconos que están a la vista en la barra
        private static AutomationElement FindVisibleIcon(TrayApp app)
        {
            IntPtr tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return null;
            AutomationElement root = AutomationElement.FromHandle(tray);
            return Match(root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon")), app);
        }

        // Iconos ocultos: se abre la flecha ⌃ y se busca en su ventana
        private static AutomationElement FindHiddenIcon(TrayApp app)
        {
            IntPtr tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return null;
            AutomationElement root = AutomationElement.FromHandle(tray);
            foreach (AutomationElement element in root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon")))
            {
                string name = (element.Current.Name ?? "").ToLowerInvariant();
                if (!name.StartsWith("mostrar iconos ocultos") && !name.StartsWith("show hidden icons")) continue;
                object pattern;
                if (element.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) ((InvokePattern)pattern).Invoke();
                break;
            }
            for (int attempt = 0; attempt < 15; attempt++)
            {
                Thread.Sleep(100);
                IntPtr overflow = NativeMethods.FindWindow("TopLevelWindowForOverflowXamlIsland", null);
                if (overflow == IntPtr.Zero) continue;
                AutomationElement found = Match(AutomationElement.FromHandle(overflow).FindAll(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.AutomationIdProperty, "NotifyItemIcon")), app);
                if (found != null) return found;
            }
            return null;
        }

        // El nombre del icono es su texto de ayuda ("WhatsApp", "Spotify", "Config NVIDIA"...): se compara con las pistas
        private static AutomationElement Match(AutomationElementCollection candidates, TrayApp app)
        {
            foreach (string hint in app.Hints)
            {
                string wanted = Normalize(hint);
                if (wanted.Length < 3) continue;
                foreach (AutomationElement element in candidates)
                {
                    string name = Normalize(element.Current.Name);
                    if (name.Length == 0) continue;
                    if (name.Contains(wanted) || wanted.Contains(name) || name.StartsWith(wanted.Split(' ')[0])) return element;
                }
            }
            return null;
        }

        private static string Normalize(string text)
        {
            return (text ?? "").Replace("\u202A", "").Replace("\u202C", "").Trim().ToLowerInvariant();
        }

        // Clic real en el centro del icono (el menú de la app aparece donde Windows lo pondría) y el ratón vuelve a su sitio
        private static void ClickAt(System.Windows.Rect rect, bool right)
        {
            if (rect.IsEmpty) return;
            NativeMethods.POINT before;
            GetCursorPos(out before);
            SetCursorPos((int)(rect.X + rect.Width / 2), (int)(rect.Y + rect.Height / 2));
            uint down = right ? 0x0008u : 0x0002u, up = right ? 0x0010u : 0x0004u;
            mouse_event(down, 0, 0, 0, UIntPtr.Zero);
            mouse_event(up, 0, 0, 0, UIntPtr.Zero);
            Thread.Sleep(60);
            SetCursorPos(before.X, before.Y);
        }

        #endregion

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);

        [DllImport("kernel32.dll")]
        private static extern IntPtr OpenProcess(int access, bool inherit, int processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out NativeMethods.POINT point);

        [DllImport("user32.dll")]
        private static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
    }
}
