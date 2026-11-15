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
                // "cobrir": preenche a área sem deformar, alinhado ao topo
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float sc = Math.Max(ir.Width / (float)Image.Width, ir.Height / (float)Image.Height);
                float w = Image.Width * sc, h = Image.Height * sc;
                g.DrawImage(Image, new RectangleF((ir.Width - w) / 2f, 0, w, h));
            }
            g.Restore(st);
            using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, ih, Width, ih);
            TextRenderer.DrawText(g, Caption, Theme.Font(11.5f * dpi), new Rectangle(Px(7), ih, Width - Px(10), Px(24)), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            Theme.StrokeRound(g, hover ? Theme.Accent : Theme.Border, hover ? 1.5f : 1f, r, rad);
            PaintFocus(g, r, rad);
        }
    }

    /// <summary>
    /// Painel que abre por cima do relógio ao clicar no ícone da bandeja.
    /// Redimensionável pelas bordas de cima e da esquerda; o tamanho fica guardado.
    /// </summary>
    class TrayPanel : Form
    {
        const int DefaultW = 460, DefaultH = 620, MinW = 360, MinH = 500, MaxTiles = 40;

        readonly float dpi;
        readonly FlatButton btnTheme, btnSettings, btnCapture, btnRecord, btnFull, btnWindow, btnDelay, btnHistory, btnOpen, btnFolder, btnExit;
        readonly ToggleRow swEditor;
        readonly ThemedPanel footer;
        readonly List<ThumbTile> tiles = new List<ThumbTile>();
        readonly ToolTip tips = new ToolTip();
        int yLine1, yRecent, yClip, yLine2, yFooter;
        int visibleTiles;
        Size tileSize;
        bool clipVisible;
        readonly ThemedLabel lblVersion;
        RectangleF clipRect;
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
            MinimumSize = new Size(Px(MinW), Px(MinH));

            btnTheme = new FlatButton { Kind = BtnKind.Ghost, Size = new Size(Px(36), Px(36)) };
            btnTheme.Click += delegate { TrayApp.Instance.ToggleTheme(); };
            btnSettings = new FlatButton { Kind = BtnKind.Ghost, IconName = "settings", Size = new Size(Px(36), Px(36)), AccessibleName = S.T("Definições", "Settings") };
            btnSettings.Click += delegate { HideNow(); TrayApp.Instance.ShowSettings(); };
            tips.SetToolTip(btnSettings, S.T("Definições", "Settings"));

            // botões de captura compactos
            btnCapture = new FlatButton { Kind = BtnKind.Primary, IconName = "region", IconSize = 18, Text = S.T("Capturar janela ou região", "Capture window or region"), Badge = "PrtScn", Font = Theme.Font(13.5f * dpi) };
            btnCapture.Click += delegate { DoCapture(CaptureMode.Region, 250); };
            btnRecord = new FlatButton { Kind = BtnKind.Secondary, IconName = "record", IconSize = 18, Text = S.T("Gravar vídeo do ecrã", "Record screen video"), Font = Theme.Font(13 * dpi), Radius = 7 };
            btnRecord.Click += delegate { HideNow(); TrayApp.Instance.ToggleRecording(); };
            btnFull = Compact("full", S.T("Ecrã inteiro", "Full screen"), S.T("Capturar todos os ecrãs", "Capture all screens"));
            btnFull.Click += delegate { DoCapture(CaptureMode.FullScreen, 250); };
            btnWindow = Compact("window", S.T("Janela ativa", "Active window"), S.T("Capturar a janela que estava ativa", "Capture the window that was active"));
            btnWindow.Click += delegate { DoCapture(CaptureMode.ActiveWindow, 400); };
            btnDelay = Compact("timer", S.T("Atraso 3 s", "3 s delay"), S.T("Modo captura daqui a 3 segundos", "Capture mode in 3 seconds"));
            btnDelay.Click += delegate { DoCapture(CaptureMode.Region, 3000); };

            btnHistory = new FlatButton { Kind = BtnKind.Ghost, Text = S.T("Ver histórico", "View history"), Font = Theme.Font(13 * dpi) };
            btnHistory.Size = new Size(btnHistory.PreferredWidth, Px(28));
            btnHistory.Click += delegate { HideNow(); TrayApp.Instance.ShowHistory(); };

            swEditor = new ToggleRow { Text = S.T("Abrir no editor depois de capturar", "Open in the editor after capture") };
            swEditor.CheckedChanged += delegate
            {
                var st = TrayApp.Instance.Settings;
                string v = swEditor.Checked ? (st.AfterCapture == "full" ? "full" : "quick") : "none";
                if (st.AfterCapture != v) { st.AfterCapture = v; try { st.Save(); } catch { } }
            };

            footer = new ThemedPanel { Back = () => Theme.Dark ? Theme.Surface : Theme.Surface2, BorderTop = true };
            btnOpen = new FlatButton { Kind = BtnKind.Ghost, Text = S.T("Abrir imagem…", "Open image…") };
            btnOpen.Size = new Size(btnOpen.PreferredWidth, Px(36));
            btnOpen.Click += delegate { HideNow(); TrayApp.Instance.OpenImageDialog(); };
            btnFolder = new FlatButton { Kind = BtnKind.Ghost, Text = S.T("Pasta", "Folder") };
            btnFolder.Size = new Size(btnFolder.PreferredWidth, Px(36));
            btnFolder.Click += delegate { HideNow(); TrayApp.Instance.OpenFolder(); };
            btnExit = new FlatButton { Kind = BtnKind.Ghost, Text = S.T("Sair", "Exit") };
            btnExit.Size = new Size(btnExit.PreferredWidth, Px(36));
            btnExit.Click += delegate { HideNow(); TrayApp.Instance.Exit(); };
            // versão no rodapé: abre Definições › Sobre
            lblVersion = new ThemedLabel { Text = "v" + Updates.CurrentVersion, Font = Theme.Font(11.5f * dpi), Cursor = Cursors.Hand }.Muted();
            lblVersion.Click += delegate { HideNow(); TrayApp.Instance.ShowSettings("about"); };
            tips.SetToolTip(lblVersion, S.T("Versão e atualizações", "Version and updates"));
            footer.Controls.AddRange(new Control[] { btnOpen, btnFolder, btnExit, lblVersion });

            Controls.AddRange(new Control[] { btnTheme, btnSettings, btnCapture, btnRecord, btnFull, btnWindow, btnDelay, btnHistory, swEditor, footer });

            var st0 = TrayApp.Instance != null ? TrayApp.Instance.Settings : null;
            int w = st0 != null && st0.PanelWidth > 0 ? st0.PanelWidth : DefaultW;
            int h = st0 != null && st0.PanelHeight > 0 ? st0.PanelHeight : DefaultH;
            ClientSize = new Size(Px(Math.Max(MinW, w)), Px(Math.Max(MinH, h)));

            Theme.Changed += ApplyTheme;
            ApplyTheme();
            DoLayout();
        }

        FlatButton Compact(string icon, string text, string tip)
        {
            var b = new FlatButton { Kind = BtnKind.Secondary, IconName = icon, IconSize = 16, Text = text, Font = Theme.Font(12.5f * dpi), Radius = 7 };
            tips.SetToolTip(b, tip);
            return b;
        }

        // ================= disposição =================

        void DoLayout()
        {
            if (btnCapture == null) return;
            int W = ClientSize.Width, H = ClientSize.Height, pad = Px(14), gap = Px(8);

            btnSettings.Location = new Point(W - pad - Px(36), Px(10));
            btnTheme.Location = new Point(W - pad - Px(76), Px(10));

            int y = Px(58);
            btnCapture.Bounds = new Rectangle(pad, y, W - 2 * pad, Px(40));
            y += Px(40) + Px(6);
            int bw = (W - 2 * pad - 2 * Px(6)) / 3;
            btnFull.Bounds = new Rectangle(pad, y, bw, Px(34));
            btnWindow.Bounds = new Rectangle(pad + bw + Px(6), y, bw, Px(34));
            btnDelay.Bounds = new Rectangle(pad + 2 * (bw + Px(6)), y, W - 2 * pad - 2 * (bw + Px(6)), Px(34));
            y += Px(34) + Px(6);
            btnRecord.Bounds = new Rectangle(pad, y, W - 2 * pad, Px(36));
            y += Px(36) + Px(14);

            yLine1 = y;
            y += Px(10);
            yRecent = y;
            btnHistory.Location = new Point(W - pad - btnHistory.Width, y - Px(2));
            y += Px(32);

            // parte de baixo, fixa
            int footerH = Px(52);
            yFooter = H - footerH;
            yLine2 = yFooter - Px(44);
            yClip = yLine2 - Px(10) - Px(44);
            footer.Bounds = new Rectangle(1, yFooter, W - 2, footerH - 1);
            btnOpen.Location = new Point(Px(10), Px(8));
            btnFolder.Location = new Point(btnOpen.Right + Px(4), Px(8));
            btnExit.Location = new Point(footer.Width - Px(10) - btnExit.Width, Px(8));
            lblVersion.Location = new Point(btnExit.Left - Px(10) - lblVersion.Width, (footerH - lblVersion.Height) / 2);
            swEditor.Bounds = new Rectangle(Px(16), yLine2, W - 2 * Px(16), Px(44));

            // grelha de recentes: colunas pela largura, linhas pela altura disponível
            int availW = W - 2 * pad, availH = yClip - Px(12) - y;
            int cols = Math.Max(2, (availW + gap) / (Px(120) + gap));
            int tw = (availW - gap * (cols - 1)) / cols;
            int th = (int)(tw * 0.58f) + Px(24);
            int rows = Math.Max(1, (availH + gap) / (th + gap));
            int count = Math.Min(MaxTiles, cols * rows);

            while (tiles.Count < count)
            {
                var t = new ThumbTile();
                t.Click += delegate { if (t.Path != null) { HideNow(); TrayApp.Instance.OpenQuickEditor(t.Path, null); } };
                tiles.Add(t);
                Controls.Add(t);
            }
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                if (i < count)
                {
                    t.Bounds = new Rectangle(pad + (i % cols) * (tw + gap), y + (i / cols) * (th + gap), tw, th);
                    t.Visible = true;
                }
                else t.Visible = false;
            }
            bool changed = count != visibleTiles || tileSize != new Size(tw, th);
            visibleTiles = count;
            tileSize = new Size(tw, th);
            Invalidate();
            if (changed && Visible && !resizing) LoadRecents();
        }

        bool resizing;

        protected override void OnResize(EventArgs e) { base.OnResize(e); DoLayout(); }
        protected override void OnResizeBegin(EventArgs e) { resizing = true; base.OnResizeBegin(e); }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            resizing = false;
            LoadRecents();
            var st = TrayApp.Instance != null ? TrayApp.Instance.Settings : null;
            if (st != null && !TrayApp.Instance.Headless)
            {
                st.PanelWidth = (int)Math.Round(ClientSize.Width / dpi);
                st.PanelHeight = (int)Math.Round(ClientSize.Height / dpi);
                try { st.Save(); } catch { }
            }
        }

        // ================= redimensionar pelas bordas (janela sem moldura) =================

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84, HTCLIENT = 1, HTLEFT = 10, HTRIGHT = 11, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
            if (m.Msg == WM_NCHITTEST)
            {
                base.WndProc(ref m);
                if ((int)m.Result == HTCLIENT)
                {
                    long lp = m.LParam.ToInt64();
                    var p = PointToClient(new Point((short)(lp & 0xffff), (short)((lp >> 16) & 0xffff)));
                    int g = Px(7);
                    bool left = p.X < g, right = p.X >= ClientSize.Width - g, top = p.Y < g;
                    if (top && left) m.Result = (IntPtr)HTTOPLEFT;
                    else if (top && right) m.Result = (IntPtr)HTTOPRIGHT;
                    else if (left) m.Result = (IntPtr)HTLEFT;
                    else if (right) m.Result = (IntPtr)HTRIGHT;
                    else if (top) m.Result = (IntPtr)HTTOP;
                }
                return;
            }
            base.WndProc(ref m);
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
            string tt = Theme.Dark ? S.T("Mudar para tema claro", "Switch to light theme") : S.T("Mudar para tema escuro", "Switch to dark theme");
            btnTheme.AccessibleName = tt;
            tips.SetToolTip(btnTheme, tt);
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
            bool rec = TrayApp.Instance.IsRecording;
            btnRecord.Text = rec ? S.T("Parar a gravação", "Stop recording") : S.T("Gravar vídeo do ecrã", "Record screen video");
            btnRecord.IconName = rec ? "stop" : "record";
            btnRecord.Badge = st.HotkeyRecord != 0 ? Settings.HotkeyText(st.HotkeyRecord) : null;
            try { clipVisible = Clipboard.ContainsImage(); } catch { clipVisible = false; }
            LoadRecents();
        }

        public void ShowPanel()
        {
            Prepare();
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int w = Math.Min(Width, wa.Width - Px(24)), h = Math.Min(Height, wa.Height - Px(24));
            if (w != Width || h != Height) Size = new Size(w, h);
            MaximumSize = new Size(wa.Width - Px(24), wa.Height - Px(24));
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

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = Updates.Pending != null && clipRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var p = Updates.Pending;
            if (e.Button == MouseButtons.Left && p != null && clipRect.Contains(e.Location))
            {
                HideNow();
                UpdateForm.ShowFor(p);
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (resizing) return;
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
                string[] exts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".mp4" };
                files = Directory.Exists(folder)
                    ? new DirectoryInfo(folder).EnumerateFiles().Where(f => exts.Contains(f.Extension.ToLowerInvariant())).OrderByDescending(f => f.LastWriteTime).Take(visibleTiles).ToList()
                    : new List<FileInfo>();
            }
            catch { files = new List<FileInfo>(); }

            Size sz = new Size(Math.Max(1, tileSize.Width), Math.Max(1, tileSize.Height - Px(24)));
            var todo = new List<KeyValuePair<ThumbTile, string>>();
            for (int i = 0; i < tiles.Count; i++)
            {
                var t = tiles[i];
                if (i < files.Count && i < visibleTiles)
                {
                    var f = files[i];
                    t.Path = f.FullName;
                    t.Caption = Ago(f.LastWriteTime);
                    t.AccessibleName = f.Name;
                    tips.SetToolTip(t, f.Name);
                    string key = f.FullName + "|" + f.LastWriteTimeUtc.Ticks + "|" + sz.Width;
                    Bitmap b;
                    if (thumbCache.TryGetValue(key, out b)) t.Image = b;
                    else
                    {
                        // enquanto carrega, reaproveitar uma miniatura de outro tamanho, se existir
                        string prefix = f.FullName + "|" + f.LastWriteTimeUtc.Ticks + "|";
                        t.Image = thumbCache.Where(kv => kv.Key.StartsWith(prefix)).Select(kv => kv.Value).FirstOrDefault();
                        todo.Add(new KeyValuePair<ThumbTile, string>(t, key));
                    }
                }
                else { t.Path = null; t.Image = null; t.Caption = ""; }
                t.Invalidate();
            }

            int ver = ++loadVersion;
            if (todo.Count == 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                foreach (var kv in todo)
                {
                    if (ver != loadVersion) return;
                    string path = kv.Value.Substring(0, kv.Value.IndexOf('|'));
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
            }
            TextRenderer.DrawText(g, S.T("Recentes", "Recent"), Theme.Font(13 * dpi, FontStyle.Bold), new Rectangle(Px(16), yRecent, Px(200), Px(24)), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

            var clip = new RectangleF(Px(14), yClip, W - Px(28), Px(44));
            clipRect = clip;
            var upd = Updates.Pending;
            if (upd != null)
            {
                // versão nova no GitHub: faixa de destaque (clicar abre a janela de atualização)
                Theme.FillRound(g, Theme.Accent, clip, Px(8));
                TextRenderer.DrawText(g, S.T("A SnapStudio ", "SnapStudio ") + upd.Version + S.T(" está disponível", " is available"),
                    Theme.Font(12.5f * dpi, FontStyle.Bold), new Rectangle((int)clip.X + Px(14), (int)clip.Y, (int)clip.Width - Px(110), (int)clip.Height), Theme.OnAccent,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, S.T("Atualizar", "Update"), Theme.Font(12.5f * dpi, FontStyle.Bold),
                    new Rectangle((int)clip.X, (int)clip.Y, (int)clip.Width - Px(14), (int)clip.Height), Theme.OnAccent,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
            }
            else if (clipVisible)
            {
                Theme.FillRound(g, Theme.AccentSoft, clip, Px(8));
                g.DrawImage(ToolIcons.Get("clipboard", Px(18), Theme.AccentSoftText), clip.X + Px(12), clip.Y + (clip.Height - Px(18)) / 2);
                TextRenderer.DrawText(g, S.T("A última captura está na área de transferência — cole com Ctrl+V", "Your last capture is on the clipboard — paste with Ctrl+V"),
                    Theme.Font(12.5f * dpi), new Rectangle((int)clip.X + Px(40), (int)clip.Y, (int)clip.Width - Px(48), (int)clip.Height), Theme.AccentSoftText,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            }
            else
            {
                TextRenderer.DrawText(g, S.T("Dica: PrtScn captura · Shift+PrtScn grava vídeo · arraste as bordas para aumentar o painel", "Tip: PrtScn captures · Shift+PrtScn records video · drag the edges to enlarge this panel"), Theme.Font(12 * dpi),
                    Rectangle.Round(clip), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak);
            }

            // pega de redimensionar no canto superior esquerdo
            using (var p = new Pen(Theme.BorderStrong, 1.2f))
            {
                g.DrawLine(p, Px(4), Px(10), Px(10), Px(4));
                g.DrawLine(p, Px(4), Px(14), Px(14), Px(4));
            }
            using (var p = new Pen(Theme.BorderStrong)) g.DrawRectangle(p, 0, 0, W - 1, ClientSize.Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            Theme.Changed -= ApplyTheme;
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }
    }
}
