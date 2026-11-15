using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace SnapStudio
{
    /// <summary>Release publicada no GitHub.</summary>
    class ReleaseInfo
    {
        public string Version, Tag, PageUrl, Notes, DownloadUrl;
    }

    [DataContract]
    class GhAsset
    {
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "browser_download_url")] public string Url;
    }

    [DataContract]
    class GhRelease
    {
        [DataMember(Name = "tag_name")] public string TagName;
        [DataMember(Name = "html_url")] public string HtmlUrl;
        [DataMember(Name = "body")] public string Body;
        [DataMember(Name = "assets")] public List<GhAsset> Assets;
    }

    /// <summary>
    /// Procura versões novas em github.com/hugolopesdecarvalho/SnapStudio/releases (API pública, sem conta).
    /// Só faz um pedido GET; não envia dados do utilizador.
    /// </summary>
    static class Updates
    {
        public const string Repo = "hugolopesdecarvalho/SnapStudio";
        public static string RepoUrl { get { return "https://github.com/" + Repo; } }
        public static string ReleasesUrl { get { return RepoUrl + "/releases"; } }

        public static ReleaseInfo Available;
        public static bool Checking;
        public static event Action Changed;

        static void RaiseChanged() { var h = Changed; if (h != null) h(); }

        /// <summary>Versão instalada (AssemblyInformationalVersion, em AssemblyInfo.cs).</summary>
        public static string CurrentVersion
        {
            get
            {
                var asm = typeof(Updates).Assembly;
                var a = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(asm, typeof(AssemblyInformationalVersionAttribute));
                if (a != null && !string.IsNullOrEmpty(a.InformationalVersion)) return a.InformationalVersion;
                return asm.GetName().Version.ToString(3);
            }
        }

        // ---------- comparação de versões ----------

        static int[] Parts(string v)
        {
            if (string.IsNullOrEmpty(v)) return new int[0];
            v = v.Trim().TrimStart('v', 'V');
            int dash = v.IndexOf('-');
            if (dash >= 0) v = v.Substring(0, dash);
            string[] s = v.Split('.');
            var r = new int[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                string digits = "";
                foreach (char c in s[i]) if (char.IsDigit(c)) digits += c;
                int n;
                r[i] = int.TryParse(digits, out n) ? n : 0;
            }
            return r;
        }

        /// <summary>True se a for mais recente do que b (1.10.0 &gt; 1.9.3; "v" e sufixos "-beta" são ignorados).</summary>
        public static bool IsNewer(string a, string b)
        {
            int[] x = Parts(a), y = Parts(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p > q;
            }
            return false;
        }

        // ---------- GitHub ----------

        /// <summary>Pede a release mais recente (bloqueia: chamar fora da thread da interface).</summary>
        public static ReleaseInfo FetchLatest()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
            req.UserAgent = "SnapStudio-Windows/" + CurrentVersion;
            req.Accept = "application/vnd.github+json";
            req.Timeout = 15000;
            req.ReadWriteTimeout = 15000;
            req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            try
            {
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var st = resp.GetResponseStream())
                {
                    var gh = (GhRelease)new DataContractJsonSerializer(typeof(GhRelease)).ReadObject(st);
                    if (gh == null || string.IsNullOrEmpty(gh.TagName)) throw new Exception(S.T("Resposta inesperada do GitHub.", "Unexpected response from GitHub."));
                    var r = new ReleaseInfo
                    {
                        Tag = gh.TagName,
                        Version = gh.TagName.TrimStart('v', 'V'),
                        PageUrl = string.IsNullOrEmpty(gh.HtmlUrl) ? ReleasesUrl : gh.HtmlUrl,
                        Notes = gh.Body ?? "",
                    };
                    // o zip para Windows (os browsers desconfiam menos de um zip do que de um .exe); senão um .exe
                    if (gh.Assets != null)
                    {
                        foreach (var a in gh.Assets)
                            if (a != null && a.Name != null && a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                                a.Name.IndexOf("windows", StringComparison.OrdinalIgnoreCase) >= 0) { r.DownloadUrl = a.Url; break; }
                        if (r.DownloadUrl == null)
                            foreach (var a in gh.Assets)
                                if (a != null && a.Name != null && a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) { r.DownloadUrl = a.Url; break; }
                    }
                    return r;
                }
            }
            catch (WebException ex)
            {
                var resp = ex.Response as HttpWebResponse;
                if (resp != null && resp.StatusCode == HttpStatusCode.NotFound)
                    throw new Exception(S.T("Ainda não há versões publicadas.", "No releases have been published yet."));
                throw;
            }
        }

        // ---------- verificação ----------

        /// <summary>Verificação automática: no máximo uma vez a cada 20 horas, se a opção estiver ligada.</summary>
        public static void CheckIfDue()
        {
            var app = TrayApp.Instance;
            if (app == null || app.Headless) return;
            var st = app.Settings;
            if (!st.AutoUpdateCheck) return;
            if (DateTime.UtcNow.Ticks - st.LastUpdateCheck < TimeSpan.FromHours(20).Ticks) return;
            Check(false);
        }

        /// <summary>interactive: pedido do utilizador — mostra sempre o resultado (mesmo "está atualizado" ou erro).</summary>
        public static void Check(bool interactive)
        {
            if (Checking) return;
            Checking = true;
            RaiseChanged();
            var ctx = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
            ThreadPool.QueueUserWorkItem(delegate
            {
                ReleaseInfo r = null;
                Exception err = null;
                try { r = FetchLatest(); }
                catch (Exception ex) { err = ex; }
                ctx.Post(delegate { Finish(r, err, interactive); }, null);
            });
        }

        static void Finish(ReleaseInfo r, Exception err, bool interactive)
        {
            Checking = false;
            var app = TrayApp.Instance;
            var st = app != null ? app.Settings : null;
            if (err != null)
            {
                RaiseChanged();
                if (interactive)
                    MessageBox.Show(S.T("Não foi possível procurar atualizações:\n\n", "Could not check for updates:\n\n") + err.Message,
                                    "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (st != null) { st.LastUpdateCheck = DateTime.UtcNow.Ticks; try { st.Save(); } catch { } }

            if (IsNewer(r.Version, CurrentVersion))
            {
                bool isNew = Available == null || Available.Version != r.Version;
                Available = r;
                RaiseChanged();
                if (interactive) UpdateForm.ShowFor(r);
                else if (isNew && (st == null || st.SkippedVersion != r.Version) && app != null) app.NotifyUpdate(r);
            }
            else
            {
                Available = null;
                RaiseChanged();
                if (interactive)
                    MessageBox.Show(S.T("A SnapStudio está atualizada.\n\nTem a versão mais recente (", "SnapStudio is up to date.\n\nYou have the latest version (") + CurrentVersion + ").",
                                    "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        /// <summary>Versão mais recente ainda não ignorada (para o painel e o menu).</summary>
        public static ReleaseInfo Pending
        {
            get
            {
                if (Available == null) return null;
                var app = TrayApp.Instance;
                if (app != null && app.Settings.SkippedVersion == Available.Version) return null;
                return Available;
            }
        }

        public static void Skip(ReleaseInfo r)
        {
            var app = TrayApp.Instance;
            if (app == null) return;
            app.Settings.SkippedVersion = r.Version;
            try { app.Settings.Save(); } catch { }
            RaiseChanged();
        }

        public static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        /// <summary>Markdown simplificado para texto (títulos, negrito, código, links, listas).</summary>
        public static string PlainNotes(string md)
        {
            if (string.IsNullOrEmpty(md)) return "";
            string s = md.Replace("\r\n", "\n");
            s = Regex.Replace(s, @"\*\*([^*]+)\*\*", "$1");
            s = Regex.Replace(s, @"`([^`]+)`", "$1");
            s = Regex.Replace(s, @"\[([^\]]+)\]\([^)]+\)", "$1");
            s = Regex.Replace(s, @"(?m)^#{1,6}\s*", "");
            s = Regex.Replace(s, @"(?m)^>\s?", "");
            s = Regex.Replace(s, @"(?m)^- ", "• ");
            return s.Trim();
        }
    }

    /// <summary>Janela "Está disponível a versão X": novidades, Descarregar, Mais tarde, Ignorar esta versão.</summary>
    class UpdateForm : Form
    {
        static UpdateForm open;

        public static void ShowFor(ReleaseInfo r)
        {
            if (open != null && !open.IsDisposed) { open.Activate(); return; }
            using (var f = new UpdateForm(r))
            {
                open = f;
                try
                {
                    Form owner = Form.ActiveForm;
                    if (owner != null && !owner.IsDisposed && owner.Visible) f.ShowDialog(owner);
                    else f.ShowDialog();
                }
                finally { open = null; }
            }
        }

        UpdateForm(ReleaseInfo r)
        {
            float dpi = Dpi.Scale;
            Func<float, int> Sc = v => (int)Math.Round(v * dpi);
            Text = "SnapStudio";
            Icon = AppIcon.FormIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Sc(540), Sc(430));
            BackColor = Theme.Panel;
            Font = Theme.Font(13.5f * dpi);
            KeyPreview = true;

            var logo = new PictureBox { Image = AppIcon.Draw(Sc(56)), SizeMode = PictureBoxSizeMode.AutoSize, Location = new Point(Sc(24), Sc(22)), BackColor = Color.Transparent };
            var title = new ThemedLabel
            {
                Text = S.T("Está disponível a SnapStudio ", "SnapStudio ") + r.Version + S.T("", " is available"),
                Font = Theme.Font(17 * dpi, FontStyle.Bold),
                Location = new Point(Sc(96), Sc(24)),
            };
            var info = new ThemedLabel
            {
                AutoSize = false,
                Size = new Size(Sc(420), Sc(48)),
                Location = new Point(Sc(96), Sc(58)),
                Font = Theme.Font(12.5f * dpi),
                Text = S.T("Tem a versão ", "You have version ") + Updates.CurrentVersion +
                       S.T(". Descarregue a nova versão, extraia o SnapStudio.exe e substitua o atual (feche primeiro a SnapStudio).",
                           ". Download the new version, extract SnapStudio.exe and replace the current one (quit SnapStudio first)."),
            }.Muted();
            var notes = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(Sc(24), Sc(118)),
                Size = new Size(Sc(492), Sc(236)),
                Text = Updates.PlainNotes(r.Notes).Replace("\n", "\r\n"),
                BackColor = Theme.Surface2,
                ForeColor = Theme.Text,
                Font = Theme.Font(12.5f * dpi),
            };

            var dl = new FlatButton { Kind = BtnKind.Primary, Text = S.T("Descarregar", "Download"), Size = new Size(Sc(140), Sc(40)) };
            var later = new FlatButton { Kind = BtnKind.Secondary, Text = S.T("Mais tarde", "Later"), Size = new Size(Sc(116), Sc(40)) };
            var skip = new FlatButton { Kind = BtnKind.Ghost, Text = S.T("Ignorar esta versão", "Skip this version") };
            skip.Size = new Size(skip.PreferredWidth, Sc(40));
            dl.Location = new Point(Sc(516) - dl.Width, Sc(372));
            later.Location = new Point(dl.Left - Sc(8) - later.Width, Sc(372));
            skip.Location = new Point(Sc(20), Sc(372));
            dl.Click += delegate { Updates.Open(r.DownloadUrl ?? r.PageUrl); Close(); };
            later.Click += delegate { Close(); };
            skip.Click += delegate { Updates.Skip(r); Close(); };

            Controls.AddRange(new Control[] { logo, title, info, notes, skip, later, dl });
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };
            Shown += delegate { Theme.StyleWindow(this); dl.Focus(); notes.SelectionLength = 0; };
        }
    }
}
