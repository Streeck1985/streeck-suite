using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Herramientas de la isla que antes solo tenían atajo (Claude, portapapeles, celular, micrófono): la isla conecta
    /// aquí sus acciones para que también se abran desde botones (Centro de control, Ajustes › Herramientas, Novedades).
    /// </summary>
    internal static class IslandTools
    {
        public static Action OpenClaude, OpenClipboard, OpenPhone, SendToPhone, ToggleMic, OpenCalculator;

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool LockWorkStation();

        public static void Lock() { LockWorkStation(); }

        // Recorte de pantalla de Windows (lo mismo que Win + Shift + S)
        public static void Screenshot()
        {
            try { System.Diagnostics.Process.Start("ms-screenclip:"); } catch (Exception) { }
        }

        public static void Run(Action action)
        {
            if (action != null) action();
        }
    }

    /// <summary>
    /// Tarjeta «Herramientas» del carrusel de la isla (mockup D aprobado): pestañas chiquitas por categoría
    /// (Productividad, Celular, Sistema) y tres botones grandes en cada una, con su atajo escrito debajo.
    /// </summary>
    internal class ToolsView : IslandView
    {
        private struct Tool
        {
            public string Glyph, Name, Keys;
            public Func<Action> Action;
        }

        private static readonly string[] Categories = { "Productividad", "Celular", "Sistema" };
        private static readonly Tool[][] Tools =
        {
            new[]
            {
                new Tool { Glyph = "\u2733", Name = "Claude", Keys = "Ctrl+Alt+C", Action = () => IslandTools.OpenClaude },
                new Tool { Glyph = "\uE77F", Name = "Portapapeles", Keys = "Ctrl+Alt+V", Action = () => IslandTools.OpenClipboard },
                new Tool { Glyph = "\uE8EF", Name = "Calcular", Keys = "En la isla", Action = () => IslandTools.OpenCalculator }
            },
            new[]
            {
                new Tool { Glyph = "\uE8EA", Name = "Recibir", Keys = "Ctrl+Alt+R", Action = () => IslandTools.OpenPhone },
                new Tool { Glyph = "\uE724", Name = "Enviar", Keys = "Lo copiado", Action = () => IslandTools.SendToPhone },
                new Tool { Glyph = "\uEB9F", Name = "Foto a la PC", Keys = "Desde el celular", Action = () => IslandTools.OpenPhone }
            },
            new[]
            {
                new Tool { Glyph = "\uE720", Name = "Micrófono", Keys = "Ctrl+Alt+M", Action = () => IslandTools.ToggleMic },
                new Tool { Glyph = "\uE7C5", Name = "Captura", Keys = "Win+Shift+S", Action = () => (Action)IslandTools.Screenshot },
                new Tool { Glyph = "\uE72E", Name = "Bloquear", Keys = "Win+L", Action = () => (Action)IslandTools.Lock }
            }
        };

        private int _category;
        private readonly RectangleF[] _tabs = new RectangleF[3];
        private readonly RectangleF[] _buttons = new RectangleF[3];

        public override string TabName { get { return "Herramientas"; } }
        public override string TabGlyph { get { return "\uE90F"; } }

        public override SizeF BaseSize
        {
            get { return new SizeF(380, 126); }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            float x = p.Px(PadX), width = size.Width - 2 * p.Px(PadX), gap = p.Px(8);
            Color ink = Color.FromArgb(alpha, IslandPainter.InkBase), gray = IslandPainter.Fade(IslandPainter.Gray, alpha);

            // Pestañas chiquitas
            float tabW = (width - 2 * p.Px(6)) / 3;
            for (int i = 0; i < 3; i++)
            {
                _tabs[i] = new RectangleF(x + i * (tabW + p.Px(6)), 0, tabW, p.Px(24));
                bool on = i == _category;
                IslandPainter.FillRounded(g, _tabs[i], _tabs[i].Height / 2, on ? Color.FromArgb(alpha, IslandPainter.InkBase) : Color.FromArgb(alpha * 22 / 255, IslandPainter.InkBase));
                p.DrawText(g, Categories[i], p.Bold12, on ? Color.FromArgb(alpha, IslandPainter.InkContrast) : gray, _tabs[i], StringAlignment.Center);
            }

            // Tres botones grandes
            float top = p.Px(32), buttonW = (width - 2 * gap) / 3, buttonH = size.Height - top;
            for (int i = 0; i < 3; i++)
            {
                Tool tool = Tools[_category][i];
                RectangleF b = new RectangleF(x + i * (buttonW + gap), top, buttonW, buttonH);
                _buttons[i] = b;
                IslandPainter.FillRounded(g, b, p.Px(16), Color.FromArgb(alpha * 22 / 255, IslandPainter.InkBase));
                p.DrawText(g, tool.Glyph, tool.Glyph == "\u2733" ? p.Bold18 : p.Glyph20, ink, new RectangleF(b.X, b.Y + p.Px(10), b.Width, p.Px(28)), StringAlignment.Center);
                p.DrawText(g, tool.Name, p.Bold12, ink, new RectangleF(b.X + p.Px(4), b.Y + p.Px(42), b.Width - p.Px(8), p.Px(16)), StringAlignment.Center);
                p.DrawText(g, tool.Keys, p.Text11, gray, new RectangleF(b.X + p.Px(4), b.Y + p.Px(58), b.Width - p.Px(8), p.Px(15)), StringAlignment.Center);
            }
        }

        public override bool OnClick(PointF point)
        {
            for (int i = 0; i < 3; i++)
            {
                if (_tabs[i].Contains(point)) { _category = i; return true; }
                if (_buttons[i].Contains(point)) { IslandTools.Run(Tools[_category][i].Action()); return true; }
            }
            return false;
        }

        public override bool OnWheel(PointF point, int delta)
        {
            // La rueda sobre las pestañas cambia de categoría; en el resto, de vista
            if (point.Y > _tabs[0].Bottom) return false;
            _category = (_category + (delta > 0 ? 2 : 1)) % 3;
            return true;
        }
    }

    /// <summary>
    /// Calculadora rápida: baja desde la isla, escribes la cuenta y Enter da el resultado (y lo copia).
    /// </summary>
    internal class CalculatorWindow : Form
    {
        private readonly TextBox _input;
        private readonly float _scale;
        private string _result = "", _history = "";
        private DateTime _shownAt;
        private readonly Font _big = new Font("Segoe UI Semibold", 20f);
        private readonly Font _small = new Font("Segoe UI", 9f);

        public CalculatorWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.Black;
            DoubleBuffered = true;
            KeyPreview = true;
            using (Graphics g = CreateGraphics()) _scale = g.DpiX / 96f;
            _input = new TextBox { BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(22, 24, 29), ForeColor = Color.White, Font = new Font("Segoe UI", 13f) };
            _input.TextChanged += (s, e) => { _result = Evaluate(_input.Text) ?? ""; Invalidate(); };
            Controls.Add(_input);
        }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= 0x80; return cp; }
        }

        public void Toggle()
        {
            if (Visible) { Hide(); return; }
            Rectangle island = IslandEyes.IslandRect;
            Screen screen = island.Width > 0 ? Screen.FromRectangle(island) : Screen.PrimaryScreen;
            int w = (int)(360 * _scale), h = (int)(150 * _scale);
            int cx = island.Width > 0 ? island.Left + island.Width / 2 : screen.Bounds.Left + screen.Bounds.Width / 2;
            Bounds = new Rectangle(Math.Max(screen.Bounds.Left, Math.Min(screen.Bounds.Right - w, cx - w / 2)), screen.Bounds.Top, w, h);
            using (GraphicsPath shape = new GraphicsPath())
            {
                float d = 52 * _scale;
                shape.AddLine(0, 0, w, 0);
                shape.AddArc(w - d, h - d, d, d, 0, 90);
                shape.AddArc(0, h - d, d, d, 90, 90);
                shape.CloseFigure();
                Region = new Region(shape);
            }
            _input.Bounds = new Rectangle((int)(22 * _scale), (int)(20 * _scale), w - (int)(44 * _scale), (int)(28 * _scale));
            Show();
            Activate();
            _input.Focus();
            _input.SelectAll();
            _shownAt = DateTime.Now;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (Visible && (DateTime.Now - _shownAt).TotalMilliseconds > 300) Hide();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Hide(); return true; }
            if (keyData == Keys.Enter && _result.Length > 0)
            {
                // Enter: el resultado queda en la caja para seguir calculando, y en el portapapeles
                try { Clipboard.SetText(_result); } catch (Exception) { }
                _history = _input.Text + " = " + _result + "  ·  copiado";
                _input.Text = _result;
                _input.SelectionStart = _input.Text.Length;
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private static string Evaluate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string expr = text.Replace("×", "*").Replace("x", "*").Replace("÷", "/").Replace(",", ".").Replace("%", "/100");
            foreach (char c in expr) if (!"0123456789.+-*/() ".Contains(c.ToString())) return null;
            try
            {
                object value = new System.Data.DataTable().Compute(expr, null);
                double number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                if (double.IsInfinity(number) || double.IsNaN(number)) return null;
                return Math.Round(number, 10).ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Black);
            IslandPainter.FillRounded(g, new RectangleF(12 * _scale, 12 * _scale, Width - 24 * _scale, 44 * _scale), 12 * _scale, Color.FromArgb(22, 24, 29));
            TextRenderer.DrawText(g, _result.Length > 0 ? "= " + _result : "Escribe una cuenta: 12*8+5", _result.Length > 0 ? _big : _small,
                Rectangle.Round(new RectangleF(22 * _scale, 62 * _scale, Width - 44 * _scale, 40 * _scale)),
                _result.Length > 0 ? Color.White : Color.FromArgb(139, 146, 163), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, _history.Length > 0 ? _history : "Enter copia el resultado · Esc cierra", _small,
                Rectangle.Round(new RectangleF(22 * _scale, 104 * _scale, Width - 44 * _scale, 22 * _scale)), Color.FromArgb(139, 146, 163), TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
        }
    }
    /// <summary>
    /// Novedades de la versión: se muestra una vez después de actualizar, con botones para probar cada cosa.
    /// </summary>
    internal class WhatsNewWindow : Form
    {
        private struct Item
        {
            public string Glyph, Title, Detail, Button;
            public Action Action;
        }

        private readonly float _scale;
        private readonly List<Item> _items = new List<Item>();
        private readonly List<KeyValuePair<RectangleF, Action>> _hits = new List<KeyValuePair<RectangleF, Action>>();
        private readonly Font _title = new Font("Segoe UI Semibold", 15f);
        private readonly Font _bold = new Font("Segoe UI Semibold", 10f);
        private readonly Font _text = new Font("Segoe UI", 9f);
        private readonly Font _glyph = new Font("Segoe Fluent Icons", 14f);
        private const float W = 560, Pad = 22, Row = 58;

        /// <summary>
        /// Si la versión cambió desde la última vez, muestra las novedades (una sola vez).
        /// </summary>
        public static void ShowIfUpdated()
        {
            if (SuiteSettings.LastSeenVersion == AppInfo.Version) return;
            SuiteSettings.LastSeenVersion = AppInfo.Version;
            SuiteSettings.Save();
            new WhatsNewWindow().Show();
        }

        public WhatsNewWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = true;
            BackColor = Color.FromArgb(13, 15, 20);
            DoubleBuffered = true;
            Text = "Novedades de Streeck Suite " + AppInfo.Version;
            using (Graphics g = CreateGraphics()) _scale = g.DpiX / 96f;

            Add("", "Reloj 3D: color negro y personalizado", "Elige cualquier color para los números en el estilo Contorno", "Elegir", () => { SettingsWindow.Open(SettingsWindow.Page.Clock); Close(); });
            Add("", "Herramientas en la isla", "Una tarjeta del carrusel con todo a la mano: Claude, celular, captura, calculadora…", null, null);
            Add("", "Apariencia: 16 skins", "Grafito, Papel, Atardecer, Terminal, Hielo y Oro; forma redondeada, letra, borde y cristal", "Elegir",
                () => { SettingsWindow.Open(SettingsWindow.Page.Appearance); Close(); });
            Add("✳", "Claude en la isla", "Pregúntale a Claude con tu cuenta, sin pagar API · Ctrl + Alt + C", "Abrir", () => { Close(); IslandTools.Run(IslandTools.OpenClaude); });
            Add("", "Historial del portapapeles", "Lo último que copiaste, con buscador y fijados · Ctrl + Alt + V", "Abrir", () => { Close(); IslandTools.Run(IslandTools.OpenClipboard); });
            Add("", "Enviar y recibir con el celular", "Por QR y tu Wi-Fi, sin internet ni apps · Ctrl + Alt + R", "Probar", () => { Close(); IslandTools.Run(IslandTools.OpenPhone); });
            Add("", "Controles rápidos nuevos", "Silenciar micrófono (Ctrl + Alt + M), luz nocturna y modo de energía", null, null);
            Add("", "Widgets nuevos", "Clima, agenda de hoy, música con botones, equipo y cuenta regresiva", "Elegir",
                () => { SettingsWindow.Open(SettingsWindow.Page.Widgets); Close(); });
            Add("", "10 ojitos nuevos y junto a los ojitos", "Fantasma, Kawaii, Pirata… y clima, hora o cronómetro al lado", "Elegir",
                () => { SettingsWindow.Open(SettingsWindow.Page.Eyes); Close(); });
            Add("", "Avisos de agentes de IA", "La isla te dice cuando Claude Code termina o pide permiso", "Conectar",
                () => { SettingsWindow.Open(SettingsWindow.Page.Assistant); Close(); });

            int w = (int)Px(W), h = (int)Px(Pad + 70 + _items.Count * Row + 64);
            Size = new Size(w, h);
            using (GraphicsPath shape = RoundedRect(new RectangleF(0, 0, w, h), Px(18))) Region = new Region(shape);
        }

        private void Add(string glyph, string title, string detail, string button, Action action)
        {
            _items.Add(new Item { Glyph = glyph, Title = title, Detail = detail, Button = button, Action = action });
        }

        private float Px(float v) { return v * _scale; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { Close(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            foreach (KeyValuePair<RectangleF, Action> hit in _hits)
                if (hit.Key.Contains(e.Location)) { hit.Value(); return; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool over = false;
            foreach (KeyValuePair<RectangleF, Action> hit in _hits) if (hit.Key.Contains(e.Location)) over = true;
            Cursor = over ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            _hits.Clear();
            Color gray = Color.FromArgb(154, 164, 181), card = Color.FromArgb(24, 27, 35), accent = Color.FromArgb(56, 189, 248);

            IslandEyes.DrawPreview(g, new PointF(Px(Pad + 26), Px(Pad + 22)), 0.7f * _scale, IslandEyes.Mood.Idle);
            TextRenderer.DrawText(g, "Novedades de la " + AppInfo.Version, _title, new Point((int)Px(Pad + 68), (int)Px(Pad + 2)), Color.White);
            TextRenderer.DrawText(g, "Esto es lo nuevo de Streeck Suite. Pruébalo desde aquí:", _text, new Point((int)Px(Pad + 70), (int)Px(Pad + 30)), gray);

            float y = Px(Pad + 70);
            foreach (Item item in _items)
            {
                RectangleF r = new RectangleF(Px(Pad), y, Width - 2 * Px(Pad), Px(Row - 8));
                IslandPainter.FillRounded(g, r, Px(12), card);
                TextRenderer.DrawText(g, item.Glyph, _glyph, Rectangle.Round(new RectangleF(r.X + Px(8), r.Y, Px(36), r.Height)), accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                float textW = r.Width - Px(52) - (item.Button != null ? Px(96) : Px(12));
                TextRenderer.DrawText(g, item.Title, _bold, Rectangle.Round(new RectangleF(r.X + Px(50), r.Y + Px(6), textW, Px(20))), Color.White, TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, item.Detail, _text, Rectangle.Round(new RectangleF(r.X + Px(50), r.Y + Px(26), textW, Px(18))), gray, TextFormatFlags.EndEllipsis);
                if (item.Button != null)
                {
                    RectangleF b = new RectangleF(r.Right - Px(90), r.Y + (r.Height - Px(30)) / 2, Px(80), Px(30));
                    IslandPainter.FillRounded(g, b, Px(9), Color.FromArgb(40, 56, 189, 248));
                    TextRenderer.DrawText(g, item.Button, _bold, Rectangle.Round(b), accent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    _hits.Add(new KeyValuePair<RectangleF, Action>(b, item.Action));
                }
                y += Px(Row);
            }

            RectangleF close = new RectangleF(Width - Px(Pad) - Px(120), Height - Px(Pad) - Px(34), Px(120), Px(34));
            IslandPainter.FillRounded(g, close, Px(10), Color.White);
            TextRenderer.DrawText(g, "¡Listo!", _bold, Rectangle.Round(close), Color.FromArgb(13, 15, 20), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            _hits.Add(new KeyValuePair<RectangleF, Action>(close, Close));
            TextRenderer.DrawText(g, "Las encuentras siempre en Ajustes › Herramientas", _text, new Point((int)Px(Pad), (int)(close.Y + Px(8))), gray);
        }

        private static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            GraphicsPath p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
