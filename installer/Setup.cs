using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace StreeckSetup
{
    /// <summary>
    /// Instalador propio de Streeck Suite (mockup aprobado): una sola pantalla negra con el logo, tres interruptores,
    /// la carpeta, barra de progreso e Instalar → Abrir. Instala sin administrador en %LOCALAPPDATA%\Programs, crea
    /// los accesos, el inicio con Windows y la entrada de Configuración → Aplicaciones. El mismo .exe, copiado como
    /// «Desinstalar Streeck.exe», desinstala con --uninstall y devuelve la barra de Windows normal.
    /// </summary>
    internal static class Program
    {
        public const string AppName = "Streeck Suite";
        public const string Version = BuildInfo.Version; // La pone build_installer.ps1 (sale de src\Core\AppInfo.cs)

        [STAThread]
        private static void Main(string[] args)
        {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool uninstall = args.Contains("--uninstall");
            if (args.Contains("--update"))
            {
                // Actualización desde la suite: sin ventanas, conserva las opciones que ya tenías y vuelve a abrirla
                using (SetupForm form = new SetupForm(false)) form.SilentUpdate();
                return;
            }
            int preview = Array.IndexOf(args, "--preview");
            if (preview >= 0 && preview + 1 < args.Length)
            {
                using (SetupForm form = new SetupForm(uninstall)) form.SavePreview(args[preview + 1]);
                return;
            }
            Application.Run(new SetupForm(uninstall));
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();
    }

    internal class SetupForm : Form
    {
        private const float W = 440, H = 414;
        private static readonly Color Back = Color.FromArgb(11, 15, 23);
        private static readonly Color Text1 = Color.FromArgb(230, 233, 239);
        private static readonly Color Gray = Color.FromArgb(154, 164, 181);
        private static readonly Color Accent = Color.FromArgb(56, 189, 248);
        private static readonly Color Field = Color.FromArgb(30, 36, 48);
        private static readonly Color ToggleOff = Color.FromArgb(58, 65, 80);

        private readonly bool _uninstall;
        private readonly float _scale;
        private readonly Image _logo;
        private readonly Font _title, _body, _small, _button;
        private readonly bool[] _options;
        private readonly string[] _labels;
        private RectangleF[] _toggles = new RectangleF[0];
        private RectangleF _primary, _cancel, _close;
        private float _progress = -1;
        private string _status = "";
        private bool _busy, _done;
        private PointF _mouse;

        private static string InstallDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", Program.AppName); }
        }

        public SetupForm(bool uninstall)
        {
            _uninstall = uninstall;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) _scale = screen.DpiX / 96f;
            _labels = uninstall ? new[] { "Borrar también mis ajustes y descargas" } : new[] { "Iniciar con Windows", "Acceso directo en el escritorio", "Abrir al terminar" };
            _options = uninstall ? new[] { false } : new[] { true, true, true };
            using (Stream logo = Assembly.GetExecutingAssembly().GetManifestResourceStream("logo.png")) _logo = logo != null ? Image.FromStream(logo) : null;
            string family = new InstalledFontCollection().Families.Any(f => f.Name == "Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";
            _title = new Font(family, 15f * _scale, FontStyle.Bold, GraphicsUnit.Point);
            _body = new Font("Segoe UI", 10f * _scale, FontStyle.Regular, GraphicsUnit.Point);
            _small = new Font("Segoe UI", 9f * _scale, FontStyle.Regular, GraphicsUnit.Point);
            _button = new Font("Segoe UI Semibold", 9.75f * _scale, FontStyle.Regular, GraphicsUnit.Point);

            Text = uninstall ? "Desinstalar " + Program.AppName : "Instalar " + Program.AppName;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size((int)(W * _scale), (int)(H * _scale));
            DoubleBuffered = true;
            BackColor = Back;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            MouseMove += (s, e) => { _mouse = new PointF(e.X / _scale, e.Y / _scale); Invalidate(); };
            MouseDown += OnDown;
        }

        // Esquinas redondeadas de Windows 11 y sombra
        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ClassStyle |= 0x20000; return cp; } // CS_DROPSHADOW
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2;
            DwmSetWindowAttribute(Handle, 33, ref round, 4); // DWMWA_WINDOW_CORNER_PREFERENCE = redondeadas
        }

        #region Dibujo

        protected override void OnPaint(PaintEventArgs e)
        {
            Draw(e.Graphics);
        }

        public void SavePreview(string path)
        {
            using (Bitmap bitmap = new Bitmap(ClientSize.Width, ClientSize.Height))
            {
                using (Graphics g = Graphics.FromImage(bitmap)) Draw(g);
                bitmap.Save(path);
            }
        }

        private void Draw(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.Clear(Back);
            g.ScaleTransform(_scale, _scale);

            // Logo, nombre y versión
            RectangleF logo = new RectangleF(22, 22, 52, 52);
            using (GraphicsPath tile = Rounded(logo, 14))
            using (LinearGradientBrush back = new LinearGradientBrush(logo, Color.FromArgb(30, 41, 59), Color.FromArgb(15, 23, 42), 135f))
            {
                g.FillPath(back, tile);
            }
            if (_logo != null) g.DrawImage(_logo, RectangleF.Inflate(logo, -6, -6));
            DrawText(g, _uninstall ? "Desinstalar Streeck Suite" : Program.AppName, _title, Text1, new RectangleF(88, 22, 320, 28));
            DrawText(g, "Versión " + Program.Version + " · " + SizeText(), _small, Gray, new RectangleF(88, 50, 320, 18));

            _close = new RectangleF(W - 40, 12, 26, 26);
            if (_close.Contains(_mouse)) FillRound(g, _close, 7, Color.FromArgb(36, 255, 255, 255));
            using (Pen x = new Pen(_close.Contains(_mouse) ? Color.White : Gray, 1.4f))
            {
                float cx = _close.X + 13, cy = _close.Y + 13;
                g.DrawLine(x, cx - 4.5f, cy - 4.5f, cx + 4.5f, cy + 4.5f);
                g.DrawLine(x, cx + 4.5f, cy - 4.5f, cx - 4.5f, cy + 4.5f);
            }

            string description = _uninstall
                ? "Se quitan la suite, sus accesos directos y el inicio con Windows. Tu barra de tareas vuelve a la normal de Windows."
                : "Isla dinámica, barra de tareas con skins, menú de inicio, reloj 3D y asistente con IA para Windows 11.";
            DrawText(g, description, _body, Gray, new RectangleF(22, 90, W - 44, 44), true);

            // Interruptores
            _toggles = new RectangleF[_labels.Length];
            for (int i = 0; i < _labels.Length; i++)
            {
                float y = 146 + i * 42;
                DrawText(g, _labels[i], _body, Text1, new RectangleF(22, y, 300, 22));
                RectangleF toggle = new RectangleF(W - 22 - 40, y, 40, 22);
                FillRound(g, toggle, 11, _options[i] ? Accent : ToggleOff);
                using (SolidBrush knob = new SolidBrush(Color.White))
                {
                    g.FillEllipse(knob, _options[i] ? toggle.Right - 19 : toggle.X + 3, toggle.Y + 3, 16, 16);
                }
                _toggles[i] = RectangleF.Inflate(toggle, 4, 4);
            }

            // Carpeta
            float boxY = 146 + _labels.Length * 42 + 6;
            RectangleF box = new RectangleF(22, boxY, W - 44, 30);
            FillRound(g, box, 8, Field);
            DrawText(g, (_uninstall ? "Instalada en " : "Se instala en ") + InstallDir, _small, Color.FromArgb(195, 201, 212), new RectangleF(box.X + 10, box.Y + 6, box.Width - 16, 20));

            // Progreso y estado
            float barY = boxY + 44;
            if (_progress >= 0)
            {
                RectangleF track = new RectangleF(22, barY, W - 44, 6);
                FillRound(g, track, 3, Field);
                if (_progress > 0) FillRound(g, new RectangleF(track.X, track.Y, Math.Max(6, track.Width * _progress), 6), 3, Accent);
            }
            DrawText(g, _status, _small, Gray, new RectangleF(22, barY + 12, W - 44, 18));

            // Botones
            string primary = _done ? (_uninstall ? "Cerrar" : _options.Length > 2 && _options[2] ? "Abrir" : "Cerrar") : _uninstall ? "Desinstalar" : "Instalar";
            float primaryW = g.MeasureString(primary, _button).Width / _scale + 32;
            _primary = new RectangleF(W - 22 - primaryW, H - 56, primaryW, 36);
            _cancel = new RectangleF(_primary.X - 10 - 96, H - 56, 96, 36);
            if (!_done)
            {
                FillRound(g, _cancel, 10, _cancel.Contains(_mouse) && !_busy ? Color.FromArgb(44, 52, 68) : Color.FromArgb(35, 42, 55));
                DrawText(g, "Cancelar", _button, _busy ? Gray : Text1, _cancel, false, StringAlignment.Center);
            }
            Color primaryColor = _busy ? Color.FromArgb(120, Accent) : _primary.Contains(_mouse) ? Color.FromArgb(125, 211, 252) : Accent;
            FillRound(g, _primary, 10, primaryColor);
            DrawText(g, primary, _button, Color.FromArgb(6, 18, 28), _primary, false, StringAlignment.Center);
        }

        private string SizeText()
        {
            try { return string.Format("{0:0.0} MB", new FileInfo(Application.ExecutablePath).Length / 1048576.0); }
            catch (Exception) { return ""; }
        }

        private void DrawText(Graphics g, string text, Font font, Color color, RectangleF rect, bool wrap = false, StringAlignment align = StringAlignment.Near)
        {
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat { Alignment = align, LineAlignment = wrap ? StringAlignment.Near : StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
            {
                if (!wrap) format.FormatFlags = StringFormatFlags.NoWrap;
                // La fuente ya va escalada a la pantalla; se dibuja sin la escala del lienzo
                GraphicsState state = g.Save();
                g.ResetTransform();
                g.DrawString(text, font, brush, new RectangleF(rect.X * _scale, rect.Y * _scale, rect.Width * _scale, rect.Height * _scale), format);
                g.Restore(state);
            }
        }

        private static void FillRound(Graphics g, RectangleF rect, float radius, Color color)
        {
            using (GraphicsPath path = Rounded(rect, radius))
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        private static GraphicsPath Rounded(RectangleF r, float radius)
        {
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        #endregion

        #region Clics

        private void OnDown(object sender, MouseEventArgs e)
        {
            PointF p = new PointF(e.X / _scale, e.Y / _scale);
            if (_close.Contains(p) || (_cancel.Contains(p) && !_done)) { if (!_busy) Close(); return; }
            for (int i = 0; i < _toggles.Length; i++)
            {
                if (_toggles[i].Contains(p) && !_busy && !_done) { _options[i] = !_options[i]; Invalidate(); return; }
            }
            if (_primary.Contains(p))
            {
                if (_busy) return;
                if (_done)
                {
                    if (!_uninstall && _options[2]) Process.Start(Path.Combine(InstallDir, "StreeckSuite.exe"));
                    Close();
                    return;
                }
                Start();
                return;
            }
            // Arrastrar la ventana desde cualquier zona libre
            ReleaseCapture();
            SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero);
        }

        private void Start()
        {
            _busy = true;
            _progress = 0;
            Thread worker = new Thread(() =>
            {
                try
                {
                    if (_uninstall) Uninstall(); else Install();
                    Report(1, _uninstall ? "Streeck Suite se desinstaló." : "Streeck Suite quedó instalada.");
                    BeginInvoke((Action)(() => { _busy = false; _done = true; Invalidate(); }));
                }
                catch (Exception ex)
                {
                    Report(_progress, "No se pudo terminar: " + ex.Message);
                    BeginInvoke((Action)(() => { _busy = false; Invalidate(); }));
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private void Report(float progress, string status)
        {
            _progress = progress;
            _status = status;
            if (IsHandleCreated) BeginInvoke((Action)Invalidate);
        }

        #endregion

        #region Instalar y desinstalar

        /// <summary>
        /// --update: instala encima sin preguntar (mismo inicio con Windows y acceso directo que antes) y abre la suite.
        /// </summary>
        public void SilentUpdate()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    _options[0] = run != null && run.GetValue("StreeckSuite") != null;
                }
                _options[1] = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Program.AppName + ".lnk"));
            }
            catch (Exception) { }

            Exception error = null;
            try { Install(); }
            catch (Exception ex) { error = ex; }

            // Pase lo que pase, la suite se vuelve a abrir. A veces el primer intento no quedaba abierto (justo después de
            // reiniciar el Explorador): se comprueba y se reintenta hasta 3 veces
            string exe = Path.Combine(InstallDir, "StreeckSuite.exe");
            for (int attempt = 0; attempt < 3 && File.Exists(exe); attempt++)
            {
                if (Process.GetProcessesByName("StreeckSuite").Length > 0) break;
                try { Process.Start(exe); } catch (Exception) { }
                Thread.Sleep(6000);
            }
            if (error != null)
            {
                MessageBox.Show("No se pudo actualizar Streeck Suite: " + error.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void Install()
        {
            Report(0.05f, "Cerrando Streeck Suite si está abierta…");
            StopSuite();

            Report(0.25f, "Copiando archivos…");
            Directory.CreateDirectory(InstallDir);
            bool oldBarLoaded = false;
            Assembly payloads = Assembly.GetExecutingAssembly();
            // x64 (streeck_taskbar_*) y ARM64 para Snapdragon (streeck_arm64_*)
            foreach (string old in Directory.GetFiles(InstallDir, "streeck_taskbar_*.dll").Concat(Directory.GetFiles(InstallDir, "streeck_arm64_*.dll")))
            {
                // La misma DLL que trae este instalador (mismo nombre): se queda, aunque el Explorador la esté usando
                if (payloads.GetManifestResourceNames().Contains("payload/" + Path.GetFileName(old))) continue;
                try { File.Delete(old); } catch (Exception) { oldBarLoaded = true; } // El Explorador aún la usa
            }
            Assembly self = Assembly.GetExecutingAssembly();
            foreach (string name in self.GetManifestResourceNames())
            {
                if (!name.StartsWith("payload/")) continue;
                string destination = Path.Combine(InstallDir, name.Substring("payload/".Length));
                if (name.EndsWith(".dll") && File.Exists(destination)) continue; // Ya está (y puede estar en uso)
                using (Stream source = self.GetManifestResourceStream(name))
                using (FileStream target = File.Create(Path.Combine(InstallDir, name.Substring("payload/".Length))))
                {
                    source.CopyTo(target);
                }
            }
            // La barra vieja sigue cargada en el Explorador: se reinicia para que tome la nueva (parpadea un momento)
            if (oldBarLoaded)
            {
                Report(0.45f, "Actualizando la barra de tareas…");
                RestartExplorer();
            }
            string uninstaller = Path.Combine(InstallDir, "Desinstalar Streeck.exe");
            File.Copy(Application.ExecutablePath, uninstaller, true);
            string exe = Path.Combine(InstallDir, "StreeckSuite.exe");

            Report(0.6f, "Creando accesos directos…");
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            Shortcut(Path.Combine(programs, Program.AppName + ".lnk"), exe);
            string desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Program.AppName + ".lnk");
            if (_options[1]) Shortcut(desktop, exe); else if (File.Exists(desktop)) File.Delete(desktop);

            Report(0.8f, "Registrando en Aplicaciones…");
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
            {
                if (_options[0]) run.SetValue("StreeckSuite", "\"" + exe + "\""); else run.DeleteValue("StreeckSuite", false);
            }
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\StreeckSuite"))
            {
                long size = Directory.GetFiles(InstallDir).Sum(f => new FileInfo(f).Length) / 1024;
                key.SetValue("DisplayName", Program.AppName);
                key.SetValue("DisplayVersion", Program.Version);
                key.SetValue("Publisher", "Streeck");
                key.SetValue("DisplayIcon", exe);
                key.SetValue("InstallLocation", InstallDir);
                key.SetValue("UninstallString", "\"" + uninstaller + "\" --uninstall");
                key.SetValue("EstimatedSize", (int)size, RegistryValueKind.DWord);
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        /// <summary>
        /// Reinicia solo el Explorador de la barra, con cuidado: primero le pide salir como lo haría Windows
        /// («Salir del Explorador»), y solo si no responde lo termina. No toca las ventanas de carpetas ni otros
        /// procesos. Si OneDrive estaba abierto y se cayó con el Explorador, lo vuelve a abrir en segundo plano.
        /// </summary>
        private static void RestartExplorer()
        {
            bool oneDriveWasRunning = Process.GetProcessesByName("OneDrive").Length > 0;
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray != IntPtr.Zero)
            {
                uint shellId;
                GetWindowThreadProcessId(tray, out shellId);
                PostMessage(tray, 0x5B4, IntPtr.Zero, IntPtr.Zero); // Salida ordenada del Explorador
                try
                {
                    using (Process shell = Process.GetProcessById((int)shellId))
                    {
                        if (!shell.WaitForExit(2000)) shell.Kill(); // No respondió (Windows 11 reciente lo ignora): solo ese proceso
                    }
                }
                catch (Exception) { } // Ya se cerró
            }
            Thread.Sleep(800);
            if (FindWindow("Shell_TrayWnd", null) == IntPtr.Zero) Process.Start("explorer.exe");
            for (int i = 0; i < 40 && FindWindow("Shell_TrayWnd", null) == IntPtr.Zero; i++) Thread.Sleep(200); // Espera la barra nueva

            // OneDrive a veces se cierra cuando el Explorador se va: se vuelve a abrir como lo abre Windows
            Thread.Sleep(1500);
            if (oneDriveWasRunning && Process.GetProcessesByName("OneDrive").Length == 0)
            {
                string oneDrive = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\OneDrive\OneDrive.exe");
                if (!File.Exists(oneDrive)) oneDrive = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft OneDrive\OneDrive.exe");
                try { if (File.Exists(oneDrive)) Process.Start(oneDrive, "/background"); } catch (Exception) { }
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        private void Uninstall()
        {
            Report(0.1f, "Cerrando Streeck Suite y devolviendo la barra de Windows…");
            StopSuite();

            // La DLL de la barra sigue cargada en el Explorador: reiniciarlo la suelta (la barra parpadea un momento)
            Report(0.35f, "Reiniciando el Explorador…");
            RestartExplorer();

            Report(0.55f, "Quitando accesos directos e inicio con Windows…");
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
            {
                if (run != null) run.DeleteValue("StreeckSuite", false);
            }
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\StreeckSuite", false);
            foreach (string link in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.AppName + ".lnk"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Program.AppName + ".lnk")
            })
            {
                if (File.Exists(link)) File.Delete(link);
            }

            Report(0.75f, "Borrando archivos…");
            foreach (string file in Directory.Exists(InstallDir) ? Directory.GetFiles(InstallDir) : new string[0])
            {
                if (string.Equals(file, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase)) continue;
                try { File.Delete(file); } catch (Exception) { }
            }
            if (_options[0])
            {
                string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StreeckSuite");
                try { if (Directory.Exists(data)) Directory.Delete(data, true); } catch (Exception) { }
            }
            // El desinstalador no puede borrarse a sí mismo: lo hace cmd en cuanto se cierre
            Process.Start(new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 4 > nul & rmdir /s /q \"" + InstallDir + "\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }

        // Cierra la suite en orden (--salir devuelve la barra de Windows); si no responde, la termina
        private static void StopSuite()
        {
            Process[] running = Process.GetProcessesByName("StreeckSuite");
            if (running.Length == 0) return;
            string path = null;
            try { path = running[0].MainModule.FileName; } catch (Exception) { }
            if (path != null && File.Exists(path))
            {
                try { using (Process exit = Process.Start(path, "--salir")) exit.WaitForExit(5000); } catch (Exception) { }
            }
            foreach (Process p in running)
            {
                try { if (!p.WaitForExit(8000)) p.Kill(); } catch (Exception) { }
            }
        }

        private static void Shortcut(string link, string target)
        {
            Type type = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(type);
            object shortcut = type.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { link });
            Type kind = shortcut.GetType();
            kind.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { target });
            kind.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { Path.GetDirectoryName(target) });
            kind.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { target + ",0" });
            kind.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { Program.AppName });
            kind.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            Marshal.ReleaseComObject(shortcut);
            Marshal.ReleaseComObject(shell);
        }

        #endregion

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_logo != null) _logo.Dispose();
                _title.Dispose(); _body.Dispose(); _small.Dispose(); _button.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
