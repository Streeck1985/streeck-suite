using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Asistente de IA (mockup G1): tarjeta como la isla, arriba al centro. Se escribe (o se dicta con 🎙) una pregunta;
    /// la respuesta se escribe en vivo. Botones rápidos sobre lo copiado; órdenes como "recuérdame en 20 minutos..."
    /// se cumplen al momento sin preguntar a la IA. Toma el foco para poder escribir; Esc o clic fuera la ocultan.
    /// </summary>
    internal class AssistantWindow : LayeredWindow
    {
        private const float CardWidth = 440, TopMargin = 14, Pad = 16, InputHeight = 44, ChipHeight = 26, MaxAnswerHeight = 300;
        private const int ClipboardLimit = 6000;
        private static readonly Color Violet = Color.FromArgb(167, 139, 250);
        private static readonly Color VioletLight = Color.FromArgb(196, 181, 253);
        private static readonly Color Listening = Color.FromArgb(251, 146, 60);
        private static readonly Color Link = Color.FromArgb(125, 211, 252);
        private static readonly string[] ChipLabels = { "Usar lo copiado", "Resumir", "Traducir", "Explicar", "Corregir texto" };
        private static readonly string[] ChipPrompts =
        {
            null,
            "Resume el texto copiado en pocas frases.",
            "Traduce el texto copiado: al español si está en otro idioma; si ya está en español, al inglés. Devuelve solo la traducción.",
            "Explica el texto copiado de forma sencilla.",
            "Corrige la ortografía, la gramática y la redacción del texto copiado. Devuelve solo el texto corregido."
        };

        private readonly float _scale;
        private readonly IslandPainter _painter;
        private readonly Font _inputFont, _answerFont;
        private readonly Timer _caretTimer, _waveTimer;
        private string _query = "";
        private bool _caretVisible = true;
        private bool _useClipboard;

        // Último intercambio que se ve
        private string _question;
        private string _answer;
        private AssistantAction _action;
        private string _error;
        private bool _streaming;
        private float _scroll;            // Desplazamiento de la respuesta larga (rueda)
        private bool _followTail = true;  // Mientras llega la respuesta, se va al final salvo que el usuario suba

        private VoiceInput _voice;
        private bool _listening;
        private bool _transcribing;    // Ya dejó de hablar: Whisper está pasando la voz a texto

        private readonly List<KeyValuePair<RectangleF, Action>> _hits = new List<KeyValuePair<RectangleF, Action>>();
        private RectangleF _answerArea;
        private float _answerHeight;

        public AssistantWindow()
        {
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) _scale = screen.DpiX / 96f;
            _painter = new IslandPainter(_scale, 1f);
            _inputFont = new Font("Segoe UI", 11.25f, FontStyle.Regular);
            _answerFont = new Font("Segoe UI", 9.75f, FontStyle.Regular);
            TopMost = true;
            KeyPreview = true;
            Text = "Asistente · Streeck Suite";

            _caretTimer = new Timer { Interval = 530 };
            _caretTimer.Tick += (s, e) => { _caretVisible = !_caretVisible; RenderSurface(); };
            _waveTimer = new Timer { Interval = 50 };
            _waveTimer.Tick += (s, e) => RenderSurface(); // Ondas del micrófono
            MouseClick += OnCardClick;
            MouseWheel += OnCardWheel;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle &= ~0x08000000; // Puede activarse (hay que escribir)
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }

        private float Px(float value)
        {
            return value * _scale;
        }

        private int ShadowPad
        {
            get { return (int)Px(20); }
        }

        #region Abrir y cerrar

        public void Toggle()
        {
            if (Visible) HideAssistant(); else ShowAssistant(null);
        }

        /// <summary>
        /// Abre el asistente; con una pregunta (desde la búsqueda) la envía directamente.
        /// </summary>
        public void ShowAssistant(string question)
        {
            AssistantService.RememberActiveApp();
            _useClipboard = false;
            _query = "";
            Relayout();
            Show();
            Activate();
            NativeMethods.SetForegroundWindow(Handle);
            _caretTimer.Start();
            if (!string.IsNullOrEmpty(question)) Send(question, null);
        }

        private void HideAssistant()
        {
            StopListening();
            VoiceOutput.Stop();
            _caretTimer.Stop();
            Hide();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (!_listening) HideAssistant(); // Mientras se dicta, no se cierra si el foco salta un momento
        }

        #endregion

        #region Preguntar

        private void Send(string question, string chipPrompt)
        {
            question = (question ?? "").Trim();
            if (question.Length == 0 && chipPrompt == null) return;
            VoiceOutput.Stop();

            // Órdenes de la suite: se cumplen al momento, sin IA
            if (chipPrompt == null && !_useClipboard)
            {
                AssistantAction action = AssistantActions.TryRun(question);
                if (action != null)
                {
                    _question = question;
                    _answer = null;
                    _error = null;
                    _action = action;
                    _streaming = false;
                    _query = "";
                    Relayout();
                    return;
                }
            }

            string clipboard = null;
            if (chipPrompt != null || _useClipboard)
            {
                clipboard = Clipboard.ContainsText() ? Clipboard.GetText() : "";
                if (clipboard.Length == 0)
                {
                    ShowError("No tienes ningún texto copiado.");
                    return;
                }
                if (clipboard.Length > ClipboardLimit) clipboard = clipboard.Substring(0, ClipboardLimit) + "…";
            }
            string prompt = chipPrompt ?? question;
            _question = chipPrompt != null ? ChipLabels[Array.IndexOf(ChipPrompts, chipPrompt)] + " lo copiado" : question;
            _answer = "";
            _action = null;
            _error = null;
            _streaming = true;
            _scroll = 0;
            _followTail = true;
            _query = "";
            Relayout();

            AssistantService.Ask(prompt, clipboard,
                chunk => Ui(() => { _answer += chunk; Relayout(); }),
                done => Ui(() => { _streaming = false; Relayout(); Speak(done); }),
                error => Ui(() => { _streaming = false; ShowError(error); }));
        }

        private void ShowError(string error)
        {
            _error = error;
            _streaming = false;
            Relayout();
        }

        private void NewConversation()
        {
            AssistantService.NewConversation();
            VoiceOutput.Stop();
            _question = _answer = _error = null;
            _action = null;
            _streaming = false;
            Relayout();
        }

        private void Speak(string text)
        {
            if (SuiteSettings.AssistantSpeak) VoiceOutput.Speak(CleanMarkdown(text));
        }

        // La IA a veces responde con **negritas** o listas en Markdown: en la isla se ve como texto normal
        private static string CleanMarkdown(string text)
        {
            string clean = Regex.Replace(text ?? "", @"\*\*|__|`|^#+\s*", "", RegexOptions.Multiline);
            return Regex.Replace(clean, @"^\s*[\*\-]\s+", "• ", RegexOptions.Multiline);
        }

        // Los avisos del asistente y del dictado llegan en otros hilos
        private void Ui(Action action)
        {
            if (IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { /* Cerrándose */ }
        }

        #endregion

        #region Voz

        private void ToggleListening()
        {
            if (_listening) { StopListening(); return; }
            VoiceOutput.Stop();
            if (_voice == null)
            {
                // Un solo dictado por ventana: su cuadro de texto invisible vive dentro del asistente
                _voice = new VoiceInput(this);
                _voice.Transcribing += () => { _transcribing = true; RenderSurface(); };
                _voice.Finished += text =>
                {
                    _listening = false;
                    _transcribing = false;
                    _waveTimer.Stop();
                    if (text != null) Send(text, null); else Relayout();
                };
                _voice.Failed += message => { _listening = false; _transcribing = false; _waveTimer.Stop(); ShowError(message); };
            }
            _listening = true;
            _error = null;
            _waveTimer.Start();
            _voice.Start();
            Relayout();
        }

        private void StopListening()
        {
            if (!_listening) return;
            _listening = false;
            _waveTimer.Stop();
            if (_voice != null) _voice.Cancel();
            Relayout();
        }

        #endregion

        #region Teclado y ratón

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape:
                    if (_listening) StopListening(); else HideAssistant();
                    return true;
                case Keys.Enter:
                    Send(_query, null);
                    return true;
                case Keys.Control | Keys.V:
                    if (Clipboard.ContainsText()) SetQuery(_query + Clipboard.GetText().Replace("\r", " ").Replace("\n", " "));
                    return true;
                case Keys.Control | Keys.Back:
                    string trimmed = _query.TrimEnd();
                    int space = trimmed.LastIndexOf(' ');
                    SetQuery(space >= 0 ? trimmed.Substring(0, space + 1) : "");
                    return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (e.KeyChar == '\b') { if (_query.Length > 0) SetQuery(_query.Substring(0, _query.Length - 1)); }
            else if (!char.IsControl(e.KeyChar)) SetQuery(_query + e.KeyChar);
            e.Handled = true;
        }

        private void SetQuery(string query)
        {
            _query = query;
            _caretVisible = true;
            RenderSurface();
        }

        private void OnCardClick(object sender, MouseEventArgs e)
        {
            PointF point = new PointF(e.X - ShadowPad, e.Y - ShadowPad);
            for (int i = _hits.Count - 1; i >= 0; i--)
            {
                if (_hits[i].Key.Contains(point)) { _hits[i].Value(); return; }
            }
        }

        private void OnCardWheel(object sender, MouseEventArgs e)
        {
            if (_answerHeight <= Px(MaxAnswerHeight)) return;
            _scroll = Math.Max(0, Math.Min(_answerHeight - Px(MaxAnswerHeight), _scroll - e.Delta / 120f * Px(40)));
            _followTail = _scroll >= _answerHeight - Px(MaxAnswerHeight) - 1;
            RenderSurface();
        }

        #endregion

        /// <summary>
        /// Herramienta de desarrollo (--preview-assistant): pregunta de verdad, espera la respuesta y guarda la tarjeta en PNG.
        /// </summary>
        internal void RenderPreview(string question, string file)
        {
            CreateHandle();
            System.Threading.ManualResetEvent finished = new System.Threading.ManualResetEvent(false);
            AssistantAction action = AssistantActions.TryRun(question);
            if (action != null)
            {
                _question = question;
                _action = action;
            }
            else
            {
                _question = question;
                _answer = "";
                AssistantService.Ask(question, null, chunk => { lock (this) _answer += chunk; }, done => finished.Set(), error => { _error = error; finished.Set(); });
                while (!finished.WaitOne(100)) Application.DoEvents();
            }
            _streaming = false;
            Relayout();
            int width = Width, height = Height;
            using (Bitmap bitmap = new Bitmap(width, height))
            {
                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.FromArgb(28, 36, 51));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawSurface(g);
                }
                bitmap.Save(file);
            }
        }

        #region Medidas y dibujo

        private string AnswerText
        {
            get { return _error ?? CleanMarkdown(_answer); }
        }

        // Alto que ocupa el texto de la respuesta (con saltos de línea) en el ancho de la tarjeta
        private float MeasureAnswer()
        {
            if (_answer == null && _error == null) return 0;
            string text = AnswerText.Length == 0 ? " " : AnswerText;
            using (Bitmap bitmap = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                return g.MeasureString(text, _answerFont, (int)AnswerWidth).Height;
            }
        }

        private float AnswerWidth
        {
            get { return Px(CardWidth) - 2 * Px(Pad) - Px(38); }
        }

        private float ContentHeight()
        {
            float height = Pad + InputHeight + 10 + ChipHeight;
            if (_question != null)
            {
                height += 14 + 18 + 6;
                if (_action != null) height += 58;
                else height += Math.Min(_answerHeight, Px(MaxAnswerHeight)) / _scale;
            }
            return height + 14 + 18 + Pad;
        }

        private void Relayout()
        {
            _answerHeight = MeasureAnswer();
            if (_followTail) _scroll = Math.Max(0, _answerHeight - Px(MaxAnswerHeight));
            Rectangle screen = Screen.PrimaryScreen.Bounds;
            int width = (int)Px(CardWidth), height = (int)Px(ContentHeight());
            Bounds = new Rectangle(screen.X + (screen.Width - width) / 2 - ShadowPad, screen.Y + (int)Px(TopMargin) - ShadowPad,
                width + 2 * ShadowPad, height + 2 * ShadowPad);
            RenderSurface();
        }

        protected override void DrawSurface(Graphics g)
        {
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            RectangleF card = new RectangleF(ShadowPad, ShadowPad, Width - 2 * ShadowPad - 1, Height - 2 * ShadowPad - 1);
            DrawSoftShadow(g, card, Px(26), ShadowPad * 0.8f, Px(6), 200);
            using (GraphicsPath path = RoundedRect(card, Px(26)))
            using (SolidBrush fill = new SolidBrush(IslandPainter.Background))
            using (Pen border = new Pen(Color.FromArgb(128, _listening ? Listening : Violet), 1))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
            g.TranslateTransform(ShadowPad, ShadowPad);
            _hits.Clear();

            float x = Px(Pad), width = card.Width - 2 * Px(Pad);
            float y = Px(Pad);
            DrawInput(g, new RectangleF(x, y, width, Px(InputHeight)));
            y += Px(InputHeight) + Px(10);
            DrawChips(g, x, y, width);
            y += Px(ChipHeight);

            if (_question != null)
            {
                y += Px(14);
                _painter.DrawText(g, _question, _painter.Text12, IslandPainter.Gray, new RectangleF(x, y, width, Px(18)), StringAlignment.Near);
                y += Px(18) + Px(6);
                if (_action != null)
                {
                    DrawAction(g, new RectangleF(x, y, width, Px(52)));
                    y += Px(58);
                }
                else
                {
                    float visible = Math.Min(_answerHeight, Px(MaxAnswerHeight));
                    DrawAnswer(g, new RectangleF(x, y, width, visible));
                    y += visible;
                }
            }

            y += Px(14);
            DrawFooter(g, x, y, width);
        }

        private void DrawInput(Graphics g, RectangleF input)
        {
            Color accent = _listening ? Listening : Violet;
            IslandPainter.FillRounded(g, input, Px(14), _listening ? Color.FromArgb(30, Listening) : Color.FromArgb(15, 255, 255, 255));
            _painter.DrawText(g, _listening ? "" : "✦", _listening ? _painter.Glyph16 : _painter.Bold15, _listening ? Color.FromArgb(253, 186, 116) : VioletLight,
                new RectangleF(input.X + Px(12), input.Y, Px(20), input.Height), StringAlignment.Center);

            float textX = input.X + Px(42);
            RectangleF mic = new RectangleF(input.Right - Px(8) - Px(32), input.Y + (input.Height - Px(32)) / 2, Px(32), Px(32));
            float textWidth = mic.X - Px(8) - textX;
            if (_listening)
            {
                _painter.DrawText(g, _transcribing ? "Entendiendo…" : "Te escucho…", _inputFont, Color.FromArgb(253, 186, 116), new RectangleF(textX, input.Y, textWidth - Px(40), input.Height), StringAlignment.Near);
                DrawWaves(g, new RectangleF(mic.X - Px(40), input.Y + Px(10), Px(32), input.Height - Px(20)));
            }
            else if (_query.Length == 0)
            {
                _painter.DrawText(g, _streaming ? "Pensando…" : "Pregunta o di «recuérdame en 10 min…»", _inputFont,
                    Color.FromArgb(139, 147, 163), new RectangleF(textX, input.Y, textWidth, input.Height), StringAlignment.Near);
            }
            else
            {
                _painter.DrawText(g, _query, _inputFont, Color.White, new RectangleF(textX, input.Y, textWidth, input.Height), StringAlignment.Near);
            }
            if (!_listening && _caretVisible)
            {
                float caretX = textX + (_query.Length == 0 ? 0 : g.MeasureString(_query, _inputFont, PointF.Empty, StringFormat.GenericTypographic).Width + Px(1));
                caretX = Math.Min(caretX, mic.X - Px(10));
                using (Pen caret = new Pen(Color.White, Px(1.2f))) g.DrawLine(caret, caretX, input.Y + Px(13), caretX, input.Bottom - Px(13));
            }

            // Botón de micrófono
            using (SolidBrush back = new SolidBrush(_listening ? Color.FromArgb(60, Listening) : Color.FromArgb(20, 255, 255, 255))) g.FillEllipse(back, mic);
            _painter.DrawText(g, _listening ? "" : "", _painter.Glyph16, _listening ? Color.FromArgb(253, 186, 116) : Color.FromArgb(195, 201, 212), mic, StringAlignment.Center);
            _hits.Add(new KeyValuePair<RectangleF, Action>(mic, ToggleListening));
        }

        // Cinco barras al ritmo del micrófono (la misma idea que el ecualizador de la música)
        private void DrawWaves(Graphics g, RectangleF area)
        {
            float level = _voice != null ? _voice.Level : 0;
            double t = _painter.Seconds;
            float barWidth = area.Width / 8f;
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(253, 186, 116)))
            {
                for (int i = 0; i < 5; i++)
                {
                    float wobble = (float)(0.55 + 0.45 * Math.Sin(t * 9 + i * 1.3));
                    float h = Math.Max(Px(4), area.Height * Math.Max(0.15f, level * wobble));
                    RectangleF bar = new RectangleF(area.X + i * barWidth * 1.75f, area.Y + (area.Height - h) / 2, barWidth, h);
                    IslandPainter.FillRounded(g, bar, barWidth / 2, brush.Color);
                }
            }
        }

        private void DrawChips(Graphics g, float x, float y, float width)
        {
            bool hasClipboard = Clipboard.ContainsText();
            float cursor = x;
            for (int i = 0; i < ChipLabels.Length; i++)
            {
                int index = i;
                string label = ChipLabels[i];
                float chipWidth = g.MeasureString(label, _painter.Text12).Width + Px(16);
                if (cursor + chipWidth > x + width) break;
                RectangleF chip = new RectangleF(cursor, y, chipWidth, Px(ChipHeight));
                bool on = i == 0 && _useClipboard;
                Color back = on ? Color.FromArgb(51, Violet) : Color.FromArgb(15, 255, 255, 255);
                Color text = !hasClipboard ? Color.FromArgb(90, 255, 255, 255) : on ? VioletLight : Color.FromArgb(195, 201, 212);
                IslandPainter.FillRounded(g, chip, Px(10), back);
                _painter.DrawText(g, label, on ? _painter.Bold12 : _painter.Text12, text, chip, StringAlignment.Center);
                if (hasClipboard)
                {
                    _hits.Add(new KeyValuePair<RectangleF, Action>(chip, i == 0
                        ? (Action)(() => { _useClipboard = !_useClipboard; RenderSurface(); })
                        : () => Send("", ChipPrompts[index])));
                }
                cursor = chip.Right + Px(6);
            }
        }

        private void DrawAnswer(Graphics g, RectangleF area)
        {
            RectangleF badge = new RectangleF(area.X, area.Y, Px(28), Px(28));
            IslandPainter.FillRounded(g, badge, Px(9), _error != null ? Color.FromArgb(46, Listening) : Color.FromArgb(51, Violet));
            _painter.DrawText(g, _error != null ? "" : "✦", _error != null ? _painter.Glyph16 : _painter.Bold15, _error != null ? Color.FromArgb(253, 186, 116) : VioletLight, badge, StringAlignment.Center);

            RectangleF text = new RectangleF(area.X + Px(38), area.Y, AnswerWidth, area.Height);
            _answerArea = text;
            GraphicsState state = g.Save();
            g.SetClip(text);
            string shown = AnswerText;
            if (_streaming && shown.Length == 0) shown = "…";
            using (SolidBrush brush = new SolidBrush(_error != null ? Color.FromArgb(253, 186, 116) : Color.FromArgb(230, 233, 239)))
            {
                g.DrawString(shown, _answerFont, brush, new RectangleF(text.X, text.Y - _scroll, text.Width, _answerHeight + Px(20)));
            }
            g.Restore(state);
        }

        private void DrawAction(Graphics g, RectangleF area)
        {
            Color green = Color.FromArgb(134, 239, 172);
            IslandPainter.FillRounded(g, area, Px(12), Color.FromArgb(26, 74, 222, 128));
            _painter.DrawText(g, _action.Glyph, _painter.Glyph16, green, new RectangleF(area.X + Px(12), area.Y, Px(20), area.Height), StringAlignment.Center);
            float textX = area.X + Px(44);
            float doneWidth = g.MeasureString("Hecho", _painter.Bold12).Width + Px(16);
            RectangleF done = new RectangleF(area.Right - Px(12) - doneWidth, area.Y + (area.Height - Px(24)) / 2, doneWidth, Px(24));
            IslandPainter.FillRounded(g, done, Px(8), green);
            _painter.DrawText(g, "Hecho", _painter.Bold12, Color.FromArgb(11, 15, 23), done, StringAlignment.Center);
            bool hasDetail = !string.IsNullOrEmpty(_action.Detail);
            _painter.DrawText(g, _action.Title, _painter.Semibold13, Color.White,
                new RectangleF(textX, area.Y + (hasDetail ? Px(8) : 0), done.X - Px(10) - textX, hasDetail ? Px(19) : area.Height), StringAlignment.Near);
            if (hasDetail) _painter.DrawText(g, _action.Detail, _painter.Text12, IslandPainter.Gray, new RectangleF(textX, area.Y + Px(27), done.X - Px(10) - textX, Px(17)), StringAlignment.Near);
        }

        private void DrawFooter(Graphics g, float x, float y, float width)
        {
            Color legend = Color.FromArgb(139, 147, 163);
            bool local = SuiteSettings.AssistantEngine == SuiteSettings.AssistantEngineKind.Local;
            _painter.DrawText(g, (local ? "⚡ " : "✦ ") + AssistantService.EngineLabel, _painter.Text11, legend, new RectangleF(x, y, width - Px(170), Px(18)), StringAlignment.Near);

            float right = x + width;
            if (_question != null)
            {
                SizeF newSize = g.MeasureString("Nueva pregunta", _painter.Bold12);
                RectangleF newButton = new RectangleF(right - newSize.Width, y, newSize.Width, Px(18));
                _painter.DrawText(g, "Nueva pregunta", _painter.Bold12, Link, newButton, StringAlignment.Far);
                _hits.Add(new KeyValuePair<RectangleF, Action>(newButton, NewConversation));
                right = newButton.X - Px(14);
            }
            if (!string.IsNullOrEmpty(_answer) && !_streaming && _error == null)
            {
                SizeF copySize = g.MeasureString("Copiar", _painter.Bold12);
                RectangleF copy = new RectangleF(right - copySize.Width, y, copySize.Width, Px(18));
                _painter.DrawText(g, "Copiar", _painter.Bold12, Link, copy, StringAlignment.Far);
                _hits.Add(new KeyValuePair<RectangleF, Action>(copy, () => Clipboard.SetText(CleanMarkdown(_answer))));
            }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _caretTimer.Dispose();
                _waveTimer.Dispose();
                _painter.Dispose();
                _inputFont.Dispose();
                _answerFont.Dispose();
                if (_voice != null) _voice.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>
    /// Atajo global del asistente: Ctrl + Alt + A (se puede apagar en el panel y abrirlo solo desde la búsqueda).
    /// </summary>
    internal class AssistantHotkey : NativeWindow, IDisposable
    {
        private const int HotkeyId = 0xB300;
        private const uint KeyA = 0x41;
        private readonly Action _pressed;
        private bool _registered;

        public AssistantHotkey(Action pressed)
        {
            _pressed = pressed;
            CreateHandle(new CreateParams());
        }

        /// <summary>
        /// Registra o quita el atajo según el ajuste. Devuelve false si estaba activado y otro programa lo usa.
        /// </summary>
        public bool Apply()
        {
            if (_registered) { NativeMethods.UnregisterHotKey(Handle, HotkeyId); _registered = false; }
            if (!SuiteSettings.AssistantHotkey) return true;
            _registered = NativeMethods.RegisterHotKey(Handle, HotkeyId, NativeMethods.MOD_NOREPEAT | NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, KeyA);
            if (!_registered) Log.Error("[AssistantHotkey] Ctrl+Alt+A está ocupado por otro programa");
            return _registered;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId && _pressed != null) _pressed();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle == IntPtr.Zero) return;
            if (_registered) NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            DestroyHandle();
        }
    }
}