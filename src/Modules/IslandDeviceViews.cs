using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Pila de batería dibujada y su color (mockup F1): verde, ámbar por debajo del 50 % y naranja en batería baja.
    /// </summary>
    internal static class BatteryDrawing
    {
        public static Color ColorFor(int percent)
        {
            if (SuiteSettings.BluetoothLowBatteryPercent > 0 && percent <= SuiteSettings.BluetoothLowBatteryPercent) return Color.FromArgb(251, 146, 60);
            return percent < 50 ? Color.FromArgb(251, 191, 36) : Color.FromArgb(74, 222, 128);
        }

        /// <summary>
        /// Pila de 26×13 px (a 96 PPP) con el relleno proporcional y el borne a la derecha.
        /// </summary>
        public static void Draw(Graphics g, IslandPainter p, PointF topLeft, int percent, Color color, int alpha)
        {
            RectangleF body = new RectangleF(topLeft.X, topLeft.Y, p.Px(26), p.Px(13));
            Color faded = IslandPainter.Fade(color, alpha);
            using (GraphicsPath outline = IslandPainter.RoundedRect(body, p.Px(4)))
            using (Pen pen = new Pen(faded, p.Px(1.5f)))
            {
                g.DrawPath(pen, outline);
            }
            float inset = p.Px(3);
            float fullWidth = body.Width - 2 * inset;
            RectangleF fill = new RectangleF(body.X + inset, body.Y + inset, Math.Max(p.Px(2), fullWidth * Math.Max(0, Math.Min(100, percent)) / 100f), body.Height - 2 * inset);
            IslandPainter.FillRounded(g, fill, p.Px(1.5f), faded);
            IslandPainter.FillRounded(g, new RectangleF(body.Right + p.Px(1), body.Y + (body.Height - p.Px(5)) / 2, p.Px(2), p.Px(5)), p.Px(1), faded);
        }
    }

    /// <summary>
    /// Aviso de dispositivo Bluetooth (mockup F1): al conectarse o con batería baja (en naranja).
    /// </summary>
    internal class BluetoothAlertView : IslandView
    {
        private static readonly Color Low = Color.FromArgb(251, 146, 60);
        private static readonly Color LowLight = Color.FromArgb(253, 186, 116);
        private static readonly Color Link = Color.FromArgb(125, 211, 252);

        public BluetoothDevice Device;
        public bool LowBattery;

        public override SizeF BaseSize
        {
            get { return new SizeF(400, 62); }
        }

        public override Color? BorderColor
        {
            get { return Color.FromArgb(128, LowBattery ? Low : IslandPainter.Sky); }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            BluetoothDevice device = Device;
            if (device == null) return;
            float x = p.Px(PadX);
            Color main = LowBattery ? LowLight : Color.White;
            Color tileColor = LowBattery ? LowLight : Link;

            RectangleF badge = new RectangleF(x, (size.Height - p.Px(34)) / 2, p.Px(34), p.Px(34));
            IslandPainter.FillRounded(g, badge, p.Px(10), IslandPainter.Fade(Color.FromArgb(46, LowBattery ? Low : IslandPainter.Sky), alpha));
            p.DrawText(g, BluetoothService.Glyph(device.Kind), p.Glyph16, IslandPainter.Fade(tileColor, alpha), badge, StringAlignment.Center);

            // Porcentaje grande y pila a la derecha
            Color batteryColor = BatteryDrawing.ColorFor(device.Battery);
            string percent = device.Battery + " %";
            float percentWidth = g.MeasureString(percent, p.Bold18).Width;
            float batteryX = size.Width - x - p.Px(29);
            BatteryDrawing.Draw(g, p, new PointF(batteryX, (size.Height - p.Px(13)) / 2), device.Battery, batteryColor, alpha);
            float percentX = batteryX - p.Px(8) - percentWidth;
            p.DrawText(g, percent, p.Bold18, IslandPainter.Fade(main, alpha), new RectangleF(percentX, 0, percentWidth + p.Px(4), size.Height), StringAlignment.Near);

            float textX = badge.Right + p.Px(14);
            float textWidth = percentX - p.Px(10) - textX;
            p.DrawText(g, LowBattery ? "Batería baja" : device.Name, p.Bold15, IslandPainter.Fade(main, alpha),
                new RectangleF(textX, p.Px(10), textWidth, p.Px(22)), StringAlignment.Near);
            p.DrawText(g, LowBattery ? device.Name + " · cárgalo pronto" : "Conectado", p.Text12, IslandPainter.Fade(IslandPainter.Gray, alpha),
                new RectangleF(textX, p.Px(32), textWidth, p.Px(18)), StringAlignment.Near);
        }
    }
}
