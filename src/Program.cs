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
        private const string OpenSettingsEventName = @"Local\StreeckSuite.OpenSettings";

        [STAThread]
        public static void Main(string[] args)
        {
            // Gancho de Claude Code: StreeckSuite.exe --agente trabajando|termino|permiso (avisa a la suite abierta y sale)
            if (args != null && args.Length >= 2 && args[0] == "--agente")
            {
                AgentNotifier.Send(args[1]);
                return;
            }

            // Herramienta de desarrollo: StreeckSuite.exe --test-claude-hooks <archivo.json> 1|0
            if (args != null && args.Length >= 3 && args[0] == "--test-claude-hooks")
            {
                AgentNotifier.ConnectClaude(args[2] == "1", args[1]);
                return;
            }

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

            // Herramienta de desarrollo: StreeckSuite.exe --preview-widgets <archivo.png>: los widgets del escritorio juntos
            if (args != null && args.Length >= 2 && args[0] == "--preview-widgets")
            {
                NativeMethods.SetProcessDPIAware();
                SuiteSettings.Load();
                WeatherService.Start();
                MediaService.EnsureStarted();
                Thread.Sleep(4000);
                WidgetLayout layout = new WidgetLayout();
                TelemetrySampler sampler = new TelemetrySampler();
                sampler.Sample(); Thread.Sleep(1000);
                TelemetryWidget telemetry = new TelemetryWidget(layout, TelemetrySampler.GetCpuName(), TelemetrySampler.GetGpuName());
                telemetry.UpdateData(sampler.Sample());
                MediaWidget media = new MediaWidget(layout);
                media.UpdateData(MediaService.Current);
                WidgetWindow[] all = { new WeatherWidget(layout), new AgendaWidget(layout), media, telemetry, new NotesWidget(layout), new CountdownWidget(layout) };
                using (System.Drawing.Bitmap sheet = new System.Drawing.Bitmap(2 * 320, 3 * 260))
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(sheet))
                {
                    g.Clear(System.Drawing.Color.FromArgb(40, 60, 100));
                    for (int i = 0; i < all.Length; i++)
                    {
                        using (System.Drawing.Bitmap card = all[i].RenderPreview()) g.DrawImage(card, (i % 2) * 320, (i / 2) * 260);
                    }
                    sheet.Save(args[1]);
                }
                return;
            }

            // Herramienta de desarrollo: StreeckSuite.exe --preview-eyes <archivo.png>: todos los estilos con sus 8 caras
            if (args != null && args.Length >= 2 && args[0] == "--preview-eyes")
            {
                SuiteSettings.Load();
                IslandEyes.Mood[] moods = { IslandEyes.Mood.Idle, IslandEyes.Mood.Happy, IslandEyes.Mood.Surprised, IslandEyes.Mood.Sleep,
                                            IslandEyes.Mood.Dizzy, IslandEyes.Mood.Love, IslandEyes.Mood.Angry, IslandEyes.Mood.Wink };
                Array styles = Enum.GetValues(typeof(SuiteSettings.EyesStyleKind));
                const int cellW = 150, cellH = 64, labelW = 110;
                using (System.Drawing.Bitmap sheet = new System.Drawing.Bitmap(labelW + moods.Length * cellW, styles.Length * cellH))
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(sheet))
                using (System.Drawing.Font font = new System.Drawing.Font("Segoe UI", 11f))
                {
                    g.Clear(System.Drawing.Color.FromArgb(24, 26, 32));
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    for (int r = 0; r < styles.Length; r++)
                    {
                        SuiteSettings.EyesStyle = (SuiteSettings.EyesStyleKind)styles.GetValue(r);
                        g.DrawString(SuiteSettings.EyesStyle.ToString(), font, System.Drawing.Brushes.White, 8, r * cellH + 22);
                        for (int m = 0; m < moods.Length; m++)
                        {
                            System.Drawing.RectangleF pill = new System.Drawing.RectangleF(labelW + m * cellW + 10, r * cellH + 8, cellW - 20, cellH - 16);
                            IslandPainter.FillRounded(g, pill, pill.Height / 2, System.Drawing.Color.Black);
                            IslandEyes.DrawPreview(g, new System.Drawing.PointF(pill.X + pill.Width / 2, pill.Y + pill.Height / 2), 1f, moods[m]);
                        }
                    }
                    sheet.Save(args[1]);
                }
                return;
            }

            // Herramienta de desarrollo: StreeckSuite.exe --preview-clock-pair <carpeta> <fondo> <prefijo>: los dos estilos
            // (detrás y contorno) con tus ajustes sobre un fondo dado, para comparar antes y después
            if (args != null && args.Length >= 4 && args[0] == "--preview-clock-pair")
            {
                NativeMethods.SetProcessDPIAware();
                System.IO.Directory.CreateDirectory(args[1]);
                ClockSettings saved = ClockSettings.Load();
                Screen target = DepthClockModule.GetTargetScreen(saved);
                System.Drawing.Size screen = target.Bounds.Size;
                using (DepthClockLayers layers = DepthClockLayers.Analyze(args[2], target))
                {
                    foreach (ClockStyle style in new[] { ClockStyle.Apple, ClockStyle.Samsung })
                    {
                        ClockSettings s = saved.Clone();
                        s.Style = style;
                        if (layers.Foreground != null) layers.Foreground.Dispose();
                        layers.Foreground = layers.ComposeForeground(s);
                        if (style == ClockStyle.Apple) layers.Foreground.Save(System.IO.Path.Combine(args[1], args[3] + "-primer-plano.png"));
                        using (System.Drawing.Bitmap bitmap = new System.Drawing.Bitmap(screen.Width, screen.Height))
                        {
                            using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(bitmap)) DesktopClockForm.PaintClock(g, screen, s, layers, null, new DateTime(2026, 11, 10, 20, 26, 21));
                            bitmap.Save(System.IO.Path.Combine(args[1], args[3] + (style == ClockStyle.Apple ? "-detras.png" : "-contorno.png")));
                        }
                    }
                }
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
                    foreach (float top in new[] { 0f, 0.02f, 0.05f, 0.15f, 0.25f, 0.35f, 0.6f, 0.8f })
                    foreach (float size in new[] { saved.Size, 0.8f })
                    {
                        ClockSettings s = saved.Clone();
                        s.Top = top;
                        s.Size = size;
                        try
                        {
                            using (System.Drawing.Bitmap test = new System.Drawing.Bitmap(screen.Width, screen.Height))
                            using (System.Drawing.Graphics tg = System.Drawing.Graphics.FromImage(test))
                            {
                                DesktopClockForm.PaintClock(tg, screen, s, layers, null, new DateTime(2026, 11, 3, 19, 44, 7));
                            }
                        }
                        catch (Exception ex)
                        {
                            System.IO.File.AppendAllText(System.IO.Path.Combine(args[1], "errores.txt"), "arriba=" + top + " tamano=" + size + ": " + ex + "\r\n");
                        }
                        if (size != saved.Size) continue;
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

                    // Si ya está abierta, pedir a la instancia activa que traiga la ventana de Ajustes al frente
                    try
                    {
                        using (EventWaitHandle open = EventWaitHandle.OpenExisting(OpenSettingsEventName))
                        {
                            open.Set();
                            Log.Info("[Program] Ya hay una instancia en ejecución; se abrió la ventana de Ajustes");
                            return;
                        }
                    }
                    catch
                    {
                        Log.Info("[Program] Ya hay una instancia en ejecución; se cierra esta");
                        return;
                    }
                }
                if (args != null && args.Length >= 1 && args[0] == "--salir") return;

                Run(args);
            }
        }

        private static void Run(string[] args)
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
            MemoryTrim.Start(); // Devuelve a Windows la memoria que ya no se usa
            Modules.UpdateService.StartAutomatic(); // Busca versiones nuevas en GitHub (al minuto y cada día)

            // Cierre ordenado y apertura de ajustes pedidos desde fuera en el hilo de la interfaz
            using (Control exitTarget = new Control())
            using (EventWaitHandle exitRequested = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEventName))
            using (EventWaitHandle settingsRequested = new EventWaitHandle(false, EventResetMode.AutoReset, OpenSettingsEventName))
            {
                IntPtr exitHandle = exitTarget.Handle;
                RegisteredWaitHandle exitWait = ThreadPool.RegisterWaitForSingleObject(exitRequested,
                    (state, timedOut) => exitTarget.BeginInvoke((Action)Application.Exit), null, Timeout.Infinite, true);

                RegisteredWaitHandle settingsWait = ThreadPool.RegisterWaitForSingleObject(settingsRequested,
                    (state, timedOut) => exitTarget.BeginInvoke((Action)(() => SettingsWindow.Open(SettingsWindow.Page.Clock))), null, Timeout.Infinite, false);

                // Si se solicitó abrir ajustes directamente desde la línea de comandos
                if (args != null && args.Length >= 1)
                {
                    if (args[0] == "--ajustes" || args[0] == "--reloj" || args[0] == "--open")
                    {
                        SettingsWindow.Open(SettingsWindow.Page.Clock);
                    }
                    else if (args[0] == "--avisos" || args[0] == "--notificaciones")
                    {
                        SettingsWindow.Open(SettingsWindow.Page.Notifications);
                    }
                }

                // 5. Ciclo de vida nativo de Windows (Bandeja y overlays)
                Application.Run();
                exitWait.Unregister(null);
                settingsWait.Unregister(null);
            }

            // 6. Limpieza al salir
            monitor.Stop();
            manager.StopAll();
            Log.Info("[Program] Suite detenida");
        }
    }
}
