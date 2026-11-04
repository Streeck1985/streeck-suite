using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using StreeckSuite.Core;
using StreeckSuite.Modules;

namespace StreeckSuite
{
    public static class Program
    {
        private const string ExitEventName = @"Local\StreeckSuite.Exit";

        [STAThread]
        public static void Main(string[] args)
        {
            // Herramienta de desarrollo: StreeckSuite.exe --preview-island <carpeta> guarda cada estado de la isla en PNG
            if (args != null && args.Length >= 2 && args[0] == "--preview-island")
            {
                NativeMethods.SetProcessDPIAware();
                SuiteSettings.Load();
                WeatherService.Start();
                MediaService.EnsureStarted();
                Thread.Sleep(4000); // Tiempo para que lleguen el clima y la música
                using (NotchOverlayForm form = new NotchOverlayForm(EventBus.Instance))
                {
                    form.RenderPreviews(args[1]);
                }
                return;
            }

            // Herramienta de desarrollo: StreeckSuite.exe --preview-settings <carpeta> guarda cada página del panel en PNG
            if (args != null && args.Length >= 2 && args[0] == "--preview-settings")
            {
                NativeMethods.SetProcessDPIAware();
                SuiteSettings.Load();
                SettingsWindow.RenderPreviews(args[1]);
                return;
            }

            // Herramienta de desarrollo: StreeckSuite.exe --preview-search <carpeta> <texto> [<texto>...] guarda la búsqueda en PNG
            if (args != null && args.Length >= 3 && args[0] == "--preview-search")
            {
                NativeMethods.SetProcessDPIAware();
                SuiteSettings.Load();
                using (SearchWindow search = new SearchWindow())
                {
                    for (int i = 2; i < args.Length; i++)
                    {
                        search.RenderPreview(args[i], System.IO.Path.Combine(args[1], "busqueda-" + i + ".png"));
                    }
                }
                return;
            }

            // Herramienta de desarrollo: StreeckSuite.exe --test-agenda <enlace .ics> anota en el registro los eventos leídos
            if (args != null && args.Length >= 2 && args[0] == "--test-agenda")
            {
                AgendaService.TestFeed(args[1]);
                return;
            }

            // Herramienta de desarrollo: StreeckSuite.exe --preview-assistant <carpeta> <pregunta> [<pregunta>...]
            if (args != null && args.Length >= 3 && args[0] == "--preview-assistant")
            {
                NativeMethods.SetProcessDPIAware();
                SuiteSettings.Load();
                for (int i = 2; i < args.Length; i++)
                {
                    using (AssistantWindow assistant = new AssistantWindow())
                    {
                        assistant.RenderPreview(args[i], System.IO.Path.Combine(args[1], "asistente-" + i + ".png"));
                    }
                }
                return;
            }

            // Herramienta de desarrollo: StreeckSuite.exe --test-voice <archivo.wav> <Voz> <texto>: guarda la voz y anota cuál habló
            if (args != null && args.Length >= 4 && args[0] == "--test-voice")
            {
                SuiteSettings.Load();
                string engine;
                Stopwatch watch = Stopwatch.StartNew();
                byte[] wav = VoiceOutput.Synthesize(args[3], (SuiteSettings.AssistantVoiceKind)Enum.Parse(typeof(SuiteSettings.AssistantVoiceKind), args[2]), out engine);
                if (wav != null) System.IO.File.WriteAllBytes(args[1], wav);
                Log.Info("[VoiceOutput] Prueba: habló {0} en {1:0.0} s", engine, watch.Elapsed.TotalSeconds);
                return;
            }

            // Herramienta de desarrollo: StreeckSuite.exe --preview-clock <carpeta>: el reloj sobre tu fondo actual,
            // clásico y estirado con tres fuentes (no toca reloj.ini)
            if (args != null && args.Length >= 2 && args[0] == "--preview-clock")
            {
                NativeMethods.SetProcessDPIAware();
                System.IO.Directory.CreateDirectory(args[1]);
                System.Drawing.Size screen = Screen.PrimaryScreen.Bounds.Size;
                using (DepthClockLayers layers = DepthClockLayers.Analyze(WallpaperInfo.GetCurrentPath(), screen))
                {
                    ClockSettings saved = ClockSettings.Load();
                    // Valores de profundidad del fondo, para ajustar qué cuenta como "cerca"
                    var probe = new System.Text.StringBuilder();
                    foreach (float p in new[] { 0.1f, 0.25f, 0.5f, 0.6f, 0.7f, 0.8f, 0.9f }) probe.AppendFormat("p{0:0}={1:0.000} ", p * 100, layers.DepthPercentile(p));
                    probe.AppendLine();
                    for (float fy = 0.05f; fy < 1f; fy += 0.1f)
                    {
                        for (float fx = 0.05f; fx < 1f; fx += 0.1f) probe.AppendFormat("{0:0.00} ", layers.DepthAt(fx, fy));
                        probe.AppendLine();
                    }
                    System.IO.File.WriteAllText(System.IO.Path.Combine(args[1], "profundidad.txt"), probe.ToString());

                    // El reloj del usuario a varias alturas (como mover el deslizador Vertical)
                    foreach (float top in new[] { 0.05f, 0.15f, 0.25f, 0.35f })
                    {
                        ClockSettings s = saved.Clone();
                        s.Top = top;
                        if (layers.Foreground != null) layers.Foreground.Dispose();
                        layers.Foreground = layers.ComposeForeground(s);
                        using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(screen.Width, screen.Height))
                        {
                            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap)) DesktopClockForm.PaintClock(g, screen, s, layers, null, new DateTime(2026, 11, 3, 19, 44, 7));
                            bitmap.Save(System.IO.Path.Combine(args[1], "vertical-" + (int)(top * 100) + ".png"));
                        }
                    }
                    var cases = new[]
                    {
                        new { Name = "tus-ajustes", Style = saved.Style, Font = saved.FontFamily, Weight = saved.SamsungWeight, Color = saved.SamsungColor },
                        new { Name = "clasico", Style = ClockStyle.Apple, Font = saved.FontFamily, Weight = 2, Color = "#ffffff" },
                        new { Name = "estirado-segoe", Style = ClockStyle.Samsung, Font = "Segoe UI Variable Display", Weight = 2, Color = "#ffffff" },
                        new { Name = "estirado-bahnschrift", Style = ClockStyle.Samsung, Font = "Bahnschrift", Weight = 1, Color = "#ffd666" },
                        new { Name = "estirado-georgia", Style = ClockStyle.Samsung, Font = "Georgia", Weight = 1, Color = "#ffebf0" },
                    };
                    foreach (var c in cases)
                    {
                        ClockSettings s = saved.Clone();
                        s.Style = c.Style; s.FontFamily = c.Font; s.SamsungWeight = c.Weight; s.SamsungColor = c.Color;
                        if (layers.Foreground != null) layers.Foreground.Dispose();
                        layers.Foreground = layers.ComposeForeground(s);
                        using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(screen.Width, screen.Height))
                        {
                            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap)) DesktopClockForm.PaintClock(g, screen, s, layers, null, new DateTime(2026, 11, 3, 19, 44, 7));
                            bitmap.Save(System.IO.Path.Combine(args[1], "reloj-" + c.Name + ".png"));
                        }
                    }
                }
                return;
            }

            // Herramienta de desarrollo: StreeckSuite.exe --preview-start <carpeta>
            if (args != null && args.Length >= 2 && args[0] == "--preview-start")
            {
                NativeMethods.SetProcessDPIAware();
                SuiteSettings.Load();
                System.IO.Directory.CreateDirectory(args[1]);
                using (StartMenuWindow menu = new StartMenuWindow()) menu.RenderPreviews(args[1]);
                return;
            }

            // Una sola instancia: dos copias pintarían dos notch y dos iconos en la bandeja
            bool isFirstInstance;
            using (Mutex singleInstance = new Mutex(true, @"Local\StreeckSuite.SingleInstance", out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    // StreeckSuite.exe --salir: pide a la instancia abierta que se cierre bien (devuelve la barra de Windows)
                    if (args != null && args.Length >= 1 && args[0] == "--salir")
                    {
                        using (EventWaitHandle exit = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName)) exit.Set();
                        return;
                    }
                    Log.Info("[Program] Ya hay una instancia en ejecución; se cierra esta");
                    return;
                }
                if (args != null && args.Length >= 1 && args[0] == "--salir") return;

                Run();
            }
        }

        private static void Run()
        {
            // Debe ir antes de crear cualquier ventana; sin esto Windows escala el notch como mapa de bits y se ve borroso
            NativeMethods.SetProcessDPIAware();

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) => Log.Error("[Program] Excepción no controlada: {0}", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => Log.Error("[Program] Excepción fatal: {0}", e.ExceptionObject);

            Log.Info("[Program] Iniciando Windows 11 Custom Suite");
            SuiteSettings.Load();

            // 1. Inicializar Núcleo de Módulos
            ModuleManager manager = ModuleManager.Instance;
            EventBus eventBus = EventBus.Instance;

            // 2. Registrar Módulos de la Suite
            manager.Register(new AeroDwmModule());
            manager.Register(new DynamicNotchModule());
            manager.Register(new TaskbarIslandModule());
            manager.Register(new DepthClockModule());
            manager.Register(new DesktopWidgetsModule());
            manager.Register(new StartMenuModule());
            manager.Register(new TrayIconModule());

            // 3. Iniciar Monitor de Rendimiento y Modo Juegos (0% impacto)
            SystemMonitor monitor = new SystemMonitor(eventBus);
            monitor.Start();

            // 4. Iniciar todos los módulos registrados
            manager.StartAll();

            // Cierre ordenado pedido desde fuera (--salir), en el hilo de la interfaz
            using (Control exitTarget = new Control())
            using (EventWaitHandle exitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName))
            {
                IntPtr exitHandle = exitTarget.Handle;
                RegisteredWaitHandle exitWait = ThreadPool.RegisterWaitForSingleObject(exitRequested,
                    (state, timedOut) => exitTarget.BeginInvoke((Action)Application.Exit), null, Timeout.Infinite, true);

                // 5. Ciclo de vida nativo de Windows (Bandeja y overlays)
                Application.Run();
                exitWait.Unregister(null);
            }

            // 6. Limpieza al salir
            monitor.Stop();
            manager.StopAll();
            Log.Info("[Program] Suite detenida");
        }
    }
}
