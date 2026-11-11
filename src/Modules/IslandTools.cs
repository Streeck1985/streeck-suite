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
        public static Action OpenClaude, OpenClipboard, OpenPhone, ToggleMic;

        public static void Run(Action action)
        {
            if (action != null) action();
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
