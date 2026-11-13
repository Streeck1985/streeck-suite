using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Ojitos de la isla cerrada (mockup «Ojitos del notch» aprobado): 9 estilos, emociones según lo que pasa
    /// y cositas por su cuenta cuando no usas la PC (bostezo, cabeceo, silbar, estirarse).
    /// Se dibujan en las unidades del mockup (un ojo mide 16×20 y están a ±18 del centro) escaladas al tamaño real.
    /// </summary>
    internal static class IslandEyes
    {
        public enum Mood { Idle, Happy, Laugh, Surprised, Focus, Sad, Love, Dizzy, Wink, Yawn, Nod, Whistle, Stretch, Sleep, Wave, Angry }

        /// <summary>
        /// La isla en pantalla (la pone el módulo en cada fotograma): para mirar hacia el ratón y reírse si pasa encima.
        /// </summary>
        public static Rectangle IslandRect;

        /// <summary>
        /// Hay algo moviéndose (mirada, parpadeo, emoción): la isla sube a ~30 fps mientras tanto.
        /// </summary>
        public static bool Lively { get; private set; }

        private static readonly Mood[] Fidgets = { Mood.Yawn, Mood.Nod, Mood.Whistle, Mood.Stretch };
        private static readonly Color Pink = Color.FromArgb(244, 114, 182);
        private static readonly Random Rng = new Random();

        private static Mood _temp = Mood.Idle;
        private static DateTime _tempStart, _tempUntil = DateTime.MinValue;
        private static float _lookX, _lookY;
        private static DateTime _lastFrame = DateTime.MinValue;
        private static DateTime _blinkAt = DateTime.MinValue, _nextBlink = DateTime.Now.AddSeconds(2.5);
        private static DateTime _nextFidget = DateTime.MinValue;
        private static bool _wasAsleep;
        private static bool _wasHovering;
        private static DateTime _asleepSince;
        private static bool? _caps;
        private static Point _lastCursor;
        private static readonly Queue<KeyValuePair<DateTime, float>> Moves = new Queue<KeyValuePair<DateTime, float>>();
        private static float _movesTotal;

        private static readonly Font TextFont = new Font("Segoe UI", 12f, FontStyle.Bold, GraphicsUnit.Pixel);
        private static readonly Font NoteFont = new Font("Segoe UI Symbol", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
        private static readonly Font EmojiFont = new Font("Segoe UI Emoji", 15f, FontStyle.Regular, GraphicsUnit.Pixel);

        /// <summary>
        /// Una emoción pasajera (aviso, tarea terminada, cariño...). No hace nada si las reacciones están apagadas.
        /// </summary>
        public static void React(Mood mood, double seconds)
        {
            if (!SuiteSettings.EyesMoods) return;
            Start(mood, seconds);
        }

        /// <summary>
        /// Se llama en cada fotograma mientras se ve un aviso: al cerrarse, los ojitos siguen sorprendidos un momento.
        /// </summary>
        public static void Notice()
        {
            if (!SuiteSettings.EyesMoods) return;
            DateTime now = DateTime.Now;
            if (_temp != Mood.Surprised || now > _tempUntil) _tempStart = now;
            _temp = Mood.Surprised;
            _tempUntil = now.AddSeconds(2.5);
        }

        private static void Start(Mood mood, double seconds)
        {
            _temp = mood;
            _tempStart = DateTime.Now;
            _tempUntil = _tempStart.AddSeconds(seconds);
        }

        private static double FidgetSeconds(Mood mood)
        {
            switch (mood)
            {
                case Mood.Yawn: return 2.4;
                case Mood.Nod: return 2.6;
                case Mood.Whistle: return 3.2;
                default: return 1.8;
            }
        }

        /// <summary>
        /// Lo que se dibuja en un fotograma (la isla lo calcula; el panel de ajustes arma uno fijo de muestra).
        /// </summary>
        private struct Frame
        {
            public Mood Mood;
            public float Phase;   // 0..1 dentro de una emoción con duración
            public float Open;    // 1 abierto, 0.1 cerrado (parpadeo, cabeceo)
            public float LookX, LookY;
            public double Ms;     // Reloj de las animaciones cíclicas
            public int Alpha;
            public bool Compact;  // Pequeños junto a la música o el tiempo: sin adornos a los lados (no caben)
        }

        /// <summary>
        /// Dibuja los ojitos centrados en center. unit: píxeles por unidad del mockup. ambient: el estado de fondo
        /// (música, sin internet, temporizador). idleSeconds: tiempo sin tocar ratón ni teclado.
        /// </summary>
        public static void Draw(Graphics g, PointF center, float unit, int alpha, Mood ambient, double idleSeconds, bool compact = false)
        {
            DateTime now = DateTime.Now;
            bool resumed = _lastFrame == DateTime.MinValue || (now - _lastFrame).TotalSeconds > 0.5;
            float dt = resumed ? 0.1f : (float)(now - _lastFrame).TotalSeconds;
            _lastFrame = now;
            Point cursor = Cursor.Position;

            // Bloq Mayús recién activado: enojados
            bool caps = Control.IsKeyLocked(Keys.CapsLock);
            if (_caps.HasValue && caps && !_caps.Value) React(Mood.Angry, 4);
            _caps = caps;

            // Sacudir el ratón muy rápido: mareados
            if (resumed)
            {
                Moves.Clear();
                _movesTotal = 0;
            }
            else
            {
                float moved = (float)Math.Sqrt((cursor.X - _lastCursor.X) * (double)(cursor.X - _lastCursor.X) + (cursor.Y - _lastCursor.Y) * (double)(cursor.Y - _lastCursor.Y));
                Moves.Enqueue(new KeyValuePair<DateTime, float>(now, moved));
                _movesTotal += moved;
            }
            _lastCursor = cursor;
            while (Moves.Count > 0 && (now - Moves.Peek().Key).TotalSeconds > 1) _movesTotal -= Moves.Dequeue().Value;
            if (_movesTotal > 5500 * unit * 16 / 11 && !(_temp == Mood.Dizzy && now < _tempUntil))
            {
                React(Mood.Dizzy, 2.5);
                Moves.Clear();
                _movesTotal = 0;
            }

            // Se duermen al minuto sin uso y saludan al volver
            bool asleep = idleSeconds >= 60;
            if (asleep && !_wasAsleep) _asleepSince = now;
            if (_wasAsleep && !asleep) Start(Mood.Wave, 1.6);
            _wasAsleep = asleep;

            // Cositas cuando no usas la PC: entre los 15 s y el minuto, una cada 20 a 40 s
            bool tempActive = now < _tempUntil;
            if (!SuiteSettings.EyesFidgets || idleSeconds < 15) _nextFidget = DateTime.MinValue;
            else if (!asleep && !tempActive && ambient == Mood.Idle)
            {
                if (_nextFidget == DateTime.MinValue) _nextFidget = now.AddSeconds(2 + Rng.Next(6));
                else if (now >= _nextFidget)
                {
                    Mood fidget = Fidgets[Rng.Next(Fidgets.Length)];
                    Start(fidget, FidgetSeconds(fidget));
                    _nextFidget = now.AddSeconds(20 + Rng.Next(21));
                    tempActive = true;
                }
            }

            Frame f = new Frame { Alpha = alpha, Ms = now.TimeOfDay.TotalMilliseconds, Open = 1, Phase = 0, Compact = compact };
            bool hovering = IslandRect.Width > 0 && IslandRect.Contains(cursor);
            // Cosquillas solo al entrar el ratón (con la isla abierta el ratón se queda encima y no debe reírse siempre)
            if (hovering && !_wasHovering && !tempActive && SuiteSettings.EyesMoods)
            {
                Start(Mood.Laugh, 1.5);
                tempActive = true;
            }
            _wasHovering = hovering;
            hovering = false;
            if (tempActive)
            {
                f.Mood = _temp;
                f.Phase = (float)((now - _tempStart).TotalSeconds / Math.Max(0.1, (_tempUntil - _tempStart).TotalSeconds));
            }
            else if (asleep)
            {
                double slept = (now - _asleepSince).TotalSeconds;
                f.Mood = slept < 2.4 ? Mood.Yawn : Mood.Sleep;
                f.Phase = (float)(slept / 2.4);
            }
            else if (hovering && SuiteSettings.EyesMoods) f.Mood = Mood.Laugh; // Cosquillas
            else f.Mood = SuiteSettings.EyesMoods ? ambient : Mood.Idle;

            // Mirada: hacia el ratón; si no lo mueves, miran a los lados
            float targetX, targetY;
            if (idleSeconds > 2.5)
            {
                int k = (int)(now.Ticks / TimeSpan.TicksPerMillisecond / 1800 % 4);
                targetX = new[] { -0.8f, 0f, 0.8f, 0f }[k];
                targetY = new[] { 0f, 0.3f, 0f, -0.2f }[k];
            }
            else
            {
                float cx = IslandRect.Width > 0 ? IslandRect.X + IslandRect.Width / 2f : cursor.X;
                float cy = IslandRect.Width > 0 ? IslandRect.Y + IslandRect.Height / 2f : cursor.Y;
                float dx = cursor.X - cx, dy = cursor.Y - cy;
                float d = Math.Max(1f, (float)Math.Sqrt(dx * dx + dy * dy));
                float reach = Math.Min(1f, d / (300 * unit * 16 / 11));
                targetX = dx / d * reach;
                targetY = dy / d * reach;
            }
            float follow = Math.Min(1f, dt * 9);
            _lookX += (targetX - _lookX) * follow;
            _lookY += (targetY - _lookY) * follow;
            f.LookX = _lookX;
            f.LookY = _lookY;

            // Parpadeo natural (a veces doble)
            if (now >= _nextBlink)
            {
                _blinkAt = now;
                _nextBlink = now.AddSeconds(Rng.NextDouble() < 0.2 ? 0.3 : 2.2 + Rng.NextDouble() * 2.6);
            }
            double bp = (now - _blinkAt).TotalMilliseconds / 140;
            if (bp < 1) f.Open = (float)(Math.Abs(1 - 2 * bp) * 0.9 + 0.1);
            TellStartButton(FaceFor(f.Mood, f.Phase));
            if (f.Mood == Mood.Nod && f.Phase < 0.75f) f.Open = Math.Max(0.12f, 1 - f.Phase / 0.75f);

            Lively = Math.Abs(targetX - _lookX) > 0.02f || Math.Abs(targetY - _lookY) > 0.02f || tempActive || hovering
                     || bp < 1.5 || (_nextBlink - now).TotalMilliseconds < 150 || f.Mood == Mood.Yawn;
            // (El baile con la música va a ~10 fps: dura toda la canción y a 30 fps gastaba el triple)

            Paint(g, center, unit, f);
        }

        /// <summary>
        /// Muestra fija para el panel de ajustes (no toca el estado de la isla).
        /// </summary>
        public static void DrawPreview(Graphics g, PointF center, float unit, Mood mood)
        {
            Frame f = new Frame { Mood = mood, Open = 1, Phase = 0.5f, LookX = 0.3f, LookY = 0.2f, Ms = 0, Alpha = 255 };
            Paint(g, center, unit, f);
        }

        /// <summary>
        /// Cara que toca para una emoción: n normal, f feliz, s sorpresa, d dormido, z mareado, a enamorado,
        /// e enojado, w guiño (las mismas 8 del mockup de cada estilo).
        /// </summary>
        public static char FaceFor(Mood mood, float phase)
        {
            switch (mood)
            {
                case Mood.Happy: case Mood.Laugh: return 'f';
                case Mood.Surprised: return 's';
                case Mood.Sleep: return 'd';
                case Mood.Yawn: return phase > 0.15f && phase < 0.85f ? 'd' : 'n';
                case Mood.Dizzy: return 'z';
                case Mood.Love: return 'a';
                case Mood.Angry: return 'e';
                case Mood.Wink: return 'w';
                case Mood.Nod: return phase > 0.75f ? 's' : 'n'; // Despierta de golpe
                default: return 'n';
            }
        }

        /// <summary>
        /// Caras que la suite prepara para el botón de Inicio ('c' = normal con los ojos cerrados, para parpadear).
        /// </summary>
        public const string StartFaces = "ncfsdzaew";

        private static readonly Dictionary<char, Mood> FaceMoods = new Dictionary<char, Mood>
        {
            { 'n', Mood.Idle }, { 'c', Mood.Idle }, { 'f', Mood.Happy }, { 's', Mood.Surprised }, { 'd', Mood.Sleep },
            { 'z', Mood.Dizzy }, { 'a', Mood.Love }, { 'e', Mood.Angry }, { 'w', Mood.Wink }
        };

        /// <summary>
        /// Icono del botón de Inicio con los mismos ojitos de la isla (estilo, color y extras) haciendo una cara:
        /// una píldora negra con la cara encima. La barra cambia de imagen según la cara de la isla.
        /// </summary>
        public static Bitmap RenderStartIcon(int size, char face, bool pill = true)
        {
            Bitmap bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = size / 128f;
                // Sin píldora: la pone la barra debajo, así los ojos pueden moverse hacia el ratón sin moverla
                if (pill) IslandPainter.FillRounded(g, new RectangleF(0, 22 * s, size, 84 * s), 42 * s, Color.Black);
                Mood mood;
                if (!FaceMoods.TryGetValue(face, out mood)) mood = Mood.Idle;
                Frame f = new Frame { Mood = mood, Open = face == 'c' ? 0.1f : 1, Phase = 0.5f, LookX = 0, LookY = 0, Ms = 0, Alpha = 255, Compact = true };
                Paint(g, new PointF(size / 2f, size / 2f + (SuiteSettings.EyesMouth ? 6 * s : 0)), 1.35f * s, f);
            }
            return bitmap;
        }

        // El botón de Inicio hace la misma cara que la isla: se le avisa solo cuando la cara cambia
        private static char _startFace = ' ';
        private static System.Threading.EventWaitHandle _faceEvent;

        private static void TellStartButton(char face)
        {
            if (SuiteSettings.StartIcon != StartIconRenderer.Eyes || face == _startFace) return;
            _startFace = face;
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Log.FilePath), "inicio_cara.txt"), face.ToString());
                if (_faceEvent == null) _faceEvent = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, @"Local\StreeckSuite.StartFace");
                _faceEvent.Set();
            }
            catch (Exception) { }
        }

        private static Color EyeColor
        {
            get
            {
                Color color;
                try { color = ColorTranslator.FromHtml(SuiteSettings.EyesColor); }
                catch (Exception) { color = Color.White; }
                // En los skins claros (Papel, Hielo) unos ojitos blancos no se verían
                int skin = IslandSkin.Current;
                if (color.GetBrightness() > 0.85f && (IslandSkin.IsLight || skin == IslandSkin.Terminal || skin == IslandSkin.Gold)) return IslandPainter.InkBase;
                return color;
            }
        }

        private static Color A(Color color, Frame f, float opacity)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, (int)(color.A * f.Alpha / 255f * opacity))), color);
        }

        private static Pen Stroke(Color color, float width)
        {
            Pen pen = new Pen(color, width);
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            return pen;
        }

        private static void Paint(Graphics g, PointF center, float unit, Frame f)
        {
            GraphicsState state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TranslateTransform(center.X, center.Y);
            g.ScaleTransform(unit, unit);
            try
            {
                SuiteSettings.EyesStyleKind style = SuiteSettings.EyesStyle;
                Color color = EyeColor;
                Mood mood = f.Mood;
                double t = f.Ms;
                float sq = mood == Mood.Stretch ? (float)Math.Sin(Math.Min(1f, f.Phase) * Math.PI) : 0;
                float bob = mood == Mood.Happy ? (float)Math.Sin(t / 180) * 3
                          : mood == Mood.Sad ? 3
                          : mood == Mood.Nod && f.Phase < 0.75f ? f.Phase * 6
                          : mood == Mood.Stretch ? -4 * sq : 0;
                float tilt = mood == Mood.Happy ? (float)Math.Sin(t / 360) * 4 : 0;
                bool cyclops = style == SuiteSettings.EyesStyleKind.Cyclops;
                float y = bob - (SuiteSettings.EyesMouth ? 3 : 0);
                float left = -18 + tilt, right = 18 + tilt;

                // Cara del estilo (mockup «Caras de cada estilo»): cada estilo dibuja sus 8 caras en las unidades del
                // mockup (ojos a ±15), que aquí se agrandan 1,2 veces para quedar a ±18 como antes
                char face = FaceFor(mood, f.Phase);
                float open = f.Open;
                if (mood == Mood.Focus) open *= 0.5f;
                else if (mood == Mood.Sad) open *= 0.8f;
                else if (mood == Mood.Whistle) open *= 0.6f;
                GraphicsState faceState = g.Save();
                g.TranslateTransform(tilt, y);
                if (sq > 0) g.ScaleTransform(1 + 0.3f * sq, 1 - 0.45f * sq); // Se estira
                g.ScaleTransform(1.2f, 1.2f);
                StyleFace(g, style, face, Math.Max(0.1f, open), f, color);
                g.Restore(faceState);

                // Cejas
                bool ownBrows = style >= SuiteSettings.EyesStyleKind.Slime; // Los 5 últimos traen cejas, banda o cabello propios
                if (SuiteSettings.EyesBrows && !ownBrows && (mood == Mood.Angry || mood == Mood.Focus || mood == Mood.Sad || mood == Mood.Surprised))
                {
                    float slant = mood == Mood.Angry ? 4 : mood == Mood.Focus ? 2 : mood == Mood.Sad ? -4 : 0;
                    float browY = y + (mood == Mood.Surprised ? -19 : -14) * (cyclops ? 1.4f : 1);
                    using (Pen pen = Stroke(A(color, f, 1), 3))
                    {
                        if (cyclops)
                        {
                            g.DrawLine(pen, tilt - 12, browY - slant, tilt, browY + slant);
                            g.DrawLine(pen, tilt, browY + slant, tilt + 12, browY - slant);
                        }
                        else
                        {
                            g.DrawLine(pen, left - 8, browY - slant, left + 8, browY + slant);
                            g.DrawLine(pen, right - 8, browY + slant, right + 8, browY - slant);
                        }
                    }
                }

                // Rubor
                if (SuiteSettings.EyesBlush && !f.Compact && (mood == Mood.Happy || mood == Mood.Love || mood == Mood.Laugh || mood == Mood.Wink))
                {
                    float spread = cyclops ? 22 : 32;
                    using (SolidBrush blush = new SolidBrush(A(Pink, f, 0.45f)))
                    {
                        g.FillEllipse(blush, tilt - spread - 6, y + 6, 12, 6);
                        g.FillEllipse(blush, tilt + spread - 6, y + 6, 12, 6);
                    }
                }

                // Boquita (el bostezo y el silbido la dibujan siempre)
                float mouthY = y + (cyclops ? 5 : 0);
                if (mood == Mood.Yawn)
                {
                    float m = (float)Math.Max(0, Math.Sin(Math.Min(1f, f.Phase) * Math.PI));
                    using (SolidBrush brush = new SolidBrush(A(color, f, 1))) g.FillEllipse(brush, tilt - (3 + 3 * m), mouthY + 13 - (1 + 7 * m), 2 * (3 + 3 * m), 2 * (1 + 7 * m));
                }
                else if (mood == Mood.Whistle)
                {
                    using (Pen pen = Stroke(A(color, f, 1), 2.5f)) g.DrawEllipse(pen, tilt + 3, mouthY + 10, 6, 6);
                }
                else if (SuiteSettings.EyesMouth)
                {
                    using (Pen pen = Stroke(A(color, f, 1), 2.5f))
                    {
                        if (mood == Mood.Happy || mood == Mood.Love || mood == Mood.Laugh || mood == Mood.Wink || mood == Mood.Wave)
                            g.DrawArc(pen, tilt - 6, mouthY + 6, 12, 12, 18, 144);
                        else if (mood == Mood.Sad) g.DrawArc(pen, tilt - 6, mouthY + 14, 12, 12, 207, 126);
                        else if (mood == Mood.Surprised) g.DrawEllipse(pen, tilt - 3.5f, mouthY + 11.5f, 7, 7);
                        else g.DrawLine(pen, tilt - 4, mouthY + 15, tilt + 4, mouthY + 15);
                    }
                }

                // Adornos
                if (f.Compact) return;
                if (mood == Mood.Sleep)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        float p = (float)((t / 900 + i / 3.0) % 1);
                        DrawString(g, "z", TextFont, A(Color.White, f, 0.75f * (1 - p)), 32 + p * 30, y - 2 - p * 30);
                    }
                }
                if (mood == Mood.Whistle)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        float p = (float)((t / 1100 + i / 2.0) % 1);
                        DrawString(g, i == 0 ? "♪" : "♫", NoteFont, A(Color.White, f, 1 - p), 16 + p * 26, y - 2 - p * 26);
                    }
                }
                if (mood == Mood.Happy)
                {
                    DrawString(g, "♪", NoteFont, A(Color.White, f, 1), -54, -16 + (float)Math.Sin(t / 200) * 4);
                    DrawString(g, "♫", NoteFont, A(Color.White, f, 1), 42, -12 + (float)Math.Cos(t / 220) * 4);
                }
                if (mood == Mood.Wave) DrawString(g, "👋", EmojiFont, A(Color.White, f, 1), 34, y - 12 + (float)Math.Sin(t / 90) * 2);
                if (mood == Mood.Angry) DrawString(g, "💢", EmojiFont, A(Color.FromArgb(248, 113, 113), f, 1), 38, -22);
            }
            finally
            {
                g.Restore(state);
            }
        }

        private static void DrawString(Graphics g, string text, Font font, Color color, float x, float y)
        {
            using (SolidBrush brush = new SolidBrush(color)) g.DrawString(text, font, brush, x, y);
        }

        #region Caras de cada estilo (unidades del mockup: ojos en x = ±15, centro en 0)

        private const float EL = -15, ER = 15;

        /// <summary>Estilos con cabello o banda que suben más que los ojos (Saiyajin y Ninja).</summary>
        public static bool TallStyle
        {
            get { return SuiteSettings.EyesStyle == SuiteSettings.EyesStyleKind.Saiyan || SuiteSettings.EyesStyle == SuiteSettings.EyesStyleKind.Ninja; }
        }
        // Pupilas: oscuras sobre ojos claros; claras si el ojo es oscuro (skins Papel y Hielo)
        private static Color Dark
        {
            get { return EyeColor.GetBrightness() < 0.4f ? Color.FromArgb(245, 247, 250) : Color.FromArgb(11, 11, 16); }
        }
        private static readonly Color PinkHeart = Color.FromArgb(244, 114, 182);
        private static readonly Color Rose = Color.FromArgb(251, 113, 133);
        private static readonly Color Amber = Color.FromArgb(251, 191, 36);
        private static readonly Color Yellow = Color.FromArgb(253, 224, 71);
        private static readonly Color Red = Color.FromArgb(239, 68, 68);
        private static readonly Color Cyan = Color.FromArgb(34, 211, 238);

        // Contexto de dibujo de una cara
        private class Face
        {
            public Graphics G;
            public Frame F;
            public Color Eye;     // Color de los ojos que eligió el usuario
            public float Open;    // Parpadeo (1 abierto)
            public float LookX, LookY;
            public double T;
            public float Pulse { get { return 1 + 0.12f * (float)Math.Sin(T / 140); } }

            public Color C(Color color, float opacity = 1) { return A(color, F, opacity); }

            public void Ell(float x, float y, float rx, float ry, Color color)
            {
                if (rx <= 0 || ry <= 0) return;
                using (SolidBrush b = new SolidBrush(C(color))) G.FillEllipse(b, x - rx, y - ry, 2 * rx, 2 * ry);
            }
            public void Dot(float x, float y, float r, Color color) { Ell(x, y, r, r, color); }
            public void Ring(float x, float y, float rx, float ry, Color color, float w)
            {
                using (Pen p = Stroke(C(color), w)) G.DrawEllipse(p, x - rx, y - ry, 2 * rx, 2 * ry);
            }
            public void RR(float x, float y, float w, float h, float r, Color color, float opacity = 1)
            {
                if (w <= 0 || h <= 0) return;
                IslandPainter.FillRounded(G, new RectangleF(x, y, w, h), Math.Max(0.1f, Math.Min(r, Math.Min(w, h) / 2)), C(color, opacity));
            }
            public void Rect(float x, float y, float w, float h, Color color, float opacity = 1)
            {
                using (SolidBrush b = new SolidBrush(C(color, opacity))) G.FillRectangle(b, x, y, w, h);
            }
            public void Box(float x, float y, float w, float h, Color color, float width)
            {
                using (Pen p = Stroke(C(color), width)) G.DrawRectangle(p, x, y, w, h);
            }
            // Arco como en el mockup: de a0·π a a1·π (sentido de las agujas del reloj)
            public void Arc(float x, float y, float r, float a0, float a1, float w, Color color, float opacity = 1)
            {
                using (Pen p = Stroke(C(color, opacity), w)) G.DrawArc(p, x - r, y - r, 2 * r, 2 * r, a0 * 180, (a1 - a0) * 180);
            }
            public void Line(float x1, float y1, float x2, float y2, float w, Color color)
            {
                using (Pen p = Stroke(C(color), w)) G.DrawLine(p, x1, y1, x2, y2);
            }
            public void Lines(float w, Color color, params float[] xy)
            {
                PointF[] points = new PointF[xy.Length / 2];
                for (int i = 0; i < points.Length; i++) points[i] = new PointF(xy[2 * i], xy[2 * i + 1]);
                using (Pen p = Stroke(C(color), w)) G.DrawLines(p, points);
            }
            public void Poly(Color color, params float[] xy)
            {
                PointF[] points = new PointF[xy.Length / 2];
                for (int i = 0; i < points.Length; i++) points[i] = new PointF(xy[2 * i], xy[2 * i + 1]);
                using (SolidBrush b = new SolidBrush(C(color))) G.FillPolygon(b, points);
            }
            public void Heart(float x, float y, float s, Color color) { DrawHeart(G, x, y, s, C(color)); }
            public void Star(float x, float y, float r, Color color)
            {
                PointF[] points = new PointF[10];
                for (int i = 0; i < 10; i++)
                {
                    double a = -Math.PI / 2 + i * Math.PI / 5;
                    float q = i % 2 == 1 ? r * 0.45f : r;
                    points[i] = new PointF(x + (float)Math.Cos(a) * q, y + (float)Math.Sin(a) * q);
                }
                using (SolidBrush b = new SolidBrush(C(color))) G.FillPolygon(b, points);
            }
            public void Spiral(float x, float y, Color color, bool square = false, float size = 1)
            {
                List<PointF> points = new List<PointF>();
                for (float a = 0; a < 11; a += 0.25f)
                {
                    float r = a * 0.85f * size;
                    float px = x + (float)Math.Cos(a + T / 140) * r, py = y + (float)Math.Sin(a + T / 140) * r;
                    points.Add(square ? new PointF((float)Math.Round(px / 3) * 3, (float)Math.Round(py / 3) * 3) : new PointF(px, py));
                }
                using (Pen p = Stroke(C(color), 2.2f)) G.DrawLines(p, points.ToArray());
            }
            public void PixHeart(float x, float y, Color color)
            {
                string[] rows = { ".##.##.", "#######", "#######", ".#####.", "..###..", "...#..." };
                for (int j = 0; j < rows.Length; j++)
                    for (int i = 0; i < rows[j].Length; i++)
                        if (rows[j][i] == '#') Rect(x - 10 + i * 3, y - 8 + j * 3, 2.6f, 2.6f, color);
            }
            // Brillo (Visor LED): dos halos y la luz
            public void Glow(float x, float y, float w, float h, Color color, float opacity = 1)
            {
                RR(x - 4, y - 4, w + 8, h + 8, (h + 8) / 2, color, 0.15f * opacity);
                RR(x - 2, y - 2, w + 4, h + 4, (h + 4) / 2, color, 0.3f * opacity);
                RR(x, y, w, h, h / 2, color, opacity);
            }
            public void Spin(float x, float y, double radians, Action draw)
            {
                GraphicsState s = G.Save();
                G.TranslateTransform(x, y);
                G.RotateTransform((float)(radians * 180 / Math.PI));
                draw();
                G.Restore(s);
            }
            public void Both(Action<float, int> draw) { draw(EL, -1); draw(ER, 1); }

            // --- Ayudas del mockup «Caras de los ojitos» (t en segundos, como en el mockup) ---
            public double Sec { get { return T / 1000.0; } }
            public float Sin(double k, double phase = 0) { return (float)Math.Sin(Sec * k + phase); }
            public float Cos(double k, double phase = 0) { return (float)Math.Cos(Sec * k + phase); }
            public void Up(float x, float y, float r, float w, Color color) { Arc(x, y + r * 0.6f, r, 1.1f, 1.9f, w, color); }
            public void Sparkle(float x, float y, float r, Color color)
            {
                if (r <= 0.2f) return;
                PointF center = new PointF(x, y);
                using (GraphicsPath p = new GraphicsPath())
                {
                    Quad(p, new PointF(x, y - r), center, new PointF(x + r, y));
                    Quad(p, new PointF(x + r, y), center, new PointF(x, y + r));
                    Quad(p, new PointF(x, y + r), center, new PointF(x - r, y));
                    Quad(p, new PointF(x - r, y), center, new PointF(x, y - r));
                    p.CloseFigure();
                    using (SolidBrush b = new SolidBrush(C(color))) G.FillPath(b, p);
                }
            }
            // Espiral del mockup: 2½ vueltas que giran con «rot»
            public void Swirl(float x, float y, float r, double rot, float w, Color color)
            {
                List<PointF> points = new List<PointF>();
                for (double a = 0; a < Math.PI * 5; a += 0.2)
                {
                    float rr = (float)(r * a / (Math.PI * 5));
                    points.Add(new PointF(x + (float)Math.Cos(a + rot) * rr, y + (float)Math.Sin(a + rot) * rr));
                }
                using (Pen p = Stroke(C(color), w)) G.DrawLines(p, points.ToArray());
            }
            // Pupila de gato sola (rasgada, sin contorno)
            public void Slit(float x, float y, float halfWidth, float halfHeight, Color color)
            {
                if (halfHeight <= 0.3f) { Line(x - 3, y, x + 3, y, 2.4f, color); return; }
                using (GraphicsPath p = new GraphicsPath())
                {
                    Quad(p, new PointF(x, y - halfHeight), new PointF(x + halfWidth, y), new PointF(x, y + halfHeight));
                    Quad(p, new PointF(x, y + halfHeight), new PointF(x - halfWidth, y), new PointF(x, y - halfHeight));
                    p.CloseFigure();
                    using (SolidBrush b = new SolidBrush(C(color))) G.FillPath(b, p);
                }
            }
            public void FillHalfTop(float x, float y, float rx, float ry, Color color)
            {
                using (SolidBrush b = new SolidBrush(C(color))) G.FillPie(b, x - rx, y - ry, 2 * rx, 2 * ry, 180, 180);
            }
            public void FillHalfBottom(float x, float y, float r, Color color)
            {
                using (SolidBrush b = new SolidBrush(C(color))) G.FillPie(b, x - r, y - r, 2 * r, 2 * r, 0, 180);
            }
        }

        // Curva cuadrática del canvas pasada a Bézier cúbica
        private static void Quad(GraphicsPath p, PointF a, PointF ctrl, PointF b)
        {
            p.AddBezier(a, new PointF(a.X + 2f / 3 * (ctrl.X - a.X), a.Y + 2f / 3 * (ctrl.Y - a.Y)),
                new PointF(b.X + 2f / 3 * (ctrl.X - b.X), b.Y + 2f / 3 * (ctrl.Y - b.Y)), b);
        }

        private static Color Op(Color color, float opacity)
        {
            return Color.FromArgb(Math.Max(0, Math.Min(255, (int)(color.A * opacity))), color);
        }

        private static Color Hsl(double h, double s, double l)
        {
            h = ((h % 360) + 360) % 360 / 360;
            Func<double, double, double, double> hue = (p, q, t) =>
            {
                if (t < 0) t += 1;
                if (t > 1) t -= 1;
                if (t < 1.0 / 6) return p + (q - p) * 6 * t;
                if (t < 0.5) return q;
                if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
                return p;
            };
            double q2 = l < 0.5 ? l * (1 + s) : l + s - l * s, p2 = 2 * l - q2;
            return Color.FromArgb((int)(hue(p2, q2, h + 1.0 / 3) * 255), (int)(hue(p2, q2, h) * 255), (int)(hue(p2, q2, h - 1.0 / 3) * 255));
        }

        private static void StyleFace(Graphics g, SuiteSettings.EyesStyleKind style, char face, float open, Frame f, Color eye)
        {
            Face c = new Face { G = g, F = f, Eye = eye, Open = open, LookX = f.LookX, LookY = f.LookY, T = f.Ms };
            switch (style)
            {
                case SuiteSettings.EyesStyleKind.Pupil: Pupil(c, face); break;
                case SuiteSettings.EyesStyleKind.Anime: Anime(c, face); break;
                case SuiteSettings.EyesStyleKind.Cat: Cat(c, face); break;
                case SuiteSettings.EyesStyleKind.Robot: Robot(c, face); break;
                case SuiteSettings.EyesStyleKind.Dots: Dots(c, face); break;
                case SuiteSettings.EyesStyleKind.Pixel: Pixel(c, face); break;
                case SuiteSettings.EyesStyleKind.Visor: Visor(c, face); break;
                case SuiteSettings.EyesStyleKind.Cyclops: Cyclops(c, face); break;
                case SuiteSettings.EyesStyleKind.Hearts: Hearts(c, face); break;
                case SuiteSettings.EyesStyleKind.Stars: Stars(c, face); break;
                case SuiteSettings.EyesStyleKind.Glasses: Glasses(c, face); break;
                case SuiteSettings.EyesStyleKind.Owl: Owl(c, face); break;
                case SuiteSettings.EyesStyleKind.Sleepy: Sleepy(c, face); break;
                case SuiteSettings.EyesStyleKind.Cyber: Cyber(c, face); break;
                case SuiteSettings.EyesStyleKind.Ghost: Ghost(c, face); break;
                case SuiteSettings.EyesStyleKind.Kawaii: Kawaii(c, face); break;
                case SuiteSettings.EyesStyleKind.Pirate: Pirate(c, face); break;
                case SuiteSettings.EyesStyleKind.Hypno: Hypno(c, face); break;
                case SuiteSettings.EyesStyleKind.Puppy: Puppy(c, face); break;
                case SuiteSettings.EyesStyleKind.Monocle: Monocle(c, face); break;
                case SuiteSettings.EyesStyleKind.Alien: Alien(c, face); break;
                case SuiteSettings.EyesStyleKind.Panda: Panda(c, face); break;
                case SuiteSettings.EyesStyleKind.Kaomoji: Kaomoji(c, face); break;
                case SuiteSettings.EyesStyleKind.Zombie: Zombie(c, face); break;
                case SuiteSettings.EyesStyleKind.Slime: Slime(c, face); break;
                case SuiteSettings.EyesStyleKind.Frog: Frog(c, face); break;
                case SuiteSettings.EyesStyleKind.Saiyan: Saiyan(c, face); break;
                case SuiteSettings.EyesStyleKind.Ninja: Ninja(c, face); break;
                case SuiteSettings.EyesStyleKind.Vampire: Vampire(c, face); break;
                default: Actual(c, face); break;
            }
        }

        private static void Actual(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f':
                    {
                        // Rebota con un brillo
                        float b = -Math.Abs(c.Sin(5)) * 3;
                        c.Both((x, s) => c.Up(x, b, 7, 3.5f, w));
                        c.Sparkle(22, -9, 2 + Math.Abs(c.Sin(5)) * 2, w);
                        break;
                    }
                case 's': c.Both((x, s) => c.RR(x - 6, -12, 12, 22, 6, w)); break;
                case 'd': c.Both((x, s) => c.Arc(x, -3, 6, 0.15f, 0.85f, 3, w)); break;
                case 'z': c.Both((x, s) => c.Spin(x, 0, c.Sin(4, s > 0 ? 2 : 0) * 0.6, () => c.RR(-6, -8, 12, 16, 6, w))); break; // Se tambalea
                case 'a': c.Both((x, s) => c.Heart(x, 0, 1.3f * c.Pulse, PinkHeart)); break;
                case 'e': c.Both((x, s) => c.RR(x - 6, -4, 12, 11, 4, w)); break;
                case 'w': c.RR(EL - 6, -8, 12, 16, 6, w); c.Arc(ER, 3, 6, 1.1f, 1.9f, 3, w); break;
                default: c.Both((x, s) => c.RR(x - 6 + c.LookX * 2.5f, -8 * c.Open + c.LookY * 2, 12, 16 * c.Open, 6, w)); break;
            }
        }

        private static void Pupil(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f': c.Both((x, s) => { c.Ring(x, 0, 7, 9, w, 3); c.Sparkle(x, -1, 3.5f + c.Sin(5), w); }); break; // Pupilas que brillan
                case 's': c.Both((x, s) => { c.Ring(x, 0, 8, 11, w, 3); c.Ring(x, 0, 4, 6, w, 2); }); break;
                case 'd': c.Both((x, s) => { c.Arc(x, -5, 6, 0.05f, 0.95f, 3, w); c.Arc(x, -3, 6, 0.1f, 0.9f, 1.5f, w, 0.35f); }); break;
                case 'z':
                    c.Both((x, s) =>
                    {
                        // La pupila da vueltas
                        c.Ring(x, 0, 7, 9, w, 3);
                        double a = c.Sec * 6 * (s > 0 ? -1 : 1);
                        c.Ell(x + (float)Math.Cos(a) * 3.5f, (float)Math.Sin(a) * 4.5f, 2.6f, 3.2f, w);
                    });
                    break;
                case 'a': c.Both((x, s) => { c.Ring(x, 0, 7, 9, w, 3); c.Heart(x, 0, 0.6f * c.Pulse, PinkHeart); }); break;
                case 'e': c.Both((x, s) => { c.Ring(x, 1, 7, 7, w, 3); c.Ell(x, 1, 3, 3, w); }); break;
                case 'w': c.Ring(EL, 0, 7, 9, w, 3); c.Ell(EL, 0, 3, 4, w); c.Arc(ER, 5, 8, 1.1f, 1.9f, 2.5f, w); break;
                default: c.Both((x, s) => { c.Ring(x, 0, 7, 9 * c.Open, w, 3); c.Ell(x + c.LookX * 3, c.LookY * 3, 3, 4 * c.Open, w); }); break;
            }
        }

        private static void Anime(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f':
                    {
                        // Ojitos > < con destellos
                        c.Lines(3, w, EL - 6, -5, EL + 5, 0, EL - 6, 5);
                        c.Lines(3, w, ER + 6, -5, ER - 5, 0, ER + 6, 5);
                        float sp = 1.5f + Math.Abs(c.Sin(6)) * 2;
                        c.Sparkle(-27, -8, sp, w);
                        c.Sparkle(27, -8, sp, w);
                        break;
                    }
                case 's':
                    c.Both((x, s) =>
                    {
                        c.Ell(x, 0, 9, 11, w); c.Dot(x, 1, 5, Dark); c.Dot(x + 2, -3, 1.8f, Color.White);
                        for (int k = -1; k <= 1; k++) c.Line(x + k * 5, -11, x + k * 7, -15, 1.5f, w);
                    });
                    break;
                case 'd': c.Both((x, s) => { c.Arc(x, -4, 7, 0.1f, 0.9f, 2.5f, w); for (int k = -1; k <= 1; k++) c.Line(x + k * 5, 2, x + k * 6, 5, 1.3f, w); }); break;
                case 'z':
                    {
                        // Espiral con gotita de sudor
                        c.Both((x, s) => { c.Ell(x, 0, 8, 9, w); c.Swirl(x, 0, 6.5f, c.Sec * 5, 1.6f, Dark); });
                        float d = (float)(c.Sec * 1.5 % 1);
                        c.Ell(26, -6 + d * 8, 2, 3, Color.FromArgb(125, 211, 252));
                        break;
                    }
                case 'a': c.Both((x, s) => { c.Heart(x, 0, 1.4f * c.Pulse, PinkHeart); c.Dot(x - 3, -3, 1.6f, Color.White); }); break;
                case 'e': c.Both((x, s) => { c.Ell(x, 2, 8, 6, w); c.Dot(x, 3, 3.5f, Dark); }); break;
                case 'w': c.Ell(EL, 0, 8, 9, w); c.Dot(EL, 1, 4.5f, Dark); c.Dot(EL + 2, -2, 1.6f, Color.White); c.Lines(3, w, ER - 7, -5, ER + 5, 0, ER - 7, 5); break;
                default:
                    c.Both((x, s) =>
                    {
                        c.Ell(x, 0, 8, 9 * c.Open, w);
                        if (c.Open > 0.5f) { c.Dot(x + c.LookX * 3, 1 + c.LookY * 2, 4.5f, Dark); c.Dot(x + 2 + c.LookX * 3, -2 + c.LookY * 2, 1.6f, Color.White); }
                    });
                    break;
            }
        }

        private static void Cat(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f':
                    {
                        // Carita ^ω^ con bigotes
                        c.Both((x, s) => c.Up(x, 0, 6, 3, w));
                        c.Arc(-3, 7, 3, 0, 1, 2, w);
                        c.Arc(3, 7, 3, 0, 1, 2, w);
                        float m = c.Sin(4) * 1.5f;
                        c.Line(-25, 4 + m, -34, 2, 1.2f, w); c.Line(-25, 7, -34, 8 + m, 1.2f, w);
                        c.Line(25, 4 + m, 34, 2, 1.2f, w); c.Line(25, 7, 34, 8 + m, 1.2f, w);
                        break;
                    }
                // Pura pupila, sin contorno (pedido del usuario)
                case 's': c.Both((x, s) => c.Slit(x, 0, 6.5f, 10.5f, w)); break;
                case 'd': c.Both((x, s) => { using (Pen p = Stroke(c.C(w), 4)) c.G.DrawBezier(p, x - 9, 0, x - 3, -2, x + 3, -2, x + 9, 0); }); break;
                case 'z': c.Both((x, s) => c.Spin(x, 0, c.Sec * 3 * (s > 0 ? -1 : 1), () => c.Slit(0, 0, 3.6f, 10, w))); break;
                case 'a': c.Both((x, s) => c.Heart(x, 0, 1.3f * c.Pulse, PinkHeart)); break;
                case 'e': c.Both((x, s) => c.Spin(x, 1, -s * 0.35, () => c.Slit(0, 0, 2.4f, 8, w))); break;
                case 'w': c.Slit(EL, 0, 3.6f, 10, w); c.Lines(3, w, ER - 7, 3, ER, -4, ER + 7, 3); break;
                default: c.Both((x, s) => c.Slit(x + c.LookX * 3, c.LookY * 2, 3.6f, 10 * c.Open, w)); break;
            }
        }

        private static void ScanRect(Face c, float x, float y, float w, float h)
        {
            c.Rect(x, y, w, h, c.Eye);
            for (float ly = y + 2; ly < y + h; ly += 3) c.Rect(x, ly, w, 1, Color.Black, 0.45f);
        }

        private static void Robot(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f':
                    {
                        // Pantalla con ^ ^ verde
                        float b = c.Sin(6) > 0 ? 0 : 1;
                        int[,] cells = { { 0, 2 }, { 1, 1 }, { 2, 0 }, { 3, 1 }, { 4, 2 } };
                        c.Both((x, s) =>
                        {
                            c.RR(x - 10, -9, 20, 18, 3, Color.FromArgb(15, 42, 23));
                            for (int k = 0; k < 5; k++) c.Rect(x - 9 + cells[k, 0] * 4, -1 + cells[k, 1] * 3 - b, 3.2f, 3.2f, Color.FromArgb(74, 222, 128));
                        });
                        break;
                    }
                case 's': c.Both((x, s) => ScanRect(c, x - 8, -13, 16, 24)); break;
                case 'd': c.Both((x, s) => { for (int k = 0; k < 4; k++) c.Rect(x - 9 + k * 5, -1 + (s < 0 ? k : 3 - k) * 1.5f, 5, 2.5f, w); }); break;
                case 'z':
                    {
                        // Estática de glitch
                        int f20 = (int)(c.Sec * 20), f30 = (int)(c.Sec * 30);
                        c.Both((x, s) =>
                        {
                            c.RR(x - 10, -9, 20, 18, 3, Color.FromArgb(26, 29, 36));
                            for (int k = 0; k < 5; k++)
                                c.Rect(x - 9 + (k * 5 + f30) % 6, -8 + (k * 7 + f20) % 17, 12, 1.6f, k % 2 == 1 ? Color.FromArgb(156, 163, 175) : Color.FromArgb(229, 231, 235));
                        });
                        break;
                    }
                case 'a': c.Both((x, s) => c.PixHeart(x, 1, PinkHeart)); break;
                case 'e': c.Both((x, s) => c.Poly(w, x - 9, s < 0 ? -2 : -7, x + 9, s < 0 ? -7 : -2, x + 9, 7, x - 9, 7)); break;
                case 'w': ScanRect(c, EL - 9, -8, 18, 16); c.Rect(ER - 9, -1, 18, 3, w); break;
                default:
                    c.Both((x, s) =>
                    {
                        float h = 16 * c.Open;
                        ScanRect(c, x - 9, -h / 2, 18, h);
                        if (c.Open > 0.5f) c.Rect(x - 3 + c.LookX * 4, -3 + c.LookY * 3, 6, 6, Dark);
                    });
                    break;
            }
        }

        private static void Dots(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f': c.Dot(EL, -Math.Abs(c.Sin(6)) * 5, 5, w); c.Dot(ER, -Math.Abs(c.Sin(6, 1.2)) * 5, 5, w); break; // Saltan
                case 's': c.Dot(EL, 0, 6, w); c.Arc(ER, 2, 6, 1.1f, 1.9f, 2.5f, w); break;
                case 'd': c.Both((x, s) => c.Arc(x, -3, 6, 0.15f, 0.85f, 2, w)); break;
                case 'z': c.Both((x, s) => { double a = c.Sec * 5 * (s > 0 ? -1 : 1); c.Dot(x + (float)Math.Cos(a) * 5, (float)Math.Sin(a) * 5, 3.5f, w); }); break; // En órbita
                case 'a': c.Both((x, s) => c.Heart(x, 0, 0.8f * c.Pulse, PinkHeart)); break;
                case 'e': c.Both((x, s) => c.Dot(x, 2, 5, w)); break;
                case 'w': c.Dot(EL, 0, 5, w); c.Rect(ER - 6, -1, 12, 2.5f, w); break;
                default: c.Both((x, s) => c.Ell(x + c.LookX * 3, c.LookY * 2, 5, 5 * c.Open, w)); break;
            }
        }

        private static void PixelGrid(Face c, float x, float top, int cols, int rows)
        {
            for (int i = 0; i < cols; i++)
                for (int j = 0; j < rows; j++) c.Rect(x + i * 4, top + j * 4, 3.4f, 3.4f, c.Eye);
        }

        private static void Pixel(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f':
                    {
                        c.Both((x, s) => { int[,] cells = { { 0, 2 }, { 1, 1 }, { 2, 0 }, { 3, 1 }, { 4, 2 } }; for (int k = 0; k < 5; k++) c.Rect(x - 10 + cells[k, 0] * 4, -2 + cells[k, 1] * 4, 3.4f, 3.4f, w); });
                        // Corazón pixelado que parpadea arriba
                        if (c.Sin(4) > 0)
                        {
                            string[] rows = { ".##.##.", "#######", ".#####.", "..###..", "...#..." };
                            for (int j = 0; j < rows.Length; j++)
                                for (int i = 0; i < 7; i++)
                                    if (rows[j][i] == '#') c.Rect(-7 + i * 2, -14 + j * 2, 1.8f, 1.8f, PinkHeart);
                        }
                        break;
                    }
                case 's': c.Both((x, s) => PixelGrid(c, x - 8, -12, 4, 6)); break;
                case 'd': c.Both((x, s) => { for (int i = 0; i < 4; i++) c.Rect(x - 9 + i * 5, 0, 3.4f, 2.6f, w); }); break;
                case 'z':
                    {
                        // X X con falla
                        float o = c.Sin(20) > 0.6f ? 2 : 0;
                        int[,] cells = { { 0, 0 }, { 1, 1 }, { 2, 2 }, { 3, 3 }, { 3, 0 }, { 2, 1 }, { 1, 2 }, { 0, 3 } };
                        c.Both((x, s) => { for (int k = 0; k < 8; k++) c.Rect(x - 7 + cells[k, 0] * 4 + o, -7 + cells[k, 1] * 4, 3.2f, 3.2f, o > 0 ? Cyan : w); });
                        break;
                    }
                case 'a': c.Both((x, s) => c.PixHeart(x, 1, PinkHeart)); break;
                case 'e':
                    c.Both((x, s) =>
                    {
                        for (int i = 0; i < 4; i++)
                            for (int j = 0; j < 3; j++)
                                if (!(j == 0 && ((s < 0 && i < 2) || (s > 0 && i > 1)))) c.Rect(x - 8 + i * 4, -4 + j * 4, 3.4f, 3.4f, w);
                    });
                    break;
                case 'w': PixelGrid(c, EL - 8, -8, 4, 4); for (int i = 0; i < 4; i++) c.Rect(ER - 9 + i * 5, 0, 3.4f, 2.6f, w); break;
                default:
                    c.Both((x, s) =>
                    {
                        int rows = Math.Max(1, (int)Math.Round(4 * c.Open));
                        PixelGrid(c, x - 8 + (float)Math.Round(c.LookX) * 4, -8 + (4 - rows) * 2, 4, rows);
                    });
                    break;
            }
        }

        private static void Visor(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f':
                    {
                        // La luz se vuelve una onda que baila (mockup aprobado)
                        c.RR(-32, -6, 64, 12, 6, w, 0.12f);
                        List<PointF> wave = new List<PointF>();
                        for (float x = -29; x <= 29; x += 2)
                        {
                            float env = (float)Math.Sin((x + 29) / 58 * Math.PI);
                            wave.Add(new PointF(x, (float)Math.Sin((x + 55) / 5 + c.T / 110) * 6 * env));
                        }
                        PointF[] points = wave.ToArray();
                        using (Pen halo = Stroke(c.C(w, 0.15f), 9)) c.G.DrawLines(halo, points);
                        using (Pen halo = Stroke(c.C(w, 0.3f), 6)) c.G.DrawLines(halo, points);
                        using (Pen core = Stroke(c.C(w), 3.5f)) c.G.DrawLines(core, points);
                        break;
                    }
                case 's': c.Glow(-32, -9, 64, 18, w, 0.85f); break;
                case 'd': c.Both((x, s) => c.Glow(x - 9, 0, 18, 3, w, 0.55f)); break;
                case 'z':
                    {
                        // La luz en zigzag
                        c.RR(-32, -6, 64, 12, 6, w, 0.12f);
                        float amp = c.Sin(6) * 5;
                        List<PointF> zig = new List<PointF>();
                        for (int x = -28; x <= 28; x += 4) zig.Add(new PointF(x, ((x / 4) % 2 != 0 ? 1 : -1) * amp));
                        PointF[] points = zig.ToArray();
                        using (Pen halo = Stroke(c.C(w, 0.15f), 9)) c.G.DrawLines(halo, points);
                        using (Pen halo = Stroke(c.C(w, 0.3f), 6)) c.G.DrawLines(halo, points);
                        using (Pen core = Stroke(c.C(w), 3.5f)) c.G.DrawLines(core, points);
                        break;
                    }
                case 'a': { float p = c.Pulse; c.Glow(-32, -6 * p, 64, 12 * p, PinkHeart); break; }
                case 'e': c.Glow(-32, -3, 64, 6, Red); break;
                case 'w': c.RR(-32, -6, 64, 12, 6, w, 0.12f); c.Glow(-29, -6, 26, 12, w); c.RR(5, -1, 24, 2, 1, w, 0.6f); break;
                default:
                    {
                        float h = Math.Max(3, 12 * c.Open);
                        c.RR(-32, -h / 2, 64, h, h / 2, w, 0.18f);
                        c.Glow(c.LookX * 20 - 9, -h / 2, 18, h, w);
                        break;
                    }
            }
        }

        private static void Cyclops(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f':
                    // Ojo grande feliz con brillo y pestañitas
                    c.Arc(0, 6, 10, 1.1f, 1.9f, 4, w);
                    c.Sparkle(9, -10, 2.5f + Math.Abs(c.Sin(5)) * 2, w);
                    c.Line(-14, -2, -18, -6, 1.6f, w);
                    c.Line(14, -2, 18, -6, 1.6f, w);
                    break;
                case 's': c.Ell(0, 0, 11, 16, w); c.Dot(0, 3, 3, Dark); c.Line(-10, -19, 10, -19, 2.5f, w); break;
                case 'd': c.Arc(0, -4, 9, 0.1f, 0.9f, 3, w); c.Arc(0, -2, 9, 0.15f, 0.85f, 1.5f, w, 0.35f); break;
                case 'z': c.Ell(0, 0, 10, 12, w); c.Dot(c.Cos(6) * 5, c.Sin(6) * 6, 3.5f, Dark); break; // La pupila orbita
                case 'a': c.Heart(0, 0, 2 * c.Pulse, PinkHeart); break;
                case 'e': c.Ell(0, 3, 10, 8, w); c.Dot(0, 4, 4, Dark); c.Lines(3, w, -12, -12, 0, -6, 12, -12); break;
                case 'w':
                    if (c.T % 1600 < 800) { c.Ell(0, 0, 9, 13, w); c.Dot(0, 2, 4, Dark); }
                    else c.Arc(0, 4, 9, 1.1f, 1.9f, 4, w);
                    break;
                default: c.Ell(0, 0, 9, 13 * c.Open, w); if (c.Open > 0.5f) c.Dot(c.LookX * 3, 2 + c.LookY * 2, 4, Dark); break;
            }
        }

        private static void Closed(Face c) { c.Both((x, s) => c.Arc(x, -3, 6, 0.15f, 0.85f, 3, c.Eye)); }

        private static void Hearts(Face c, char face)
        {
            switch (face)
            {
                case 'f':
                    {
                        // Laten y sueltan un corazoncito
                        float sz = 1.3f + 0.15f * c.Sin(8);
                        c.Both((x, s) => c.Heart(x, 0, sz, PinkHeart));
                        float p = (float)(c.Sec * 0.8 % 1);
                        c.Heart(24, -4 - p * 12, 0.4f, Op(PinkHeart, 1 - p));
                        break;
                    }
                case 's': c.Both((x, s) => c.Heart(x, 0, 1.8f, Rose)); break;
                case 'd': Closed(c); break;
                case 'z':
                    {
                        // Giran con estrellitas alrededor
                        c.Both((x, s) => c.Spin(x, 0, c.Sin(5, s > 0 ? 1 : 0) * 0.8, () => c.Heart(0, 0, 1, PinkHeart)));
                        for (int i = 0; i < 3; i++) { double a = c.Sec * 3 + i * 2.1; c.Star((float)Math.Cos(a) * 20, -14 + (float)Math.Sin(a) * 3, 2.5f, Yellow); }
                        break;
                    }
                case 'a': c.Both((x, s) => c.Heart(x, 0, 1.7f * c.Pulse, Rose)); break;
                case 'e': c.Both((x, s) => c.Heart(x, 2, 1, Red)); break;
                case 'w': c.Heart(EL, 0, 1.2f, PinkHeart); c.Arc(ER, 3, 6, 1.1f, 1.9f, 3, c.Eye); break;
                default: c.Both((x, s) => c.Heart(x + c.LookX * 2, c.LookY * 2, 1.1f * c.Open + 0.1f, PinkHeart)); break;
            }
        }

        private static void Stars(Face c, char face)
        {
            switch (face)
            {
                case 'f':
                    {
                        // Destellan
                        float sz = 9 + Math.Abs(c.Sin(7)) * 3;
                        c.Both((x, s) => c.Star(x, 0, sz, Yellow));
                        c.Sparkle(EL + 8, -8, 2 + Math.Abs(c.Cos(7)) * 2, Color.White);
                        c.Sparkle(ER + 8, -8, 2 + Math.Abs(c.Sin(7)) * 2, Color.White);
                        break;
                    }
                case 's': c.Both((x, s) => c.Star(x, 0, 12, Color.White)); break;
                case 'd': Closed(c); break;
                case 'z': c.Both((x, s) => { float y = (float)((c.Sec * 12 + (s > 0 ? 10 : 0)) % 24) - 12; c.Spin(x, y, c.Sec * 6, () => c.Star(0, 0, 7, Amber)); }); break; // Caen dando vueltas
                case 'a': c.Both((x, s) => c.Star(x, 0, 10 * c.Pulse, PinkHeart)); break;
                case 'e': c.Both((x, s) => c.Star(x, 2, 7, Red)); break;
                case 'w': c.Star(EL, 0, 9, Amber); c.Arc(ER, 3, 6, 1.1f, 1.9f, 3, c.Eye); break;
                default: c.Both((x, s) => c.Star(x + c.LookX * 2, c.LookY * 2, 8 * c.Open + 1, Amber)); break;
            }
        }

        private static void Glasses(Face c, char face)
        {
            Color w = c.Eye;
            Action frames = () =>
            {
                c.Box(EL - 10, -8, 20, 16, w, 2);
                c.Box(ER - 10, -8, 20, 16, w, 2);
                c.Line(EL + 10, -2, ER - 10, -2, 2, w);
            };
            if (face != 'z') frames();
            switch (face)
            {
                case 'f':
                    {
                        c.Both((x, s) => c.Arc(x, 3, 5, 1.15f, 1.85f, 2.5f, w));
                        // Reflejo que cruza los cristales
                        float rx = -30 + (float)(c.Sec * 40 % 60);
                        GraphicsState st = c.G.Save();
                        using (Region glass = new Region(new RectangleF(EL - 10, -8, 20, 16)))
                        {
                            glass.Union(new RectangleF(ER - 10, -8, 20, 16));
                            c.G.IntersectClip(glass);
                            c.Line(rx, 10, rx + 8, -10, 2.5f, Op(Color.White, 0.7f));
                        }
                        c.G.Restore(st);
                        break;
                    }
                case 's': c.Both((x, s) => c.Dot(x, 0, 5, w)); break;
                case 'd': c.Both((x, s) => c.Line(x - 6, 0, x + 6, 0, 2, w)); break;
                case 'z':
                    // Lentes chuecos y los ojitos de un lado a otro
                    c.Spin(0, 0, c.Sin(4) * 0.35, frames);
                    c.Dot(EL + c.Sin(5) * 3, 1, 2.6f, w);
                    c.Dot(ER - c.Sin(5) * 3, -1, 2.6f, w);
                    break;
                case 'a': c.Both((x, s) => c.Heart(x, 0, 0.7f * c.Pulse, PinkHeart)); break;
                case 'e': c.Both((x, s) => c.RR(x - 3, -1, 6, 4, 1, w)); break;
                case 'w': c.RR(EL - 3, -3, 6, 6, 1, w); c.Line(ER - 6, 0, ER + 6, 0, 2, w); break;
                default: c.Both((x, s) => c.RR(x - 3 + c.LookX * 3, -3 * c.Open + c.LookY * 2, 6, 6 * c.Open, 1, w)); break;
            }
        }

        private static void Owl(Face c, char face)
        {
            switch (face)
            {
                case 'f':
                    {
                        // Ojos ^ ^ con plumitas que se mueven
                        c.Both((x, s) => { c.Dot(x, 0, 10, Amber); c.Up(x, -2, 5, 2.6f, Color.Black); });
                        float p = c.Sin(5) * 2;
                        c.Line(-22, -9, -28, -14 + p, 2, Amber);
                        c.Line(22, -9, 28, -14 - p, 2, Amber);
                        break;
                    }
                case 's': c.Both((x, s) => { c.Dot(x, 0, 12, Amber); c.Dot(x, 0, 3, Color.Black); }); break;
                case 'd': c.Both((x, s) => { c.Dot(x, 0, 10, Amber); c.Rect(x - 11, -11, 22, 13, Color.Black); }); break;
                case 'z': c.Spin(0, 0, c.Sin(2.5) * Math.PI * 0.6, () => c.Both((x, s) => { c.Dot(x, 0, 10, Amber); c.Dot(x, 0, 4, Color.Black); })); break; // La cabeza da la vuelta
                case 'a': c.Both((x, s) => { c.Dot(x, 0, 10, Amber); c.Heart(x, 0, 0.6f * c.Pulse, Color.FromArgb(190, 24, 93)); }); break;
                case 'e': c.Both((x, s) => { c.Dot(x, 0, 10, Amber); c.Dot(x, 2, 4, Color.Black); c.Poly(Color.Black, x - 11, -11, x + 11, -11, s < 0 ? x + 11 : x - 11, -1); }); break;
                case 'w': c.Dot(EL, 0, 10, Amber); c.Dot(EL, 0, 5, Color.Black); c.Dot(ER, 0, 10, Amber); c.Rect(ER - 11, -11, 22, 13, Color.Black); break;
                default: c.Both((x, s) => { c.Dot(x, 0, 10, Amber); c.Dot(x + c.LookX * 3, c.LookY * 2, 5 * c.Open, Color.Black); }); break;
            }
        }

        private static void HalfMoon(Face c, float x)
        {
            using (SolidBrush b = new SolidBrush(c.C(c.Eye))) c.G.FillPie(b, x - 7, -6, 14, 14, 0, 180);
            c.Rect(x - 7, -1, 14, 2, c.Eye);
        }

        private static void Sleepy(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f': c.Both((x, s) => c.FillHalfBottom(x, -2, 7, w)); c.Arc(0, 6, 4, 0.15f, 0.85f, 2, w); break; // Medias lunas sonrientes
                case 's': c.Both((x, s) => c.Dot(x, 0, 7, w)); break;
                case 'd': c.Both((x, s) => c.Rect(x - 7, 0, 14, 2, w)); break;
                case 'z':
                    {
                        // Cabecea con zzz
                        float n = Math.Max(0, c.Sin(2)) * 5;
                        c.Both((x, s) => c.Line(x - 7, n, x + 7, n, 2.6f, w));
                        Color zc = Color.FromArgb(174, 182, 200);
                        Glyph(c, 24, "z", 9, zc, -8 - (float)(c.Sec * 10 % 8));
                        Glyph(c, 30, "z", 9, zc, -14 - (float)((c.Sec * 10 + 4) % 8));
                        break;
                    }
                case 'a': c.Both((x, s) => HalfMoon(c, x)); c.Heart(0, -12, 0.6f * c.Pulse, PinkHeart); break;
                case 'e': c.Both((x, s) => c.Spin(x, 0, s * 0.25, () => c.Rect(-7, 0, 14, 3, w))); break;
                case 'w': HalfMoon(c, EL); c.Rect(ER - 7, 0, 14, 2, w); break;
                default: c.Both((x, s) => HalfMoon(c, x + c.LookX * 2)); break;
            }
        }

        private static void CyberEye(Face c, float x, float o)
        {
            c.Poly(Cyan, x - 9, 5 * o, x - 4, -6 * o, x + 9, -6 * o, x + 4, 5 * o);
        }

        private static void Cyber(Face c, char face)
        {
            switch (face)
            {
                case 'f':
                    {
                        // Chevrons de neón que brillan
                        float glow = 0.4f + Math.Abs(c.Sin(5)) * 0.6f;
                        c.Both((x, s) => { c.Lines(6, Op(Cyan, glow * 0.35f), x - 8, 4, x, -4, x + 8, 4); c.Lines(3, Cyan, x - 8, 4, x, -4, x + 8, 4); });
                        break;
                    }
                case 's': c.Both((x, s) => { c.Box(x - 9, -9, 18, 18, Cyan, 2); c.Rect(x - 3, -3, 6, 6, Cyan); }); break;
                case 'd': c.Both((x, s) => c.Rect(x - 9, 0, 18, 2, Cyan)); break;
                case 'z':
                    {
                        // Falla RGB
                        float o = c.Sin(25) * 2;
                        c.Both((x, s) =>
                        {
                            c.Poly(Color.FromArgb(204, 255, 0, 80), x - 9 + o, 5, x - 4 + o, -6, x + 9 + o, -6, x + 4 + o, 5);
                            c.Poly(Color.FromArgb(217, 34, 211, 238), x - 9 - o, 5, x - 4 - o, -6, x + 9 - o, -6, x + 4 - o, 5);
                        });
                        break;
                    }
                case 'a':
                    c.Both((x, s) =>
                    {
                        GraphicsState st = c.G.Save();
                        c.G.TranslateTransform(x, 0);
                        c.G.ScaleTransform(c.Pulse, c.Pulse);
                        using (Pen p = Stroke(c.C(PinkHeart), 2))
                            c.G.DrawPolygon(p, new[] { new PointF(0, 7), new PointF(-8, -1), new PointF(-4, -6), new PointF(0, -2), new PointF(4, -6), new PointF(8, -1) });
                        c.G.Restore(st);
                    });
                    break;
                case 'e': c.Both((x, s) => c.Poly(Red, x - 9, 5, x - 9, s < 0 ? -1 : -5, x + 9, s < 0 ? -5 : -1, x + 9, 5)); break;
                case 'w': CyberEye(c, EL, 1); c.Rect(ER - 9, 0, 18, 2, Cyan); break;
                default: c.Both((x, s) => CyberEye(c, x + c.LookX * 2, c.Open)); break;
            }
        }

        // --- Estilos nuevos (mockup «10 ojitos nuevos») ---

        private static readonly Color PatchGray = Color.FromArgb(70, 74, 86);
        private static readonly Color PuppyBlue = Color.FromArgb(91, 140, 255);
        private static readonly Color Gold = Color.FromArgb(232, 196, 106);
        private static readonly Color AlienGreen = Color.FromArgb(141, 255, 158);
        private static readonly Color Stitch = Color.FromArgb(154, 161, 178);

        // Enojado: un triángulo negro tapa la parte de arriba hacia el centro (como el Búho)
        private static void AngryCut(Face c, float x, int s, float top, float bottom, float half)
        {
            c.Poly(Color.Black, x - half, top, x + half, top, s < 0 ? x + half : x - half, bottom);
        }

        private static void HappyArc(Face c, float x, float r, float w, Color color) { c.Arc(x, r * 0.6f, r, 1.1f, 1.9f, w, color); }
        private static void SleepArc(Face c, float x, float r, float w, Color color) { c.Arc(x, -r * 0.5f, r, 0.15f, 0.85f, w, color); }

        private static void Ghost(Face c, char face)
        {
            Color w = c.Eye;
            bool mouth = !SuiteSettings.EyesMouth; // Si el usuario ya tiene boquita, no se dibuja la propia
            switch (face)
            {
                case 'f':
                    {
                        // Flota: cada ojo sube y baja
                        float b = c.Sin(3) * 3;
                        c.Ell(EL, -2 + b, 4, 7, w);
                        c.Ell(ER, -2 - b, 4, 7, w);
                        if (mouth) c.Arc(0, 7, 5, 0.15f, 0.85f, 2.4f, w);
                        break;
                    }
                case 's': c.Both((x, s) => c.Ell(x, -1, 6.5f, 11, w)); if (mouth) c.Ring(0, 11, 2.5f, 3.5f, w, 2); break;
                case 'd': c.Both((x, s) => SleepArc(c, x, 6, 3, w)); if (mouth) c.Dot(0, 10, 1.6f, w); break;
                case 'z':
                    // Se derrite: los ojos se vuelven ondas
                    c.Both((x, s) =>
                    {
                        float[] wave = new float[34];
                        for (int k = -8; k <= 8; k++) { wave[2 * (k + 8)] = x + k; wave[2 * (k + 8) + 1] = c.Sin(5, k * 0.7 + (s > 0 ? 1 : 0)) * 2.5f - 2; }
                        c.Lines(2.6f, w, wave);
                    });
                    if (mouth) c.Lines(2, w, -6, 10, -3, 8, 0, 10, 3, 8, 6, 10);
                    break;
                case 'a': c.Both((x, s) => c.Heart(x, 0, 1.1f * c.Pulse, PinkHeart)); if (mouth) c.Arc(0, 6, 4, 0.15f, 0.85f, 2.5f, w); break;
                case 'e': c.Both((x, s) => { c.Ell(x, 0, 5, 9, w); AngryCut(c, x, s, -11, 0, 7); }); if (mouth) c.Arc(0, 14, 4, 1.15f, 1.85f, 2.5f, w); break;
                case 'w': c.Ell(EL, 0, 5, 9, w); HappyArc(c, ER, 6, 3.5f, w); if (mouth) c.Arc(0, 6, 4, 0.15f, 0.85f, 2.5f, w); break;
                default: c.Both((x, s) => c.Ell(x + c.LookX * 2, c.LookY * 2, 5, 9 * c.Open, w)); if (mouth) c.Ell(0, 10, 2, 2.4f, w); break;
            }
        }

        private static void KawaiiEye(Face c, float x, float r, float lookScale)
        {
            c.Ell(x, 0, r, r * 1.08f * c.Open, c.Eye);
            if (c.Open < 0.5f) return;
            float px = x + c.LookX * 2.5f * lookScale, py = 1 + c.LookY * 2 * lookScale;
            c.Ell(px, py, r * 0.7f, r * 0.76f, Dark);
            c.Dot(px - r * 0.25f, py - r * 0.3f, r * 0.24f, Color.White);
            c.Dot(px + r * 0.25f, py + r * 0.28f, r * 0.11f, Color.White);
        }

        private static void Kawaii(Face c, char face)
        {
            Color w = c.Eye;
            if (face != 'd')
            {
                c.RR(-31, 7, 9, 4, 2, Rose, 0.55f);
                c.RR(22, 7, 9, 4, 2, Rose, 0.55f);
            }
            switch (face)
            {
                case 'f': { float sp = 6 + c.Sin(6) * 1.2f; c.Both((x, s) => c.Sparkle(x, 0, sp, w)); break; } // Ojos de estrella
                case 's': c.Both((x, s) => KawaiiEye(c, x, 11, 0)); break;
                case 'd': c.Both((x, s) => SleepArc(c, x, 7, 3, w)); break;
                case 'z':
                    // Mareado rosa, bien sonrojado
                    c.Both((x, s) => { c.Dot(x, 0, 9, w); c.Swirl(x, 0, 7, c.Sec * 5 * (s > 0 ? -1 : 1), 1.8f, PinkHeart); });
                    c.Ell(-27, 7, 5, 2.5f, Color.FromArgb(217, 255, 60, 120));
                    c.Ell(27, 7, 5, 2.5f, Color.FromArgb(217, 255, 60, 120));
                    break;
                case 'a': c.Both((x, s) => { c.Heart(x, 0, 1.4f * c.Pulse, PinkHeart); c.Dot(x - 4, -3, 1.8f, Color.White); }); break;
                case 'e': c.Both((x, s) => { KawaiiEye(c, x, 9, 0.5f); AngryCut(c, x, s, -12, -1, 11); }); break;
                case 'w': KawaiiEye(c, EL, 9, 1); c.Lines(3, w, ER - 7, -5, ER + 5, 0, ER - 7, 5); break;
                default: c.Both((x, s) => KawaiiEye(c, x, 9, 1)); break;
            }
        }

        private static void Pirate(Face c, char face)
        {
            Color w = c.Eye;
            // El parche se queda en todas las caras
            Color patch = Color.FromArgb(104, 110, 126);
            if (face != 'z')
            {
                c.Line(-34, -9, 0, -14, 2f, patch);
                c.Ell(EL, 0, 9, 8, patch);
                c.Ring(EL, 0, 9, 8, Color.FromArgb(140, 146, 160), 1);
            }
            switch (face)
            {
                case 'f':
                    // Sonrisa de lado con diente de oro
                    HappyArc(c, ER, 7, 3.5f, w);
                    c.Arc(2, 6, 7, 0.1f, 0.75f, 2.4f, w);
                    c.Rect(4, 10, 3, 3, Gold);
                    break;
                case 's': c.RR(ER - 7, -12, 14, 24, 7, w); break;
                case 'd': SleepArc(c, ER, 6, 3, w); break;
                case 'z':
                    // Parche ladeado que se columpia y ojo ✕ girando
                    c.Spin(EL, 0, 0.5 + c.Sin(3) * 0.15, () => c.Ell(0, 4, 8, 7, patch));
                    c.Spin(ER, 0, c.Sec * 6, () => { c.Line(-5, -5, 5, 5, 2.6f, w); c.Line(-5, 5, 5, -5, 2.6f, w); });
                    break;
                case 'a': c.Heart(ER, 0, 1.3f * c.Pulse, PinkHeart); break;
                case 'e': c.RR(ER - 6, -8, 12, 16, 6, w); AngryCut(c, ER, 1, -11, -1, 9); break;
                case 'w': c.Arc(ER, -3, 6, 0.15f, 0.85f, 3, w); c.Lines(2.2f, w, -3, 13, 3, 14, 8, 11); break;
                default: c.RR(ER - 6 + c.LookX * 2.5f, -8 * c.Open + c.LookY * 2, 12, 16 * c.Open, 6, w); break;
            }
        }

        private static void Hypno(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f':
                    {
                        // Espirales de colores
                        double hue = c.Sec * 120 % 360;
                        c.Dot(EL, 0, 9, w); c.Swirl(EL, 0, 8, c.Sec * 8, 2, Hsl(hue, 0.9, 0.55));
                        c.Dot(ER, 0, 9, w); c.Swirl(ER, 0, 8, c.Sec * 8, 2, Hsl(hue + 120, 0.9, 0.55));
                        break;
                    }
                case 's': c.Both((x, s) => { c.Dot(x, 0, 11, w); c.Spiral(x, 0, Dark, false, 0.95f); }); break;
                case 'd': c.Both((x, s) => SleepArc(c, x, 6, 3, w)); break;
                case 'z':
                    // Al revés y a distinto ritmo
                    c.Dot(EL, 0, 9, w); c.Swirl(EL, 0, 8, c.Sec * 7, 2, Color.Black);
                    c.Dot(ER, 0, 9, w); c.Swirl(ER, 0, 8, -c.Sec * 11, 2, Color.Black);
                    break;
                case 'a': c.Both((x, s) => { c.Heart(x, 0, 1.3f * c.Pulse, PinkHeart); }); break;
                case 'e': c.Both((x, s) => { c.Dot(x, 0, 9, w); c.Spiral(x, 0, Red, false, 0.75f); AngryCut(c, x, s, -11, -1, 10); }); break;
                case 'w': c.Dot(EL, 0, 9, w); c.Spiral(EL, 0, Dark, false, 0.75f); HappyArc(c, ER, 7, 3.5f, w); break;
                default: c.Both((x, s) => { c.Ell(x, 0, 9, 9 * c.Open, w); if (c.Open > 0.5f) c.Spiral(x + c.LookX * 1.5f, c.LookY, Dark, false, 0.75f); }); break;
            }
        }

        private static void PuppyEye(Face c, float x, float r, bool brow)
        {
            c.Ell(x, 0, r, r * 1.1f * c.Open, c.Eye);
            if (c.Open < 0.5f) return;
            float px = x + c.LookX * 2, py = 1.5f + c.LookY * 1.5f;
            c.Dot(px, py, r * 0.74f, PuppyBlue);
            c.Dot(px, py + 0.5f, r * 0.42f, Dark);
            c.Dot(px - r * 0.28f, py - r * 0.32f, r * 0.27f, Color.White);
            c.Dot(px + r * 0.3f, py + r * 0.3f, r * 0.12f, Color.White);
        }

        private static void PuppyBrows(Face c)
        {
            // Cejitas de ternura: el lado de adentro más alto
            c.Both((x, s) => c.Line(x + s * 7, -13, x - s * 5, -17, 2.2f, c.Eye));
        }

        private static void Puppy(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f': c.Both((x, s) => HappyArc(c, x, 7, 3.5f, w)); c.Ell(c.Sin(8) * 1.5f, 11, 4, 5, Color.FromArgb(255, 111, 143)); break; // Lengua de fuera
                case 's': c.Both((x, s) => PuppyEye(c, x, 11, false)); PuppyBrows(c); break;
                case 'd': c.Both((x, s) => SleepArc(c, x, 6, 3, w)); PuppyBrows(c); break;
                case 'z':
                    // Estrellitas dando vueltas en la cabeza
                    c.Both((x, s) => { c.Dot(x, 0, 8, w); c.Dot(x, 1, 5, PuppyBlue); });
                    for (int i = 0; i < 3; i++) { double a = c.Sec * 3 + i * 2.1; c.Star((float)Math.Cos(a) * 22, -14 + (float)Math.Sin(a) * 3, 3, Color.FromArgb(255, 216, 74)); }
                    break;
                case 'a': c.Both((x, s) => { c.Heart(x, 0, 1.3f * c.Pulse, PinkHeart); c.Dot(x - 4, -3, 1.6f, Color.White); }); break;
                case 'e': c.Both((x, s) => { PuppyEye(c, x, 9, false); AngryCut(c, x, s, -12, -1, 11); }); break;
                case 'w': PuppyEye(c, EL, 9, false); HappyArc(c, ER, 7, 3.5f, w); c.RR(-3, 7, 6, 6, 3, Rose); break;
                default: c.Both((x, s) => PuppyEye(c, x, 9, true)); PuppyBrows(c); break;
            }
        }

        private static void Monocle(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f': c.Both((x, s) => HappyArc(c, x, 5, 2.8f, w)); c.Sparkle(ER + 6, -6, (c.Sin(4) + 1) * 2.5f + 1, Color.FromArgb(255, 246, 200)); break; // Destello
                case 's': c.Both((x, s) => c.Dot(x, -1, 5, w)); break;
                case 'd': c.Both((x, s) => c.Line(x - 5, 1, x + 5, 1, 2.6f, w)); break;
                case 'z':
                    {
                        // El monóculo se resbala
                        c.Swirl(EL, 0, 5, c.Sec * 4, 1.8f, w);
                        c.Dot(ER, 0, 3.5f, w);
                        float drop = 6 + c.Sin(3) * 2;
                        c.Ring(ER + 3, drop + 6, 7, 7, Gold, 2);
                        c.Line(ER + 3, drop - 1, ER + 8, -8, 1.2f, Gold);
                        break;
                    }
                case 'a': c.Both((x, s) => c.Heart(x, 0, 0.7f * c.Pulse, PinkHeart)); break;
                case 'e': c.Both((x, s) => { c.Dot(x, 1, 3.5f, w); c.Line(x - s * 7, -9, x + s * 5, -4, 2.4f, w); }); break;
                case 'w': c.Dot(EL, 0, 3.5f, w); c.Line(ER - 5, 1, ER + 5, 1, 2.6f, w); break;
                default: c.Both((x, s) => c.Ell(x + c.LookX * 2.5f, c.LookY * 2, 3.5f, 3.5f * c.Open + 0.4f, w)); break;
            }
            // Monóculo dorado con cadenita y bigote, en todas las caras
            if (face != 'z')
            {
                c.Ring(ER, 0, 9, 9, Gold, 2);
                c.Line(ER + 6, 7, ER + 11, 17, 1.2f, Gold);
            }
            using (Pen p = Stroke(c.C(w), 2.6f))
            {
                c.G.DrawBezier(p, 0, 10, -4, 14, -9, 14, -12, 9);
                c.G.DrawBezier(p, 0, 10, 4, 14, 9, 14, 12, 9);
            }
        }

        private static void AlienEye(Face c, float x, int s, float rx, float ry, Color color)
        {
            c.Spin(x, 0, -s * 0.45, () => c.Ell(0, 0, rx, Math.Max(0.6f, ry), color));
        }

        private static void Alien(Face c, char face)
        {
            Color g = AlienGreen;
            switch (face)
            {
                case 'f':
                    // Medias lunas con brillo
                    c.Both((x, s) => c.Spin(x, 0, -s * 0.45, () => c.FillHalfTop(0, 0, 10, 5, g)));
                    c.Sparkle(EL - 3, -4, 2 + c.Sin(5), Color.White);
                    c.Sparkle(ER - 3, -4, 2 + c.Cos(5), Color.White);
                    break;
                case 's': c.Both((x, s) => { AlienEye(c, x, s, 12, 7, g); c.Dot(x, 0, 2.2f, Dark); }); break;
                case 'd': c.Both((x, s) => c.Spin(x, 0, -s * 0.45, () => c.Line(-9, 0, 9, 0, 2.6f, g))); break;
                case 'z': c.Spin(EL, 0, c.Sec * 4, () => c.Ell(0, 0, 9, 4.5f, g)); c.Spin(ER, 0, -c.Sec * 6, () => c.Ell(0, 0, 9, 4.5f, g)); break; // Cada ojo a un lado
                case 'a': c.Both((x, s) => c.Heart(x, 0, 1.3f * c.Pulse, PinkHeart)); break;
                case 'e': c.Both((x, s) => { AlienEye(c, x, s, 10, 5, g); AngryCut(c, x, s, -10, 1, 12); }); break;
                case 'w': AlienEye(c, EL, -1, 10, 5, g); c.Spin(ER, 0, -0.45, () => c.Line(-9, 0, 9, 0, 2.6f, g)); break;
                default: c.Both((x, s) => AlienEye(c, x + c.LookX * 2, s, 10, 5 * c.Open, g)); break;
            }
        }

        private static void Panda(Face c, char face)
        {
            Color w = c.Eye;
            // Manchas de panda, en todas las caras
            float bob = face == 'f' ? c.Sin(4) * 1.5f : 0;
            if (face != 'z') c.Both((x, s) => c.Spin(x, 1 + bob, s * 0.5, () => c.Ell(0, 0, 8.5f, 11.5f, PatchGray)));
            switch (face)
            {
                case 'f': c.Both((x, s) => c.Up(x, bob, 4.5f, 2.6f, w)); c.Ell(0, 9, 3, 2, Color.FromArgb(255, 143, 176)); break; // Arquitos y naricita
                case 's': c.Both((x, s) => { c.Dot(x, 0, 5, w); c.Dot(x, 0, 2.2f, Dark); }); break;
                case 'd': c.Both((x, s) => c.Line(x - 4, 1, x + 4, 1, 2.4f, w)); break;
                case 'z': c.Both((x, s) => { c.Spin(x, 1, c.Sec * 3 * s, () => c.Ell(0, 0, 8.5f, 11.5f, PatchGray)); c.Dot(x, 0, 3, w); }); break; // Manchas que giran
                case 'a': c.Both((x, s) => c.Heart(x, 0, 0.7f * c.Pulse, PinkHeart)); break;
                case 'e': c.Both((x, s) => { c.Dot(x, 1, 3.8f, w); c.Dot(x, 1.5f, 1.8f, Dark); c.Line(x - s * 6, -7, x + s * 4, -3, 2.2f, w); }); break;
                case 'w': c.Dot(EL, 0, 3.8f, w); c.Dot(EL, 0.5f, 1.8f, Dark); c.Line(ER - 4, 1, ER + 4, 1, 2.4f, w); break;
                default:
                    c.Both((x, s) =>
                    {
                        float px = x + c.LookX * 2, py = c.LookY * 2;
                        c.Ell(px, py, 3.8f, 3.8f * c.Open + 0.3f, w);
                        if (c.Open > 0.5f) c.Dot(px + c.LookX, py + 0.5f, 1.8f, Dark);
                    });
                    break;
            }
        }

        // Caritas de texto: cada ojo es un signo, centrado en su lugar
        private static void Glyph(Face c, float x, string text, float size, Color color, float y = 0)
        {
            using (GraphicsPath path = new GraphicsPath())
            using (FontFamily family = new FontFamily("Segoe UI"))
            {
                path.AddString(text, family, (int)FontStyle.Bold, size, PointF.Empty, StringFormat.GenericTypographic);
                RectangleF b = path.GetBounds();
                if (b.Width <= 0) return;
                using (Matrix m = new Matrix())
                {
                    m.Translate(x - (b.X + b.Width / 2), y - (b.Y + b.Height / 2));
                    path.Transform(m);
                }
                using (SolidBrush brush = new SolidBrush(c.C(color))) c.G.FillPath(brush, path);
            }
        }

        private static void Kaomoji(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f':
                    {
                        // ^▽^ que rebota
                        float b = c.Sin(6) * 1.5f;
                        Glyph(c, EL, "^", 24, w, 2 + b);
                        Glyph(c, ER, "^", 24, w, 2 - b);
                        c.Lines(1.8f, w, -4, 7, 4, 7, 0, 12, -4, 7);
                        break;
                    }
                case 's': Glyph(c, EL, "O", 20, w); Glyph(c, ER, "O", 20, w); break;
                case 'd': Glyph(c, EL, "–", 22, w); Glyph(c, ER, "–", 22, w); break;
                case 'z':
                    // @_@ girando
                    c.Spin(EL, 0, c.Sec * 5, () => Glyph(c, 0, "@", 20, w));
                    c.Spin(ER, 0, -c.Sec * 5, () => Glyph(c, 0, "@", 20, w));
                    c.Line(-3, 9, 3, 9, 2, w);
                    break;
                case 'a': c.Both((x, s) => c.Heart(x, 0, 1.1f * c.Pulse, PinkHeart)); break;
                case 'e': Glyph(c, EL, ">", 22, w); Glyph(c, ER, "<", 22, w); break;
                case 'w': Glyph(c, EL, "•", 26, w); Glyph(c, ER, "~", 24, w); break;
                default:
                    if (c.Open < 0.5f) { Glyph(c, EL, "–", 22, w); Glyph(c, ER, "–", 22, w); }
                    else { Glyph(c, EL + c.LookX * 2, "•", 28, w); Glyph(c, ER + c.LookX * 2, "•", 28, w); }
                    break;
            }
        }

        private static void Zombie(Face c, char face)
        {
            Color w = c.Eye;
            // Puntadas sobre el ojo derecho, en todas las caras
            c.Line(ER - 6, -15, ER + 11, -11, 1.4f, Stitch);
            for (int i = 0; i < 3; i++) c.Line(ER - 2 + i * 5, -16 + i, ER - 3 + i * 5, -10 + i, 1.2f, Stitch);
            switch (face)
            {
                case 'f':
                    // Sonrisa con puntadas
                    HappyArc(c, EL, 9, 3.5f, w); HappyArc(c, ER, 5, 3, w);
                    c.Arc(0, 4, 8, 0.15f, 0.85f, 2.2f, w);
                    for (int i = -1; i <= 1; i++) c.Line(i * 4, 9, i * 4, 13, 1.4f, Stitch);
                    break;
                case 's': c.Dot(EL, 0, 12, w); c.Dot(EL + 2, -1, 3, Dark); c.Dot(ER, 1, 8, w); c.Dot(ER, 1, 2, Dark); break;
                case 'd': SleepArc(c, EL, 8, 3, w); c.Line(ER - 4, -3, ER + 4, 5, 2.4f, w); c.Line(ER - 4, 5, ER + 4, -3, 2.4f, w); break;
                case 'z':
                    // Pupila que da vueltas y un ojo ✕
                    c.Dot(EL, 0, 9, w);
                    c.Dot(EL + c.Cos(5) * 4, c.Sin(5) * 4, 3, Color.Black);
                    c.Line(ER - 4, -4, ER + 4, 4, 2.4f, w);
                    c.Line(ER - 4, 4, ER + 4, -4, 2.4f, w);
                    break;
                case 'a': c.Heart(EL, 0, 1.4f * c.Pulse, PinkHeart); c.Heart(ER, 1, 0.8f * c.Pulse, PinkHeart); break;
                case 'e': c.Dot(EL, 0, 10, w); c.Dot(EL + 2, 1, 3, Dark); AngryCut(c, EL, -1, -11, -1, 11); c.Dot(ER, 1, 6, w); c.Dot(ER, 2, 2, Dark); break;
                case 'w': c.Dot(EL, 0, 10, w); c.Dot(EL + 2, -2, 3, Dark); c.Line(ER - 6, 1, ER + 6, 1, 2.6f, w); break;
                default:
                    // Ojos desparejos: uno grande y otro chico, cada pupila mira a su ritmo
                    c.Ell(EL, 0, 10, 10 * c.Open, w);
                    c.Ell(ER, 1, 6, 6 * c.Open, w);
                    if (c.Open > 0.5f)
                    {
                        c.Dot(EL + 2 + c.LookX * 3, -2 + c.LookY * 2, 3, Dark);
                        c.Dot(ER + c.LookX * 1.5f, 1 + c.LookY, 2, Dark);
                    }
                    break;
            }
        }

        // --- 5 estilos más (mockup «Ojitos para elegir 5»): Slime, Rana, Saiyajin, Ninja y Vampiro ---

        // Trazo tipo canvas (moveTo/lineTo/quadraticCurveTo) para copiar los dibujos del mockup tal cual
        private class Shape : IDisposable
        {
            public readonly GraphicsPath Path = new GraphicsPath();
            private PointF _cur;
            public Shape M(float x, float y) { Path.StartFigure(); _cur = new PointF(x, y); return this; }
            public Shape L(float x, float y) { PointF p = new PointF(x, y); Path.AddLine(_cur, p); _cur = p; return this; }
            public Shape Q(float cx, float cy, float x, float y) { PointF p = new PointF(x, y); Quad(Path, _cur, new PointF(cx, cy), p); _cur = p; return this; }
            public Shape Z() { Path.CloseFigure(); return this; }
            public void Dispose() { Path.Dispose(); }
        }

        private static void FillShape(Face c, Shape shape, Color color)
        {
            using (shape)
            using (SolidBrush b = new SolidBrush(c.C(color))) c.G.FillPath(b, shape.Path);
        }

        private static void StrokeShape(Face c, Shape shape, float width, Color color)
        {
            using (shape)
            using (Pen p = Stroke(c.C(color), width)) c.G.DrawPath(p, shape.Path);
        }

        private static void DownArc(Face c, float x, float y, float r, float w, Color color) { c.Arc(x, y - r * 0.5f, r, 0.15f, 0.85f, w, color); }

        // Caras comunes del mockup cuando el estilo no trae la suya: sorprendido (ojo 1,25 veces) y enojado (corte + cejas)
        private static void GenericSurprised(Action<float, int, float, float, float> open) { open(EL, -1, 0, 0, 1.25f); open(ER, 1, 0, 0, 1.25f); }
        private static void GenericAngry(Face c, Color accent, Action<float, int, float, float, float> open)
        {
            open(EL, -1, 0, 0.4f, 1); open(ER, 1, 0, 0.4f, 1);
            AngryCut(c, EL, -1, -14, -1, 12); AngryCut(c, ER, 1, -14, -1, 12);
            c.Line(EL - 6, -12, EL + 6, -6, 2.2f, accent);
            c.Line(ER + 6, -12, ER - 6, -6, 2.2f, accent);
        }

        private static readonly Color SlimeGreen = Color.FromArgb(124, 242, 156);
        private static readonly Color SlimeShine = Color.FromArgb(232, 255, 233);

        private static void Slime(Face c, char face)
        {
            Action<float, int, float, float, float> open = (x, s, lx, ly, k) =>
            {
                float j = c.Sin(9, s) * 0.8f;
                c.Ell(x, 1, (8 + j) * k, (8 - j) * k, SlimeGreen);
                c.Ell(x + lx * 2.5f, ly * 2, 3 * k, 3.4f * k, Dark);
                c.Dot(x - 3, -3, 1.6f, SlimeShine);
            };
            switch (face)
            {
                case 'f':
                    c.Both((x, s) =>
                    {
                        // Gelatina feliz que tiembla
                        float j = c.Sin(12) * 1.2f;
                        GraphicsState st = c.G.Save();
                        c.G.TranslateTransform(x, 0);
                        c.G.ScaleTransform(1 + j * 0.06f, 1 - j * 0.06f);
                        c.Up(0, 0, 7, 4, SlimeGreen);
                        c.G.Restore(st);
                    });
                    break;
                case 's': GenericSurprised(open); break;
                case 'z': c.Both((x, s) => { float j = c.Sin(8, s > 0 ? 1 : 0) * 2; c.Ell(x, 1, 8 + j, 8 - j, SlimeGreen); c.Swirl(x, 1, 5, c.Sec * 5 * (s > 0 ? -1 : 1), 1.6f, Dark); }); break;
                case 'd':
                    c.Both((x, s) => c.Ell(x, 3 + c.Sin(2), 9, 4, SlimeGreen));
                    c.Dot(26, -2 - (float)(c.Sec * 6 % 10), 2, Op(SlimeGreen, 0.6f)); // Gotita que sube
                    break;
                case 'e': GenericAngry(c, SlimeGreen, open); break;
                case 'a': c.Both((x, s) => { c.Heart(x, 0, 1.1f + 0.12f * c.Sin(6), SlimeGreen); c.Dot(x - 3, -3, 1.5f, SlimeShine); }); break;
                case 'w': open(EL, -1, 0, 0, 1); c.Up(ER, 0, 7, 4, SlimeGreen); break;
                default:
                    if (c.Open < 0.5f) c.Both((x, s) => c.Line(x - 6, 1, x + 6, 1, 2.4f, SlimeGreen));
                    else { open(EL, -1, c.LookX, c.LookY, 1); open(ER, 1, c.LookX, c.LookY, 1); }
                    break;
            }
        }

        private static readonly Color FrogGreen = Color.FromArgb(155, 229, 100);
        private static readonly Color FrogSkin = Color.FromArgb(95, 181, 58);

        private static void Frog(Face c, char face)
        {
            bool mouth = !SuiteSettings.EyesMouth;
            Action<float, int, float, float, float> open = (x, s, lx, ly, k) =>
            {
                c.Ell(x, -2, 9 * k, 9 * k, FrogSkin);
                c.Ell(x, -2, 7 * k, 7 * k, Color.White);
                c.Ell(x + lx * 2.5f, -2 + ly * 2, 3 * k, 4.5f * k, Dark);
            };
            switch (face)
            {
                case 'f':
                    c.Both((x, s) => { c.Ell(x, -2, 9, 9, FrogSkin); c.Up(x, -3, 5, 2.6f, Dark); });
                    if (mouth) c.Arc(0, 4, 9, 0.1f, 0.9f, 2, FrogGreen);
                    break;
                case 's': GenericSurprised(open); break;
                case 'z': c.Both((x, s) => { c.Ell(x, -2, 9, 9, FrogSkin); c.Ell(x, -2, 7, 7, Color.White); c.Swirl(x, -2, 5, c.Sec * 6 * (s > 0 ? -1 : 1), 1.5f, Dark); }); break;
                case 'd':
                    {
                        c.Both((x, s) => { c.Ell(x, -2, 9, 9, FrogSkin); c.Line(x - 6, -1, x + 6, -1, 2.4f, Dark); });
                        float tongue = Math.Max(0, c.Sin(1.5)) * 10; // Se le sale la lengüita
                        if (tongue > 0.3f) c.Line(0, 6, tongue, 6 + tongue * 0.2f, 2, Color.FromArgb(255, 122, 154));
                        break;
                    }
                case 'e': GenericAngry(c, FrogGreen, open); break;
                case 'a': c.Both((x, s) => { c.Ell(x, -2, 9, 9, FrogSkin); c.Heart(x, -2, 0.75f + 0.1f * c.Sin(6), PinkHeart); }); break;
                case 'w':
                    open(EL, -1, 0, 0, 1);
                    c.Ell(ER, -2, 9, 9, FrogSkin); c.Line(ER - 6, -1, ER + 6, -1, 2.4f, Dark);
                    if (mouth) c.Line(-4, 7, 6, 7, 2, FrogGreen);
                    break;
                default:
                    if (c.Open < 0.5f) c.Both((x, s) => { c.Ell(x, -2, 9, 9, FrogSkin); c.Line(x - 6, -1, x + 6, -1, 2.4f, Dark); });
                    else { open(EL, -1, c.LookX, c.LookY, 1); open(ER, 1, c.LookX, c.LookY, 1); }
                    break;
            }
        }

        private static readonly Color VampDark = Color.FromArgb(127, 29, 29);

        private static void Fangs(Face c, float y)
        {
            c.Poly(Color.White, -5, y, -3, y + 5, -1, y);
            c.Poly(Color.White, 1, y, 3, y + 5, 5, y);
        }

        private static void Vampire(Face c, char face)
        {
            Action<float, int, float, float, float> open = (x, s, lx, ly, k) =>
            {
                c.Ell(x, 0, 7 * k, 7 * k, VampDark);
                c.Ell(x + lx * 2, ly * 1.5f, 5 * k, 5 * k, Red);
                c.Ell(x + lx * 2, ly * 1.5f, 1.4f * k, 4 * k, Dark);
            };
            switch (face)
            {
                case 'f': c.Both((x, s) => c.Up(x, 0, 7, 3.2f, Red)); Fangs(c, 8); break;
                case 's': GenericSurprised(open); Fangs(c, 9); break;
                case 'z': c.Both((x, s) => { c.Ell(x, 0, 7, 7, VampDark); c.Swirl(x, 0, 5, c.Sec * 6 * (s > 0 ? -1 : 1), 1.6f, Red); }); break;
                case 'd':
                    {
                        c.Both((x, s) => DownArc(c, x, 0, 6, 2.6f, Red));
                        float f = c.Sin(3) * 3; // Murcielaguito
                        c.Poly(Color.FromArgb(75, 85, 99), 24, -10 + f, 30, -14 + f, 28, -8 + f, 34, -10 + f, 28, -5 + f);
                        break;
                    }
                case 'e': GenericAngry(c, Red, open); Fangs(c, 9); break;
                case 'a': c.Both((x, s) => c.Heart(x, 0, 1.1f + 0.1f * c.Sin(7), Red)); break;
                case 'w': open(EL, -1, 0, 0, 1); c.Up(ER, 0, 6, 3, Red); Fangs(c, 9); break;
                default:
                    if (c.Open < 0.5f) c.Both((x, s) => c.Line(x - 6, 1, x + 6, 1, 2.4f, Red));
                    else { open(EL, -1, c.LookX, c.LookY, 1); open(ER, 1, c.LookX, c.LookY, 1); }
                    Fangs(c, 9);
                    break;
            }
        }

        // Saiyajin calcado de la hoja del usuario
        private static readonly Color SaiGold = Color.FromArgb(217, 169, 42);
        private static readonly Color SaiTeal = Color.FromArgb(45, 212, 191);
        private static readonly Color Iris = Color.FromArgb(20, 184, 166);

        // Flamas doradas que suben detrás del cabello (enojado)
        private static void SaiFlames(Face c)
        {
            for (int i = 0; i < 11; i++)
            {
                float x = -40 + i * 8, h = 16 + c.Sin(14, i * 1.9) * 6, tip = x + c.Sin(9, i) * 2;
                Shape flame = new Shape().M(x - 6, 8).Q(x - 2, -h * 0.4f, tip, -h - 6).Q(x + 2, -h * 0.4f, x + 6, 8).Z();
                using (flame)
                using (LinearGradientBrush b = new LinearGradientBrush(new PointF(0, 9), new PointF(0, -h - 7), Color.Black, Color.Black))
                {
                    ColorBlend blend = new ColorBlend();
                    blend.Colors = new[] { c.C(Color.FromArgb(0, 250, 204, 21)), c.C(Color.FromArgb(115, 250, 204, 21)), c.C(Color.FromArgb(217, 255, 251, 224)) };
                    blend.Positions = new[] { 0f, 0.5f, 1f };
                    b.InterpolationColors = blend;
                    c.G.FillPath(b, flame.Path);
                }
            }
        }

        private static void FillGold(Face c, Shape shape, float top, float bottom, bool hot = false)
        {
            using (shape)
            using (LinearGradientBrush b = new LinearGradientBrush(new PointF(0, top - 1), new PointF(0, bottom + 1), Color.Black, Color.Black))
            {
                ColorBlend blend = new ColorBlend();
                blend.Colors = hot
                    ? new[] { c.C(Color.FromArgb(255, 251, 224)), c.C(Color.FromArgb(250, 204, 21)), c.C(Color.FromArgb(184, 134, 15)) }
                    : new[] { c.C(Color.FromArgb(246, 214, 107)), c.C(Color.FromArgb(226, 181, 56)), c.C(Color.FromArgb(184, 134, 15)) };
                blend.Positions = new[] { 0f, 0.55f, 1f };
                b.InterpolationColors = blend;
                c.G.FillPath(b, shape.Path);
            }
        }

        private static void SaiHair(Face c, float k, bool angry = false)
        {
            float sw = c.Sin(5) + (angry ? c.Sin(40) * 0.8f : 0); // Enojado: tiembla
            float[,] tips = { { -37, -13 }, { -32, -24 }, { -22, -30 }, { -12, -27 }, { -3, -35 }, { 7, -28 }, { 17, -33 }, { 27, -25 }, { 37, -13 } };
            float[] vals = { -15, -21, -22, -22, -23, -22, -22, -18 };
            Shape hair = new Shape().M(-31, -1);
            for (int i = 0; i < 9; i++)
            {
                hair.L(tips[i, 0] + sw * (i % 2 == 1 ? 1 : -1), -6 + (tips[i, 1] + 6) * k);
                if (i < 8) hair.L(tips[i, 0] + 5, -6 + (vals[i] + 6) * k);
            }
            hair.L(31, -1).L(24, -9).L(14, -12).L(6, -10).L(0, -3).L(-5, -11).L(-16, -12).L(-24, -9).Z();
            FillGold(c, hair, -6 - 30 * k, -1, angry);
            FillShape(c, new Shape().M(-6, -14).L(-3 + sw, -6 - 27 * k).L(1, -15).Z(), Color.FromArgb(115, 255, 246, 200));
        }

        private static void SaiFlatHair(Face c)
        {
            FillGold(c, new Shape().M(-30, -2).Q(-32, -22, -16, -25).L(16, -25).Q(25, -25, 27, -16).L(32, -4).L(24, -9).Q(0, -14, -23, -9).Z(), -25, -2);
        }

        private static void SaiEye(Face c, float x, int s, float lx, float ly, float k, bool glow = false)
        {
            GraphicsState st = c.G.Save();
            c.G.TranslateTransform(x, 1);
            if (s > 0) c.G.ScaleTransform(-1, 1);
            Shape eye = new Shape().M(-8 * k, -4 * k).L(7 * k, 0).Q(5 * k, 5 * k, -1, 5 * k).Q(-7 * k, 4.5f * k, -8 * k, -4 * k).Z();
            using (SolidBrush b = new SolidBrush(c.C(Color.White))) c.G.FillPath(b, eye.Path);
            StrokeShape(c, eye, 1.3f, SaiGold);
            c.Line(-8.5f * k, -4.5f * k, 7.5f * k, -0.3f, 2.2f, SaiGold);
            c.G.Restore(st);
            float px = x + lx * 2 - s * 1.2f, py = 2.4f + ly;
            if (glow) c.Dot(px, py, 4.4f * k, Color.FromArgb(89, 45, 212, 191)); // Ojos con brillo (enojado)
            c.Dot(px, py, 3.1f * k, Iris);
            c.Dot(px, py, 1.3f * k, Dark);
            c.Dot(px - 1, py - 1, 0.8f, Color.White);
        }

        private static void Saiyan(Face c, char face)
        {
            bool mouth = !SuiteSettings.EyesMouth;
            switch (face)
            {
                case 'f':
                    SaiHair(c, 1);
                    c.Both((x, s) => { c.Arc(x, 1, 9, 1.2f, 1.8f, 1.5f, SaiGold); c.Up(x, 2, 5.5f, 2.4f, SaiTeal); });
                    if (mouth) c.Arc(0, 7, 3, 0.15f, 0.85f, 1.8f, SaiTeal);
                    break;
                case 's':
                    SaiHair(c, 1);
                    c.Both((x, s) => { c.Arc(x, 0, 10, 1.2f, 1.8f, 1.5f, SaiGold); c.Dot(x, 2, 6.5f, Color.White); c.Ring(x, 2, 6.5f, 6.5f, SaiGold, 1.3f); c.Dot(x, 2, 3, Iris); });
                    if (mouth) c.Ell(0, 12, 1.7f, 2.3f, SaiTeal);
                    break;
                case 'z':
                    SaiHair(c, 1);
                    c.Both((x, s) => c.Swirl(x, 2, 6, c.Sec * 6 * (s > 0 ? -1 : 1), 1.8f, SaiTeal));
                    if (mouth) c.Lines(1.4f, SaiGold, -6, 11, -4, 9, -2, 11, 0, 9, 2, 11, 4, 9, 6, 11);
                    break;
                case 'd':
                    SaiFlatHair(c);
                    c.Both((x, s) => c.Line(x - 6, 3, x + 6, 3, 2.4f, SaiTeal));
                    if (mouth) c.Line(-2.5f, 10, 2.5f, 10, 1.8f, SaiTeal);
                    break;
                case 'e':
                    {
                        // Opción A del mockup «Saiyajin enojado» (Súper Saiyajin): rayitos alrededor, cabello más alto y
                        // brillante que tiembla, ojos con brillo y dientes apretados
                        for (int i = 0; i < 5; i++)
                        {
                            if (c.Sin(11, i * 2) < 0.3f) continue; // Aparecen y desaparecen
                            double a = c.Sec * 3 + i * 1.3;
                            float r = 34 + c.Sin(7, i) * 4;
                            float x = (float)Math.Cos(a) * r, y = (float)Math.Sin(a) * r * 0.45f;
                            c.Lines(1.4f, Yellow, x, y, x + 3, y - 2, x + 1, y + 1, x + 5, y - 1);
                        }
                        SaiHair(c, 1.18f, true);
                        SaiEye(c, EL, -1, 0, 0.3f, 0.95f, true);
                        SaiEye(c, ER, 1, 0, 0.3f, 0.95f, true);
                        c.Rect(-5, 9, 10, 4, Color.White);
                        Color gap = Color.FromArgb(85, 85, 85);
                        c.Line(-5, 11, 5, 11, 0.8f, gap);
                        c.Line(-2, 9, -2, 13, 0.8f, gap);
                        c.Line(2, 9, 2, 13, 0.8f, gap);
                        break;
                    }
                case 'a':
                    SaiHair(c, 1);
                    c.Both((x, s) => { c.Heart(x, 2, 0.95f + 0.08f * c.Sin(7), Color.FromArgb(255, 111, 154)); c.Dot(x + 3, -1, 1.2f, Color.White); });
                    break;
                case 'w':
                    SaiHair(c, 1.1f);
                    SaiEye(c, EL, -1, 0, 0, 1);
                    c.Up(ER, 2, 5.5f, 2.4f, SaiTeal);
                    if (mouth) StrokeShape(c, new Shape().M(-2, 11).Q(2, 13, 5, 9), 1.8f, Color.White);
                    break;
                default:
                    SaiHair(c, 1);
                    if (c.Open < 0.5f) c.Both((x, s) => c.Line(x - 6, 2, x + 6, 2, 2.4f, SaiTeal));
                    else c.Both((x, s) => SaiEye(c, x, s, c.LookX, c.LookY, 1));
                    if (mouth) c.Line(-2, 10, 2, 10, 1.6f, Color.White);
                    break;
            }
        }

        // Ninja calcado de la hoja del usuario
        private static void NinjaBand(Face c)
        {
            c.RR(-24, -21, 48, 9, 2, Color.FromArgb(59, 67, 86));
            float w = c.Sin(6) * 1.5f;
            Color tail = Color.FromArgb(43, 50, 67);
            c.Poly(tail, 23, -20, 32, -24 + w, 33, -21 + w, 23, -16);
            c.Poly(tail, 23, -16, 31, -15 + w, 31, -12 + w, 23, -13);
            Glyph(c, 0, "G", 7, Color.FromArgb(163, 172, 189), -16.5f);
        }

        private static void NinjaMask(Face c, bool mouth)
        {
            FillShape(c, new Shape().M(-25, 1).Q(-8, 4, 0, 6).Q(8, 4, 25, 1).Q(22, 15, 0, 15).Q(-22, 15, -25, 1).Z(), Color.FromArgb(42, 48, 61));
            if (mouth) c.Line(-2.5f, 9.5f, 2.5f, 9.5f, 1.6f, Color.White);
        }

        private static void NinjaEye(Face c, float x, int s, float k)
        {
            k *= 1.2f;
            GraphicsState st = c.G.Save();
            c.G.TranslateTransform(x, 0);
            if (s > 0) c.G.ScaleTransform(-1, 1);
            FillShape(c, new Shape().M(-8 * k, -5 * k).L(8 * k, 0).Q(6 * k, 6 * k, 0, 6 * k).Q(-8 * k, 6 * k, -8 * k, -5 * k).Z(), Color.White);
            c.G.Restore(st);
        }

        private static void Ninja(Face c, char face)
        {
            bool mouth = !SuiteSettings.EyesMouth;
            Color white = Color.White;
            if (face != 'a') NinjaBand(c); // Enamorado va sin banda, como en la hoja
            switch (face)
            {
                case 'f':
                    c.Both((x, s) => c.Up(x, 1, 5.5f, 2.6f, white));
                    c.Ell(-23, 6, 3.5f, 2, Color.FromArgb(184, 80, 104));
                    c.Ell(23, 6, 3.5f, 2, Color.FromArgb(184, 80, 104));
                    if (mouth) c.Arc(0, 6, 3.5f, 0.15f, 0.85f, 1.8f, white);
                    break;
                case 's':
                    c.Both((x, s) => { c.Dot(x, 1, 6.5f, white); c.Dot(x, 1, 2.3f, Iris); });
                    if (mouth) c.Ell(0, 11, 2, 2.7f, white);
                    break;
                case 'z':
                    c.Both((x, s) => c.Swirl(x, 1, 6, c.Sec * 6 * (s > 0 ? -1 : 1), 1.7f, white));
                    if (mouth) { c.Line(-3.5f, 9.5f, 3.5f, 12.5f, 1.6f, white); c.Line(-3.5f, 12.5f, 3.5f, 9.5f, 1.6f, white); }
                    break;
                case 'd':
                    c.Both((x, s) => c.Line(x - 6, 2, x + 6, 2, 2.6f, white));
                    if (mouth) c.Line(-2.5f, 9, 2.5f, 9, 1.8f, white);
                    break;
                case 'e':
                    c.Both((x, s) => { NinjaEye(c, x, s, 0.9f); c.Dot(x - s * 3, 2.5f, 2.3f, Iris); c.Dot(x - s * 3, 2.5f, 1, Dark); });
                    NinjaMask(c, mouth);
                    break;
                case 'a':
                    {
                        float p = 1 + 0.08f * c.Sin(7);
                        c.Heart(EL, 1, p, Color.FromArgb(232, 112, 143));
                        c.Heart(ER, 1, p, Color.FromArgb(217, 79, 110));
                        break;
                    }
                case 'w':
                    c.Dot(EL, 1, 6, white); c.Dot(EL + 1.5f, 2, 2.4f, Iris);
                    c.Line(ER - 5, 1, ER + 5, 1, 2.4f, Color.FromArgb(227, 179, 65));
                    if (mouth) c.Arc(3, 7, 3, 0.2f, 0.8f, 1.8f, white);
                    break;
                default:
                    if (c.Open < 0.5f) c.Both((x, s) => c.Line(x - 6, 2, x + 6, 2, 2.4f, white));
                    else c.Both((x, s) => { NinjaEye(c, x, s, 1); c.Dot(x - s * 3 + c.LookX * 1.5f, 2.5f + c.LookY, 1.9f, Dark); });
                    NinjaMask(c, mouth);
                    break;
            }
        }

        #endregion

        private static void DrawHeart(Graphics g, float cx, float cy, float s, Color color)
        {
            using (GraphicsPath heart = new GraphicsPath())
            {
                heart.AddBezier(cx, cy + 7 * s, cx - 12 * s, cy - 2 * s, cx - 6 * s, cy - 11 * s, cx, cy - 4 * s);
                heart.AddBezier(cx, cy - 4 * s, cx + 6 * s, cy - 11 * s, cx + 12 * s, cy - 2 * s, cx, cy + 7 * s);
                heart.CloseFigure();
                using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, heart);
            }
        }
    }
}
