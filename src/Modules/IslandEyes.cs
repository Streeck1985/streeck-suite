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

                // Cara del estilo (mockup «Caras de cada estilo»): cada estilo dibuja sus 8 caras en las unidades del
                // mockup (ojos a ±15), que aquí se agrandan 1,2 veces para quedar a ±18 como antes
                char face;
                float open = f.Open;
                switch (mood)
                {
                    case Mood.Happy: case Mood.Laugh: face = 'f'; break;
                    case Mood.Surprised: face = 's'; break;
                    case Mood.Sleep: face = 'd'; break;
                    case Mood.Yawn: face = f.Phase > 0.15f && f.Phase < 0.85f ? 'd' : 'n'; break;
                    case Mood.Dizzy: face = 'z'; break;
                    case Mood.Love: face = 'a'; break;
                    case Mood.Angry: face = 'e'; break;
                    case Mood.Wink: face = 'w'; break;
                    case Mood.Nod: face = f.Phase > 0.75f ? 's' : 'n'; break; // Despierta de golpe
                    default: face = 'n'; break;
                }
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

        #region Caras de cada estilo (unidades del mockup: ojos en x = ±15, centro en 0)

        private const float EL = -15, ER = 15;
        private static readonly Color Dark = Color.FromArgb(11, 11, 16);
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
                default: Actual(c, face); break;
            }
        }

        private static void Actual(Face c, char face)
        {
            Color w = c.Eye;
            switch (face)
            {
                case 'f': c.Both((x, s) => c.Arc(x, 4, 7, 1.1f, 1.9f, 3.5f, w)); break;
                case 's': c.Both((x, s) => c.RR(x - 6, -12, 12, 22, 6, w)); break;
                case 'd': c.Both((x, s) => c.Arc(x, -3, 6, 0.15f, 0.85f, 3, w)); break;
                case 'z': c.Both((x, s) => c.Spiral(x, 0, w)); break;
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
                case 'f': c.Both((x, s) => { c.Arc(x, 5, 8, 1.1f, 1.9f, 2.5f, w); c.Arc(x, 5, 4, 1.1f, 1.9f, 2.5f, w); }); break;
                case 's': c.Both((x, s) => { c.Ring(x, 0, 8, 11, w, 3); c.Ring(x, 0, 4, 6, w, 2); }); break;
                case 'd': c.Both((x, s) => { c.Arc(x, -5, 6, 0.05f, 0.95f, 3, w); c.Arc(x, -3, 6, 0.1f, 0.9f, 1.5f, w, 0.35f); }); break;
                case 'z': c.Both((x, s) => { c.Ring(x, 0, 7, 9, w, 2); c.Spiral(x, 0, w, false, 0.6f); }); break;
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
                case 'f': c.Both((x, s) => { c.Arc(x, 4, 7, 1.1f, 1.9f, 3, w); c.Line(x - 2, -6, x - 3, -9, 1.5f, w); c.Line(x + 2, -6, x + 3, -9, 1.5f, w); }); break;
                case 's':
                    c.Both((x, s) =>
                    {
                        c.Ell(x, 0, 9, 11, w); c.Dot(x, 1, 5, Dark); c.Dot(x + 2, -3, 1.8f, Color.White);
                        for (int k = -1; k <= 1; k++) c.Line(x + k * 5, -11, x + k * 7, -15, 1.5f, w);
                    });
                    break;
                case 'd': c.Both((x, s) => { c.Arc(x, -4, 7, 0.1f, 0.9f, 2.5f, w); for (int k = -1; k <= 1; k++) c.Line(x + k * 5, 2, x + k * 6, 5, 1.3f, w); }); break;
                case 'z': c.Both((x, s) => { c.Spiral(x, 0, w); c.Line(x - 4, -11, x - 5, -14, 1.5f, w); c.Line(x + 4, -11, x + 5, -14, 1.5f, w); }); break;
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
                    c.Both((x, s) =>
                    {
                        using (GraphicsPath arch = new GraphicsPath())
                        using (Pen p = Stroke(c.C(w), 3.5f))
                        {
                            arch.AddLine(x - 6, 8, x - 6, -2);
                            arch.AddArc(x - 6, -8, 12, 12, 180, 180);
                            arch.AddLine(x + 6, -2, x + 6, 8);
                            c.G.DrawPath(p, arch);
                        }
                    });
                    break;
                case 's': c.Both((x, s) => { c.Ring(x, 0, 7, 12, w, 2.5f); c.Ell(x, 0, 2, 7, w); }); break;
                case 'd': c.Both((x, s) => { using (Pen p = Stroke(c.C(w), 4)) c.G.DrawBezier(p, x - 9, 0, x - 3, -2, x + 3, -2, x + 9, 0); }); break;
                case 'z': c.Both((x, s) => c.Spin(x, 0, c.T / 160, () => c.Ell(0, 0, 2.5f, 10, w))); break;
                case 'a': c.Both((x, s) => c.Heart(x, 0, 1.3f * c.Pulse, PinkHeart)); break;
                case 'e': c.Both((x, s) => { c.Ell(x, 2, 8, 5, w); c.Ell(x, 2, 1.5f, 4, Dark); }); break;
                case 'w': c.Ell(EL, 0, 6, 11, w); c.Lines(3, w, ER - 7, 3, ER, -4, ER + 7, 3); break;
                default: c.Both((x, s) => { c.Ell(x + c.LookX * 1.5f, c.LookY, 6, 11 * c.Open, w); if (c.Open > 0.5f) c.Ell(x + c.LookX * 3, c.LookY * 2, 1.6f, 8 * c.Open, Dark); }); break;
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
                    c.Both((x, s) =>
                    {
                        for (int k = 0; k < 7; k++)
                        {
                            double a = Math.PI * (1.05 + k * 0.15);
                            c.Line(x + (float)Math.Cos(a) * 5, 5 + (float)Math.Sin(a) * 5, x + (float)Math.Cos(a) * 10, 5 + (float)Math.Sin(a) * 10, 2.2f, w);
                        }
                    });
                    break;
                case 's': c.Both((x, s) => ScanRect(c, x - 8, -13, 16, 24)); break;
                case 'd': c.Both((x, s) => { for (int k = 0; k < 4; k++) c.Rect(x - 9 + k * 5, -1 + (s < 0 ? k : 3 - k) * 1.5f, 5, 2.5f, w); }); break;
                case 'z': c.Both((x, s) => c.Spin(x, 0, c.T / 200, () => { c.Box(-8, -8, 16, 16, w, 2); c.Box(-4, -4, 8, 8, w, 2); })); break;
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
                case 'f': c.Both((x, s) => c.Arc(x, 3, 6, 1.1f, 1.9f, 2.5f, w)); break;
                case 's': c.Dot(EL, 0, 6, w); c.Arc(ER, 2, 6, 1.1f, 1.9f, 2.5f, w); break;
                case 'd': c.Both((x, s) => c.Arc(x, -3, 6, 0.15f, 0.85f, 2, w)); break;
                case 'z': c.Both((x, s) => c.Spiral(x, 0, w, false, 0.6f)); break;
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
                case 'f': c.Both((x, s) => { int[,] cells = { { 0, 2 }, { 1, 1 }, { 2, 0 }, { 3, 1 }, { 4, 2 } }; for (int k = 0; k < 5; k++) c.Rect(x - 10 + cells[k, 0] * 4, -2 + cells[k, 1] * 4, 3.4f, 3.4f, w); }); break;
                case 's': c.Both((x, s) => PixelGrid(c, x - 8, -12, 4, 6)); break;
                case 'd': c.Both((x, s) => { for (int i = 0; i < 4; i++) c.Rect(x - 9 + i * 5, 0, 3.4f, 2.6f, w); }); break;
                case 'z': c.Both((x, s) => c.Spiral(x, 0, w, true)); break;
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
                        c.RR(-32, -6, 64, 12, 6, w, 0.12f);
                        float x = (float)Math.Sin(c.T / 60) * 26;
                        c.Glow(x - 6, -6, 12, 12, w, Math.Sin(c.T / 40) > 0 ? 1 : 0.4f);
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
                case 'f': c.Arc(0, 6, 10, 1.1f, 1.9f, 4, w); break;
                case 's': c.Ell(0, 0, 11, 16, w); c.Dot(0, 3, 3, Dark); c.Line(-10, -19, 10, -19, 2.5f, w); break;
                case 'd': c.Arc(0, -4, 9, 0.1f, 0.9f, 3, w); c.Arc(0, -2, 9, 0.15f, 0.85f, 1.5f, w, 0.35f); break;
                case 'z': c.Spiral(0, 0, w, false, 1.3f); break;
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
                case 'f': c.Both((x, s) => c.Heart(x, 0, 1.4f + 0.12f * (float)Math.Sin(c.T / 150), PinkHeart)); break;
                case 's': c.Both((x, s) => c.Heart(x, 0, 1.8f, Rose)); break;
                case 'd': Closed(c); break;
                case 'z': c.Both((x, s) => c.Spin(x, 0, c.T / 160, () => c.Heart(0, 0, 1.1f, PinkHeart))); break;
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
                case 'f': c.Both((x, s) => c.Star(x, 0, 10 + (float)Math.Sin(c.T / 150), Yellow)); break;
                case 's': c.Both((x, s) => c.Star(x, 0, 12, Color.White)); break;
                case 'd': Closed(c); break;
                case 'z': c.Both((x, s) => c.Spin(x, 0, c.T / 150, () => c.Star(0, 0, 9, Amber))); break;
                case 'a': c.Both((x, s) => c.Star(x, 0, 10 * c.Pulse, PinkHeart)); break;
                case 'e': c.Both((x, s) => c.Star(x, 2, 7, Red)); break;
                case 'w': c.Star(EL, 0, 9, Amber); c.Arc(ER, 3, 6, 1.1f, 1.9f, 3, c.Eye); break;
                default: c.Both((x, s) => c.Star(x + c.LookX * 2, c.LookY * 2, 8 * c.Open + 1, Amber)); break;
            }
        }

        private static void Glasses(Face c, char face)
        {
            Color w = c.Eye;
            c.Box(EL - 10, -8, 20, 16, w, 2);
            c.Box(ER - 10, -8, 20, 16, w, 2);
            c.Line(EL + 10, -2, ER - 10, -2, 2, w);
            switch (face)
            {
                case 'f': c.Both((x, s) => c.Arc(x, 3, 5, 1.15f, 1.85f, 2.5f, w)); break;
                case 's': c.Both((x, s) => c.Dot(x, 0, 5, w)); break;
                case 'd': c.Both((x, s) => c.Line(x - 6, 0, x + 6, 0, 2, w)); break;
                case 'z': c.Both((x, s) => c.Spiral(x, 0, w, false, 0.55f)); break;
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
                case 'f': c.Both((x, s) => { c.Dot(x, 0, 10, Amber); c.Rect(x - 11, 0, 22, 11, Color.Black); }); break;
                case 's': c.Both((x, s) => { c.Dot(x, 0, 12, Amber); c.Dot(x, 0, 3, Color.Black); }); break;
                case 'd': c.Both((x, s) => { c.Dot(x, 0, 10, Amber); c.Rect(x - 11, -11, 22, 13, Color.Black); }); break;
                case 'z': c.Both((x, s) => { c.Dot(x, 0, 10, Amber); c.Spiral(x, 0, Color.Black, false, 0.6f); }); break;
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
                case 'f': c.Both((x, s) => c.Arc(x, 3, 6, 1.1f, 1.9f, 3, w)); break;
                case 's': c.Both((x, s) => c.Dot(x, 0, 7, w)); break;
                case 'd': c.Both((x, s) => c.Rect(x - 7, 0, 14, 2, w)); break;
                case 'z': c.Both((x, s) => c.Spin(x, 0, Math.Sin(c.T / 120) * 0.5, () => HalfMoon(c, 0))); break;
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
                case 'f': c.Both((x, s) => c.Lines(3, Cyan, x - 8, 4, x, -4, x + 8, 4)); break;
                case 's': c.Both((x, s) => { c.Box(x - 9, -9, 18, 18, Cyan, 2); c.Rect(x - 3, -3, 6, 6, Cyan); }); break;
                case 'd': c.Both((x, s) => c.Rect(x - 9, 0, 18, 2, Cyan)); break;
                case 'z': c.Both((x, s) => c.Spin(x, 0, c.T / 120, () => c.Box(-7, -7, 14, 14, Cyan, 2))); break;
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
