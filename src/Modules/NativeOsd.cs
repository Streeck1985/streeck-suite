using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Esconde el aviso de volumen de Windows 11 (fase J5, como el Bloom del usuario) porque la isla muestra el suyo.
    /// El aviso vive en una ventana pequeña del Explorador (XamlExplorerHostIslandWindow que no se activa); se vuelve
    /// transparente al 100 % y sin clics. La misma clase sirve para Alt+Tab y la Vista de tareas, que son de pantalla
    /// completa y la comprobación de tamaño deja fuera. Al salir se restaura.
    /// </summary>
    internal static class NativeOsd
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000;
        private const int LWA_ALPHA = 0x2;

        /// <summary>
        /// Aplica el ajuste: escondido o visible. Barato; se puede llamar cada pocos segundos (Explorer crea la ventana
        /// la primera vez que cambia el volumen y la recrea si se reinicia).
        /// </summary>
        public static void Apply(bool suppress)
        {
            foreach (IntPtr hwnd in Windows())
            {
                int style = GetWindowLong(hwnd, GWL_EXSTYLE);
                byte alpha = 255;
                uint key, flags;
                bool suppressed = (style & WS_EX_LAYERED) != 0 && GetLayeredWindowAttributes(hwnd, out key, out alpha, out flags) && alpha == 0;
                if (suppress && !suppressed)
                {
                    SetWindowLong(hwnd, GWL_EXSTYLE, style | WS_EX_LAYERED | WS_EX_TRANSPARENT);
                    SetLayeredWindowAttributes(hwnd, 0, 0, LWA_ALPHA);
                }
                else if (!suppress && suppressed)
                {
                    SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA);
                    SetWindowLong(hwnd, GWL_EXSTYLE, style & ~(WS_EX_LAYERED | WS_EX_TRANSPARENT));
                }
            }
        }

        private static List<IntPtr> Windows()
        {
            List<IntPtr> found = new List<IntPtr>();
            IntPtr tray = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero) return found;
            uint explorer;
            NativeMethods.GetWindowThreadProcessId(tray, out explorer);
            int screenW = GetSystemMetrics(0), screenH = GetSystemMetrics(1);
            IntPtr after = IntPtr.Zero;
            while ((after = NativeMethods.FindWindowEx(IntPtr.Zero, after, "XamlExplorerHostIslandWindow", null)) != IntPtr.Zero)
            {
                uint pid;
                NativeMethods.GetWindowThreadProcessId(after, out pid);
                NativeMethods.RECT rect;
                bool small = NativeMethods.GetWindowRect(after, out rect) && rect.Right - rect.Left < screenW / 2 && rect.Bottom - rect.Top < screenH / 2;
                if (pid == explorer && (GetWindowLong(after, GWL_EXSTYLE) & WS_EX_NOACTIVATE) != 0 && small) found.Add(after);
            }
            return found;
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint key, byte alpha, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetLayeredWindowAttributes(IntPtr hWnd, out uint key, out byte alpha, out uint flags);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);
    }
}
