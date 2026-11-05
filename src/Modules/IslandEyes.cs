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
            if (f.Mood == Mood.Nod && f.Phase < 0.75f) f.Open = Math.Max(0.12f, 1 - f.Phase / 0.75f);

            Lively = Math.Abs(targetX - _lookX) > 0.02f || Math.Abs(targetY - _lookY) > 0.02f || tempActive || hovering
                     || bp < 1.5 || (_nextBlink - now).TotalMilliseconds < 150 || f.Mood == Mood.Happy || f.Mood == Mood.Yawn;

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

        private static Color EyeColor
        {
            get
            {
                try { return ColorTranslator.FromHtml(SuiteSettings.EyesColor); }
                catch (Exception) { return Color.White; }
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

                bool plain = mood == Mood.Idle || mood == Mood.Surprised || mood == Mood.Focus || mood == Mood.Sad || mood == Mood.Angry
                             || mood == Mood.Wave || mood == Mood.Nod || mood == Mood.Whistle || mood == Mood.Stretch;
                if (style == SuiteSettings.EyesStyleKind.Visor && plain) DrawVisor(g, y, tilt, f, color);
                else if (cyclops)
                {
                    GraphicsState one = g.Save();
                    g.TranslateTransform(tilt, y);
                    g.ScaleTransform(1.5f, 1.5f);
                    DrawEye(g, 0, 0, 1, sq, f, color, style);
                    g.Restore(one);
                }
                else
                {
                    DrawEye(g, left, y, -1, sq, f, color, style);
                    DrawEye(g, right, y, 1, sq, f, color, style);
                }

                // Cejas
                if (SuiteSettings.EyesBrows && (mood == Mood.Angry || mood == Mood.Focus || mood == Mood.Sad || mood == Mood.Surprised))
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

        private static void DrawEye(Graphics g, float cx, float cy, int side, float sq, Frame f, Color color, SuiteSettings.EyesStyleKind style)
        {
            Mood mood = f.Mood;
            float open = f.Open;
            float w = 16 + 6 * sq, h = 20 * open * (1 - 0.45f * sq);
            Color fill = A(color, f, 1), dark = A(Color.FromArgb(11, 11, 16), f, 1);

            // Formas de las emociones (iguales en todos los estilos)
            if (mood == Mood.Happy || mood == Mood.Laugh)
            {
                using (Pen pen = Stroke(fill, 4)) g.DrawArc(pen, cx - 8, cy - 4, 16, 16, 198, 144);
                return;
            }
            if (mood == Mood.Love)
            {
                DrawHeart(g, cx, cy, 1 + 0.12f * (float)Math.Sin(f.Ms / 120), A(Pink, f, 1));
                return;
            }
            if (mood == Mood.Dizzy)
            {
                List<PointF> spiral = new List<PointF>();
                for (float a = 0; a < 12; a += 0.2f)
                {
                    float r = a * 0.8f;
                    spiral.Add(new PointF(cx + (float)Math.Cos(a + f.Ms / 150) * r, cy + (float)Math.Sin(a + f.Ms / 150) * r));
                }
                using (Pen pen = Stroke(fill, 2.5f)) g.DrawLines(pen, spiral.ToArray());
                return;
            }
            if (mood == Mood.Wink && side > 0)
            {
                using (Pen pen = Stroke(fill, 4)) g.DrawLine(pen, cx - 7, cy, cx + 7, cy);
                return;
            }
            if (mood == Mood.Sleep)
            {
                using (Pen pen = Stroke(fill, 3.5f)) g.DrawArc(pen, cx - 7, cy - 9, 14, 14, 27, 126);
                return;
            }
            if (mood == Mood.Yawn && f.Phase > 0.15f && f.Phase < 0.85f)
            {
                float d = side < 0 ? 1 : -1;
                using (Pen pen = Stroke(fill, 3.5f))
                {
                    g.DrawLines(pen, new[] { new PointF(cx - 6 * d, cy - 6), new PointF(cx + 5 * d, cy), new PointF(cx - 6 * d, cy + 6) });
                }
                return;
            }

            float hh = h;
            if (mood == Mood.Surprised) hh = 26 * open;
            else if (mood == Mood.Focus) hh = 10 * open;
            else if (mood == Mood.Sad) hh = 16 * open;
            else if (mood == Mood.Angry) hh = 13 * open;
            else if (mood == Mood.Whistle) hh = 12 * open;
            if (mood == Mood.Nod && f.Phase > 0.75f) hh = 26 * open; // Despierta de golpe
            hh = Math.Max(3, hh);

            switch (style)
            {
                case SuiteSettings.EyesStyleKind.Actual:
                    IslandPainter.FillRounded(g, new RectangleF(cx - w / 2 + f.LookX * 3, cy - hh / 2 + f.LookY * 2, w, hh), Math.Min(w, hh) / 2, fill);
                    return;
                case SuiteSettings.EyesStyleKind.Pixel:
                    {
                        int rows = Math.Max(1, (int)Math.Round(hh / 8));
                        float shift = (float)Math.Round(f.LookX) * 4;
                        using (SolidBrush brush = new SolidBrush(fill))
                        {
                            for (int i = -2; i < 2; i++)
                                for (int j = -rows; j < rows; j++) g.FillRectangle(brush, cx + i * 4 + shift, cy + j * 4, 3.5f, 3.5f);
                        }
                        return;
                    }
                case SuiteSettings.EyesStyleKind.Dots:
                    {
                        float ry = Math.Max(1.5f, 5.5f * hh / 20);
                        using (SolidBrush brush = new SolidBrush(fill)) g.FillEllipse(brush, cx + f.LookX * 4 - 5.5f, cy + f.LookY * 3 - ry, 11, 2 * ry);
                        return;
                    }
                case SuiteSettings.EyesStyleKind.Cat:
                    using (SolidBrush brush = new SolidBrush(fill)) g.FillEllipse(brush, cx - w / 2 - 2, cy - hh / 2, w + 4, hh);
                    if (hh > 8)
                    {
                        float slit = mood == Mood.Surprised ? 4.5f : 1.8f;
                        using (SolidBrush brush = new SolidBrush(dark)) g.FillEllipse(brush, cx + f.LookX * 4 - slit, cy + f.LookY * 2 - (hh / 2 - 2), 2 * slit, hh - 4);
                    }
                    return;
                case SuiteSettings.EyesStyleKind.Robot:
                    using (SolidBrush brush = new SolidBrush(fill)) g.FillRectangle(brush, cx - w / 2 - 1, cy - hh / 2, w + 2, hh);
                    using (SolidBrush lines = new SolidBrush(A(Color.Black, f, 0.35f)))
                    {
                        for (float ly = cy - hh / 2 + 2; ly < cy + hh / 2; ly += 3) g.FillRectangle(lines, cx - w / 2 - 1, ly, w + 2, 1);
                    }
                    if (hh > 8)
                    {
                        using (SolidBrush brush = new SolidBrush(dark)) g.FillRectangle(brush, cx - 3 + f.LookX * 4, cy - 3 + f.LookY * (hh / 2 - 5), 6, 6);
                    }
                    return;
            }

            // Con pupila, Anime, Cíclope (y el Visor cuando hace una emoción con forma propia)
            IslandPainter.FillRounded(g, new RectangleF(cx - w / 2, cy - hh / 2, w, hh), Math.Min(8, hh / 2), fill);
            if (hh > 8)
            {
                float px = cx + f.LookX * 4, py = cy + f.LookY * (hh / 2 - 5);
                IslandPainter.FillRounded(g, new RectangleF(px - 4, py - 5, 8, 10), 4, dark);
                if (style == SuiteSettings.EyesStyleKind.Anime)
                {
                    using (SolidBrush shine = new SolidBrush(A(Color.White, f, 1)))
                    {
                        g.FillEllipse(shine, px + 1.5f - 1.8f, py - 2.5f - 1.8f, 3.6f, 3.6f);
                        g.FillEllipse(shine, px - 1.5f - 1, py + 2.5f - 1, 2, 2);
                    }
                }
            }
        }

        private static void DrawVisor(Graphics g, float y, float tilt, Frame f, Color color)
        {
            Mood mood = f.Mood;
            float baseH = mood == Mood.Surprised ? 18 : mood == Mood.Focus ? 7 : mood == Mood.Angry ? 9 : 13;
            float hh = Math.Max(3, baseH * f.Open);
            IslandPainter.FillRounded(g, new RectangleF(tilt - 32, y - hh / 2, 64, hh), hh / 2, A(color, f, 0.22f));
            float x = tilt + f.LookX * 20;
            // Brillo: dos halos y la luz
            IslandPainter.FillRounded(g, new RectangleF(x - 14, y - hh / 2 - 4, 28, hh + 8), (hh + 8) / 2, A(color, f, 0.15f));
            IslandPainter.FillRounded(g, new RectangleF(x - 11, y - hh / 2 - 2, 22, hh + 4), (hh + 4) / 2, A(color, f, 0.3f));
            IslandPainter.FillRounded(g, new RectangleF(x - 9, y - hh / 2, 18, hh), hh / 2, A(color, f, 1));
        }

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
