using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SnapStudio
{
    [DataContract]
    public class Settings
    {
        [DataMember] public string OutputFolder;
        [DataMember] public string Format;            // "png" ou "jpg"
        [DataMember] public bool OpenEditor;
        [DataMember] public bool CopyToClipboard;
        [DataMember] public bool ShowNotification;
        [DataMember] public int HotkeyRegion;
        [DataMember] public int HotkeyWindow;
        [DataMember] public int HotkeyFull;
        [DataMember] public bool UsePrintScreen;      // PrtScn entra em modo captura
        [DataMember] public string ThemeMode;         // system | light | dark
        [DataMember] public string Accent;            // indigo | blue | teal | orange | windows
        [DataMember] public string AfterCapture;      // quick | full | none

        public bool IsNew;

        public Settings() { Defaults(); }

        [OnDeserializing]
        void OnDeserializing(StreamingContext c) { Defaults(); }

        void Defaults()
        {
            OutputFolder = DefaultFolder;
            Format = "png";
            OpenEditor = true;
            CopyToClipboard = true;
            ShowNotification = true;
            HotkeyRegion = (int)Keys.PrintScreen;
            HotkeyWindow = (int)(Keys.PrintScreen | Keys.Alt);
            HotkeyFull = (int)(Keys.PrintScreen | Keys.Control);
            UsePrintScreen = true;
            ThemeMode = "system";
            Accent = "indigo";
            AfterCapture = null; // definido em Load (migra o antigo OpenEditor)
        }

        /// <summary>True se o Windows 11 estiver configurado para abrir a Ferramenta de Recorte com PrtScn.</summary>
        public static bool WindowsSnippingOwnsPrintScreen
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Keyboard"))
                    {
                        if (k == null) return false;
                        object v = k.GetValue("PrintScreenKeyForSnippingEnabled");
                        return v != null && Convert.ToInt32(v) != 0;
                    }
                }
                catch { return false; }
            }
        }

        public static string DefaultFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "SnapStudio"); }
        }

        public static string AppDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnapStudio"); }
        }

        static string FilePath { get { return Path.Combine(AppDir, "settings.json"); } }

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    using (var fs = File.OpenRead(FilePath))
                    {
                        var s = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(fs);
                        if (string.IsNullOrEmpty(s.OutputFolder)) s.OutputFolder = DefaultFolder;
                        if (s.Format != "jpg") s.Format = "png";
                        if (s.AfterCapture != "quick" && s.AfterCapture != "full" && s.AfterCapture != "none")
                            s.AfterCapture = s.OpenEditor ? "quick" : "none";
                        if (s.ThemeMode != "light" && s.ThemeMode != "dark") s.ThemeMode = "system";
                        if (string.IsNullOrEmpty(s.Accent)) s.Accent = "indigo";
                        return s;
                    }
                }
            }
            catch { }
            var n = new Settings();
            n.IsNew = true;
            n.AfterCapture = "quick";
            return n;
        }

        public void Save()
        {
            OpenEditor = AfterCapture != "none";
            Directory.CreateDirectory(AppDir);
            using (var fs = File.Create(FilePath))
                new DataContractJsonSerializer(typeof(Settings)).WriteObject(fs, this);
        }

        public string NewCapturePath()
        {
            Directory.CreateDirectory(OutputFolder);
            string ext = Format == "jpg" ? ".jpg" : ".png";
            string b = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string p = Path.Combine(OutputFolder, b + ext);
            int i = 2;
            while (File.Exists(p)) p = Path.Combine(OutputFolder, b + "_" + (i++) + ext);
            return p;
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool StartWithWindows
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue("SnapStudio") != null;
            }
            set
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (value) k.SetValue("SnapStudio", "\"" + Application.ExecutablePath + "\"");
                    else if (k.GetValue("SnapStudio") != null) k.DeleteValue("SnapStudio");
                }
            }
        }

        public static string HotkeyText(int keys)
        {
            if (keys == 0) return S.T("(nenhum)", "(none)");
            var k = (Keys)keys;
            string s = "";
            if ((k & Keys.Control) != 0) s += "Ctrl+";
            if ((k & Keys.Shift) != 0) s += "Shift+";
            if ((k & Keys.Alt) != 0) s += "Alt+";
            Keys code = k & Keys.KeyCode;
            s += code == Keys.PrintScreen ? "PrtScn" : code.ToString();
            return s;
        }
    }

    /// <summary>Ficheiros laterais (imagem original + anotações) para reabrir uma captura e continuar a editar.</summary>
    static class Sidecar
    {
        static string Dir { get { return Path.Combine(Settings.AppDir, "edit"); } }

        static string Key(string path)
        {
            string full = Path.GetFullPath(path).ToLowerInvariant();
            uint h = 2166136261;
            foreach (char c in full) { h ^= c; h *= 16777619; }
            return Path.GetFileName(path) + "_" + h.ToString("x8");
        }

        public static string Base(string path) { return Path.Combine(Dir, Key(path) + ".base.png"); }
        public static string Json(string path) { return Path.Combine(Dir, Key(path) + ".json"); }
        public static void EnsureDir() { Directory.CreateDirectory(Dir); }

        public static void Delete(string path)
        {
            try { File.Delete(Base(path)); } catch { }
            try { File.Delete(Json(path)); } catch { }
        }

        public static void Move(string from, string to)
        {
            try
            {
                if (File.Exists(Base(from))) { File.Delete(Base(to)); File.Move(Base(from), Base(to)); }
                if (File.Exists(Json(from))) { File.Delete(Json(to)); File.Move(Json(from), Json(to)); }
            }
            catch { }
        }
    }
}
