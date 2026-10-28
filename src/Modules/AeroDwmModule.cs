using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Módulo Aero DWM: Controla la inyección global de Aero Glass, Acrylic y el Modo Privacidad Aero Real.
    /// Crea una lámina de cristal acrílico en el Z-Order colocada dinámicamente justo detrás de la ventana en uso,
    /// desenfocando el escritorio y todas las aplicaciones de fondo.
    /// </summary>
    public class AeroDwmModule : IModule
    {
        public string Id { get { return "aero_dwm"; } }
        public string Name { get { return "Motor Global Aero DWM y Privacidad"; } }
        public string Version { get { return "1.1.0"; } }
        public bool IsEnabled { get; set; }

        private EventBus _eventBus;
        private bool _isPrivacyModeActive;
        private bool _isGameModeSuspended;
        private PrivacyOverlayForm _overlayForm;
        private HotkeyWindow _hotkeyWindow;

        /// <summary>
        /// Instancia en marcha, para la página Privacidad del panel de ajustes.
        /// </summary>
        internal static AeroDwmModule Current { get; private set; }

        public AeroDwmModule()
        {
            IsEnabled = true;
        }

        internal bool IsPrivacyActive
        {
            get { return _isPrivacyModeActive; }
        }

        /// <summary>
        /// Atajo registrado ("Ctrl+Alt+P"...), o null si todos estaban ocupados.
        /// </summary>
        internal string ActiveShortcut
        {
            get { return _hotkeyWindow != null ? _hotkeyWindow.ActiveShortcut : null; }
        }

        public void Initialize(EventBus eventBus)
        {
            Current = this;
            _eventBus = eventBus;
            _eventBus.Subscribe(Events.AeroSetStyle, OnSetAeroStyle);
            _eventBus.Subscribe(Events.PrivacyToggle, OnTogglePrivacy);
            _eventBus.Subscribe(Events.GameModeActive, OnGameModeActive);
        }

        public void Start()
        {
            // Registrar ventana receptora para el atajo global de privacidad
            try
            {
                _hotkeyWindow = new HotkeyWindow(() => TogglePrivacy());
            }
            catch (Exception ex)
            {
                Log.Error("[AeroDwmModule] No se pudo crear la ventana del atajo global: {0}", ex);
            }

            if (_hotkeyWindow != null && _hotkeyWindow.ActiveShortcut != null)
            {
                Log.Info("[AeroDwmModule] Iniciado y listo para composición (Atajo: {0})", _hotkeyWindow.ActiveShortcut);
            }
            else
            {
                Log.Error("[AeroDwmModule] Ningún atajo de privacidad está libre; otro programa los tiene registrados");
                _eventBus.Publish(Events.NotchShowMessage, "⚠️ Atajo de privacidad ocupado por otra app; usa la bandeja");
            }
        }

        public void Stop()
        {
            if (_overlayForm != null && !_overlayForm.IsDisposed)
            {
                _overlayForm.Close();
                _overlayForm = null;
            }

            if (_hotkeyWindow != null)
            {
                _hotkeyWindow.Dispose();
                _hotkeyWindow = null;
            }

            _isPrivacyModeActive = false;

            Log.Info("[AeroDwmModule] Detenido");
        }

        private void OnSetAeroStyle(object payload)
        {
            string style = payload as string ?? "glass-aero";
            Log.Info("[AeroDwmModule] Aplicando estilo global: {0}", style);
        }

        private void OnTogglePrivacy(object payload)
        {
            TogglePrivacy();
        }

        public void TogglePrivacy()
        {
            _isPrivacyModeActive = !_isPrivacyModeActive;

            if (_isPrivacyModeActive)
            {
                if (!_isGameModeSuspended)
                {
                    ShowOverlay();
                }
                _eventBus.Publish(Events.PrivacyStateChanged, true);
                string shortcut = _hotkeyWindow != null ? _hotkeyWindow.ActiveShortcut : null;
                _eventBus.Publish(Events.NotchShowMessage, shortcut != null
                    ? string.Format("🛡️ Modo Privacidad Aero: ACTIVADO ({0})", shortcut)
                    : "🛡️ Modo Privacidad Aero: ACTIVADO");
                Log.Info("[AeroDwmModule] Modo privacidad: ACTIVADO");
            }
            else
            {
                HideOverlay();
                _eventBus.Publish(Events.PrivacyStateChanged, false);
                _eventBus.Publish(Events.NotchShowMessage, "🛡️ Modo Privacidad Aero: DESACTIVADO");
                Log.Info("[AeroDwmModule] Modo privacidad: DESACTIVADO");
            }
        }

        private void ShowOverlay()
        {
            if (_overlayForm == null || _overlayForm.IsDisposed)
            {
                _overlayForm = new PrivacyOverlayForm();
            }
            _overlayForm.ActivatePrivacy();
        }

        private void HideOverlay()
        {
            if (_overlayForm != null && !_overlayForm.IsDisposed)
            {
                _overlayForm.DeactivatePrivacy();
            }
        }

        private void OnGameModeActive(object payload)
        {
            _isGameModeSuspended = payload is bool && (bool)payload;

            if (_isGameModeSuspended)
            {
                HideOverlay();
            }
            else if (_isPrivacyModeActive)
            {
                ShowOverlay();
            }
        }

        /// <summary>
        /// Quita el borde de 1 px y las esquinas redondeadas que Windows 11 añade por su cuenta. Necesario en ventanas
        /// recortadas con Region (notch, widgets): DWM dibuja ese borde siguiendo el rectángulo completo, fuera del recorte.
        /// </summary>
        public static void RemoveDwmFrame(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            int cornerPref = NativeMethods.DWMWCP_DONOTROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

            int noBorder = NativeMethods.DWMWA_COLOR_NONE;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
        }

        /// <summary>
        /// Aplica el material Acrylic de Windows a una ventana. Ojo: tintColor va en orden 0xAABBGGRR (no 0xAARRGGBB);
        /// usar AcrylicColor para construirlo.
        /// </summary>
        public static void ApplyAcrylicGlass(IntPtr hwnd, int tintColor)
        {
            if (hwnd == IntPtr.Zero) return;

            // 1. Activar redondeo de bordes Windows 11
            int cornerPref = NativeMethods.DWMWCP_ROUND;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

            // 2. Activar modo oscuro inmersivo
            int darkMode = 1;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

            // 3. Extender marco de cristal
            NativeMethods.MARGINS margins = new NativeMethods.MARGINS(-1, -1, -1, -1);
            NativeMethods.DwmExtendFrameIntoClientArea(hwnd, ref margins);

            // 4. Inyectar política de Acrylic Blur
            NativeMethods.AccentPolicy policy = new NativeMethods.AccentPolicy();
            policy.AccentState = NativeMethods.AccentState.ACCENT_ENABLE_ACRYLICBLURBEHIND;
            policy.GradientColor = tintColor;

            int size = Marshal.SizeOf(policy);
            IntPtr policyPtr = Marshal.AllocHGlobal(size);
            Marshal.StructureToPtr(policy, policyPtr, false);

            NativeMethods.WindowCompositionAttributeData data = new NativeMethods.WindowCompositionAttributeData();
            data.Attribute = 19; // WCA_ACCENT_POLICY
            data.Data = policyPtr;
            data.SizeOfData = size;

            NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
            Marshal.FreeHGlobal(policyPtr);
        }

        public static int AcrylicColor(Color color, int alpha)
        {
            return (alpha << 24) | (color.B << 16) | (color.G << 8) | color.R;
        }
    }

    /// <summary>
    /// Capa de pantalla completa que se coloca justo detrás de la ventana activa y desenfoca todo lo demás.
    /// El desenfoque lo calcula la suite (intensidad ajustable): ~10 veces por segundo captura la pantalla reducida
    /// sin la propia capa (WDA_EXCLUDEFROMCAPTURE), la desenfoca y le mezcla el tinte; si la imagen no cambió no repinta.
    /// Los clics la atraviesan (WS_EX_LAYERED + WS_EX_TRANSPARENT).
    /// </summary>
    internal class PrivacyOverlayForm : Form
    {
        private static readonly string[] ShellWindowClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        private const int FrameMs = 100;
        private const int BlurPasses = 3;          // Tres pasadas de caja ≈ desenfoque gaussiano

        private readonly Timer _frameTimer;
        private IntPtr _lastTargetHwnd = IntPtr.Zero;
        private bool _canExcludeFromCapture;
        private Bitmap _blurred;                   // Captura reducida ya desenfocada y teñida
        private int[] _pixels, _scratch;
        private long _lastCaptureHash;
        private readonly Action _settingsChanged;
        private readonly System.Diagnostics.Stopwatch _cost = new System.Diagnostics.Stopwatch(); // Coste medio, al registro
        private int _frames;

        public PrivacyOverlayForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            Bounds = SystemInformation.VirtualScreen;

            _frameTimer = new Timer();
            _frameTimer.Interval = FrameMs;
            _frameTimer.Tick += (s, e) =>
            {
                RefreshWindowZOrder();
                if (!UsesOwnBlur) return;
                _cost.Start();
                CaptureAndBlur(false);
                Update(); // Pintar ya, para medir también la ampliación a pantalla completa
                _cost.Stop();
                if (++_frames == 100)
                {
                    Log.Info("[AeroDwmModule] Desenfoque propio: {0:0.0} ms por fotograma de media ({1} px)",
                        _cost.Elapsed.TotalMilliseconds / _frames, SuiteSettings.PrivacyBlurPx);
                    _frames = 0;
                    _cost.Reset();
                }
            };

            _settingsChanged = () => { if (!IsDisposed) ApplyLook(); };
            SuiteSettings.Changed += _settingsChanged;
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; } // Jamás robar el foco a la ventana en uso
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080;                    // WS_EX_TOOLWINDOW: No aparece en Alt + Tab
                cp.ExStyle |= NativeMethods.WS_EX_TRANSPARENT; // Los clics del ratón pasan de largo
                cp.ExStyle |= NativeMethods.WS_EX_LAYERED;     // Necesario para que los clics atraviesen y para la opacidad
                cp.ExStyle |= 0x08000000;                    // WS_EX_NOACTIVATE: No recibe foco
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            AeroDwmModule.RemoveDwmFrame(Handle);
            _canExcludeFromCapture = NativeMethods.SetWindowDisplayAffinity(Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
            if (!_canExcludeFromCapture)
            {
                Log.Error("[AeroDwmModule] Windows no permite excluir la capa de la captura; el modo privacidad solo oscurecerá");
            }
            ApplyLook();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SuiteSettings.Changed -= _settingsChanged;
                _frameTimer.Dispose();
                if (_blurred != null) _blurred.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool UsesOwnBlur
        {
            get { return _canExcludeFromCapture && SuiteSettings.PrivacyBlurPx > 0; }
        }

        /// <summary>
        /// Sin desenfoque basta con un velo del color del tinte (la opacidad la da la ventana); con desenfoque la capa
        /// es opaca y la imagen desenfocada ya lleva el tinte mezclado.
        /// </summary>
        private void ApplyLook()
        {
            if (!IsHandleCreated) return;
            byte alpha = UsesOwnBlur ? (byte)255 : (byte)(SuiteSettings.PrivacyDarkenPercent * 255 / 100);
            NativeMethods.SetLayeredWindowAttributes(Handle, 0, alpha, NativeMethods.LWA_ALPHA);
            if (UsesOwnBlur && Visible) CaptureAndBlur(true);
            Invalidate();
        }

        public void ActivatePrivacy()
        {
            Bounds = SystemInformation.VirtualScreen;
            if (!IsHandleCreated) CreateHandle();
            if (UsesOwnBlur) CaptureAndBlur(true); // Antes de mostrarla, para que el primer fotograma no salga vacío
            Show();
            RefreshWindowZOrder();
            _frameTimer.Start();
        }

        public void DeactivatePrivacy()
        {
            _frameTimer.Stop();
            Hide();
            _lastTargetHwnd = IntPtr.Zero;
            if (_blurred != null)
            {
                _blurred.Dispose();
                _blurred = null;
            }
        }

        #region Desenfoque

        private void CaptureAndBlur(bool force)
        {
            Rectangle area = Bounds;
            int radiusPx = SuiteSettings.PrivacyBlurPx;
            // Se trabaja a tamaño reducido: cuanto más desenfoque, más se puede reducir sin que se note
            float downscale = Math.Max(2f, radiusPx / 3f);
            int width = Math.Max(1, (int)(area.Width / downscale));
            int height = Math.Max(1, (int)(area.Height / downscale));
            int radius = Math.Max(1, (int)Math.Round(radiusPx / downscale));

            if (_blurred == null || _blurred.Width != width || _blurred.Height != height)
            {
                if (_blurred != null) _blurred.Dispose();
                _blurred = new Bitmap(width, height, PixelFormat.Format32bppRgb);
                _pixels = new int[width * height];
                _scratch = new int[width * height];
                force = true;
            }

            // Captura reducida directa de la pantalla (la capa queda fuera gracias a WDA_EXCLUDEFROMCAPTURE)
            IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
            try
            {
                using (Graphics g = Graphics.FromImage(_blurred))
                {
                    IntPtr hdc = g.GetHdc();
                    NativeMethods.SetStretchBltMode(hdc, NativeMethods.HALFTONE);
                    NativeMethods.StretchBlt(hdc, 0, 0, width, height, screenDc, area.X, area.Y, area.Width, area.Height, NativeMethods.SRCCOPY);
                    g.ReleaseHdc(hdc);
                }
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            }

            BitmapData data = _blurred.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
            try
            {
                Marshal.Copy(data.Scan0, _pixels, 0, _pixels.Length);

                // Si lo que hay detrás no cambió, no hace falta repintar la pantalla entera
                long hash = 17;
                for (int i = 0; i < _pixels.Length; i += 7) hash = hash * 31 + _pixels[i];
                if (!force && hash == _lastCaptureHash) return;
                _lastCaptureHash = hash;

                for (int pass = 0; pass < BlurPasses; pass++)
                {
                    BoxBlur(_pixels, _scratch, width, height, radius, true);
                    BoxBlur(_scratch, _pixels, width, height, radius, false);
                }
                MixTint(_pixels, SuiteSettings.PrivacyTint, SuiteSettings.PrivacyDarkenPercent);
                Marshal.Copy(_pixels, 0, data.Scan0, _pixels.Length);
            }
            finally
            {
                _blurred.UnlockBits(data);
            }
            Invalidate();
        }

        /// <summary>
        /// Media móvil de (2·radius+1) píxeles en una dirección; los bordes repiten el último píxel.
        /// </summary>
        private static void BoxBlur(int[] source, int[] target, int width, int height, int radius, bool horizontal)
        {
            int lines = horizontal ? height : width;
            int length = horizontal ? width : height;
            int step = horizontal ? 1 : width;
            int window = 2 * radius + 1;

            for (int line = 0; line < lines; line++)
            {
                int start = horizontal ? line * width : line;
                int last = start + (length - 1) * step;
                int r = 0, g = 0, b = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int p = source[start + Math.Max(0, Math.Min(length - 1, k)) * step];
                    r += (p >> 16) & 0xFF; g += (p >> 8) & 0xFF; b += p & 0xFF;
                }
                for (int i = 0; i < length; i++)
                {
                    target[start + i * step] = (r / window << 16) | (g / window << 8) | (b / window);
                    int outgoing = source[i - radius <= 0 ? start : start + (i - radius) * step];
                    int incoming = source[i + radius + 1 >= length ? last : start + (i + radius + 1) * step];
                    r += ((incoming >> 16) & 0xFF) - ((outgoing >> 16) & 0xFF);
                    g += ((incoming >> 8) & 0xFF) - ((outgoing >> 8) & 0xFF);
                    b += (incoming & 0xFF) - (outgoing & 0xFF);
                }
            }
        }

        private static void MixTint(int[] pixels, Color tint, int percent)
        {
            int keep = 100 - percent;
            int tr = tint.R * percent, tg = tint.G * percent, tb = tint.B * percent;
            for (int i = 0; i < pixels.Length; i++)
            {
                int p = pixels[i];
                int r = (((p >> 16) & 0xFF) * keep + tr) / 100;
                int g = (((p >> 8) & 0xFF) * keep + tg) / 100;
                int b = ((p & 0xFF) * keep + tb) / 100;
                pixels[i] = (r << 16) | (g << 8) | b;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (!UsesOwnBlur || _blurred == null)
            {
                g.Clear(SuiteSettings.PrivacyTint);
                return;
            }

            // Ampliación bilineal: la imagen reducida y desenfocada se estira a toda la pantalla sin escalones
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.CompositingMode = CompositingMode.SourceCopy;
            using (ImageAttributes wrap = new ImageAttributes())
            {
                wrap.SetWrapMode(WrapMode.TileFlipXY); // Sin franja oscura en los bordes
                g.DrawImage(_blurred, ClientRectangle, 0, 0, _blurred.Width, _blurred.Height, GraphicsUnit.Pixel, wrap);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Todo se dibuja en OnPaintBackground
        }

        #endregion

        private void RefreshWindowZOrder()
        {
            if (!Visible || IsDisposed) return;

            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == Handle) return;

            // Al pasar al escritorio o la barra de tareas, la capa se queda donde estaba: mandarla al fondo
            // dejaría todas las ventanas sin desenfocar. Las ventanas propias (notch, menú de la bandeja) tampoco la mueven.
            uint processId;
            NativeMethods.GetWindowThreadProcessId(fg, out processId);
            if (IsShellWindow(fg) || processId == (uint)System.Diagnostics.Process.GetCurrentProcess().Id) return;

            // Colocar el overlay en el Z-Order inmediatamente después de la ventana activa (justo detrás de ella)
            if (fg != _lastTargetHwnd)
            {
                _lastTargetHwnd = fg;
                NativeMethods.SetWindowPos(Handle, fg, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            }
        }

        private static bool IsShellWindow(IntPtr hwnd)
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
    }

    /// <summary>
    /// Ventana oculta para escuchar el atajo global de privacidad. Prueba varias combinaciones
    /// porque otras apps (PowerToys, overlays de GPU...) pueden tener ya registrada la primera.
    /// </summary>
    internal class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int HotkeyId = 0xB100;
        private const uint KeyP = 0x50;
        private const uint KeyQ = 0x51;

        private readonly Action _onHotkeyPressed;
        private readonly string _activeShortcut;

        public HotkeyWindow(Action onHotkeyPressed)
        {
            _onHotkeyPressed = onHotkeyPressed;
            CreateHandle(new CreateParams());

            const uint ctrlAlt = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;
            if (TryRegister(ctrlAlt, KeyP)) _activeShortcut = "Ctrl+Alt+P";
            else if (TryRegister(ctrlAlt | NativeMethods.MOD_SHIFT, KeyP)) _activeShortcut = "Ctrl+Alt+Shift+P";
            else if (TryRegister(ctrlAlt, KeyQ)) _activeShortcut = "Ctrl+Alt+Q";
        }

        /// <summary>
        /// Combinación registrada, o null si todas estaban ocupadas.
        /// </summary>
        public string ActiveShortcut
        {
            get { return _activeShortcut; }
        }

        private bool TryRegister(uint modifiers, uint key)
        {
            return NativeMethods.RegisterHotKey(Handle, HotkeyId, modifiers, key);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                if (_onHotkeyPressed != null)
                {
                    _onHotkeyPressed();
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
            {
                NativeMethods.UnregisterHotKey(Handle, HotkeyId);
                DestroyHandle();
            }
        }
    }
}
