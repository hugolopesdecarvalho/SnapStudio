using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace SnapStudio
{
    class CaptureItem
    {
        public string Path, Name, Key;
        public DateTime Time;
        public long Size;
        public Size Dim;
        public bool Annotated, Selected;
        public Rectangle Bounds;
    }

    /// <summary>Grelha de cartões com miniaturas, agrupada por data, com ações ao passar o rato.</summary>
    class CardGrid : ScrollableControl
    {
        class Group { public string Title; public List<CaptureItem> Items = new List<CaptureItem>(); public int Y; }

        readonly float dpi;
        List<Group> groups = new List<Group>();
        public List<CaptureItem> Items = new List<CaptureItem>();
        readonly Dictionary<string, Bitmap> thumbs;
        CaptureItem hoverItem, anchor;
        int hoverAction = -1;
        Point downPt;
        CaptureItem downItem;
        public string EmptyText = "";

        public event Action<CaptureItem> OpenItem;
        public event Action<CaptureItem, int> ActionClicked;   // 0 copiar, 1 editar, 2 apagar
        public event Action<Point> MenuRequested;
        public event Action SelectionChanged;

        public Size ThumbSize { get { return new Size(Px(320), Px(150)); } }

        int Px(float v) { return (int)Math.Round(v * dpi); }

        public CardGrid(Dictionary<string, Bitmap> thumbCache)
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
        protected override void Dispose(bool disposing) { Theme.Changed -= OnTheme; base.Dispose(disposing); }

        public void SetItems(List<CaptureItem> items)
        {
            Items = items;
            DateTime today = DateTime.Today;
            var gs = new[] {
                new Group { Title = S.T("Hoje", "Today") }, new Group { Title = S.T("Ontem", "Yesterday") },
                new Group { Title = S.T("Últimos 7 dias", "Last 7 days") }, new Group { Title = S.T("Últimos 30 dias", "Last 30 days") },
                new Group { Title = S.T("Mais antigas", "Older") } };
            foreach (var it in items)
            {
                DateTime d = it.Time.Date;
                int gi = d >= today ? 0 : d >= today.AddDays(-1) ? 1 : d >= today.AddDays(-7) ? 2 : d >= today.AddDays(-30) ? 3 : 4;
                gs[gi].Items.Add(it);
            }
            groups = gs.Where(g => g.Items.Count > 0).ToList();
            hoverItem = null;
            LayoutItems();
            Invalidate();
        }

        public List<CaptureItem> Selected { get { return Items.Where(i => i.Selected).ToList(); } }

        public void SelectAll() { foreach (var i in Items) i.Selected = true; Invalidate(); RaiseSel(); }

        void RaiseSel() { var h = SelectionChanged; if (h != null) h(); }

        void LayoutItems()
        {
            int pad = Px(28), gap = Px(18), minW = Px(230);
            int avail = Math.Max(minW, ClientSize.Width - 2 * pad);
            int cols = Math.Max(1, (avail + gap) / (minW + gap));
            int cw = (avail - gap * (cols - 1)) / cols;
            int ch = Px(150) + Px(56);
            int y = Px(20);
            foreach (var g in groups)
            {
                g.Y = y;
                y += Px(34);
                for (int i = 0; i < g.Items.Count; i++)
                {
                    int r = i / cols, c = i % cols;
                    g.Items[i].Bounds = new Rectangle(pad + c * (cw + gap), y + r * (ch + gap), cw, ch);
                }
                int rows = (g.Items.Count + cols - 1) / cols;
                y += rows * (ch + gap) + Px(10);
            }
            AutoScrollMinSize = new Size(0, y + Px(10));
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

        Rectangle ActionRect(CaptureItem it, int i)
        {
            int s = Px(34), g = Px(4);
            return new Rectangle(it.Bounds.Right - Px(8) - (3 - i) * s - (2 - i) * g, it.Bounds.Y + Px(8), s, s);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Point c = ToContent(e.Location);
            var it = ItemAt(c);
            int act = -1;
            if (it != null) for (int i = 0; i < 3; i++) if (ActionRect(it, i).Contains(c)) act = i;
            if (it != hoverItem || act != hoverAction)
            {
                hoverItem = it; hoverAction = act;
                Cursor = it != null ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            if (downItem != null && e.Button == MouseButtons.Left &&
                (Math.Abs(e.X - downPt.X) > SystemInformation.DragSize.Width || Math.Abs(e.Y - downPt.Y) > SystemInformation.DragSize.Height))
            {
                var paths = Selected.Select(x => x.Path).ToArray();
                downItem = null;
                if (paths.Length > 0) DoDragDrop(new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy);
            }
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hoverItem = null; hoverAction = -1; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            Point c = ToContent(e.Location);
            var it = ItemAt(c);
            downItem = null;
            if (it == null)
            {
                if (e.Button == MouseButtons.Left && (ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
                { foreach (var i in Items) i.Selected = false; Invalidate(); RaiseSel(); }
                return;
            }
            if (e.Button == MouseButtons.Left)
            {
                for (int i = 0; i < 3; i++)
                    if (ActionRect(it, i).Contains(c))
                    {
                        var h = ActionClicked;
                        if (h != null) h(it, i);
                        return;
                    }
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
                }
                downItem = it; downPt = e.Location;
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
            Point c = ToContent(e.Location);
            var it = ItemAt(c);
            if (e.Button == MouseButtons.Left && downItem != null && it == downItem && (ModifierKeys & (Keys.Control | Keys.Shift)) == 0)
            {
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
            int cols = 1;
            if (Items.Count > 1) { int y0 = Items[0].Bounds.Y; cols = Items.TakeWhile(i => i.Bounds.Y == y0).Count(); }
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
            if (it.Bounds.Y < top + Px(10)) AutoScrollPosition = new Point(0, Math.Max(0, it.Bounds.Y - Px(40)));
            else if (it.Bounds.Bottom > top + ClientSize.Height) AutoScrollPosition = new Point(0, it.Bounds.Bottom - ClientSize.Height + Px(20));
        }

        public void Repaint(CaptureItem it) { Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            if (Items.Count == 0)
            {
                TextRenderer.DrawText(g, EmptyText, Theme.Font(14 * dpi), ClientRectangle, Theme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }
            g.TranslateTransform(AutoScrollPosition.X, AutoScrollPosition.Y);
            var view = new Rectangle(-AutoScrollPosition.X, -AutoScrollPosition.Y, ClientSize.Width, ClientSize.Height);
            Font title = Theme.Font(14 * dpi, FontStyle.Bold), name = Theme.Font(13.5f * dpi, FontStyle.Bold), meta = Theme.Font(12 * dpi), badge = Theme.Font(11 * dpi, FontStyle.Bold);

            foreach (var grp in groups)
            {
                if (grp.Y + Px(30) >= view.Y && grp.Y <= view.Bottom)
                    DrawText(g, grp.Title + "  ·  " + grp.Items.Count, title, new Rectangle(Px(28), grp.Y, Px(400), Px(26)), Theme.Muted);
                foreach (var it in grp.Items)
                {
                    if (!it.Bounds.IntersectsWith(view)) continue;
                    DrawCard(g, it, name, meta, badge);
                }
            }
        }

        void DrawText(Graphics g, string text, Font f, Rectangle r, Color c)
        {
            // TextRenderer ignora a transformação: converter para coordenadas do ecrã
            r.Offset(AutoScrollPosition.X, AutoScrollPosition.Y);
            var st = g.Save(); g.ResetTransform();
            TextRenderer.DrawText(g, text, f, r, c, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            g.Restore(st);
        }

        void DrawCard(Graphics g, CaptureItem it, Font name, Font meta, Font badge)
        {
            var r = it.Bounds;
            bool hov = it == hoverItem;
            float rad = Px(12);
            var rf = new RectangleF(r.X + 0.5f, r.Y + 0.5f, r.Width - 1, r.Height - 1);
            if (it.Selected) { var sh = rf; sh.Offset(0, Px(2)); Theme.FillRound(g, Color.FromArgb(40, Theme.Accent), sh, rad); }
            Theme.FillRound(g, Theme.Surface, rf, rad);

            int th = Px(150);
            var ir = new Rectangle(r.X, r.Y, r.Width, th);
            var st = g.Save();
            using (var clip = Theme.Round(rf, rad)) g.SetClip(clip);
            using (var b = new SolidBrush(Theme.Surface2)) g.FillRectangle(b, ir);
            Bitmap bmp;
            if (it.Key != null && thumbs.TryGetValue(it.Key, out bmp))
            {
                float sc = Math.Max(ir.Width / (float)bmp.Width, ir.Height / (float)bmp.Height);
                float w = bmp.Width * sc, h = bmp.Height * sc;
                g.DrawImage(bmp, new RectangleF(ir.X + (ir.Width - w) / 2, ir.Y + (h > ir.Height ? 0 : (ir.Height - h) / 2), w, h));
            }
            g.Restore(st);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, r.X, r.Y + th, r.Right, r.Y + th);

            if (it.Annotated)
            {
                string t = S.T("Anotada", "Annotated");
                int tw = TextRenderer.MeasureText(t, badge).Width + Px(14);
                var br = new RectangleF(r.X + Px(10), r.Y + th - Px(30), tw, Px(22));
                Theme.FillRound(g, Theme.Dark ? Theme.Panel : Color.White, br, Px(11));
                DrawCentered(g, t, badge, Rectangle.Round(br), Theme.AccentSoftText);
            }

            if (hov)
            {
                string[] icons = { "copy", "edit", "delete" };
                for (int i = 0; i < 3; i++)
                {
                    var ar = ActionRect(it, i);
                    Theme.FillRound(g, Color.FromArgb(30, 0, 0, 0), new RectangleF(ar.X, ar.Y + 1, ar.Width, ar.Height), Px(8));
                    Theme.FillRound(g, i == hoverAction ? Theme.Hover : Theme.Panel, ar, Px(8));
                    Color ic = i == 2 ? Theme.Danger : Theme.Text;
                    int isz = Px(16);
                    g.DrawImage(ToolIcons.Get(icons[i], isz, ic), ar.X + (ar.Width - isz) / 2, ar.Y + (ar.Height - isz) / 2, isz, isz);
                }
            }

            DrawText(g, it.Name, name, new Rectangle(r.X + Px(12), r.Y + th + Px(9), r.Width - Px(24), Px(20)), Theme.Text);
            string m = it.Time.ToString("HH:mm") + (it.Dim.Width > 0 ? "  ·  " + it.Dim.Width + " × " + it.Dim.Height : "") + "  ·  " + Math.Max(1, it.Size / 1024) + " KB";
            DrawText(g, m, meta, new Rectangle(r.X + Px(12), r.Y + th + Px(29), r.Width - Px(24), Px(18)), Theme.Muted);

            if (it.Selected) Theme.StrokeRound(g, Theme.Accent, Px(2), new RectangleF(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2), rad);
            else Theme.StrokeRound(g, hov ? Theme.BorderStrong : Theme.Border, 1, rf, rad);
        }

        void DrawCentered(Graphics g, string text, Font f, Rectangle r, Color c)
        {
            r.Offset(AutoScrollPosition.X, AutoScrollPosition.Y);
            var st = g.Save(); g.ResetTransform();
            TextRenderer.DrawText(g, text, f, r, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            g.Restore(st);
        }
    }

    /// <summary>Histórico de capturas: pesquisa, filtros e cartões.</summary>
    class HistoryForm : Form
    {
        static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".mp4" };
        static readonly Dictionary<string, Bitmap> thumbCache = new Dictionary<string, Bitmap>();
        static readonly Dictionary<string, Size> dimCache = new Dictionary<string, Size>();

        readonly float dpi;
        readonly CardGrid grid;
        readonly TextBox search;
        readonly FlatButton navAll, navToday, navWeek, navAnnotated;
        readonly ThemedLabel lblTip;
        readonly System.Windows.Forms.Timer reloadTimer;
        FileSystemWatcher watcher;
        volatile int loadVersion;
        string filter = "all";

        int Px(float v) { return (int)Math.Round(v * dpi); }

        public HistoryForm()
        {
            dpi = Dpi.Scale;
            Text = S.T("SnapStudio — Histórico de capturas", "SnapStudio — Capture history");
            Icon = AppIcon.FormIcon;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Font(13 * dpi);
            Size = new Size(Px(1180), Px(780));
            MinimumSize = new Size(Px(640), Px(420));
            KeyPreview = true;

            // --- cabeçalho
            var header = new ThemedPanel { Dock = DockStyle.Top, Height = Px(64), BorderBottom = true };
            header.Paint += (s, e) => { using (var logo = AppIcon.Draw(Px(26))) e.Graphics.DrawImage(logo, Px(20), Px(19)); };
            header.Controls.Add(new ThemedLabel { Text = S.T("Histórico", "History"), Font = Theme.Font(16 * dpi, FontStyle.Bold), Location = new Point(Px(56), Px(20)) });
            var searchBox = new ThemedPanel { Back = () => Theme.Surface2, Size = new Size(Px(420), Px(40)), Location = new Point(Px(200), Px(12)) };
            searchBox.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Theme.StrokeRound(e.Graphics, search != null && search.Focused ? Theme.Accent : Theme.BorderStrong, 1, new RectangleF(0.5f, 0.5f, searchBox.Width - 1, searchBox.Height - 1), Px(8));
                e.Graphics.DrawImage(ToolIcons.Get("search", Px(16), Theme.Muted), Px(12), Px(12));
                TextRenderer.DrawText(e.Graphics, "Ctrl+F", Theme.Font(11.5f * dpi), new Rectangle(searchBox.Width - Px(60), 0, Px(50), searchBox.Height), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
            };
            search = new TextBox { BorderStyle = BorderStyle.None, Font = Theme.Font(13.5f * dpi), Location = new Point(Px(36), Px(11)), Width = Px(310), AccessibleName = S.T("Procurar capturas", "Search captures") };
            search.TextChanged += delegate { reloadTimer.Stop(); reloadTimer.Start(); };
            search.GotFocus += delegate { searchBox.Invalidate(); };
            search.LostFocus += delegate { searchBox.Invalidate(); };
            searchBox.Controls.Add(search);
            header.Controls.Add(searchBox);
            var bCapture = new FlatButton { Kind = BtnKind.Primary, IconName = "region", Text = S.T("Capturar", "Capture") };
            bCapture.Click += delegate { TrayApp.Instance.StartCapture(CaptureMode.Region, 0, this); };
            var bFull = new FlatButton { Kind = BtnKind.Secondary, IconName = "full", Text = S.T("Ecrã inteiro", "Full screen") };
            bFull.Click += delegate { TrayApp.Instance.StartCapture(CaptureMode.FullScreen, 0, this); };
            header.Controls.Add(bCapture); header.Controls.Add(bFull);
            header.Layout += delegate
            {
                bCapture.Size = new Size(bCapture.PreferredWidth, Px(40));
                bFull.Size = new Size(bFull.PreferredWidth, Px(40));
                bCapture.Location = new Point(header.Width - Px(20) - bCapture.Width, Px(12));
                bFull.Location = new Point(bCapture.Left - Px(8) - bFull.Width, Px(12));
                searchBox.Width = Math.Max(Px(160), Math.Min(Px(460), bFull.Left - Px(24) - searchBox.Left));
                search.Width = searchBox.Width - Px(36) - Px(64);
            };

            // --- filtros
            var nav = new ThemedPanel { Dock = DockStyle.Left, Width = Px(232), BorderRight = true, Back = () => Theme.Bg };
            int y = Px(16);
            navAll = Nav(nav, S.T("Todas", "All"), "all", ref y);
            navToday = Nav(nav, S.T("Hoje", "Today"), "today", ref y);
            navWeek = Nav(nav, S.T("Esta semana", "This week"), "week", ref y);
            navAnnotated = Nav(nav, S.T("Com anotações", "Annotated"), "annotated", ref y);
            var bFolder = new FlatButton { Kind = BtnKind.Nav, IconName = "folder", Text = S.T("Abrir pasta", "Open folder"), Size = new Size(Px(208), Px(40)), Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            bFolder.Click += delegate { ShowInFolder(); };
            var bSettings = new FlatButton { Kind = BtnKind.Nav, IconName = "settings", Text = S.T("Definições", "Settings"), Size = new Size(Px(208), Px(40)), Anchor = AnchorStyles.Left | AnchorStyles.Bottom };
            bSettings.Click += delegate { TrayApp.Instance.ShowSettings(); };
            nav.Controls.Add(bFolder); nav.Controls.Add(bSettings);
            nav.Resize += delegate
            {
                bSettings.Location = new Point(Px(12), nav.Height - Px(56));
                bFolder.Location = new Point(Px(12), nav.Height - Px(100));
            };

            // --- grelha
            var main = new ThemedPanel { Dock = DockStyle.Fill, Back = () => Theme.Bg };
            grid = new CardGrid(thumbCache) { Dock = DockStyle.Fill };
            grid.EmptyText = S.T("Ainda não há capturas aqui.\nPrima PrtScn para fazer a primeira.", "No captures here yet.\nPress PrtScn to take your first one.");
            grid.OpenItem += it => TrayApp.Instance.OpenEditor(it.Path, null);
            grid.ActionClicked += (it, a) =>
            {
                if (a == 0) CopyPaths(new List<string> { it.Path });
                else if (a == 1) TrayApp.Instance.OpenEditor(it.Path, null);
                else DeletePaths(new List<string> { it.Path });
            };
            grid.MenuRequested += ShowMenu;
            lblTip = new ThemedLabel
            {
                Dock = DockStyle.Bottom,
                AutoSize = false,
                Height = Px(36),
                Padding = new Padding(Px(28), 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.Font(12.5f * dpi),
                Text = S.T("Dica: arraste um cartão para o Outlook, Teams ou Explorador. Duplo clique abre o editor. F2 muda o nome.",
                           "Tip: drag a card into Outlook, Teams or Explorer. Double-click opens the editor. F2 renames.")
            }.Muted();
            main.Controls.Add(grid);
            main.Controls.Add(lblTip);

            Controls.Add(main);
            Controls.Add(nav);
            Controls.Add(header);

            reloadTimer = new System.Windows.Forms.Timer { Interval = 350 };
            reloadTimer.Tick += delegate { reloadTimer.Stop(); Reload(); };

            Theme.Changed += ApplyTheme;
            ApplyTheme();
            SetupWatcher();
            Reload();
        }

        FlatButton Nav(Control parent, string text, string key, ref int y)
        {
            var b = new FlatButton { Kind = BtnKind.Nav, Text = text, Size = new Size(Px(208), Px(40)), Location = new Point(Px(12), y), Font = Theme.Font(14 * dpi), Checked = key == filter };
            string k = key;
            b.Click += delegate { filter = k; foreach (var n in new[] { navAll, navToday, navWeek, navAnnotated }) n.Checked = n == b; Reload(); };
            parent.Controls.Add(b);
            y += Px(42);
            return b;
        }

        void ApplyTheme()
        {
            if (IsDisposed) return;
            BackColor = Theme.Bg;
            search.BackColor = Theme.Surface2;
            search.ForeColor = Theme.Text;
            Theme.StyleWindow(this);
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.StyleWindow(this); }

        string Folder { get { return TrayApp.Instance.Settings.OutputFolder; } }

        public void FolderChanged() { SetupWatcher(); Reload(); }

        void SetupWatcher()
        {
            if (watcher != null) { watcher.EnableRaisingEvents = false; watcher.Dispose(); watcher = null; }
            try
            {
                Directory.CreateDirectory(Folder);
                watcher = new FileSystemWatcher(Folder)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    SynchronizingObject = this,
                };
                FileSystemEventHandler h = delegate { reloadTimer.Stop(); reloadTimer.Start(); };
                watcher.Created += h; watcher.Changed += h; watcher.Deleted += h;
                watcher.Renamed += delegate { reloadTimer.Stop(); reloadTimer.Start(); };
                watcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        public void Reload()
        {
            var sel = new HashSet<string>(grid.Selected.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
            List<FileInfo> files;
            try
            {
                files = Directory.Exists(Folder)
                    ? new DirectoryInfo(Folder).EnumerateFiles().Where(f => Exts.Contains(f.Extension.ToLowerInvariant())).OrderByDescending(f => f.LastWriteTime).ToList()
                    : new List<FileInfo>();
            }
            catch { files = new List<FileInfo>(); }

            DateTime today = DateTime.Today;
            DateTime weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var all = files.Select(f => new CaptureItem
            {
                Path = f.FullName,
                Name = System.IO.Path.GetFileNameWithoutExtension(f.Name),
                Time = f.LastWriteTime,
                Size = f.Length,
                Key = f.FullName + "|" + f.LastWriteTimeUtc.Ticks + "|" + f.Length,
                Annotated = File.Exists(Sidecar.Json(f.FullName)),
            }).ToList();
            foreach (var it in all) { Size d; if (dimCache.TryGetValue(it.Key, out d)) it.Dim = d; it.Selected = sel.Contains(it.Path); }

            navAll.Badge = all.Count.ToString();
            navToday.Badge = all.Count(i => i.Time >= today).ToString();
            navWeek.Badge = all.Count(i => i.Time >= weekStart).ToString();
            navAnnotated.Badge = all.Count(i => i.Annotated).ToString();

            IEnumerable<CaptureItem> shown = all;
            if (filter == "today") shown = shown.Where(i => i.Time >= today);
            else if (filter == "week") shown = shown.Where(i => i.Time >= weekStart);
            else if (filter == "annotated") shown = shown.Where(i => i.Annotated);
            string q = search.Text.Trim();
            if (q.Length > 0) shown = shown.Where(i => i.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0 || i.Time.ToString("dd/MM/yyyy HH:mm").Contains(q));
            var list = shown.ToList();
            grid.SetItems(list);
            LoadThumbnails(list);
        }

        void LoadThumbnails(List<CaptureItem> items)
        {
            int ver = ++loadVersion;
            var todo = items.Where(i => !thumbCache.ContainsKey(i.Key)).ToList();
            if (todo.Count == 0) return;
            Size sz = grid.ThumbSize;
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (var it in todo)
                {
                    if (ver != loadVersion || IsDisposed) return;
                    Bitmap th = null;
                    Size dim = Size.Empty;
                    try { th = ImageUtil.ThumbCover(it.Path, sz, out dim); } catch { }
                    if (th == null) continue;
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            if (IsDisposed) return;
                            thumbCache[it.Key] = th;
                            dimCache[it.Key] = dim;
                            it.Dim = dim;
                            grid.Invalidate();
                        });
                    }
                    catch { return; }
                }
            });
        }

        // ---------- ações ----------

        List<string> SelectedPaths() { return grid.Selected.Select(i => i.Path).ToList(); }

        void OpenSelected() { foreach (var p in SelectedPaths().Take(8)) TrayApp.Instance.OpenEditor(p, null); }

        void CopyPaths(List<string> paths)
        {
            if (paths.Count == 0) return;
            try
            {
                var data = new DataObject();
                var sc = new StringCollection();
                sc.AddRange(paths.ToArray());
                data.SetFileDropList(sc);
                if (paths.Count == 1 && !VideoFile.Is(paths[0])) data.SetImage(ImageUtil.LoadNoLock(paths[0]));
                Clipboard.SetDataObject(data, true, 5, 100);
                lblTip.Text = paths.Count == 1 && VideoFile.Is(paths[0]) ? S.T("Vídeo copiado — cole com Ctrl+V no Teams, Outlook ou numa pasta.", "Video copied — paste with Ctrl+V into Teams, Outlook or a folder.")
                           : paths.Count == 1 ? S.T("Imagem copiada para a área de transferência.", "Image copied to the clipboard.")
                                               : paths.Count + S.T(" ficheiros copiados.", " files copied.");
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "SnapStudio"); }
        }

        void DeletePaths(List<string> paths)
        {
            if (paths.Count == 0) return;
            string msg = paths.Count == 1
                ? S.T("Enviar \"", "Send \"") + System.IO.Path.GetFileName(paths[0]) + S.T("\" para a Reciclagem?", "\" to the Recycle Bin?")
                : S.T("Enviar ", "Send ") + paths.Count + S.T(" capturas para a Reciclagem?", " captures to the Recycle Bin?");
            if (MessageBox.Show(this, msg, "SnapStudio", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            foreach (var p in paths)
            {
                try
                {
                    FileSystem.DeleteFile(p, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    Sidecar.Delete(p);
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "SnapStudio"); }
            }
            Reload();
        }

        void RenameSelected()
        {
            var paths = SelectedPaths();
            if (paths.Count != 1) return;
            string oldPath = paths[0];
            string name = Prompt.Show(this, S.T("Mudar o nome", "Rename"), S.T("Novo nome:", "New name:"), System.IO.Path.GetFileNameWithoutExtension(oldPath));
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, S.T("O nome contém caracteres inválidos.", "The name contains invalid characters."), "SnapStudio");
                return;
            }
            string newPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(oldPath), name + System.IO.Path.GetExtension(oldPath));
            if (string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(newPath)) { MessageBox.Show(this, S.T("Já existe um ficheiro com esse nome.", "A file with that name already exists."), "SnapStudio"); return; }
            try { File.Move(oldPath, newPath); Sidecar.Move(oldPath, newPath); Reload(); }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "SnapStudio"); }
        }

        void ShowInFolder()
        {
            var paths = SelectedPaths();
            if (paths.Count > 0) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + paths[0] + "\"");
            else System.Diagnostics.Process.Start("explorer.exe", "\"" + Folder + "\"");
        }

        void ShowMenu(Point p)
        {
            var m = new ContextMenuStrip { Renderer = new ThemedRenderer(), Font = Theme.Font(13 * dpi) };
            var open = m.Items.Add(S.T("Abrir no editor", "Open in editor"), null, delegate { OpenSelected(); });
            open.Font = Theme.Font(13 * dpi, FontStyle.Bold);
            m.Items.Add(S.T("Abrir no editor rápido", "Open in quick editor"), null, delegate { var s = SelectedPaths(); if (s.Count > 0) TrayApp.Instance.OpenQuickEditor(s[0], null); });
            m.Items.Add(S.T("Copiar", "Copy"), null, delegate { CopyPaths(SelectedPaths()); });
            m.Items.Add(S.T("Mostrar na pasta", "Show in folder"), null, delegate { ShowInFolder(); });
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(S.T("Mudar o nome (F2)", "Rename (F2)"), null, delegate { RenameSelected(); });
            m.Items.Add(S.T("Apagar", "Delete"), null, delegate { DeletePaths(SelectedPaths()); });
            m.Closed += delegate { BeginInvoke((Action)m.Dispose); };
            m.Show(grid, p);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (search.Focused)
            {
                if (keyData == Keys.Escape) { search.Text = ""; grid.Focus(); return true; }
                if (keyData == Keys.Down) { grid.Focus(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            switch (keyData)
            {
                case Keys.Enter: OpenSelected(); return true;
                case Keys.Delete: DeletePaths(SelectedPaths()); return true;
                case Keys.F2: RenameSelected(); return true;
                case Keys.F5: Reload(); return true;
                case Keys.Control | Keys.C: CopyPaths(SelectedPaths()); return true;
                case Keys.Control | Keys.A: grid.SelectAll(); return true;
                case Keys.Control | Keys.F: search.Focus(); return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            loadVersion++;
            Theme.Changed -= ApplyTheme;
            if (watcher != null) watcher.Dispose();
            reloadTimer.Dispose();
            base.OnFormClosed(e);
        }
    }
}
