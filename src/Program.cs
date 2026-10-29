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

            // Una sola instancia: dos copias pintarían dos notch y dos iconos en la bandeja
            bool isFirstInstance;
            using (Mutex singleInstance = new Mutex(true, @"Local\StreeckSuite.SingleInstance", out isFirstInstance))
            {
                if (!isFirstInstance)
                {
                    Log.Info("[Program] Ya hay una instancia en ejecución; se cierra esta");
                    return;
                }

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
            manager.Register(new TrayIconModule());

            // 3. Iniciar Monitor de Rendimiento y Modo Juegos (0% impacto)
            SystemMonitor monitor = new SystemMonitor(eventBus);
            monitor.Start();

            // 4. Iniciar todos los módulos registrados
            manager.StartAll();

            // 5. Ciclo de vida nativo de Windows (Bandeja y overlays)
            Application.Run();

            // 6. Limpieza al salir
            monitor.Stop();
            manager.StopAll();
            Log.Info("[Program] Suite detenida");
        }
    }
}
