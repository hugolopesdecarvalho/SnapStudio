using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace SnapStudio
{
    /// <summary>Miniatura clicável de uma captura recente.</summary>
    class ThumbTile : ThemedControl
    {
        public string Path;
        public string Caption;
        public Bitmap Image;

        public ThumbTile()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float rad = Px(8);
            if (Path == null)
            {
                using (var pen = new Pen(Theme.Border) { DashStyle = DashStyle.Dash }) using (var p = Theme.Round(r, rad)) g.DrawPath(pen, p);
                return;
            }
            Theme.FillRound(g, Theme.Surface, r, rad);
            int ih = Height - Px(24);
            var st = g.Save();
            using (var clip = Theme.Round(r, rad)) g.SetClip(clip);
            var ir = new Rectangle(0, 0, Width, ih);
            using (var b = new SolidBrush(Theme.Surface2)) g.FillRectangle(b, ir);
            if (Image != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(Image, ir);
            }
            g.Restore(st);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, ih, Width, ih);
            TextRenderer.DrawText(g, Caption, Theme.Font(11.5f * dpi), new Rectangle(Px(7), ih, Width - Px(10), Px(24)), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            Theme.StrokeRound(g, hover ? Theme.Accent : Theme.Border, hover ? 1.5f : 1f, r, rad);
            PaintFocus(g, r, rad);
        }
    }

    /// <summary>Painel que abre por cima do relógio ao clicar no ícone da bandeja.</summary>
    class TrayPanel : Form
    {
        readonly float dpi;
        readonly FlatButton btnTheme, btnSettings, btnCapture, btnFull, btnWindow, btnDelay, btnHistory, btnOpen, btnFolder, btnExit;
        readonly ToggleRow swEditor;
        readonly ThemedPanel footer;
        readonly List<ThumbTile> tiles = new List<ThumbTile>();
        int yLine1, yRecent, yClip, yLine2, yFooter;
        bool clipVisible;
        DateTime lastHidden = DateTime.MinValue;

        static readonly Dictionary<string, Bitmap> thumbCache = new Dictionary<string, Bitmap>();
        int loadVersion;

        int Px(float v) { return (int)Math.Round(v * dpi); }

        public TrayPanel()
        {
            dpi = Dpi.Scale;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            Text = "SnapStudio";
            Font = Theme.Font(13 * dpi);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);

            int W = Px(384), pad = Px(14);
            btnTheme = new FlatButton { Kind = BtnKind.Ghost, Size = new Size(Px(36), Px(36)), Location = new Point(W - pad - Px(76), Px(10)) };
            btnTheme.Click += delegate { TrayApp.Instance.ToggleTheme(); };
            btnSettings = new FlatButton { Kind = BtnKind.Ghost, IconName = "settings", Size = new Size(Px(36), Px(36)), Location = new Point(W - pad - Px(36), Px(10)), AccessibleName = S.T("Definições", "Settings") };
            btnSettings.Click += delegate { HideNow(); TrayApp.Instance.ShowSettings(); };

            int y = Px(60);
            btnCapture = new FlatButton { Kind = BtnKind.Primary, IconName = "region", IconSize = 20, Text = S.T("Capturar janela ou região", "Capture window or region"), Size = new Size(W - 2 * pad, Px(56)), Location = new Point(pad, y), Font = Theme.Font(14.5f * dpi) };
            btnCapture.Click += delegate { DoCapture(CaptureMode.Region, 250); };
            y += Px(56) + Px(8);
            int bw = (W - 2 * pad - 2 * Px(8)) / 3;
            btnFull = SmallVertical("full", S.T("Ecrã inteiro", "Full screen"), pad, y, bw);
            btnFull.Click += delegate { DoCapture(CaptureMode.FullScreen, 250); };
            btnWindow = SmallVertical("window", S.T("Janela ativa", "Active window"), pad + bw + Px(8), y, bw);
            btnWindow.Click += delegate { DoCapture(CaptureMode.ActiveWindow, 400); };
            btnDelay = SmallVertical("timer", S.T("Atraso 3 s", "3 s delay"), pad + 2 * (bw + Px(8)), y, bw);
            btnDelay.Click += delegate { DoCapture(CaptureMode.Region, 3000); };
            y += Px(64) + Px(14);

            yLine1 = y;
            y += Px(10);
            yRecent = y;
            btnHistory = new FlatButton { Kind = BtnKind.Ghost, Text = S.T("Ver histórico", "View history"), Size = new Size(Px(116), Px(28)), Location = new Point(W - pad - Px(116), y - Px(2)), Font = Theme.Font(13 * dpi) };
            btnHistory.Click += delegate { HideNow(); TrayApp.Instance.ShowHistory(); };
            y += Px(32);
            int tw = (W - 2 * pad - 2 * Px(8)) / 3, th = Px(86);
            for (int i = 0; i < 6; i++)
            {
                var t = new ThumbTile { Size = new Size(tw, th), Location = new Point(pad + (i % 3) * (tw + Px(8)), y + (i / 3) * (th + Px(8))) };
                t.Click += delegate { if (t.Path != null) { HideNow(); TrayApp.Instance.OpenQuickEditor(t.Path, null); } };
                tiles.Add(t);
            }
            y += 2 * th + Px(8) + Px(12);
            yClip = y;
            y += Px(44) + Px(10);
            yLine2 = y;
            swEditor = new ToggleRow { Text = S.T("Abrir no editor depois de capturar", "Open in the editor after capture"), Size = new Size(W - 2 * Px(16), Px(44)), Location = new Point(Px(16), y) };
            swEditor.CheckedChanged += delegate
            {
                var st = TrayApp.Instance.Settings;
                string v = swEditor.Checked ? (st.AfterCapture == "full" ? "full" : "quick") : "none";
                if (st.AfterCapture != v) { st.AfterCapture = v; try { st.Save(); } catch { } }
            };
            y += Px(44);
            yFooter = y;
            footer = new ThemedPanel { Back = () => Theme.Dark ? Theme.Surface : Theme.Surface2, BorderTop = true, Location = new Point(1, y), Size = new Size(W - 2, Px(52) - 1) };
            btnOpen = new FlatButton { Kind = BtnKind.Ghost, Text = S.T("Abrir imagem…", "Open image…"), Size = new Size(Px(120), Px(36)), Location = new Point(Px(10), Px(8)) };
            btnOpen.Click += delegate { HideNow(); TrayApp.Instance.OpenImageDialog(); };
            btnFolder = new FlatButton { Kind = BtnKind.Ghost, Text = S.T("Pasta", "Folder"), Size = new Size(Px(70), Px(36)), Location = new Point(Px(134), Px(8)) };
            btnFolder.Click += delegate { HideNow(); TrayApp.Instance.OpenFolder(); };
            btnExit = new FlatButton { Kind = BtnKind.Ghost, Text = S.T("Sair", "Exit"), Size = new Size(Px(64), Px(36)), Location = new Point(W - Px(76), Px(8)) };
            btnExit.Click += delegate { HideNow(); TrayApp.Instance.Exit(); };
            y += Px(52);

            ClientSize = new Size(W, y);
            footer.Controls.AddRange(new Control[] { btnOpen, btnFolder, btnExit });
            Controls.AddRange(new Control[] { btnTheme, btnSettings, btnCapture, btnFull, btnWindow, btnDelay, btnHistory, swEditor, footer });
            foreach (var t in tiles) Controls.Add(t);

            Theme.Changed += ApplyTheme;
            ApplyTheme();
        }

        FlatButton SmallVertical(string icon, string text, int x, int y, int w)
        {
            return new FlatButton { Kind = BtnKind.Secondary, IconName = icon, Text = text, Vertical = true, Size = new Size(w, Px(64)), Location = new Point(x, y), Font = Theme.Font(12.5f * dpi) };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;     // CS_DROPSHADOW
                cp.ExStyle |= 0x80;           // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.RoundCorners(this);
        }

        void ApplyTheme()
        {
            if (IsDisposed) return;
            BackColor = Theme.Panel;
            btnTheme.IconName = Theme.Dark ? "sun" : "moon";
            btnTheme.AccessibleName = Theme.Dark ? S.T("Mudar para tema claro", "Switch to light theme") : S.T("Mudar para tema escuro", "Switch to dark theme");
            Invalidate(true);
        }

        void DoCapture(CaptureMode mode, int delay)
        {
            HideNow();
            TrayApp.Instance.StartCapture(mode, delay, null);
        }

        public void Toggle()
        {
            if (Visible) { HideNow(); return; }
            // o clique no ícone fecha o painel (perde o foco) e chega logo a seguir: não reabrir
            if ((DateTime.Now - lastHidden).TotalMilliseconds < 350) return;
            ShowPanel();
        }

        public void Prepare()
        {
            var st = TrayApp.Instance.Settings;
            swEditor.Checked = st.AfterCapture != "none";
            try { clipVisible = Clipboard.ContainsImage(); } catch { clipVisible = false; }
            LoadRecents();
        }

        public void ShowPanel()
        {
            Prepare();

            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Right - Width - Px(12), wa.Bottom - Height - Px(12));
            Show();
            Activate();
            Native.SetForegroundWindow(Handle);
            btnCapture.Focus();
        }

        public void HideNow()
        {
            if (!Visible) return;
            Hide();
            lastHidden = DateTime.Now;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            HideNow();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { HideNow(); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        void LoadRecents()
        {
            List<FileInfo> files;
            try
            {
                string folder = TrayApp.Instance.Settings.OutputFolder;
                string[] exts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
                files = Directory.Exists(folder)
                    ? new DirectoryInfo(folder).EnumerateFiles().Where(f => exts.Contains(f.Extension.ToLowerInvariant())).OrderByDescending(f => f.LastWriteTime).Take(6).ToList()
                    : new List<FileInfo>();
            }
            catch { files = new List<FileInfo>(); }

            var todo = new List<KeyValuePair<ThumbTile, string>>();
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                if (i < files.Count)
                {
                    var f = files[i];
                    t.Path = f.FullName;
                    t.Caption = Ago(f.LastWriteTime);
                    t.AccessibleName = f.Name;
                    string key = f.FullName + "|" + f.LastWriteTimeUtc.Ticks;
                    Bitmap b;
                    if (thumbCache.TryGetValue(key, out b)) t.Image = b;
                    else { t.Image = null; todo.Add(new KeyValuePair<ThumbTile, string>(t, key)); }
                    t.Visible = true;
                }
                else { t.Path = null; t.Image = null; t.Caption = ""; }
                t.Invalidate();
            }

            int ver = ++loadVersion;
            Size sz = new Size(tiles[0].Width, tiles[0].Height - Px(24));
            if (todo.Count == 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (var kv in todo)
                {
                    if (ver != loadVersion) return;
                    string path = kv.Value.Substring(0, kv.Value.LastIndexOf('|'));
                    Bitmap b = null;
                    Size dim;
                    try { b = ImageUtil.ThumbCover(path, sz, out dim); } catch { }
                    if (b == null) continue;
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            thumbCache[kv.Value] = b;
                            if (kv.Key.Path == path) { kv.Key.Image = b; kv.Key.Invalidate(); }
                        });
                    }
                    catch { return; }
                }
            });
        }

        static string Ago(DateTime t)
        {
            TimeSpan d = DateTime.Now - t;
            if (d.TotalMinutes < 1) return S.T("Agora", "Just now");
            if (d.TotalMinutes < 60) return S.T("Há ", "") + (int)d.TotalMinutes + S.T(" min", " min ago");
            if (t.Date == DateTime.Today) return t.ToString("HH:mm");
            if (t.Date == DateTime.Today.AddDays(-1)) return S.T("Ontem ", "Yesterday ") + t.ToString("HH:mm");
            return t.ToString("dd/MM HH:mm");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            int W = ClientSize.Width;

            using (var logo = AppIcon.Draw(Px(26))) g.DrawImage(logo, Px(16), Px(15));
            TextRenderer.DrawText(g, "SnapStudio", Theme.Font(15 * dpi, FontStyle.Bold), new Point(Px(50), Px(17)), Theme.Text, TextFormatFlags.NoPadding);

            using (var p = new Pen(Theme.Border))
            {
                g.DrawLine(p, 0, yLine1, W, yLine1);
                g.DrawLine(p, 0, yLine2, W, yLine2);
                g.DrawLine(p, 0, yFooter, W, yFooter);
            }
            TextRenderer.DrawText(g, S.T("Recentes", "Recent"), Theme.Font(13 * dpi, FontStyle.Bold), new Rectangle(Px(16), yRecent, Px(200), Px(24)), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var clip = new RectangleF(Px(14), yClip, W - Px(28), Px(44));
            if (clipVisible)
            {
                Theme.FillRound(g, Theme.AccentSoft, clip, Px(8));
                g.DrawImage(ToolIcons.Get("clipboard", Px(18), Theme.AccentSoftText), clip.X + Px(12), clip.Y + (clip.Height - Px(18)) / 2);
                TextRenderer.DrawText(g, S.T("A última captura está na área de transferência — cole com Ctrl+V", "Your last capture is on the clipboard — paste with Ctrl+V"),
                    Theme.Font(12.5f * dpi), new Rectangle((int)clip.X + Px(40), (int)clip.Y, (int)clip.Width - Px(48), (int)clip.Height), Theme.AccentSoftText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            }
            else
            {
                TextRenderer.DrawText(g, S.T("Dica: PrtScn entra em modo captura", "Tip: PrtScn enters capture mode"), Theme.Font(12.5f * dpi),
                    Rectangle.Round(clip), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            }

            using (var p = new Pen(Theme.BorderStrong)) g.DrawRectangle(p, 0, 0, W - 1, ClientSize.Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            Theme.Changed -= ApplyTheme;
            base.Dispose(disposing);
        }
    }
}
