using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Ventana incrustada en la capa del fondo de escritorio: queda encima del fondo de Windows y debajo de los iconos.
    /// </summary>
    internal class DesktopClockForm : Form
    {
        private static readonly CultureInfo Spanish = new CultureInfo("es-ES");

        private readonly Timer _minuteTimer;
        private readonly Timer _zOrderGuard;
        private DepthClockLayers _layers;
        private ClockSettings _settings;
        private WeatherService.Conditions _weather;
        private IntPtr _host = IntPtr.Zero;
        private IntPtr _defView = IntPtr.Zero; // Capa de los iconos; el reloj debe quedar siempre justo debajo

        public DesktopClockForm(ClockSettings settings)
        {
            _settings = settings;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            BackColor = Color.Black;
            DoubleBuffered = true;

            // Se redibuja justo al cambiar el minuto, o cada segundo (solo la zona de la hora) si se muestran los segundos
            _minuteTimer = new Timer();
            _minuteTimer.Tick += (s, e) => OnClockTick();
            ScheduleNextMinute();

            // Si Explorer reordena las capas del escritorio, devolver el reloj debajo de los iconos
            _zOrderGuard = new Timer();
            _zOrderGuard.Interval = 1000;
            _zOrderGuard.Tick += (s, e) => KeepBelowIcons();
            _zOrderGuard.Start();
        }

        public bool IsEmbedded
        {
            get
            {
                return !IsDisposed && IsHandleCreated && NativeMethods.IsWindow(_host)
                       && NativeMethods.IsWindow(Handle);
            }
        }

        public void SetLayers(DepthClockLayers layers)
        {
            _layers = layers;
            Invalidate();
        }

        public void SetSettings(ClockSettings settings)
        {
            bool secondsChanged = settings.ShowSeconds != _settings.ShowSeconds;
            _settings = settings;
            Invalidate();
            if (secondsChanged && _minuteTimer.Enabled) ScheduleNextMinute();
        }

        private void OnClockTick()
        {
            DateTime now = DateTime.Now;
            if (now.Second == 0 || !_settings.ShowSeconds || _settings.Style == ClockStyle.Samsung)
            {
                Invalidate(); // Cambio de minuto o modo Samsung: redibujar el área completa
            }
            else
            {
                // Solo los dígitos (con margen para fuentes cuyo trazo se sale de la referencia)
                RectangleF ink;
                using (DepthClockLayout.BuildDigitsPath(ClientSize, _settings, now.ToString(DepthClockLayout.TimeFormat(_settings)), out ink)) { }
                float margin = DepthClockLayout.ClockFontPx(ClientSize, _settings) * 0.15f;
                ink.Inflate(margin, margin);
                Invalidate(Rectangle.Round(RectangleF.FromLTRB(0, ink.Top, ClientSize.Width, ink.Bottom)));
            }
            ScheduleNextMinute();
        }

        public void SetPaused(bool paused)
        {
            // En modo juego el escritorio no se ve: no hace falta redibujar cada minuto
            if (paused)
            {
                _minuteTimer.Stop();
            }
            else
            {
                Invalidate();
                ScheduleNextMinute();
            }
        }

        private void ScheduleNextMinute()
        {
            DateTime now = DateTime.Now;
            int msToNext = _settings.ShowSeconds
                ? 1000 - now.Millisecond + 20
                : (60 - now.Second) * 1000 - now.Millisecond + 50;
            _minuteTimer.Stop();
            _minuteTimer.Interval = Math.Max(_settings.ShowSeconds ? 50 : 1000, msToNext);
            _minuteTimer.Start();
        }

        #region Incrustación en el escritorio

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW: Ocultar de Alt+Tab
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                return cp;
            }
        }

        /// <summary>
        /// Coloca la ventana entre el fondo de Windows y los iconos. En Windows 11 24H2+ el fondo (WorkerW) y los iconos
        /// (SHELLDLL_DefView) son hijos de Progman; en versiones anteriores el WorkerW es una ventana aparte.
        /// </summary>
        public void EmbedInDesktop()
        {
            IntPtr progman = NativeMethods.FindWindow("Progman", null);
            if (progman == IntPtr.Zero)
            {
                Log.Error("[DepthClockModule] No se encontró la ventana del escritorio (Progman)");
                return;
            }

            IntPtr ignored;
            NativeMethods.SendMessageTimeout(progman, NativeMethods.WM_SPAWN_WORKERW, IntPtr.Zero, IntPtr.Zero, 0, 1000, out ignored);

            IntPtr insertAfter;
            IntPtr defView = NativeMethods.FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                _host = progman;
                insertAfter = defView; // Justo debajo de los iconos
                _defView = defView;
            }
            else
            {
                _host = FindLegacyWallpaperWorker();
                insertAfter = NativeMethods.HWND_TOP;
            }

            if (_host == IntPtr.Zero)
            {
                Log.Error("[DepthClockModule] No se encontró la capa del fondo de escritorio");
                return;
            }

            if (!Visible) Show();
            int style = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_STYLE);
            NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_STYLE, (style & ~NativeMethods.WS_POPUP) | NativeMethods.WS_CHILD);
            // Layered: en 24H2+ Progman no tiene superficie de redirección propia y una ventana hija solo se compone
            // en pantalla si es layered (opaca, con alfa 255).
            // Transparent: los clics la atraviesan y llegan al escritorio como siempre (clic derecho > Personalizar,
            // seleccionar iconos arrastrando...); la capa de iconos es transparente donde no hay iconos.
            int exStyle = NativeMethods.GetWindowLong(Handle, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(Handle, NativeMethods.GWL_EXSTYLE,
                exStyle | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT);
            NativeMethods.SetLayeredWindowAttributes(Handle, 0, 255, NativeMethods.LWA_ALPHA);
            NativeMethods.SetParent(Handle, _host);

            // Coordenadas de la pantalla principal convertidas a las de la ventana anfitriona
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            NativeMethods.RECT rect = new NativeMethods.RECT();
            rect.Left = screen.Left;
            rect.Top = screen.Top;
            rect.Right = screen.Right;
            rect.Bottom = screen.Bottom;
            NativeMethods.MapWindowPoints(IntPtr.Zero, _host, ref rect, 2);
            NativeMethods.SetWindowPos(Handle, insertAfter, rect.Left, rect.Top, rect.Width, rect.Height,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
            Log.Info("[DepthClockModule] Reloj incrustado en el escritorio ({0})", defView != IntPtr.Zero ? "Progman 24H2+" : "WorkerW clásico");
        }

        private static IntPtr FindLegacyWallpaperWorker()
        {
            IntPtr worker = IntPtr.Zero;
            IntPtr top = IntPtr.Zero;
            while ((top = NativeMethods.FindWindowEx(IntPtr.Zero, top, "WorkerW", null)) != IntPtr.Zero)
            {
                if (NativeMethods.FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero)
                {
                    // El WorkerW del fondo es el siguiente al que contiene los iconos
                    worker = NativeMethods.FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
                    break;
                }
            }
            return worker;
        }

        private void KeepBelowIcons()
        {
            if (_defView == IntPtr.Zero || !IsHandleCreated) return;
            if (NativeMethods.GetWindow(Handle, NativeMethods.GW_HWNDPREV) == _defView) return;

            NativeMethods.SetWindowPos(Handle, _defView, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            Log.Info("[DepthClockModule] El reloj había quedado encima de los iconos; recolocado debajo");
        }

        protected override void WndProc(ref Message m)
        {
            // El reloj nunca debe subir por encima de la capa de iconos (taparía el escritorio entero)
            if (m.Msg == NativeMethods.WM_WINDOWPOSCHANGING && _defView != IntPtr.Zero)
            {
                NativeMethods.WINDOWPOS pos = (NativeMethods.WINDOWPOS)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.WINDOWPOS));
                if ((pos.flags & NativeMethods.SWP_NOZORDER) == 0)
                {
                    pos.hwndInsertAfter = _defView;
                    Marshal.StructureToPtr(pos, m.LParam, false);
                }
            }
            base.WndProc(ref m);
        }

        #endregion

        #region Dibujo

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            // Todo se pinta en OnPaint; evitar el borrado previo elimina el parpadeo al cambiar el minuto
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (_layers == null)
            {
                g.Clear(Color.Black);
                return;
            }

            PaintClock(g, ClientSize, _settings, _layers, _weather, DateTime.Now);
        }

        /// <summary>
        /// Fondo, hora (clásica o estirada) y primer plano. También lo usa la vista previa --preview-clock.
        /// </summary>
        internal static void PaintClock(Graphics g, Size screen, ClockSettings settings, DepthClockLayers layers, WeatherService.Conditions weather, DateTime now)
        {
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.DrawImageUnscaled(layers.Background, 0, 0);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;

            if (settings.Style == ClockStyle.Samsung)
            {
                SamsungClockRenderer.Draw(g, screen, settings, layers, now, Spanish, weather);
            }
            else
            {
                RectangleF ink;
                using (GraphicsPath digits = DepthClockLayout.BuildDigitsPath(screen, settings, now.ToString(DepthClockLayout.TimeFormat(settings)), out ink))
                using (SolidBrush white = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
                {
                    g.FillPath(white, digits);
                }

                float captionFont = DepthClockLayout.CaptionFontPx(screen, settings);
                float gap = DepthClockLayout.CaptionGapPx(screen, settings);
                float centerX = settings.CenterX * screen.Width;
                if (settings.ShowDate)
                {
                    string date = now.ToString("dddd, d 'de' MMMM", Spanish).ToUpper(Spanish);
                    DrawSpacedCaption(g, date, captionFont, centerX, ink.Top - gap);
                }
                if (settings.ShowWeather && weather != null)
                {
                    DrawWeatherPill(g, weather, captionFont, centerX, ink.Bottom + gap);
                }
            }

            // El estirado nunca queda detrás de nada: rodea los objetos en lugar de pasar por debajo
            if (layers.Foreground != null && settings.Style != ClockStyle.Samsung) g.DrawImageUnscaled(layers.Foreground, 0, 0);
        }

        /// <summary>
        /// Fecha en mayúsculas con espaciado entre letras (como el letter-spacing del simulador), centrada en
        /// <paramref name="centerX"/> y con su base visible en <paramref name="bottom"/>.
        /// </summary>
        private static void DrawSpacedCaption(Graphics g, string text, float fontPx, float centerX, float bottom)
        {
            float spacing = fontPx * 0.18f;
            using (FontFamily family = DepthClockLayout.ResolveFamily(ClockSettings.DefaultFontFamily))
            using (Font font = new Font(family, fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat measure = (StringFormat)StringFormat.GenericTypographic.Clone())
            using (GraphicsPath path = new GraphicsPath())
            {
                measure.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                float x = 0;
                foreach (char c in text)
                {
                    string s = c.ToString();
                    path.AddString(s, family, (int)FontStyle.Bold, fontPx, new PointF(x, 0), StringFormat.GenericTypographic);
                    x += g.MeasureString(s, font, PointF.Empty, measure).Width + spacing;
                }

                RectangleF bounds = path.GetBounds();
                using (Matrix move = new Matrix())
                {
                    move.Translate(centerX - (bounds.X + bounds.Width / 2), bottom - bounds.Bottom);
                    path.Transform(move);
                }
                using (SolidBrush shadow = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(225, 255, 255, 255)))
                {
                    using (Matrix down = new Matrix())
                    using (GraphicsPath shadowPath = (GraphicsPath)path.Clone())
                    {
                        down.Translate(0, fontPx * 0.06f);
                        shadowPath.Transform(down);
                        g.FillPath(shadow, shadowPath); // Sombra leve para que se lea sobre fondos claros
                    }
                    g.FillPath(brush, path);
                }
            }
        }

        /// <summary>
        /// Píldora de cristal con el clima ("⛅ 24°C • Despejado"), centrada en <paramref name="centerX"/>.
        /// </summary>
        internal static void DrawWeatherPill(Graphics g, WeatherService.Conditions weather, float fontPx, float centerX, float top)
        {
            string text = string.Format("{0:0}°C • {1}", weather.TemperatureC, weather.Description);
            using (FontFamily family = DepthClockLayout.ResolveFamily(ClockSettings.DefaultFontFamily))
            using (Font font = new Font(family, fontPx, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                SizeF textSize = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
                float icon = fontPx * 1.35f;
                float padX = fontPx * 0.9f, height = fontPx * 2.1f, iconGap = fontPx * 0.5f;
                float width = padX + icon + iconGap + textSize.Width + padX;
                RectangleF pill = new RectangleF(centerX - width / 2, top, width, height);

                using (GraphicsPath path = LayeredWindow.RoundedRect(pill, height / 2))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(110, 18, 22, 40)))
                using (Pen border = new Pen(Color.FromArgb(70, 255, 255, 255), Math.Max(1f, fontPx * 0.06f)))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }

                WeatherIcons.Draw(g, new RectangleF(pill.X + padX, pill.Y + (height - icon) / 2, icon, icon), weather.Code, weather.IsDay);

                using (SolidBrush brush = new SolidBrush(Color.FromArgb(240, 255, 255, 255)))
                {
                    g.DrawString(text, font, brush, pill.X + padX + icon + iconGap, pill.Y + (height - textSize.Height) / 2, StringFormat.GenericTypographic);
                }
            }
        }

        public void SetWeather(WeatherService.Conditions weather)
        {
            _weather = weather;
            Invalidate();
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _minuteTimer.Dispose();
                _zOrderGuard.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
