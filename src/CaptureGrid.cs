using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace SnapStudio
{
    class CaptureItem
    {
        public string Path, Name, Key;
        public DateTime Time;
        public long Size;
        public bool Selected;
        public Rectangle Bounds;
    }

    /// <summary>
    /// Grelha das capturas no painel: miniaturas com scroll vertical.
    /// Clique seleciona (Ctrl junta, Shift seleciona um intervalo), arrastar desenha um retângulo
    /// de seleção a partir de espaço vazio, duplo clique abre. Arrastar uma imagem leva-a para outra app.
    /// </summary>
    class CaptureGrid : ScrollableControl
    {
        readonly float dpi;
        readonly Dictionary<string, Bitmap> thumbs;
        readonly ToolTip tip = new ToolTip();
        public List<CaptureItem> Items = new List<CaptureItem>();
        CaptureItem hoverItem, anchor, downItem;
        Point downPt;
        // seleção com o rato (retângulo), em coordenadas do conteúdo
        bool marquee;
        Point marqueeStart, marqueeEnd;
        HashSet<CaptureItem> marqueeBase = new HashSet<CaptureItem>();
        Timer marqueeScroll;
        public string EmptyText = "";
        public Func<CaptureItem, string> CaptionOf;

        public event Action<CaptureItem> OpenItem;
        public event Action<Point> MenuRequested;
        public event Action SelectionChanged;

        /// <summary>Tamanho das miniaturas geradas (são desenhadas em "cobrir" no tamanho do mosaico).</summary>
        public Size ThumbSize { get { return new Size(Px(240), Px(140)); } }

        int Px(float v) { return (int)Math.Round(v * dpi); }

        public CaptureGrid(Dictionary<string, Bitmap> thumbCache)
        {
            thumbs = thumbCache;
            dpi = Dpi.Scale;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            AutoScroll = true;
            TabStop = true;
            Theme.Changed += OnTheme;
        }

        void OnTheme() { if (!IsDisposed) { Theme.ScrollbarTheme(this); Invalidate(); } }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.ScrollbarTheme(this); }
        protected override void Dispose(bool disposing)
        {
            Theme.Changed -= OnTheme;
            if (disposing)
            {
                tip.Dispose();
                if (marqueeScroll != null) marqueeScroll.Dispose();
            }
            base.Dispose(disposing);
        }

        public void SetItems(List<CaptureItem> items)
        {
            Items = items;
            hoverItem = null;
            downItem = null;
            anchor = anchor == null ? null : items.FirstOrDefault(i => string.Equals(i.Path, anchor.Path, StringComparison.OrdinalIgnoreCase));
            LayoutItems();
            Invalidate();
        }

        public List<CaptureItem> Selected { get { return Items.Where(i => i.Selected).ToList(); } }

        public void SelectAll() { foreach (var i in Items) i.Selected = true; Invalidate(); RaiseSel(); }

        public void ClearSelection()
        {
            if (!Items.Any(i => i.Selected)) return;
            foreach (var i in Items) i.Selected = false;
            Invalidate(); RaiseSel();
        }

        void RaiseSel() { var h = SelectionChanged; if (h != null) h(); }

        void LayoutItems()
        {
            int pad = Px(7), gap = Px(8);
            int avail = Math.Max(Px(100), ClientSize.Width - 2 * pad);
            int cols = Math.Max(2, (avail + gap) / (Px(120) + gap));
            int tw = (avail - gap * (cols - 1)) / cols;
            int th = (int)(tw * 0.58f) + Px(24);
            int top = Px(2);
            for (int i = 0; i < Items.Count; i++)
                Items[i].Bounds = new Rectangle(pad + (i % cols) * (tw + gap), top + (i / cols) * (th + gap), tw, th);
            int rows = (Items.Count + cols - 1) / cols;
            AutoScrollMinSize = new Size(0, top + rows * (th + gap) + Px(4));
        }

        int Columns()
        {
            if (Items.Count < 2) return 1;
            int y0 = Items[0].Bounds.Y;
            return Math.Max(1, Items.TakeWhile(i => i.Bounds.Y == y0).Count());
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); LayoutItems(); Invalidate(); }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(); }

        Point ToContent(Point p) { return new Point(p.X - AutoScrollPosition.X, p.Y - AutoScrollPosition.Y); }

        CaptureItem ItemAt(Point content)
        {
            foreach (var it in Items) if (it.Bounds.Contains(content)) return it;
            return null;
        }

        // ================= rato =================

        Rectangle MarqueeRect()
        {
            return Rectangle.FromLTRB(Math.Min(marqueeStart.X, marqueeEnd.X), Math.Min(marqueeStart.Y, marqueeEnd.Y),
                                      Math.Max(marqueeStart.X, marqueeEnd.X), Math.Max(marqueeStart.Y, marqueeEnd.Y));
        }

        void BeginMarquee(Point contentStart, bool add)
        {
            marqueeBase = add ? new HashSet<CaptureItem>(Items.Where(i => i.Selected)) : new HashSet<CaptureItem>();
            if (!add) { foreach (var i in Items) i.Selected = false; RaiseSel(); }
            marquee = true;
            marqueeStart = marqueeEnd = contentStart;
            Capture = true;
            if (marqueeScroll == null) { marqueeScroll = new Timer { Interval = 30 }; marqueeScroll.Tick += MarqueeScrollTick; }
            marqueeScroll.Start();
            Invalidate();
        }

        void UpdateMarquee(Point client)
        {
            marqueeEnd = ToContent(client);
            Rectangle r = MarqueeRect();
            bool changed = false;
            foreach (var i in Items)
            {
                bool sel = marqueeBase.Contains(i) || (r.Width > 2 && r.Height > 2 && i.Bounds.IntersectsWith(r));
                if (sel != i.Selected) { i.Selected = sel; changed = true; }
            }
            Invalidate();
            if (changed) RaiseSel();
        }

        /// <summary>Ao arrastar o retângulo para lá da margem, a lista desliza.</summary>
        void MarqueeScrollTick(object sender, EventArgs e)
        {
            if (!marquee) { marqueeScroll.Stop(); return; }
            Point m = PointToClient(Cursor.Position);
            int dy = m.Y < 0 ? m.Y : m.Y > ClientSize.Height ? m.Y - ClientSize.Height : 0;
            if (dy == 0) return;
            int top = -AutoScrollPosition.Y;
            AutoScrollPosition = new Point(0, Math.Max(0, top + Math.Max(-Px(40), Math.Min(Px(40), dy))));
            UpdateMarquee(m);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (marquee)
            {
                if (e.Button == MouseButtons.Left) UpdateMarquee(e.Location);
                return;
            }
            var it = ItemAt(ToContent(e.Location));
            if (it != hoverItem)
            {
                hoverItem = it;
                Cursor = it != null ? Cursors.Hand : Cursors.Default;
                tip.SetToolTip(this, it != null ? it.Name : null);
                Invalidate();
            }
            if (downItem != null && e.Button == MouseButtons.Left &&
                (Math.Abs(e.X - downPt.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - downPt.Y) > SystemInformation.DragSize.Height))
            {
                // arrastar uma imagem (e as restantes selecionadas): largar no Outlook, Teams, Explorador…
                downItem = null;
                var paths = Selected.Select(x => x.Path).ToArray();
                if (paths.Length > 0) DoDragDrop(new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy);
            }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverItem = null; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            Point c = ToContent(e.Location);
            var it = ItemAt(c);
            downItem = null;
            if (it == null)
            {
                // espaço vazio: começa um retângulo de seleção (Ctrl/Shift juntam à seleção atual)
                if (e.Button == MouseButtons.Left) BeginMarquee(c, (ModifierKeys & (Keys.Control | Keys.Shift)) != 0);
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                if ((ModifierKeys & Keys.Control) != 0) { it.Selected = !it.Selected; anchor = it; }
                else if ((ModifierKeys & Keys.Shift) != 0 && anchor != null)
                {
                    int a = Items.IndexOf(anchor), b = Items.IndexOf(it);
                    for (int i = 0; i < Items.Count; i++) Items[i].Selected = i >= Math.Min(a, b) && i <= Math.Max(a, b);
                }
                else
                {
                    if (!it.Selected) foreach (var i in Items) i.Selected = i == it;
                    anchor = it;
                    downItem = it; downPt = e.Location;
                }
            }
            else if (e.Button == MouseButtons.Right)
            {
                if (!it.Selected) { foreach (var i in Items) i.Selected = i == it; anchor = it; }
            }
            Invalidate();
            RaiseSel();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (marquee && e.Button == MouseButtons.Left)
            {
                UpdateMarquee(e.Location);
                marquee = false;
                Capture = false;
                if (marqueeScroll != null) marqueeScroll.Stop();
                var first = Items.FirstOrDefault(i => i.Selected);
                if (first != null) anchor = first;
                Invalidate();
                return;
            }
            var it = ItemAt(ToContent(e.Location));
            if (e.Button == MouseButtons.Left && downItem != null && it == downItem)
            {
                // clique simples numa imagem que fazia parte de uma seleção maior: fica só esta
                foreach (var i in Items) i.Selected = i == it;
                Invalidate(); RaiseSel();
            }
            downItem = null;
            if (e.Button == MouseButtons.Right && it != null) { var h = MenuRequested; if (h != null) h(e.Location); }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            var it = ItemAt(ToContent(e.Location));
            if (it != null && e.Button == MouseButtons.Left) { var h = OpenItem; if (h != null) h(it); }
        }

        // ================= teclado =================

        protected override bool IsInputKey(Keys k)
        {
            Keys c = k & Keys.KeyCode;
            return c == Keys.Left || c == Keys.Right || c == Keys.Up || c == Keys.Down || base.IsInputKey(k);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (Items.Count == 0) return;
            int cur = anchor != null ? Items.IndexOf(anchor) : -1;
            int cols = Columns();
            int next = cur;
            if (e.KeyCode == Keys.Right) next = cur + 1;
            else if (e.KeyCode == Keys.Left) next = cur - 1;
            else if (e.KeyCode == Keys.Down) next = cur + cols;
            else if (e.KeyCode == Keys.Up) next = cur - cols;
            else return;
            next = Math.Max(0, Math.Min(Items.Count - 1, cur < 0 ? 0 : next));
            foreach (var i in Items) i.Selected = false;
            anchor = Items[next]; anchor.Selected = true;
            ScrollIntoView(anchor);
            Invalidate(); RaiseSel();
            e.Handled = true;
        }

        void ScrollIntoView(CaptureItem it)
        {
            int top = -AutoScrollPosition.Y;
            if (it.Bounds.Y < top) AutoScrollPosition = new Point(0, Math.Max(0, it.Bounds.Y - Px(4)));
            else if (it.Bounds.Bottom > top + ClientSize.Height) AutoScrollPosition = new Point(0, it.Bounds.Bottom - ClientSize.Height + Px(4));
        }

        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

        // ================= desenho =================

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Panel);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (Items.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, Theme.Font(12.5f * dpi), ClientRectangle, Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            var view = new Rectangle(-AutoScrollPosition.X, -AutoScrollPosition.Y, ClientSize.Width, ClientSize.Height);
            Font cap = Theme.Font(11.5f * dpi);
            foreach (var it in Items)
                if (it.Bounds.IntersectsWith(view)) DrawTile(g, it, cap);

            if (marquee)
            {
                Rectangle r = MarqueeRect();
                if (r.Width > 0 && r.Height > 0)
                {
                    g.SmoothingMode = SmoothingMode.None;
                    using (var b = new SolidBrush(Color.FromArgb(40, Theme.Accent))) g.FillRectangle(b, r);
                    using (var pen = new Pen(Color.FromArgb(200, Theme.Accent), 1)) g.DrawRectangle(pen, r);
                }
            }
        }

        void DrawTile(Graphics g, CaptureItem it, Font cap)
        {
            Rectangle r = it.Bounds;
            bool hov = it == hoverItem;
            float rad = Px(8);
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            Theme.FillRound(g, Theme.Surface, rf, rad);

            int ih = r.Height - Px(24);
            var ir = new Rectangle(r.X, r.Y, r.Width, ih);
            var st = g.Save();
            using (var clip = Theme.Round(rf, rad)) g.SetClip(clip);
            using (var b = new SolidBrush(Theme.Surface2)) g.FillRectangle(b, ir);
            Bitmap bmp;
            if (it.Key != null && thumbs.TryGetValue(it.Key, out bmp))
            {
                // "cobrir": preenche a área sem deformar, alinhado ao topo
                float sc = Math.Max(ir.Width / (float)bmp.Width, ir.Height / (float)bmp.Height);
                float w = bmp.Width * sc, h = bmp.Height * sc;
                g.DrawImage(bmp, new RectangleF(ir.X + (ir.Width - w) / 2f, ir.Y, w, h));
            }
            if (it.Selected) using (var b = new SolidBrush(Color.FromArgb(45, Theme.Accent))) g.FillRectangle(b, ir);
            g.Restore(st);

            if (VideoFile.Is(it.Path))
            {
                // vídeo: botão de "play" ao centro da miniatura
                int d = Px(26);
                var pr = new Rectangle(ir.X + (ir.Width - d) / 2, ir.Y + (ir.Height - d) / 2, d, d);
                using (var b = new SolidBrush(Color.FromArgb(150, 0, 0, 0))) g.FillEllipse(b, pr);
                int isz = Px(12);
                g.DrawImage(ToolIcons.Get("play", isz, Color.White), pr.X + (d - isz) / 2 + Px(1), pr.Y + (d - isz) / 2, isz, isz);
            }

            using (var p = new Pen(Theme.Border)) g.DrawLine(p, r.X, r.Y + ih, r.Right, r.Y + ih);
            string caption = CaptionOf != null ? CaptionOf(it) : it.Name;
            DrawText(g, caption, cap, new Rectangle(r.X + Px(7), r.Y + ih, r.Width - Px(10), Px(24)), Theme.Muted);

            if (it.Selected)
            {
                // visto no canto superior direito
                int d = Px(20);
                var cr = new RectangleF(r.Right - d - Px(6), r.Y + Px(6), d, d);
                using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, cr);
                using (var p = new Pen(Theme.OnAccent, Px(2)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    g.DrawLines(p, new[] {
                        new PointF(cr.X + d * 0.28f, cr.Y + d * 0.52f),
                        new PointF(cr.X + d * 0.44f, cr.Y + d * 0.68f),
                        new PointF(cr.X + d * 0.73f, cr.Y + d * 0.36f) });
                Theme.StrokeRound(g, Theme.Accent, Px(2), new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), rad);
            }
            else Theme.StrokeRound(g, hov ? Theme.Accent : Theme.Border, hov ? 1.5f : 1f, rf, rad);
        }

        void DrawText(Graphics g, string text, Font f, Rectangle r, Color c)
        {
            // TextRenderer ignora a transformação: converter para coordenadas do ecrã
            r.Offset(AutoScrollPosition.X, AutoScrollPosition.Y);
            var st = g.Save(); g.ResetTransform();
            TextRenderer.DrawText(g, text, f, r, c, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            g.Restore(st);
        }
    }
}
