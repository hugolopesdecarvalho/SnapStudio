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
    /// <summary>
    /// Painel que abre por cima do relógio ao clicar no ícone da bandeja.
    /// Redimensionável pelas bordas de cima e da esquerda; o tamanho fica guardado.
    /// </summary>
    class TrayPanel : Form
    {
        const int DefaultW = 460, DefaultH = 620, MinW = 360, MinH = 500;
        static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".mp4" };

        readonly float dpi;
        readonly FlatButton btnTheme, btnSettings, btnCapture, btnRecord, btnFull, btnWindow, btnDelay, btnDelete, btnOpen, btnFolder, btnExit;
        readonly ToggleRow swEditor;
        readonly ThemedPanel footer;
        readonly CaptureGrid grid;
        readonly ToolTip tips = new ToolTip();
        int yLine1, yRecent, yClip, yLine2, yFooter;
        bool dialogOpen;
        bool clipVisible;
        readonly ThemedLabel lblVersion;
        RectangleF clipRect;
        DateTime lastHidden = DateTime.MinValue;

        static readonly Dictionary<string, Bitmap> thumbCache = new Dictionary<string, Bitmap>();
        volatile int loadVersion;

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

            // todas as capturas da pasta: clique seleciona, arrastar seleciona várias, duplo clique abre
            grid = new CaptureGrid(thumbCache)
            {
                EmptyText = S.T("Ainda não há capturas.\nPrima PrtScn para fazer a primeira.", "No captures yet.\nPress PrtScn to take your first one."),
                CaptionOf = it => Ago(it.Time),
            };
            grid.OpenItem += it => { HideNow(); TrayApp.Instance.OpenEditor(it.Path, null); };
            grid.MenuRequested += ShowMenu;
            grid.SelectionChanged += UpdateSelectionUi;
            btnDelete = new FlatButton { Kind = BtnKind.Ghost, IconName = "delete", Font = Theme.Font(12.5f * dpi), Visible = false };
            btnDelete.Click += delegate { DeletePaths(SelectedPaths()); };
            tips.SetToolTip(btnDelete, S.T("Enviar as capturas selecionadas para a Reciclagem (Delete)", "Send the selected captures to the Recycle Bin (Delete)"));

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

            Controls.AddRange(new Control[] { btnTheme, btnSettings, btnCapture, btnRecord, btnFull, btnWindow, btnDelay, btnDelete, grid, swEditor, footer });

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
            int W = ClientSize.Width, H = ClientSize.Height, pad = Px(14);

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
            btnDelete.Location = new Point(W - pad - btnDelete.Width, y - Px(2));
            y += Px(30);

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

            // margem de 7 px livre dos dois lados: as bordas continuam a servir para redimensionar
            grid.Bounds = new Rectangle(Px(7), y, W - 2 * Px(7), Math.Max(Px(60), yClip - Px(10) - y));
            Invalidate();
        }

        bool resizing;

        protected override void OnResize(EventArgs e) { base.OnResize(e); DoLayout(); }
        protected override void OnResizeBegin(EventArgs e) { resizing = true; base.OnResizeBegin(e); }

        protected override void OnResizeEnd(EventArgs e)
        {
            base.OnResizeEnd(e);
            resizing = false;
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
            grid.ClearSelection();   // cada abertura do painel começa sem seleção
            Reload();
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
            if (resizing || dialogOpen) return;
            HideNow();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { HideNow(); return true; }
            if (grid.Focused)
            {
                switch (keyData)
                {
                    case Keys.Enter: OpenSelected(); return true;
                    case Keys.Delete: DeletePaths(SelectedPaths()); return true;
                    case Keys.F2: RenameSelected(); return true;
                    case Keys.Control | Keys.C: CopyPaths(SelectedPaths()); return true;
                    case Keys.Control | Keys.A: grid.SelectAll(); return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ================= capturas =================

        string Folder { get { return TrayApp.Instance.Settings.OutputFolder; } }

        /// <summary>Lê todas as capturas da pasta (mais recentes primeiro), mantendo a seleção.</summary>
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

            var items = files.Select(f => new CaptureItem
            {
                Path = f.FullName,
                Name = f.Name,
                Time = f.LastWriteTime,
                Size = f.Length,
                Key = f.FullName + "|" + f.LastWriteTimeUtc.Ticks + "|" + f.Length,
            }).ToList();
            foreach (var it in items) it.Selected = sel.Contains(it.Path);

            // libertar as miniaturas de ficheiros que já não existem ou mudaram
            var keep = new HashSet<string>(items.Select(i => i.Key));
            foreach (var k in thumbCache.Keys.Where(k => !keep.Contains(k)).ToList()) { thumbCache[k].Dispose(); thumbCache.Remove(k); }

            grid.SetItems(items);
            UpdateSelectionUi();
            LoadThumbnails(items);
        }

        void LoadThumbnails(List<CaptureItem> items)
        {
            int ver = ++loadVersion;
            var todo = items.Where(i => !thumbCache.ContainsKey(i.Key)).ToList();
            if (todo.Count == 0) return;
            if (!IsHandleCreated) CreateHandle();
            Size sz = grid.ThumbSize;
            ThreadPool.QueueUserWorkItem(delegate
            {
                // por ordem (mais recentes primeiro): as que se veem chegam logo
                foreach (var it in todo)
                {
                    if (ver != loadVersion || IsDisposed) return;
                    Bitmap th = null;
                    Size dim;
                    try { th = ImageUtil.ThumbCover(it.Path, sz, out dim); } catch { }
                    if (th == null) continue;
                    var item = it;
                    var bmp = th;
                    try
                    {
                        BeginInvoke((Action)delegate
                        {
                            if (IsDisposed || thumbCache.ContainsKey(item.Key)) { bmp.Dispose(); return; }
                            thumbCache[item.Key] = bmp;
                            grid.Invalidate();
                        });
                    }
                    catch { bmp.Dispose(); return; }
                }
            });
        }

        void UpdateSelectionUi()
        {
            int n = grid.Selected.Count;
            btnDelete.Visible = n > 0;
            if (n > 0)
            {
                btnDelete.Text = S.T("Apagar", "Delete") + (n > 1 ? " (" + n + ")" : "");
                btnDelete.Size = new Size(btnDelete.PreferredWidth, Px(28));
                btnDelete.Location = new Point(ClientSize.Width - Px(14) - btnDelete.Width, yRecent - Px(2));
            }
            Invalidate(new Rectangle(0, yRecent - Px(4), ClientSize.Width, Px(32)));
        }

        List<string> SelectedPaths() { return grid.Selected.Select(i => i.Path).ToList(); }

        void OpenSelected()
        {
            var paths = SelectedPaths();
            if (paths.Count == 0) return;
            HideNow();
            foreach (var p in paths.Take(8)) TrayApp.Instance.OpenEditor(p, null);
        }

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
            }
            catch (Exception ex) { Ask(ex.Message, MessageBoxButtons.OK); }
        }

        void DeletePaths(List<string> paths)
        {
            if (paths.Count == 0) return;
            string msg = paths.Count == 1
                ? S.T("Enviar \"", "Send \"") + Path.GetFileName(paths[0]) + S.T("\" para a Reciclagem?", "\" to the Recycle Bin?")
                : S.T("Enviar ", "Send ") + paths.Count + S.T(" capturas para a Reciclagem?", " captures to the Recycle Bin?");
            if (Ask(msg, MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            foreach (var p in paths)
            {
                try
                {
                    FileSystem.DeleteFile(p, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                    Sidecar.Delete(p);
                }
                catch (Exception ex) { Ask(ex.Message, MessageBoxButtons.OK); }
            }
            grid.ClearSelection();
            Reload();
        }

        void RenameSelected()
        {
            var paths = SelectedPaths();
            if (paths.Count != 1) return;
            string oldPath = paths[0];
            string name;
            dialogOpen = true;
            try { name = Prompt.Show(this, S.T("Mudar o nome", "Rename"), S.T("Novo nome:", "New name:"), Path.GetFileNameWithoutExtension(oldPath)); }
            finally { dialogOpen = false; }
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.Trim();
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { Ask(S.T("O nome contém caracteres inválidos.", "The name contains invalid characters."), MessageBoxButtons.OK); return; }
            string newPath = Path.Combine(Path.GetDirectoryName(oldPath), name + Path.GetExtension(oldPath));
            if (string.Equals(newPath, oldPath, StringComparison.OrdinalIgnoreCase)) return;
            if (File.Exists(newPath)) { Ask(S.T("Já existe um ficheiro com esse nome.", "A file with that name already exists."), MessageBoxButtons.OK); return; }
            try { File.Move(oldPath, newPath); Sidecar.Move(oldPath, newPath); Reload(); }
            catch (Exception ex) { Ask(ex.Message, MessageBoxButtons.OK); }
        }

        void ShowInFolder()
        {
            var paths = SelectedPaths();
            HideNow();
            try
            {
                if (paths.Count > 0) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + paths[0] + "\"");
                else TrayApp.Instance.OpenFolder();
            }
            catch { }
        }

        void ShowMenu(Point p)
        {
            int n = grid.Selected.Count;
            var m = new ContextMenuStrip { Renderer = new ThemedRenderer(), Font = Theme.Font(13 * dpi) };
            var open = m.Items.Add(S.T("Abrir no editor", "Open in editor"), null, delegate { OpenSelected(); });
            open.Font = Theme.Font(13 * dpi, FontStyle.Bold);
            m.Items.Add(S.T("Copiar", "Copy"), null, delegate { CopyPaths(SelectedPaths()); });
            m.Items.Add(S.T("Mostrar na pasta", "Show in folder"), null, delegate { ShowInFolder(); });
            m.Items.Add(new ToolStripSeparator());
            var rename = m.Items.Add(S.T("Mudar o nome (F2)", "Rename (F2)"), null, delegate { RenameSelected(); });
            rename.Enabled = n == 1;
            m.Items.Add(n > 1 ? S.T("Apagar ", "Delete ") + n + S.T(" capturas", " captures") : S.T("Apagar", "Delete"), null, delegate { DeletePaths(SelectedPaths()); });
            m.Closed += delegate { BeginInvoke((Action)m.Dispose); };
            m.Show(grid, p);
        }

        /// <summary>Caixa de mensagem por cima do painel, sem que o painel se feche ao perder o foco.</summary>
        DialogResult Ask(string text, MessageBoxButtons buttons)
        {
            dialogOpen = true;
            try { return MessageBox.Show(this, text, "SnapStudio", buttons, buttons == MessageBoxButtons.YesNo ? MessageBoxIcon.Question : MessageBoxIcon.None); }
            finally { dialogOpen = false; }
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
            var titleFont = Theme.Font(13 * dpi, FontStyle.Bold);
            string title = S.T("Capturas", "Captures");
            TextRenderer.DrawText(g, title, titleFont, new Rectangle(Px(16), yRecent, Px(200), Px(24)), Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            int tx = Px(16) + TextRenderer.MeasureText(g, title, titleFont, Size.Empty, TextFormatFlags.NoPadding).Width + Px(8);
            if (grid.Items.Count > 0)
                TextRenderer.DrawText(g, grid.Items.Count.ToString(), Theme.Font(12 * dpi), new Rectangle(tx, yRecent, Px(60), Px(24)), Theme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            if (!btnDelete.Visible && grid.Items.Count > 0)
                TextRenderer.DrawText(g, S.T("Duplo clique para editar", "Double-click to edit"), Theme.Font(12 * dpi), new Rectangle(Px(120), yRecent, W - Px(16) - Px(120), Px(24)), Theme.Muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

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
