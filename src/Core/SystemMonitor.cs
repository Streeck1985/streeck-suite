using System;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace StreeckSuite.Core
{
    /// <summary>
    /// Monitor de rendimiento y detección de modo juego a pantalla completa.
    /// Suspende la suite cuando un videojuego o app ocupa la pantalla completa.
    /// </summary>
    public class SystemMonitor
    {
        // Ventanas del shell que ocupan toda la pantalla sin ser un juego:
        // el fondo del escritorio (Progman / WorkerW) y la barra de tareas.
        private static readonly string[] ShellWindowClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        private readonly Timer _timer;
        private readonly EventBus _eventBus;
        private readonly uint _ownProcessId;
        private bool _isGameModeSuspended;

        public bool IsGameModeSuspended
        {
            get { return _isGameModeSuspended; }
        }

        public SystemMonitor(EventBus eventBus)
        {
            _eventBus = eventBus;
            _ownProcessId = (uint)Process.GetCurrentProcess().Id;
            _timer = new Timer();
            _timer.Interval = 1500; // Verificación suave cada 1.5s (cero impacto en CPU)
            _timer.Tick += OnTick;
        }

        public void Start()
        {
            _timer.Start();
        }

        public void Stop()
        {
            _timer.Stop();
        }

        private void OnTick(object sender, EventArgs e)
        {
            IntPtr foregroundHwnd = NativeMethods.GetForegroundWindow();
            if (foregroundHwnd == IntPtr.Zero) return;

            // Mientras el foco esté en una ventana propia (notch, menú de la bandeja) se mantiene el estado actual
            uint processId;
            NativeMethods.GetWindowThreadProcessId(foregroundHwnd, out processId);
            if (processId == _ownProcessId) return;

            bool isFullScreen = !IsShellWindow(foregroundHwnd) && CoversItsScreen(foregroundHwnd);

            if (isFullScreen && !_isGameModeSuspended)
            {
                _isGameModeSuspended = true;
                _eventBus.Publish(Events.GameModeActive, true);
                // Se anota quién fue, para encontrar falsas alarmas (ventanas que cubren la pantalla sin ser juegos)
                string who = "?";
                try { using (Process p = Process.GetProcessById((int)processId)) who = p.ProcessName; } catch (Exception) { }
                StringBuilder cls = new StringBuilder(256);
                NativeMethods.GetClassName(foregroundHwnd, cls, cls.Capacity);
                Log.Info("[SystemMonitor] Juego / Pantalla completa detectada -> Suite en suspensión ({0}, {1})", who, cls);
            }
            else if (!isFullScreen && _isGameModeSuspended)
            {
                _isGameModeSuspended = false;
                _eventBus.Publish(Events.GameModeActive, false);
                Log.Info("[SystemMonitor] Retorno al escritorio -> Suite reactivada");
            }
        }

        public static bool IsShellWindow(IntPtr hwnd)
        {
            if (hwnd == NativeMethods.GetShellWindow() || hwnd == NativeMethods.GetDesktopWindow()) return true;

            StringBuilder className = new StringBuilder(256);
            NativeMethods.GetClassName(hwnd, className, className.Capacity);
            string name = className.ToString();
            foreach (string shellClass in ShellWindowClasses)
            {
                if (string.Equals(name, shellClass, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        private static bool CoversItsScreen(IntPtr hwnd)
        {
            // Ventanas que cubren la pantalla sin verse (capas de NVIDIA, Discord, clientes de juegos...): no cuentan
            if (!IsWindowVisible(hwnd)) return false;
            int cloaked;
            if (DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out cloaked, 4) == 0 && cloaked != 0) return false;
            int exStyle = GetWindowLong(hwnd, -20 /* GWL_EXSTYLE */);
            if ((exStyle & 0x20) != 0 && (exStyle & 0x80000) != 0) return false; // Transparente a los clics (WS_EX_TRANSPARENT + LAYERED)
            if ((exStyle & 0x80) != 0) return false; // Ventana de herramienta (WS_EX_TOOLWINDOW): no es un juego

            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(hwnd, out rect)) return false;

            Rectangle bounds = Screen.FromHandle(hwnd).Bounds;
            return rect.Left <= bounds.Left && rect.Top <= bounds.Top &&
                   rect.Right >= bounds.Right && rect.Bottom >= bounds.Bottom;
        }
    }
}
