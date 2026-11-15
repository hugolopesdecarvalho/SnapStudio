using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace SnapStudio
{
    /// <summary>Caixa que regista uma combinação de teclas (inclui PrtScn).</summary>
    class HotkeyBox : TextBox
    {
        int value;
        public int Value { get { return value; } set { this.value = value; Text = Settings.HotkeyText(value); } }

        public HotkeyBox() { ReadOnly = true; ShortcutsEnabled = false; BorderStyle = BorderStyle.FixedSingle; }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.Tab) return base.ProcessCmdKey(ref msg, keyData);
            if (code == Keys.Back || code == Keys.Delete) { Value = 0; return true; }
            if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu) return true;
            Value = (int)keyData;
            return true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            // PrtScn só gera KeyUp
            if (e.KeyCode == Keys.PrintScreen) Value = (int)e.KeyData;
            e.Handled = true;
        }
    }

    /// <summary>Definições com secções: Geral, Aparência, Depois de capturar, Atalhos, Ficheiros.</summary>
    class SettingsForm : Form
    {
        readonly Settings s;
        readonly float dpi;
        readonly string origTheme, origAccent;
        string theme, accent;

        readonly Dictionary<string, Panel> pages = new Dictionary<string, Panel>();
        readonly List<FlatButton> navButtons = new List<FlatButton>();
        readonly ToggleRow tStartup, tPrtScn, tClipboard, tNotify, tCursor;
        readonly List<ThemeCard> themeCards = new List<ThemeCard>();
        readonly List<FlatButton> accentButtons = new List<FlatButton>();
        readonly List<OptionRow> afterRows = new List<OptionRow>();
        readonly HotkeyBox hkRegion, hkWindow, hkFull, hkRecord;
        readonly List<FlatButton> fpsButtons = new List<FlatButton>();
        readonly TextBox txtFolder;
        readonly FlatButton fmtPng, fmtJpg;
        readonly List<TextBox> textBoxes = new List<TextBox>();
        readonly ToggleRow tAutoUpdate;
        readonly ThemedLabel lblUpdate;
        readonly FlatButton btnCheck;
        readonly int pageW;

        int Px(float v) { return (int)Math.Round(v * dpi); }

        public SettingsForm(Settings settings) : this(settings, null) { }

        public SettingsForm(Settings settings, string startPage)
        {
            s = settings;
            dpi = Dpi.Scale;
            origTheme = theme = s.ThemeMode;
            origAccent = accent = s.Accent;

            Text = S.T("SnapStudio — Definições", "SnapStudio — Settings");
            Icon = AppIcon.FormIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Px(880), Px(620));
            Font = Theme.Font(13.5f * dpi);
            ShowInTaskbar = true;
            KeyPreview = true;

            // --- navegação
            var nav = new ThemedPanel { Dock = DockStyle.Left, Width = Px(220), BorderRight = true, Back = () => Theme.Bg };
            nav.Paint += (o, e) =>
            {
                using (var logo = AppIcon.Draw(Px(24))) e.Graphics.DrawImage(logo, Px(20), Px(22));
                TextRenderer.DrawText(e.Graphics, S.T("Definições", "Settings"), Theme.Font(16 * dpi, FontStyle.Bold), new Point(Px(54), Px(24)), Theme.Text, TextFormatFlags.NoPadding);
            };
            var content = new ThemedPanel { Dock = DockStyle.Fill, Back = () => Theme.Bg };
            var footer = new ThemedPanel { Dock = DockStyle.Bottom, Height = Px(68), BorderTop = true, Back = () => Theme.Bg };
            var ok = new FlatButton { Kind = BtnKind.Primary, Text = S.T("Guardar", "Save"), Size = new Size(Px(110), Px(40)) };
            var cancel = new FlatButton { Kind = BtnKind.Secondary, Text = S.T("Cancelar", "Cancel"), Size = new Size(Px(110), Px(40)) };
            ok.Click += Ok_Click;
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            footer.Controls.Add(ok); footer.Controls.Add(cancel);
            footer.Layout += delegate
            {
                ok.Location = new Point(footer.Width - Px(32) - ok.Width, Px(14));
                cancel.Location = new Point(ok.Left - Px(10) - cancel.Width, Px(14));
            };

            int pw = Px(880 - 220 - 64);
            pageW = pw;

            // Geral
            var pGeneral = Page(content, "general", S.T("Geral", "General"));
            int y = Px(80);
            tStartup = Toggle(pGeneral, S.T("Iniciar automaticamente com o Windows", "Start automatically with Windows"), null, SafeStartup(), ref y, pw);
            tPrtScn = Toggle(pGeneral, S.T("Usar a tecla Print Screen para entrar em modo captura", "Use the Print Screen key to enter capture mode"),
                             S.T("Clique numa janela, arraste uma região ou prima Enter para o ecrã inteiro", "Click a window, drag a region, or press Enter for full screen"), s.UsePrintScreen, ref y, pw);
            if (Settings.WindowsSnippingOwnsPrintScreen)
            {
                var warn = new LinkLabel
                {
                    AutoSize = false,
                    Size = new Size(pw, Px(46)),
                    Location = new Point(Px(32), y),
                    Font = Theme.Font(12.5f * dpi),
                    Text = S.T("O Windows está a usar PrtScn para a Ferramenta de Recorte. Desligue essa opção para a SnapStudio receber a tecla: abrir Definições do Windows",
                               "Windows is using PrtScn for the Snipping Tool. Turn that off so SnapStudio gets the key: open Windows Settings"),
                    BackColor = Color.Transparent,
                };
                int idx = warn.Text.LastIndexOf(':') + 2;
                warn.LinkArea = new LinkArea(idx, warn.Text.Length - idx);
                warn.LinkClicked += delegate { OpenKeyboardSettings(); };
                Theme.Changed += () => { if (!warn.IsDisposed) { warn.ForeColor = Theme.Danger; warn.LinkColor = Theme.Accent; } };
                warn.ForeColor = Theme.Danger; warn.LinkColor = Theme.Accent;
                pGeneral.Controls.Add(warn);
            }

            // Aparência
            var pLook = Page(content, "look", S.T("Aparência", "Appearance"));
            y = Px(80);
            Section(pLook, S.T("Tema", "Theme"), ref y);
            string[][] themes = { new[] { "system", S.T("Igual ao Windows", "Same as Windows") }, new[] { "light", S.T("Claro", "Light") }, new[] { "dark", S.T("Escuro", "Dark") } };
            int cw = (pw - 2 * Px(12)) / 3;
            for (int i = 0; i < 3; i++)
            {
                string mode = themes[i][0];
                var c = new ThemeCard { Text = themes[i][1], PreviewMode = mode, Group = "theme", Value = mode, Size = new Size(cw, Px(140)), Location = new Point(Px(32) + i * (cw + Px(12)), y) };
                c.Checked = theme == mode;
                c.CheckedChanged += delegate { if (c.Checked && theme != mode) { theme = mode; Theme.Configure(theme, accent); } };
                pLook.Controls.Add(c);
                themeCards.Add(c);
            }
            y += Px(140) + Px(28);
            Section(pLook, S.T("Cor de destaque", "Accent color"), ref y);
            string[][] accents = { new[] { "indigo", S.T("Índigo", "Indigo") }, new[] { "blue", S.T("Azul", "Blue") }, new[] { "teal", S.T("Verde-azulado", "Teal") }, new[] { "orange", S.T("Laranja", "Orange") } };
            int ax = Px(32);
            foreach (var a in accents)
            {
                string name = a[0];
                var b = new FlatButton { Kind = BtnKind.Swatch, RoundShape = true, SwatchColor = Theme.AccentColor(name, false), Size = new Size(Px(38), Px(38)), Location = new Point(ax, y), AccessibleName = a[1], Tag = name };
                b.Click += delegate { SetAccent(name); };
                pLook.Controls.Add(b);
                accentButtons.Add(b);
                ax += Px(46);
            }
            var bWin = new FlatButton { Kind = BtnKind.Secondary, Text = S.T("Usar a cor do Windows", "Use Windows accent color"), Radius = 19, Tag = "windows", Location = new Point(ax + Px(8), y) };
            bWin.Size = new Size(bWin.PreferredWidth, Px(38));
            bWin.Click += delegate { SetAccent("windows"); };
            pLook.Controls.Add(bWin);
            accentButtons.Add(bWin);
            UpdateAccentButtons();

            // Depois de capturar
            var pAfter = Page(content, "after", S.T("Depois de capturar", "After capture"));
            y = Px(80);
            Section(pAfter, S.T("Abrir", "Open"), ref y);
            string[][] opts = {
                new[] { "quick", S.T("Editor rápido (pop-up junto ao relógio)", "Quick editor (pop-up next to the clock)"), S.T("Seta, caixa, texto e esbater à mão; Copiar e fechar", "Arrow, box, text and blur at hand; Copy and close") },
                new[] { "full", S.T("Editor completo", "Full editor"), S.T("Janela grande com todas as ferramentas", "Large window with every tool") },
                new[] { "none", S.T("Nada — só guardar e copiar", "Nothing — just save and copy"), S.T("Mostra uma notificação; clique nela para editar", "Shows a notification; click it to edit") } };
            foreach (var o in opts)
            {
                var r = new OptionRow { Text = o[1], Subtitle = o[2], Group = "after", Value = o[0], Size = new Size(pw, Px(62)), Location = new Point(Px(32), y) };
                r.Checked = s.AfterCapture == o[0];
                pAfter.Controls.Add(r);
                afterRows.Add(r);
                y += Px(66);
            }
            y += Px(14);
            tClipboard = Toggle(pAfter, S.T("Copiar para a área de transferência", "Copy to the clipboard"), S.T("A imagem fica em memória para colar com Ctrl+V", "The image stays in memory to paste with Ctrl+V"), s.CopyToClipboard, ref y, pw);
            tNotify = Toggle(pAfter, S.T("Mostrar notificação", "Show a notification"), S.T("Quando nada abre depois de capturar", "When nothing opens after capture"), s.ShowNotification, ref y, pw);

            // Atalhos
            var pKeys = Page(content, "keys", S.T("Atalhos", "Shortcuts"));
            y = Px(80);
            hkRegion = HotkeyRow(pKeys, S.T("Modo captura (janela / região)", "Capture mode (window / region)"), s.HotkeyRegion, ref y, pw);
            hkWindow = HotkeyRow(pKeys, S.T("Janela ativa", "Active window"), s.HotkeyWindow, ref y, pw);
            hkFull = HotkeyRow(pKeys, S.T("Todos os ecrãs", "All screens"), s.HotkeyFull, ref y, pw);
            hkRecord = HotkeyRow(pKeys, S.T("Gravar vídeo (iniciar / parar)", "Record video (start / stop)"), s.HotkeyRecord, ref y, pw);
            pKeys.Controls.Add(new ThemedLabel { Text = S.T("Clique na caixa e prima a combinação. Backspace limpa.", "Click a box and press the keys. Backspace clears."), Font = Theme.Font(12.5f * dpi), Location = new Point(Px(32), y + Px(6)) }.Muted());

            // Ficheiros
            var pFiles = Page(content, "files", S.T("Ficheiros", "Files"));
            y = Px(80);
            Section(pFiles, S.T("Pasta das capturas", "Captures folder"), ref y);
            txtFolder = new TextBox { Text = s.OutputFolder, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Font(13.5f * dpi), Location = new Point(Px(32), y + Px(4)), Width = pw - Px(130) };
            textBoxes.Add(txtFolder);
            var browse = new FlatButton { Kind = BtnKind.Secondary, Text = S.T("Escolher…", "Browse…"), Size = new Size(Px(116), Px(36)), Location = new Point(Px(32) + pw - Px(116), y) };
            browse.Click += delegate
            {
                using (var d = new FolderBrowserDialog { SelectedPath = txtFolder.Text, ShowNewFolderButton = true })
                    if (d.ShowDialog(this) == DialogResult.OK) txtFolder.Text = d.SelectedPath;
            };
            pFiles.Controls.Add(txtFolder); pFiles.Controls.Add(browse);
            y += Px(64);
            Section(pFiles, S.T("Formato", "Format"), ref y);
            fmtPng = new FlatButton { Kind = BtnKind.Secondary, Text = "PNG", Size = new Size(Px(90), Px(38)), Location = new Point(Px(32), y), Checked = s.Format != "jpg" };
            fmtJpg = new FlatButton { Kind = BtnKind.Secondary, Text = "JPG", Size = new Size(Px(90), Px(38)), Location = new Point(Px(130), y), Checked = s.Format == "jpg" };
            fmtPng.Click += delegate { fmtPng.Checked = true; fmtJpg.Checked = false; };
            fmtJpg.Click += delegate { fmtJpg.Checked = true; fmtPng.Checked = false; };
            pFiles.Controls.Add(fmtPng); pFiles.Controls.Add(fmtJpg);
            pFiles.Controls.Add(new ThemedLabel { Text = S.T("PNG mantém o texto nítido; JPG gera ficheiros mais pequenos.", "PNG keeps text crisp; JPG makes smaller files."), Font = Theme.Font(12.5f * dpi), Location = new Point(Px(32), y + Px(48)) }.Muted());

            // Vídeo
            var pVideo = Page(content, "video", S.T("Vídeo", "Video"));
            y = Px(80);
            Section(pVideo, S.T("Imagens por segundo", "Frames per second"), ref y);
            int fx = Px(32);
            foreach (int f in new[] { 15, 30, 60 })
            {
                var fb = new FlatButton { Kind = BtnKind.Secondary, Text = f + " fps", Size = new Size(Px(90), Px(38)), Location = new Point(fx, y), Checked = s.VideoFps == f, Tag = f };
                fb.Click += delegate { foreach (var o in fpsButtons) o.Checked = o == fb; };
                fpsButtons.Add(fb);
                pVideo.Controls.Add(fb);
                fx += Px(98);
            }
            pVideo.Controls.Add(new ThemedLabel { Text = S.T("30 fps chega para demonstrações; 60 fps fica mais fluido mas gera ficheiros maiores.", "30 fps is fine for demos; 60 fps is smoother but makes bigger files."), Font = Theme.Font(12.5f * dpi), Location = new Point(Px(32), y + Px(48)) }.Muted());
            y += Px(92);
            tCursor = Toggle(pVideo, S.T("Mostrar o cursor do rato", "Show the mouse pointer"), null, s.VideoCursor, ref y, pw);
            pVideo.Controls.Add(new ThemedLabel { Text = S.T("Os vídeos são guardados em MP4 (H.264) na pasta das capturas, sem som.", "Videos are saved as MP4 (H.264) in the captures folder, without sound."), Font = Theme.Font(12.5f * dpi), Location = new Point(Px(32), y + Px(6)) }.Muted());

            // Sobre: versão e atualizações
            var pAbout = Page(content, "about", S.T("Sobre", "About"));
            y = Px(84);
            pAbout.Controls.Add(new PictureBox { Image = AppIcon.Draw(Px(56)), SizeMode = PictureBoxSizeMode.AutoSize, Location = new Point(Px(32), y), BackColor = Color.Transparent });
            pAbout.Controls.Add(new ThemedLabel { Text = "SnapStudio", Font = Theme.Font(18 * dpi, FontStyle.Bold), Location = new Point(Px(102), y + Px(4)) });
            pAbout.Controls.Add(new ThemedLabel { Text = S.T("Versão ", "Version ") + Updates.CurrentVersion, Font = Theme.Font(13.5f * dpi), Location = new Point(Px(102), y + Px(34)) }.Muted());
            y += Px(88);
            Section(pAbout, S.T("Atualizações", "Updates"), ref y);
            lblUpdate = new ThemedLabel { AutoSize = false, Size = new Size(pw - Px(230), Px(38)), Location = new Point(Px(32), y), Font = Theme.Font(13 * dpi), TextAlign = ContentAlignment.MiddleLeft }.Muted();
            btnCheck = new FlatButton { Kind = BtnKind.Secondary, Text = S.T("Procurar atualizações", "Check for updates") };
            btnCheck.Size = new Size(btnCheck.PreferredWidth, Px(38));
            btnCheck.Location = new Point(Px(32) + pw - btnCheck.Width, y);
            btnCheck.Click += delegate
            {
                var p = Updates.Pending;
                if (p != null) UpdateForm.ShowFor(p); else Updates.Check(true);
            };
            pAbout.Controls.Add(lblUpdate);
            pAbout.Controls.Add(btnCheck);
            y += Px(54);
            tAutoUpdate = Toggle(pAbout, S.T("Procurar atualizações automaticamente", "Check for updates automatically"),
                                 S.T("Uma vez por dia, na página de versões do GitHub. Não envia dados seus.", "Once a day, on the GitHub releases page. No personal data is sent."),
                                 s.AutoUpdateCheck, ref y, pw);
            y += Px(8);
            var lnkRepo = new LinkLabel { AutoSize = true, Location = new Point(Px(32), y), Font = Theme.Font(13 * dpi), BackColor = Color.Transparent, Text = S.T("Página do projeto no GitHub", "Project page on GitHub") };
            lnkRepo.LinkClicked += delegate { Updates.Open(Updates.RepoUrl); };
            var lnkReleases = new LinkLabel { AutoSize = true, Location = new Point(Px(32), y + Px(30)), Font = Theme.Font(13 * dpi), BackColor = Color.Transparent, Text = S.T("Todas as versões", "All releases") };
            lnkReleases.LinkClicked += delegate { Updates.Open(Updates.ReleasesUrl); };
            foreach (var l in new[] { lnkRepo, lnkReleases })
            {
                var link = l;
                link.LinkColor = Theme.Accent; link.ActiveLinkColor = Theme.Accent;
                Theme.Changed += () => { if (!link.IsDisposed) { link.LinkColor = Theme.Accent; link.ActiveLinkColor = Theme.Accent; } };
                pAbout.Controls.Add(link);
            }
            UpdateAboutStatus();
            Updates.Changed += UpdateAboutStatus;

            // botões de navegação
            int ny = Px(70);
            foreach (var kv in new[] { new[] { "general", S.T("Geral", "General") }, new[] { "look", S.T("Aparência", "Appearance") }, new[] { "after", S.T("Depois de capturar", "After capture") }, new[] { "keys", S.T("Atalhos", "Shortcuts") }, new[] { "files", S.T("Ficheiros", "Files") }, new[] { "video", S.T("Vídeo", "Video") }, new[] { "about", S.T("Sobre", "About") } })
            {
                string key = kv[0];
                var b = new FlatButton { Kind = BtnKind.Nav, Text = kv[1], Size = new Size(Px(196), Px(40)), Location = new Point(Px(12), ny), Font = Theme.Font(14 * dpi), Tag = key };
                b.Click += delegate { ShowPage(key); };
                nav.Controls.Add(b);
                navButtons.Add(b);
                ny += Px(42);
            }

            Controls.Add(content);
            Controls.Add(footer);
            Controls.Add(nav);
            ShowPage(startPage != null && pages.ContainsKey(startPage) ? startPage : "general");

            Theme.Changed += ApplyTheme;
            ApplyTheme();
        }

        static bool SafeStartup() { try { return Settings.StartWithWindows; } catch { return false; } }

        Panel Page(Control parent, string key, string title)
        {
            var p = new ThemedPanel { Dock = DockStyle.Fill, Back = () => Theme.Bg, Visible = false };
            p.Controls.Add(new ThemedLabel { Text = title, Font = Theme.Font(22 * dpi, FontStyle.Bold), Location = new Point(Px(30), Px(28)) });
            parent.Controls.Add(p);
            pages[key] = p;
            return p;
        }

        void Section(Control page, string text, ref int y)
        {
            page.Controls.Add(new ThemedLabel { Text = text, Font = Theme.Font(14 * dpi, FontStyle.Bold), Location = new Point(Px(32), y) });
            y += Px(32);
        }

        ToggleRow Toggle(Control page, string text, string sub, bool value, ref int y, int w)
        {
            var t = new ToggleRow { Text = text, Subtitle = sub, Checked = value, Size = new Size(w, Px(string.IsNullOrEmpty(sub) ? 44 : 56)), Location = new Point(Px(32), y) };
            page.Controls.Add(t);
            y += t.Height + Px(10);
            return t;
        }

        HotkeyBox HotkeyRow(Control page, string label, int value, ref int y, int w)
        {
            page.Controls.Add(new ThemedLabel { Text = label, Font = Theme.Font(13.5f * dpi), Location = new Point(Px(32), y + Px(8)) });
            var hb = new HotkeyBox { Value = value, Font = Theme.Font(13.5f * dpi), Width = Px(220), Location = new Point(Px(32) + w - Px(220), y + Px(4)) };
            page.Controls.Add(hb);
            textBoxes.Add(hb);
            y += Px(48);
            return hb;
        }

        public void ShowPage(string key)
        {
            if (!pages.ContainsKey(key)) return;
            foreach (var kv in pages) kv.Value.Visible = kv.Key == key;
            foreach (var b in navButtons) b.Checked = (string)b.Tag == key;
        }

        /// <summary>Texto e botão da secção Atualizações (página Sobre).</summary>
        void UpdateAboutStatus()
        {
            if (IsDisposed || lblUpdate == null || btnCheck == null) return;
            var p = Updates.Pending;
            if (Updates.Checking) lblUpdate.Text = S.T("A procurar…", "Checking…");
            else if (p != null) lblUpdate.Text = S.T("Está disponível a versão ", "Version ") + p.Version + S.T(".", " is available.");
            else if (s.LastUpdateCheck > 0) lblUpdate.Text = S.T("Última verificação: ", "Last checked: ") + new DateTime(s.LastUpdateCheck, DateTimeKind.Utc).ToLocalTime().ToString("g");
            else lblUpdate.Text = S.T("Ainda não foi verificado.", "Not checked yet.");
            btnCheck.Kind = p != null ? BtnKind.Primary : BtnKind.Secondary;
            btnCheck.Text = p != null ? S.T("Ver e descarregar…", "View and download…") : S.T("Procurar atualizações", "Check for updates");
            btnCheck.Size = new Size(btnCheck.PreferredWidth, Px(38));
            btnCheck.Left = Px(32) + pageW - btnCheck.Width;
            btnCheck.Enabled = !Updates.Checking;
        }

        void SetAccent(string name)
        {
            accent = name;
            UpdateAccentButtons();
            Theme.Configure(theme, accent);
        }

        void UpdateAccentButtons()
        {
            foreach (var b in accentButtons) b.Checked = (string)b.Tag == accent;
        }

        void ApplyTheme()
        {
            if (IsDisposed) return;
            BackColor = Theme.Bg;
            foreach (var tb in textBoxes) { tb.BackColor = Theme.Surface2; tb.ForeColor = Theme.Text; }
            foreach (var b in accentButtons) if ((string)b.Tag != "windows") b.SwatchColor = Theme.AccentColor((string)b.Tag, Theme.Dark);
            Theme.StyleWindow(this);
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.StyleWindow(this); }

        public static void OpenKeyboardSettings()
        {
            try { System.Diagnostics.Process.Start("ms-settings:easeofaccess-keyboard"); } catch { }
        }

        void Ok_Click(object sender, EventArgs e)
        {
            string folder = txtFolder.Text.Trim();
            try { Directory.CreateDirectory(folder); }
            catch (Exception ex)
            {
                ShowPage("files");
                MessageBox.Show(this, S.T("Pasta inválida: ", "Invalid folder: ") + ex.Message, "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            s.OutputFolder = folder;
            s.Format = fmtJpg.Checked ? "jpg" : "png";
            s.UsePrintScreen = tPrtScn.Checked;
            foreach (var r in afterRows) if (r.Checked) s.AfterCapture = (string)r.Value;
            s.CopyToClipboard = tClipboard.Checked;
            s.ShowNotification = tNotify.Checked;
            s.HotkeyRegion = hkRegion.Value;
            s.HotkeyWindow = hkWindow.Value;
            s.HotkeyFull = hkFull.Value;
            s.HotkeyRecord = hkRecord.Value;
            foreach (var fb in fpsButtons) if (fb.Checked) s.VideoFps = (int)fb.Tag;
            s.VideoCursor = tCursor.Checked;
            s.AutoUpdateCheck = tAutoUpdate.Checked;
            s.ThemeMode = theme;
            s.Accent = accent;
            try { Settings.StartWithWindows = tStartup.Checked; } catch { }
            DialogResult = DialogResult.OK;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape) { DialogResult = DialogResult.Cancel; return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Theme.Changed -= ApplyTheme;
            Updates.Changed -= UpdateAboutStatus;
            // cancelado: repor o tema original
            if (DialogResult != DialogResult.OK && (theme != origTheme || accent != origAccent))
                Theme.Configure(origTheme, origAccent);
            base.OnFormClosed(e);
        }
    }
}
