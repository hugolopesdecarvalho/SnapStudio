using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SnapStudio
{
    /// <summary>Janela oculta que recebe as teclas globais e o pedido de "mostrar" de uma segunda instância.</summary>
    class MessageWindow : NativeWindow, IDisposable
    {
        public static readonly uint WM_SHOWME = Native.RegisterWindowMessage("SnapStudio_ShowMe");
        public event Action<int> HotkeyPressed;
        public event Action ShowRequested;
        readonly HashSet<int> registered = new HashSet<int>();

        public MessageWindow() { CreateHandle(new CreateParams { Caption = "SnapStudioMessageWindow" }); }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                var h = HotkeyPressed;
                if (h != null) h(m.WParam.ToInt32());
            }
            else if ((uint)m.Msg == WM_SHOWME)
            {
                var h = ShowRequested;
                if (h != null) h();
            }
            base.WndProc(ref m);
        }

        public bool Register(int id, int keys)
        {
            if (keys == 0) return true;
            var k = (Keys)keys;
            uint mods = 0x4000; // MOD_NOREPEAT
            if ((k & Keys.Alt) != 0) mods |= 1;
            if ((k & Keys.Control) != 0) mods |= 2;
            if ((k & Keys.Shift) != 0) mods |= 4;
            bool ok = Native.RegisterHotKey(Handle, id, mods, (uint)(k & Keys.KeyCode));
            if (ok) registered.Add(id);
            return ok;
        }

        public void UnregisterAll()
        {
            foreach (int id in registered) Native.UnregisterHotKey(Handle, id);
            registered.Clear();
        }

        public void Dispose() { UnregisterAll(); DestroyHandle(); }
    }

    class TrayApp : ApplicationContext
    {
        public static TrayApp Instance;
        public Settings Settings;

        const int HK_REGION = 1, HK_WINDOW = 2, HK_FULL = 3, HK_PRTSCN = 4, HK_RECORD = 5;

        readonly NotifyIcon tray;
        readonly MessageWindow msg;
        public readonly bool Headless;
        readonly List<EditorForm> editors = new List<EditorForm>();
        TrayPanel panel;
        HistoryForm history;
        SettingsForm settingsForm;
        ToolStripMenuItem miRegion, miFull, miStartup, miRecord, miUpdate;
        Timer updateTimer;
        bool lastBalloonIsUpdate;
        ScreenRecording recording;
        public bool IsRecording { get { return recording != null; } }
        string lastBalloonPath;
        bool capturing;

        /// <summary>Só para o modo de teste --preview: sem ícone, sem teclas globais.</summary>
        public TrayApp(string folder, string theme)
        {
            Instance = this;
            Headless = true;
            Settings = Settings.Load();
            Settings.OutputFolder = folder;
            Theme.Configure(theme, "indigo");
        }

        public TrayApp()
        {
            Instance = this;
            Settings = Settings.Load();
            try { Directory.CreateDirectory(Settings.OutputFolder); } catch { }
            Theme.Configure(Settings.ThemeMode, Settings.Accent);

            tray = new NotifyIcon
            {
                Icon = AppIcon.Create(SystemInformation.SmallIconSize.Width),
                Text = "SnapStudio",
                Visible = true,
                ContextMenuStrip = BuildMenu(),
            };
            panel = new TrayPanel();
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) panel.Toggle(); };
            tray.BalloonTipClicked += delegate
            {
                if (lastBalloonIsUpdate) { if (Updates.Available != null) UpdateForm.ShowFor(Updates.Available); return; }
                if (lastBalloonPath != null && File.Exists(lastBalloonPath)) OpenEditor(lastBalloonPath, null);
            };

            msg = new MessageWindow();
            msg.HotkeyPressed += OnHotkey;
            msg.ShowRequested += ShowHistory;
            RegisterHotkeys(true);

            // versões novas no GitHub: pouco depois de arrancar e depois de hora a hora (só verifica de facto 1×/dia)
            updateTimer = new Timer { Interval = 8000 };
            updateTimer.Tick += delegate { updateTimer.Interval = 3600 * 1000; Updates.CheckIfDue(); };
            updateTimer.Start();
            Updates.Changed += delegate { if (panel != null && !panel.IsDisposed) panel.Invalidate(); };

            Theme.Changed += OnThemeChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

            if (Settings.IsNew)
            {
                Settings.IsNew = false;
                try { Settings.Save(); } catch { }
                Balloon(S.T("A SnapStudio está pronta", "SnapStudio is ready"),
                        S.T("Prima PrtScn para entrar em modo captura, ou clique no ícone junto ao relógio.", "Press PrtScn to enter capture mode, or click the icon next to the clock."), ToolTipIcon.Info, null);
            }
        }

        void OnThemeChanged()
        {
            var old = tray.ContextMenuStrip;
            tray.ContextMenuStrip = BuildMenu();
            if (old != null) old.Dispose();
        }

        void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // o utilizador mudou o tema do Windows: acompanhar se estiver em "Igual ao Windows"
            if (Settings.ThemeMode == "system" || Settings.Accent == "windows")
            {
                bool before = Theme.Dark;
                Color acc = Theme.Accent;
                Theme.Refresh();
                if (before == Theme.Dark && acc == Theme.Accent) return;
            }
        }

        ContextMenuStrip BuildMenu()
        {
            int s = SystemInformation.SmallIconSize.Width;
            Color ink = Theme.Text;
            var m = new ContextMenuStrip { ImageScalingSize = new Size(s, s), Renderer = new ThemedRenderer(), Font = Theme.Font(13 * Dpi.Scale) };
            miRegion = new ToolStripMenuItem(S.T("Modo captura (janela / região)", "Capture mode (window / region)"), ToolIcons.Get("region", s, ink), delegate { StartCapture(CaptureMode.Region, 200, null); });
            miRegion.Font = Theme.Font(13 * Dpi.Scale, FontStyle.Bold);
            miFull = new ToolStripMenuItem(S.T("Capturar todos os ecrãs", "Capture all screens"), ToolIcons.Get("full", s, ink), delegate { StartCapture(CaptureMode.FullScreen, 200, null); });
            var delay = new ToolStripMenuItem(S.T("Com atraso de 3 segundos", "With a 3-second delay"), ToolIcons.Get("timer", s, ink));
            delay.DropDown.Renderer = new ThemedRenderer();
            delay.DropDownItems.Add(S.T("Modo captura", "Capture mode"), null, delegate { StartCapture(CaptureMode.Region, 3000, null); });
            delay.DropDownItems.Add(S.T("Janela ativa", "Active window"), null, delegate { StartCapture(CaptureMode.ActiveWindow, 3000, null); });
            delay.DropDownItems.Add(S.T("Todos os ecrãs", "All screens"), null, delegate { StartCapture(CaptureMode.FullScreen, 3000, null); });
            m.Items.Add(miRegion);
            m.Items.Add(miFull);
            m.Items.Add(delay);
            miRecord = new ToolStripMenuItem(S.T("Gravar vídeo do ecrã", "Record screen video"), ToolIcons.Get("record", s, ink), delegate { ToggleRecording(); });
            m.Items.Add(miRecord);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem(S.T("Histórico de capturas", "Capture history"), ToolIcons.Get("history", s, ink), delegate { ShowHistory(); }));
            m.Items.Add(new ToolStripMenuItem(S.T("Abrir imagem…", "Open image…"), ToolIcons.Get("open", s, ink), delegate { OpenImageDialog(); }));
            m.Items.Add(new ToolStripMenuItem(S.T("Abrir pasta das capturas", "Open captures folder"), ToolIcons.Get("folder", s, ink), delegate { OpenFolder(); }));
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(new ToolStripMenuItem(Theme.Dark ? S.T("Tema claro", "Light theme") : S.T("Tema escuro", "Dark theme"), ToolIcons.Get(Theme.Dark ? "sun" : "moon", s, ink), delegate { ToggleTheme(); }));
            miStartup = new ToolStripMenuItem(S.T("Iniciar com o Windows", "Start with Windows")) { Checked = SafeStartup() };
            miStartup.Click += delegate
            {
                try { Settings.StartWithWindows = !Settings.StartWithWindows; } catch (Exception ex) { MessageBox.Show(ex.Message, "SnapStudio"); }
                miStartup.Checked = SafeStartup();
            };
            m.Items.Add(miStartup);
            m.Items.Add(new ToolStripMenuItem(S.T("Definições…", "Settings…"), ToolIcons.Get("settings", s, ink), delegate { ShowSettings(); }));
            miUpdate = new ToolStripMenuItem(S.T("Procurar atualizações…", "Check for updates…"), null, delegate
            {
                var p = Updates.Pending;
                if (p != null) UpdateForm.ShowFor(p); else Updates.Check(true);
            });
            m.Items.Add(miUpdate);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(S.T("Sair", "Exit"), null, delegate { Exit(); });
            m.Opening += delegate { miStartup.Checked = SafeStartup(); UpdateMenuShortcuts(); };
            return m;
        }

        static bool SafeStartup() { try { return Settings.StartWithWindows; } catch { return false; } }

        void UpdateMenuShortcuts()
        {
            int region = Settings.HotkeyRegion != 0 ? Settings.HotkeyRegion : (Settings.UsePrintScreen ? (int)Keys.PrintScreen : 0);
            miRegion.ShortcutKeyDisplayString = region != 0 ? Settings.HotkeyText(region) : null;
            miFull.ShortcutKeyDisplayString = Settings.HotkeyFull != 0 ? Settings.HotkeyText(Settings.HotkeyFull) : null;
            miRecord.Text = IsRecording ? S.T("Parar a gravação", "Stop recording") : S.T("Gravar vídeo do ecrã", "Record screen video");
            miRecord.ShortcutKeyDisplayString = Settings.HotkeyRecord != 0 ? Settings.HotkeyText(Settings.HotkeyRecord) : null;
            var pu = Updates.Pending;
            miUpdate.Text = pu != null ? S.T("Atualizar para a versão ", "Update to version ") + pu.Version + "…" : S.T("Procurar atualizações…", "Check for updates…");
        }

        void Balloon(string title, string text, ToolTipIcon icon, string path)
        {
            lastBalloonPath = path;
            lastBalloonIsUpdate = false;
            tray.ShowBalloonTip(4000, title, text, icon);
        }

        /// <summary>Aviso de versão nova (clicar no balão abre a janela de atualização).</summary>
        public void NotifyUpdate(ReleaseInfo r)
        {
            Balloon(S.T("Nova versão da SnapStudio", "New SnapStudio version"),
                    S.T("A versão ", "Version ") + r.Version + S.T(" está disponível (tem a ", " is available (you have ") + Updates.CurrentVersion +
                    S.T("). Clique para ver as novidades.", "). Click to see what's new."), ToolTipIcon.Info, null);
            lastBalloonIsUpdate = true;
        }

        public void ToggleTheme()
        {
            Settings.ThemeMode = Theme.Dark ? "light" : "dark";
            try { Settings.Save(); } catch { }
            Theme.Configure(Settings.ThemeMode, Settings.Accent);
        }

        // ================= teclas globais =================

        void RegisterHotkeys(bool notify)
        {
            msg.UnregisterAll();
            var failed = new List<string>();
            int prt = (int)Keys.PrintScreen;
            Action<int, int> reg = (id, keys) =>
            {
                if (keys == 0) return;
                if (keys == prt && !Settings.UsePrintScreen) return;
                if (!msg.Register(id, keys)) failed.Add(Settings.HotkeyText(keys));
            };
            reg(HK_REGION, Settings.HotkeyRegion);
            reg(HK_WINDOW, Settings.HotkeyWindow);
            reg(HK_FULL, Settings.HotkeyFull);
            reg(HK_RECORD, Settings.HotkeyRecord);
            if (Settings.UsePrintScreen && Settings.HotkeyRegion != prt && Settings.HotkeyWindow != prt && Settings.HotkeyFull != prt && Settings.HotkeyRecord != prt)
                reg(HK_PRTSCN, prt);

            if (!notify) return;
            if (failed.Count > 0)
                Balloon(S.T("Atalho em uso", "Shortcut in use"),
                        S.T("Não foi possível registar: ", "Could not register: ") + string.Join(", ", failed) +
                        S.T(". Outra aplicação está a usá-lo. Pode mudá-lo nas Definições.", ". Another app is using it. You can change it in Settings."), ToolTipIcon.Warning, null);
            else if (Settings.UsePrintScreen && Settings.WindowsSnippingOwnsPrintScreen)
                Balloon(S.T("PrtScn está com a Ferramenta de Recorte", "PrtScn is taken by the Snipping Tool"),
                        S.T("Para a SnapStudio usar PrtScn, desligue em Definições do Windows > Acessibilidade > Teclado a opção \"Usar a tecla Print Screen para abrir a captura de ecrã\".",
                            "To let SnapStudio use PrtScn, turn off Windows Settings > Accessibility > Keyboard > \"Use the Print screen key to open screen capture\"."), ToolTipIcon.Warning, null);
        }

        void OnHotkey(int id)
        {
            switch (id)
            {
                case HK_REGION: case HK_PRTSCN: StartCapture(CaptureMode.Region, 0, null); break;
                case HK_WINDOW: StartCapture(CaptureMode.ActiveWindow, 0, null); break;
                case HK_FULL: StartCapture(CaptureMode.FullScreen, 0, null); break;
                case HK_RECORD: ToggleRecording(); break;
            }
        }

        // ================= captura =================

        public void StartCapture(CaptureMode mode, int delayMs, Form hideDuring)
        {
            if (capturing || recording != null) return;
            capturing = true;
            // um pop-up aberto guarda e fecha-se antes de capturar
            foreach (var ed in editors.Where(e => e.Compact).ToList()) ed.Close();
            if (panel.Visible) panel.HideNow();
            if (hideDuring != null) { hideDuring.Hide(); if (delayMs < 250) delayMs = 250; }
            if (delayMs <= 0) { DoCapture(mode, null); return; }
            var t = new Timer { Interval = delayMs };
            t.Tick += delegate { t.Stop(); t.Dispose(); DoCapture(mode, hideDuring); };
            t.Start();
        }

        void DoCapture(CaptureMode mode, Form restore)
        {
            Bitmap bmp = null;
            try
            {
                if (mode == CaptureMode.FullScreen) bmp = CaptureService.CaptureScreen(CaptureService.VirtualScreen);
                else if (mode == CaptureMode.ActiveWindow) bmp = CaptureService.CaptureActiveWindow();
                else bmp = CaptureService.SelectRegion();
            }
            catch (Exception ex)
            {
                MessageBox.Show(S.T("Erro na captura: ", "Capture error: ") + ex.Message, "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                capturing = false;
                if (restore != null && !restore.IsDisposed) restore.Show();
            }
            if (bmp != null)
            {
                try { HandleCapture(bmp); }
                catch (Exception ex) { MessageBox.Show(S.T("Erro ao guardar a captura: ", "Error saving the capture: ") + ex.Message, "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
                finally { bmp.Dispose(); }
            }
        }

        void HandleCapture(Bitmap bmp)
        {
            string path = Settings.NewCapturePath();
            ImageUtil.Save(bmp, path);
            if (Settings.CopyToClipboard)
            {
                // cópia persistente: a imagem fica na área de transferência mesmo depois de fechar a app
                try { Clipboard.SetDataObject(new Bitmap(bmp), true, 5, 100); } catch { }
            }
            if (Settings.AfterCapture == "quick") OpenQuickEditor(path, bmp);
            else if (Settings.AfterCapture == "full") OpenEditor(path, bmp);
            else if (Settings.ShowNotification)
                Balloon(S.T("Captura guardada", "Capture saved"),
                        Path.GetFileName(path) + (Settings.CopyToClipboard ? S.T(" — copiada para a área de transferência", " — copied to the clipboard") : "") +
                        S.T("\nClique para editar.", "\nClick to edit."), ToolTipIcon.Info, path);
        }

        // ================= vídeo =================

        /// <summary>Inicia a gravação (escolhendo a zona) ou, se já estiver a gravar, pára e guarda.</summary>
        public void ToggleRecording()
        {
            if (recording != null) { recording.Stop(); return; }
            if (capturing) return;
            capturing = true;
            foreach (var ed in editors.Where(e => e.Compact).ToList()) ed.Close();
            if (panel.Visible) panel.HideNow();
            // dar tempo ao painel/menu para desaparecer antes de congelar o ecrã
            var t = new Timer { Interval = 200 };
            t.Tick += delegate { t.Stop(); t.Dispose(); BeginRecording(); };
            t.Start();
        }

        void BeginRecording()
        {
            Rectangle? area;
            try { area = CaptureService.SelectArea(S.T("Escolha o que gravar: clique numa janela ou arraste uma região", "Choose what to record: click a window or drag a region")); }
            catch (Exception ex)
            {
                MessageBox.Show(S.T("Erro: ", "Error: ") + ex.Message, "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Error);
                area = null;
            }
            finally { capturing = false; }
            if (area == null) return;
            if (area.Value.Width < 64 || area.Value.Height < 64)
            {
                Balloon(S.T("Zona demasiado pequena", "Area too small"), S.T("Escolha uma zona com pelo menos 64 × 64 pixels.", "Choose an area of at least 64 × 64 pixels."), ToolTipIcon.Warning, null);
                return;
            }
            string path;
            try { path = Settings.NewVideoPath(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
            recording = new ScreenRecording(area.Value, path, Settings.VideoFps, Settings.VideoCursor);
            recording.Ended += OnRecordingEnded;
            tray.Text = S.T("SnapStudio — a gravar", "SnapStudio — recording");
            recording.Begin();
        }

        void OnRecordingEnded(ScreenRecording r)
        {
            if (recording == r) recording = null;
            tray.Text = "SnapStudio";
            var rec = r.Recorder;
            if (rec.Error != null)
            {
                MessageBox.Show(S.T("Não foi possível gravar o vídeo:\n\n", "Could not record the video:\n\n") + rec.Error.Message, "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            if (rec.Cancelled || !File.Exists(rec.Path)) return;
            if (Settings.CopyToClipboard)
            {
                // como ficheiro: cola-se no Teams, Outlook, Explorador…
                try
                {
                    var sc = new System.Collections.Specialized.StringCollection();
                    sc.Add(rec.Path);
                    var data = new DataObject();
                    data.SetFileDropList(sc);
                    Clipboard.SetDataObject(data, true, 5, 100);
                }
                catch { }
            }
            long kb = 0;
            try { kb = new FileInfo(rec.Path).Length / 1024; } catch { }
            string size = kb >= 1024 ? (kb / 1024.0).ToString("0.0") + " MB" : Math.Max(1, kb) + " KB";
            Balloon(S.T("Vídeo guardado", "Video saved"),
                    Path.GetFileName(rec.Path) + "  ·  " + ScreenRecording.Format(r.Duration) + "  ·  " + size +
                    (Settings.CopyToClipboard ? S.T("\nCopiado — cole com Ctrl+V. ", "\nCopied — paste with Ctrl+V. ") : "\n") +
                    S.T("Clique para ver.", "Click to watch."), ToolTipIcon.Info, rec.Path);
        }

        public void OpenVideo(string path)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { MessageBox.Show(S.T("Não foi possível abrir o vídeo: ", "Could not open the video: ") + ex.Message, "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }

        // ================= janelas =================

        public void OpenEditor(string path, Bitmap initial)
        {
            if (VideoFile.Is(path)) { OpenVideo(path); return; }
            var ex = editors.FirstOrDefault(e => !e.Compact && !e.IsDisposed && string.Equals(e.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (ex != null)
            {
                if (ex.WindowState == FormWindowState.Minimized) ex.WindowState = FormWindowState.Normal;
                ex.Activate();
                return;
            }
            ShowEditor(new Func<EditorForm>(() => new EditorForm(path, initial, false)));
        }

        public void OpenQuickEditor(string path, Bitmap initial)
        {
            if (VideoFile.Is(path)) { OpenVideo(path); return; }
            foreach (var ed in editors.Where(e => e.Compact).ToList()) ed.Close();
            ShowEditor(new Func<EditorForm>(() => new EditorForm(path, initial, true)));
        }

        void ShowEditor(Func<EditorForm> make)
        {
            EditorForm f;
            try { f = make(); }
            catch (Exception e2)
            {
                MessageBox.Show(S.T("Não foi possível abrir a imagem: ", "Could not open the image: ") + e2.Message, "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            editors.Add(f);
            f.FormClosed += delegate { editors.Remove(f); };
            f.Show();
            f.Activate();
        }

        public void OpenImageDialog()
        {
            using (var d = new OpenFileDialog { Filter = S.T("Imagens", "Images") + "|*.png;*.jpg;*.jpeg;*.bmp;*.gif", InitialDirectory = Settings.OutputFolder })
                if (d.ShowDialog() == DialogResult.OK) OpenEditor(d.FileName, null);
        }

        public void OpenFolder()
        {
            try { System.Diagnostics.Process.Start("explorer.exe", "\"" + Settings.OutputFolder + "\""); } catch { }
        }

        public void ShowHistory()
        {
            if (history == null || history.IsDisposed) history = new HistoryForm();
            history.Show();
            if (history.WindowState == FormWindowState.Minimized) history.WindowState = FormWindowState.Normal;
            history.Activate();
            Native.SetForegroundWindow(history.Handle);
        }

        public void ShowSettings() { ShowSettings(null); }

        /// <summary>Abre as Definições, opcionalmente numa página ("general", "look", …, "about").</summary>
        public void ShowSettings(string page)
        {
            if (settingsForm != null && !settingsForm.IsDisposed) { if (page != null) settingsForm.ShowPage(page); settingsForm.Activate(); return; }
            string oldFolder = Settings.OutputFolder;
            msg.UnregisterAll(); // para poder carregar nas teclas dentro da caixa de atalhos
            using (settingsForm = new SettingsForm(Settings, page))
            {
                if (settingsForm.ShowDialog() == DialogResult.OK)
                {
                    try { Settings.Save(); } catch (Exception ex) { MessageBox.Show(ex.Message, "SnapStudio"); }
                    Theme.Configure(Settings.ThemeMode, Settings.Accent);
                    if (!string.Equals(oldFolder, Settings.OutputFolder, StringComparison.OrdinalIgnoreCase) && history != null && !history.IsDisposed)
                        history.FolderChanged();
                }
            }
            settingsForm = null;
            RegisterHotkeys(true);
        }

        public void Exit()
        {
            // confirmar antes de sair: sem a aplicação, PrtScn e os atalhos deixam de funcionar
            string text = recording != null
                ? S.T("Está a gravar o ecrã. Parar a gravação e sair da SnapStudio?", "A screen recording is in progress. Stop it and exit SnapStudio?")
                : S.T("Sair da SnapStudio?\n\nA tecla PrtScn e os atalhos de captura deixam de funcionar até voltar a abrir a aplicação.",
                      "Exit SnapStudio?\n\nThe PrtScn key and the capture shortcuts will stop working until you open the app again.");
            if (MessageBox.Show(text, "SnapStudio", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            if (recording != null) recording.StopAndWait();
            foreach (var e in editors.ToList())
            {
                e.Close();
                if (!e.IsDisposed && e.Visible) return; // utilizador cancelou
            }
            if (history != null && !history.IsDisposed) history.Close();
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            panel.Dispose();
            tray.Visible = false;
            tray.Dispose();
            msg.Dispose();
            ExitLog.Ended("user chose Exit");
            ExitThread();
        }
    }
}
