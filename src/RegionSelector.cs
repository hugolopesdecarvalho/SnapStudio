using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SnapStudio
{
    /// <summary>
    /// Modo captura: mostra o ecrã congelado e escurecido.
    /// Clicar numa janela captura-a; arrastar captura uma região; barra no topo para ecrã inteiro.
    /// </summary>
    class RegionSelector : Form
    {
        readonly Bitmap original;      // 24bpp, coordenadas = cliente
        readonly Bitmap shot, dark;    // versões PArgb para desenho rápido
        readonly List<Rectangle> windows = new List<Rectangle>();
        readonly Rectangle vs;
        readonly float dpi;
        readonly Font uiFont, smallFont;
        static Color Accent { get { return Theme.Accent; } }

        bool mouseDown, dragging;
        Point start, mouse;
        Rectangle sel, hover, lastDirty;

        // barra de topo
        Rectangle barRect;
        readonly List<BarButton> buttons = new List<BarButton>();
        string hintText;
        Rectangle hintRect;
        int hotButton = -1;

        class BarButton { public string Text; public Rectangle Rect; public Action Click; }

        public Rectangle Selection;

        public RegionSelector(Bitmap screenshot, List<Rectangle> screenWindows, Rectangle virtualScreen)
        {
            original = screenshot;
            vs = virtualScreen;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            Cursor = Cursors.Cross;
            Bounds = vs;
            Text = "SnapStudio";

            using (var g = CreateGraphics()) dpi = g.DpiX / 96f;
            uiFont = new Font("Segoe UI", 13f * dpi, FontStyle.Regular, GraphicsUnit.Pixel);
            smallFont = new Font("Segoe UI", 11f * dpi, FontStyle.Regular, GraphicsUnit.Pixel);

            shot = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppPArgb);
            dark = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(shot)) g.DrawImage(original, new Rectangle(0, 0, original.Width, original.Height));
            using (var g = Graphics.FromImage(dark))
            {
                g.DrawImage(shot, new Rectangle(0, 0, shot.Width, shot.Height));
                using (var b = new SolidBrush(Color.FromArgb(120, 0, 0, 0))) g.FillRectangle(b, 0, 0, dark.Width, dark.Height);
            }

            var client = new Rectangle(0, 0, vs.Width, vs.Height);
            foreach (var w in screenWindows)
            {
                var r = w; r.Offset(-vs.X, -vs.Y); r.Intersect(client);
                if (r.Width > 0 && r.Height > 0) windows.Add(r);
            }

            BuildBar();
        }

        Rectangle ToClient(Rectangle screenRect) { var r = screenRect; r.Offset(-vs.X, -vs.Y); return r; }

        void BuildBar()
        {
            Screen scr = Screen.FromPoint(Cursor.Position);
            Rectangle mon = ToClient(scr.Bounds);
            hintText = S.T("Clique numa janela ou arraste para selecionar uma região", "Click a window or drag to select a region");

            buttons.Add(new BarButton { Text = S.T("Ecrã inteiro  (Enter)", "Full screen  (Enter)"), Click = CaptureCurrentMonitor });
            if (Screen.AllScreens.Length > 1)
                buttons.Add(new BarButton { Text = S.T("Todos os ecrãs  (A)", "All screens  (A)"), Click = CaptureAllScreens });
            buttons.Add(new BarButton { Text = S.T("Cancelar  (Esc)", "Cancel  (Esc)"), Click = Cancel });

            int pad = (int)(10 * dpi), gap = (int)(6 * dpi), h = (int)(34 * dpi);
            Size hs = TextRenderer.MeasureText(hintText, uiFont);
            int x = pad;
            hintRect = new Rectangle(x, 0, hs.Width, h);
            x += hs.Width + pad;
            foreach (var b in buttons)
            {
                Size s = TextRenderer.MeasureText(b.Text, uiFont);
                b.Rect = new Rectangle(x, 0, s.Width + 2 * pad, h - (int)(8 * dpi));
                x += b.Rect.Width + gap;
            }
            int barW = x - gap + pad, barH = h + (int)(8 * dpi);
            barRect = new Rectangle(mon.X + (mon.Width - barW) / 2, mon.Y + (int)(16 * dpi), barW, barH);
            int cy = barRect.Y + (int)(4 * dpi);
            hintRect.Offset(barRect.X, cy);
            foreach (var b in buttons) b.Rect.Offset(barRect.X, cy + (int)(4 * dpi));
        }

        void CaptureCurrentMonitor()
        {
            Selection = ToClient(Screen.FromPoint(new Point(mouse.X + vs.X, mouse.Y + vs.Y)).Bounds);
            DialogResult = DialogResult.OK;
        }

        void CaptureAllScreens()
        {
            Selection = new Rectangle(0, 0, vs.Width, vs.Height);
            DialogResult = DialogResult.OK;
        }

        void Cancel() { DialogResult = DialogResult.Cancel; }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Bounds = vs;
            Activate();
            Native.SetForegroundWindow(Handle);
            Point p = PointToClient(Cursor.Position);
            mouse = p;
            hover = WindowAt(p);
            Invalidate();
        }

        Rectangle WindowAt(Point p)
        {
            foreach (var w in windows) if (w.Contains(p)) return w;
            return ToClient(Screen.FromPoint(new Point(p.X + vs.X, p.Y + vs.Y)).Bounds);
        }

        int ButtonAt(Point p)
        {
            for (int i = 0; i < buttons.Count; i++) if (buttons[i].Rect.Contains(p)) return i;
            return -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                if (dragging) { dragging = false; mouseDown = false; hover = WindowAt(e.Location); Invalidate(); }
                else Cancel();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            int b = ButtonAt(e.Location);
            if (b >= 0) { buttons[b].Click(); return; }
            mouseDown = true; dragging = false; start = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            mouse = e.Location;
            if (mouseDown && !dragging && (Math.Abs(e.X - start.X) > 3 || Math.Abs(e.Y - start.Y) > 3)) dragging = true;
            if (dragging)
                sel = Rectangle.FromLTRB(Math.Min(start.X, e.X), Math.Min(start.Y, e.Y), Math.Max(start.X, e.X) + 1, Math.Max(start.Y, e.Y) + 1);
            else
                hover = WindowAt(e.Location);

            int hb = mouseDown ? -1 : ButtonAt(e.Location);
            if (hb != hotButton) { hotButton = hb; Invalidate(barRect); }
            Cursor = hb >= 0 ? Cursors.Hand : Cursors.Cross;
            InvalidateDirty();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || !mouseDown) return;
            mouseDown = false;
            Rectangle r = dragging ? sel : hover;
            dragging = false;
            if (r.Width >= 2 && r.Height >= 2) { Selection = r; DialogResult = DialogResult.OK; }
            else Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Escape: Cancel(); return true;
                case Keys.Enter: case Keys.Space: case Keys.F: CaptureCurrentMonitor(); return true;
                case Keys.A: CaptureAllScreens(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- desenho ----------

        Rectangle MagnifierRect()
        {
            int size = (int)(120 * dpi), info = (int)(44 * dpi), off = (int)(24 * dpi);
            Rectangle mon = ToClient(Screen.FromPoint(new Point(mouse.X + vs.X, mouse.Y + vs.Y)).Bounds);
            int x = mouse.X + off, y = mouse.Y + off;
            if (x + size > mon.Right) x = mouse.X - off - size;
            if (y + size + info > mon.Bottom) y = mouse.Y - off - size - info;
            return new Rectangle(x, y, size, size + info);
        }

        Rectangle CurrentDirty()
        {
            Rectangle hi = dragging ? sel : hover;
            Rectangle d = MagnifierRect();
            if (hi.Width > 0) { var h2 = hi; h2.Inflate((int)(4 * dpi), (int)(30 * dpi)); d = Rectangle.Union(d, h2); }
            d.Inflate(2, 2);
            return d;
        }

        void InvalidateDirty()
        {
            Rectangle d = CurrentDirty();
            Invalidate(lastDirty.IsEmpty ? d : Rectangle.Union(lastDirty, d));
            lastDirty = d;
        }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle clip = e.ClipRectangle;
            g.DrawImage(dark, clip, clip, GraphicsUnit.Pixel);

            Rectangle hi = dragging ? sel : hover;
            if (hi.Width > 0 && hi.Height > 0)
            {
                Rectangle r = Rectangle.Intersect(hi, clip);
                if (r.Width > 0 && r.Height > 0) g.DrawImage(shot, r, r, GraphicsUnit.Pixel);
                using (var p = new Pen(Accent, Math.Max(2f, 2f * dpi)))
                    g.DrawRectangle(p, hi.X + 1, hi.Y + 1, hi.Width - 2, hi.Height - 2);
                DrawSizeLabel(g, hi);
            }

            DrawBar(g);
            if (!mouseDown || dragging) DrawMagnifier(g);
        }

        void DrawSizeLabel(Graphics g, Rectangle hi)
        {
            string t = hi.Width + " × " + hi.Height;
            Size s = TextRenderer.MeasureText(t, smallFont);
            int pad = (int)(4 * dpi);
            var box = new Rectangle(hi.X, hi.Y - s.Height - 2 * pad, s.Width + 2 * pad, s.Height + pad);
            if (box.Y < 0) box.Y = hi.Y + pad;
            using (var b = new SolidBrush(Color.FromArgb(220, 20, 20, 24))) g.FillRectangle(b, box);
            TextRenderer.DrawText(g, t, smallFont, box, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        void DrawBar(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Rounded(barRect, (int)(8 * dpi)))
            using (var b = new SolidBrush(Color.FromArgb(235, 28, 28, 32)))
                g.FillPath(b, path);
            TextRenderer.DrawText(g, hintText, uiFont, hintRect, Color.FromArgb(230, 230, 230), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            for (int i = 0; i < buttons.Count; i++)
            {
                var bt = buttons[i];
                Color bg = i == hotButton ? Accent : Color.FromArgb(60, 60, 68);
                using (var path = Rounded(bt.Rect, (int)(5 * dpi)))
                using (var b = new SolidBrush(bg))
                    g.FillPath(b, path);
                TextRenderer.DrawText(g, bt.Text, uiFont, bt.Rect, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            g.SmoothingMode = SmoothingMode.None;
        }

        void DrawMagnifier(Graphics g)
        {
            Rectangle box = MagnifierRect();
            int size = box.Width, cells = 15;
            var dest = new Rectangle(box.X, box.Y, size, size);
            var src = new Rectangle(mouse.X - cells / 2, mouse.Y - cells / 2, cells, cells);
            g.FillRectangle(Brushes.Black, box);
            var st = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.SetClip(dest);
            g.DrawImage(shot, dest, src, GraphicsUnit.Pixel);
            g.Restore(st);
            float cell = size / (float)cells;
            using (var p = new Pen(Color.FromArgb(160, Accent), 1))
            {
                g.DrawLine(p, dest.X, dest.Y + size / 2f, dest.Right, dest.Y + size / 2f);
                g.DrawLine(p, dest.X + size / 2f, dest.Y, dest.X + size / 2f, dest.Bottom);
            }
            using (var p = new Pen(Color.White, 1))
                g.DrawRectangle(p, dest.X + (cells / 2) * cell, dest.Y + (cells / 2) * cell, cell, cell);

            Color c = Color.Black;
            if (mouse.X >= 0 && mouse.Y >= 0 && mouse.X < original.Width && mouse.Y < original.Height)
                c = original.GetPixel(mouse.X, mouse.Y);
            string l1 = "X " + (mouse.X + vs.X) + "   Y " + (mouse.Y + vs.Y);
            string l2 = "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            var info = new Rectangle(box.X, dest.Bottom, size, box.Height - size);
            TextRenderer.DrawText(g, l1 + "\n" + l2, smallFont, info, Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            using (var p = new Pen(Color.FromArgb(200, 255, 255, 255), 1))
                g.DrawRectangle(p, box.X, box.Y, box.Width - 1, box.Height - 1);
        }

        static GraphicsPath Rounded(Rectangle r, int rad)
        {
            var p = new GraphicsPath();
            int d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                shot.Dispose(); dark.Dispose(); uiFont.Dispose(); smallFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
