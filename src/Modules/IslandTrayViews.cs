using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using StreeckSuite.Core;

namespace StreeckSuite.Modules
{
    /// <summary>
    /// Pestaña Bandeja (mockup E1): hasta 4 archivos a la vista (la rueda pasa al resto) con su miniatura, nombre
    /// y tamaño. Clic abre, ✕ quita de la bandeja, «Vaciar» la deja vacía; arrastrar un archivo fuera lo copia
    /// donde se suelte (eso lo gestiona la isla, que es la ventana que recibe el ratón).
    /// </summary>
    internal class TrayView : IslandView
    {
        private const int VisibleFiles = 4;
        private const float CellWidth = 92, CellGap = 8, ThumbSize = 64;

        private int _offset;
        private readonly List<KeyValuePair<RectangleF, TrayFile>> _cells = new List<KeyValuePair<RectangleF, TrayFile>>();
        private readonly List<KeyValuePair<RectangleF, TrayFile>> _removeButtons = new List<KeyValuePair<RectangleF, TrayFile>>();
        private RectangleF _clearButton;

        public override string TabName { get { return "Bandeja"; } }
        public override string TabGlyph { get { return ""; } }

        public override bool IsAvailable
        {
            get { return SuiteSettings.FileTrayEnabled; }
        }

        public override SizeF BaseSize
        {
            get { return FileTrayService.Files.Count == 0 ? new SizeF(440, 56) : new SizeF(440, 132); }
        }

        public override void OnShown()
        {
            _offset = 0;
        }

        /// <summary>
        /// Archivo bajo el punto (coordenadas de la vista), para arrastrarlo fuera.
        /// </summary>
        public TrayFile FileAt(PointF point)
        {
            foreach (KeyValuePair<RectangleF, TrayFile> cell in _cells)
            {
                if (cell.Key.Contains(point)) return cell.Value;
            }
            return null;
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            float x = p.Px(PadX), width = size.Width - 2 * x;
            Color white = Color.FromArgb(alpha, Color.White);
            Color legend = IslandPainter.Fade(Color.FromArgb(139, 147, 163), alpha);
            _cells.Clear();
            _removeButtons.Clear();
            _clearButton = RectangleF.Empty;

            List<TrayFile> files = FileTrayService.Files;
            if (files.Count == 0)
            {
                p.DrawText(g, "Arrastra archivos hasta la isla para tenerlos a mano", p.Text13, IslandPainter.Fade(IslandPainter.Gray, alpha),
                    new RectangleF(x, 0, width, p.Px(40)), StringAlignment.Center);
                return;
            }

            _offset = Math.Max(0, Math.Min(_offset, files.Count - VisibleFiles));
            int shown = Math.Min(VisibleFiles, files.Count - _offset);
            float cellX = x;
            for (int i = 0; i < shown; i++)
            {
                TrayFile file = files[_offset + i];
                RectangleF cell = new RectangleF(cellX, 0, p.Px(CellWidth), p.Px(104));
                RectangleF thumb = new RectangleF(cell.X + (cell.Width - p.Px(ThumbSize)) / 2, p.Px(6), p.Px(ThumbSize), p.Px(ThumbSize));
                if (file.Thumbnail != null)
                {
                    // Miniatura dentro del cuadro sin deformarla (las fotos no son cuadradas)
                    float scale = Math.Min(thumb.Width / file.Thumbnail.Width, thumb.Height / file.Thumbnail.Height);
                    SizeF fitted = new SizeF(file.Thumbnail.Width * scale, file.Thumbnail.Height * scale);
                    RectangleF image = new RectangleF(thumb.X + (thumb.Width - fitted.Width) / 2, thumb.Y + (thumb.Height - fitted.Height) / 2, fitted.Width, fitted.Height);
                    p.DrawRoundedImage(g, file.Thumbnail, image, p.Px(10), alpha);
                }
                else
                {
                    IslandPainter.FillRounded(g, thumb, p.Px(14), Color.FromArgb(alpha * 46 / 255, IslandPainter.Sky));
                }

                RectangleF remove = new RectangleF(thumb.Right - p.Px(10), thumb.Y - p.Px(4), p.Px(18), p.Px(18));
                using (SolidBrush back = new SolidBrush(IslandPainter.Fade(IslandPainter.Track, alpha))) g.FillEllipse(back, remove);
                using (Pen cross = new Pen(IslandPainter.Fade(Color.FromArgb(195, 201, 212), alpha), p.Px(1.4f)))
                {
                    float c = p.Px(4);
                    PointF center = new PointF(remove.X + remove.Width / 2, remove.Y + remove.Height / 2);
                    g.DrawLine(cross, center.X - c, center.Y - c, center.X + c, center.Y + c);
                    g.DrawLine(cross, center.X + c, center.Y - c, center.X - c, center.Y + c);
                }

                p.DrawText(g, file.Name, p.Text12, white, new RectangleF(cell.X, p.Px(74), cell.Width, p.Px(16)), StringAlignment.Center);
                p.DrawText(g, FileTrayService.SizeText(file), p.Text11, legend, new RectangleF(cell.X, p.Px(90), cell.Width, p.Px(14)), StringAlignment.Center);

                _cells.Add(new KeyValuePair<RectangleF, TrayFile>(cell, file));
                _removeButtons.Add(new KeyValuePair<RectangleF, TrayFile>(RectangleF.Inflate(remove, p.Px(3), p.Px(3)), file));
                cellX = cell.Right + p.Px(CellGap);
            }

            // Hueco para soltar más mientras quede sitio en la fila
            if (shown < VisibleFiles)
            {
                RectangleF thumb = new RectangleF(cellX + (p.Px(CellWidth) - p.Px(ThumbSize)) / 2, p.Px(6), p.Px(ThumbSize), p.Px(ThumbSize));
                using (GraphicsPath path = IslandPainter.RoundedRect(thumb, p.Px(14)))
                using (Pen dashed = new Pen(Color.FromArgb(alpha * 64 / 255, Color.White), p.Px(1)))
                {
                    dashed.DashPattern = new float[] { 3, 3 };
                    g.DrawPath(dashed, path);
                }
                p.DrawText(g, "+", p.Bold18, legend, thumb, StringAlignment.Center);
                p.DrawText(g, "Suelta más", p.Text12, legend, new RectangleF(cellX, p.Px(74), p.Px(CellWidth), p.Px(16)), StringAlignment.Center);
            }

            float footerY = p.Px(112);
            string hint = files.Count > VisibleFiles
                ? string.Format("{0}-{1} de {2} · rueda para ver más", _offset + 1, _offset + shown, files.Count)
                : "Arrastra un archivo fuera para soltarlo en otra ventana";
            p.DrawText(g, hint, p.Text12, legend, new RectangleF(x, footerY, width - p.Px(70), p.Px(18)), StringAlignment.Near);
            SizeF clearSize = g.MeasureString("Vaciar", p.Bold12);
            _clearButton = new RectangleF(x + width - clearSize.Width, footerY, clearSize.Width, p.Px(18));
            p.DrawText(g, "Vaciar", p.Bold12, IslandPainter.Fade(Color.FromArgb(125, 211, 252), alpha), _clearButton, StringAlignment.Far);
        }

        public override bool OnClick(PointF point)
        {
            if (_clearButton.Contains(point)) { FileTrayService.Clear(); return true; }
            foreach (KeyValuePair<RectangleF, TrayFile> remove in _removeButtons)
            {
                if (remove.Key.Contains(point)) { FileTrayService.Remove(remove.Value); return true; }
            }
            TrayFile file = FileAt(point);
            if (file != null) { FileTrayService.Open(file); return true; }
            return false;
        }

        public override bool OnWheel(PointF point, int delta)
        {
            int count = FileTrayService.Files.Count;
            int next = Math.Max(0, Math.Min(count - VisibleFiles, _offset + (delta < 0 ? 1 : -1)));
            if (next == _offset) return false;
            _offset = next;
            return true;
        }
    }

    /// <summary>
    /// Zona para soltar archivos (mockup E1): aparece mientras se arrastran archivos sobre la isla.
    /// </summary>
    internal class DropZoneView : IslandView
    {
        private const string GlyphDownload = "";
        public int Count;

        public override SizeF BaseSize
        {
            get { return new SizeF(440, 110); }
        }

        public override Color? BorderColor
        {
            get { return Color.FromArgb(153, IslandPainter.Sky); }
        }

        public override void Draw(Graphics g, SizeF size, int alpha, IslandPainter p)
        {
            RectangleF zone = RectangleF.Inflate(new RectangleF(0, 0, size.Width, size.Height), -p.Px(14), -p.Px(14));
            Color link = Color.FromArgb(125, 211, 252);
            using (GraphicsPath path = IslandPainter.RoundedRect(zone, p.Px(16)))
            using (Pen dashed = new Pen(IslandPainter.Fade(Color.FromArgb(153, link), alpha), p.Px(2)))
            {
                dashed.DashPattern = new float[] { 4, 3 };
                g.DrawPath(dashed, path);
            }

            string detail = (Count == 1 ? "1 archivo" : Count + " archivos") + " · quedan en la bandeja hasta que los saques";
            float textWidth = Math.Max(g.MeasureString("Suelta aquí para guardarlos", p.Bold15).Width, g.MeasureString(detail, p.Text12).Width);
            float left = zone.X + (zone.Width - p.Px(38) - textWidth) / 2;
            p.DrawText(g, GlyphDownload, p.Glyph20, IslandPainter.Fade(link, alpha), new RectangleF(left, zone.Y, p.Px(26), zone.Height), StringAlignment.Center);
            p.DrawText(g, "Suelta aquí para guardarlos", p.Bold15, IslandPainter.Fade(link, alpha),
                new RectangleF(left + p.Px(38), zone.Y + zone.Height / 2 - p.Px(21), textWidth + p.Px(4), p.Px(22)), StringAlignment.Near);
            p.DrawText(g, detail, p.Text12, IslandPainter.Fade(IslandPainter.Gray, alpha),
                new RectangleF(left + p.Px(38), zone.Y + zone.Height / 2 + p.Px(1), textWidth + p.Px(4), p.Px(18)), StringAlignment.Near);
        }
    }
}
