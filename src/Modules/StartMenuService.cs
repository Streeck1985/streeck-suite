using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    internal class RecentFile
    {
        public string Name;
        public string LinkPath;   // Acceso directo de la carpeta Recent (es lo que se abre)
        public string Folder;
        public DateTime When;
    }

    /// <summary>
    /// Datos del menú de inicio (fase H): apps ancladas (las elige el usuario; al principio sus más usadas según el
    /// historial de Windows), todas las apps del menú Inicio, archivos recientes y la cuenta del usuario.
    /// </summary>
    internal static class StartMenuService
    {
        public const int PinnedCount = 12;
        private const string UserAssistKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}\Count";

        private static readonly string PinnedFile = Path.Combine(Path.GetDirectoryName(Log.FilePath), "inicio_anclados.txt");
        private static List<ShellImage.App> _apps;
        private static DateTime _appsLoadedAt = DateTime.MinValue;
        private static List<string> _pinned;

        /// <summary>
        /// Todas las apps del menú Inicio, por orden alfabético (se recargan cada 10 minutos).
        /// </summary>
        public static List<ShellImage.App> AllApps
        {
            get
            {
                if (_apps == null || (DateTime.Now - _appsLoadedAt).TotalMinutes > 10)
                {
                    List<ShellImage.App> apps = ShellImage.StartMenuApps();
                    foreach (ShellImage.App app in apps) app.Name = app.Name.Trim(); // Algunas traen espacios al principio
                    // Fuera desinstaladores, ayudas y enlaces web que también viven en el menú Inicio
                    apps.RemoveAll(a => a.Name.IndexOf("Desinstalar", StringComparison.OrdinalIgnoreCase) >= 0
                                        || a.Name.IndexOf("Uninstall", StringComparison.OrdinalIgnoreCase) >= 0
                                        || a.Id.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                                        || a.Id.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                                        || a.Id.EndsWith(".chm", StringComparison.OrdinalIgnoreCase)
                                        || a.Id.EndsWith(".txt", StringComparison.OrdinalIgnoreCase));
                    Dictionary<string, ShellImage.App> unique = new Dictionary<string, ShellImage.App>(StringComparer.OrdinalIgnoreCase);
                    foreach (ShellImage.App app in apps) if (!unique.ContainsKey(app.Name)) unique[app.Name] = app;
                    _apps = new List<ShellImage.App>(unique.Values);
                    _apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
                    _appsLoadedAt = DateTime.Now;
                }
                return _apps;
            }
        }

        #region Anclados

        public static List<ShellImage.App> Pinned
        {
            get
            {
                LoadPinned();
                List<ShellImage.App> all = AllApps;
                List<ShellImage.App> pinned = new List<ShellImage.App>();
                foreach (string id in _pinned)
                {
                    ShellImage.App app = all.Find(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
                    if (app != null) pinned.Add(app);
                }
                return pinned;
            }
        }

        public static bool IsPinned(string id)
        {
            LoadPinned();
            return _pinned.Exists(p => string.Equals(p, id, StringComparison.OrdinalIgnoreCase));
        }

        public static void Pin(string id)
        {
            LoadPinned();
            if (IsPinned(id)) return;
            _pinned.Add(id);
            SavePinned();
        }

        public static void Unpin(string id)
        {
            LoadPinned();
            _pinned.RemoveAll(p => string.Equals(p, id, StringComparison.OrdinalIgnoreCase));
            SavePinned();
        }

        public static void MoveToFront(string id)
        {
            Unpin(id);
            _pinned.Insert(0, id);
            SavePinned();
        }

        /// <summary>
        /// Vuelve a las apps más usadas (botón Restablecer del panel).
        /// </summary>
        public static void ResetPinned()
        {
            _pinned = MostUsed();
            SavePinned();
        }

        private static void LoadPinned()
        {
            if (_pinned != null) return;
            if (File.Exists(PinnedFile))
            {
                _pinned = new List<string>(File.ReadAllLines(PinnedFile));
                _pinned.RemoveAll(l => l.Trim().Length == 0);
            }
            else
            {
                _pinned = MostUsed();
                SavePinned();
            }
        }

        private static void SavePinned()
        {
            try { File.WriteAllLines(PinnedFile, _pinned.ToArray()); }
            catch (IOException ex) { Log.Error("[StartMenuService] No se pudieron guardar los anclados: {0}", ex.Message); }
        }

        /// <summary>
        /// Las apps que más se abren, según el registro UserAssist de Windows (nombres cifrados en ROT13 y con el
        /// número de usos en el byte 4). Solo cuentan las que existen en el menú Inicio.
        /// </summary>
        private static List<string> MostUsed()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UserAssistKey))
                {
                    if (key != null)
                    {
                        foreach (string name in key.GetValueNames())
                        {
                            byte[] data = key.GetValue(name) as byte[];
                            if (data == null || data.Length < 8) continue;
                            counts[Rot13(name)] = BitConverter.ToInt32(data, 4);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("[StartMenuService] No se pudo leer el uso de las apps: {0}", ex.Message);
            }

            List<KeyValuePair<int, string>> ranked = new List<KeyValuePair<int, string>>();
            foreach (ShellImage.App app in AllApps)
            {
                int count;
                if (counts.TryGetValue(app.Id, out count) && count > 0) ranked.Add(new KeyValuePair<int, string>(count, app.Id));
            }
            ranked.Sort((a, b) => b.Key.CompareTo(a.Key));
            List<string> ids = ranked.ConvertAll(r => r.Value);

            // Si hay pocas con historial, se completan con apps habituales
            foreach (string name in new[] { "Explorador de archivos", "Microsoft Edge", "Configuración", "Calculadora", "Bloc de notas", "Terminal", "Microsoft Store", "Fotos" })
            {
                if (ids.Count >= PinnedCount) break;
                ShellImage.App app = AllApps.Find(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
                if (app != null && !ids.Contains(app.Id)) ids.Add(app.Id);
            }
            return ids.Count > PinnedCount ? ids.GetRange(0, PinnedCount) : ids;
        }

        private static string Rot13(string text)
        {
            StringBuilder result = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c >= 'a' && c <= 'z') result.Append((char)('a' + (c - 'a' + 13) % 26));
                else if (c >= 'A' && c <= 'Z') result.Append((char)('A' + (c - 'A' + 13) % 26));
                else result.Append(c);
            }
            return result.ToString();
        }

        #endregion

        #region Recientes

        /// <summary>
        /// Últimos archivos abiertos (carpeta Recent de Windows), sin carpetas.
        /// </summary>
        public static List<RecentFile> Recent(int count)
        {
            List<RecentFile> files = new List<RecentFile>();
            try
            {
                string folder = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                List<FileInfo> links = new List<FileInfo>(new DirectoryInfo(folder).GetFiles("*.lnk"));
                links.Sort((a, b) => b.LastWriteTime.CompareTo(a.LastWriteTime));
                foreach (FileInfo link in links)
                {
                    if (files.Count >= count) break;
                    string target = LinkTarget(link.FullName);
                    if (string.IsNullOrEmpty(target) || Directory.Exists(target) || !File.Exists(target)) continue;
                    files.Add(new RecentFile
                    {
                        Name = Path.GetFileName(target),
                        LinkPath = link.FullName,
                        Folder = Path.GetFileName(Path.GetDirectoryName(target)),
                        When = link.LastWriteTime
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Error("[StartMenuService] No se pudieron leer los recientes: {0}", ex.Message);
            }
            return files;
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int size, IntPtr findData, int flags);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid classId);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, int mode);
        }

        private static string LinkTarget(string linkPath)
        {
            object link = new ShellLink();
            try
            {
                ((IPersistFile)link).Load(linkPath, 0);
                StringBuilder path = new StringBuilder(1024);
                ((IShellLinkW)link).GetPath(path, path.Capacity, IntPtr.Zero, 0);
                return path.ToString();
            }
            catch (Exception)
            {
                return null;
            }
            finally
            {
                Marshal.ReleaseComObject(link);
            }
        }

        public static string WhenText(DateTime when)
        {
            TimeSpan age = DateTime.Now - when;
            if (age.TotalMinutes < 60) return "Hace " + Math.Max(1, (int)age.TotalMinutes) + " min";
            if (when.Date == DateTime.Today) return "Hoy " + TimeText.Short(when);
            if (when.Date == DateTime.Today.AddDays(-1)) return "Ayer " + TimeText.Short(when);
            if (age.TotalDays < 7) return when.ToString("dddd", new System.Globalization.CultureInfo("es-MX"));
            return when.ToString("d 'de' MMM", new System.Globalization.CultureInfo("es-MX"));
        }

        #endregion

        #region Cuenta del usuario

        [DllImport("secur32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetUserNameEx(int format, StringBuilder name, ref int size);

        private static string _userName;
        private static Bitmap _userPicture;
        private static bool _pictureLoaded;

        public static string UserName
        {
            get
            {
                if (_userName != null) return _userName;
                StringBuilder name = new StringBuilder(256);
                int size = name.Capacity;
                _userName = GetUserNameEx(3, name, ref size) && name.Length > 0 ? name.ToString() : Environment.UserName; // 3 = nombre para mostrar
                return _userName;
            }
        }

        /// <summary>
        /// Foto de la cuenta de Windows, o null (se dibuja la inicial).
        /// </summary>
        public static Bitmap UserPicture
        {
            get
            {
                if (_pictureLoaded) return _userPicture;
                _pictureLoaded = true;
                try
                {
                    string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                    string path = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\" + sid, "Image96", null) as string;
                    if (path != null && File.Exists(path))
                    {
                        using (Bitmap file = new Bitmap(path)) _userPicture = new Bitmap(file);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error("[StartMenuService] Sin foto de cuenta: {0}", ex.Message);
                }
                return _userPicture;
            }
        }

        #endregion

        #region Abrir y energía

        public static void Launch(ShellImage.App app, bool asAdmin)
        {
            try
            {
                string exe = ExePath(app.Id);
                if (asAdmin && exe != null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas" });
                else System.Diagnostics.Process.Start("explorer.exe", "shell:AppsFolder\\" + app.Id);
            }
            catch (Exception ex)
            {
                Log.Error("[StartMenuService] No se pudo abrir {0}: {1}", app.Name, ex.Message);
            }
        }

        /// <summary>
        /// Ruta del .exe de una app de escritorio ("{GUID de carpeta}\ruta\app.exe"), o null para las de la tienda.
        /// </summary>
        public static string ExePath(string id)
        {
            if (id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(id)) return id;
            if (!id.StartsWith("{") || id.IndexOf('}') < 0) return null;
            Guid folder;
            try { folder = new Guid(id.Substring(0, id.IndexOf('}') + 1)); }
            catch (FormatException) { return null; }
            IntPtr pointer;
            if (SHGetKnownFolderPath(ref folder, 0, IntPtr.Zero, out pointer) != 0) return null;
            string root = Marshal.PtrToStringUni(pointer);
            Marshal.FreeCoTaskMem(pointer);
            string path = Path.Combine(root, id.Substring(id.IndexOf('}') + 2));
            return File.Exists(path) ? path : null;
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);

        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        [DllImport("powrprof.dll")]
        private static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);

        public enum PowerAction { Lock, Sleep, Restart, Shutdown }

        public static void Power(PowerAction action)
        {
            Log.Info("[StartMenuService] Energía: {0}", action);
            switch (action)
            {
                case PowerAction.Lock: LockWorkStation(); break;
                case PowerAction.Sleep: SetSuspendState(false, false, false); break;
                case PowerAction.Restart: System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/r /t 0") { CreateNoWindow = true, UseShellExecute = false }); break;
                case PowerAction.Shutdown: System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("shutdown", "/s /t 0") { CreateNoWindow = true, UseShellExecute = false }); break;
            }
        }

        #endregion
    }
}
