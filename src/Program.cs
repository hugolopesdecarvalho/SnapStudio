using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace SnapStudio
{
    static class Program
    {
        static string previewLog;

        /// <summary>Modo de teste: desenha uma janela para um PNG (nome saída tema pasta [imagem]).</summary>
        static int Preview(string name, string outPath, string theme, string folder, string image)
        {
            try
            {
                new TrayApp(folder, theme);
                Form f;
                if (name == "panel") { var p = new TrayPanel(); p.Prepare(); f = p; }
                else if (name == "history") f = new HistoryForm();
                else if (name == "settings") f = new SettingsForm(TrayApp.Instance.Settings);
                else f = new EditorForm(image, null, name == "quick");
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(-20000, -20000);
                f.ShowInTaskbar = false;
                f.Show();
                for (int i = 0; i < 30; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(50); }
                f.Refresh();
                using (var bmp = new Bitmap(f.Width, f.Height))
                {
                    f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                    bmp.Save(outPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                return 0;
            }
            catch (Exception ex) { System.IO.File.WriteAllText(outPath + ".error.txt", ex.ToString()); return 1; }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                if (previewLog != null) { System.IO.File.AppendAllText(previewLog, e.Exception.ToString() + "\r\n"); return; }
                MessageBox.Show(S.T("Ocorreu um erro inesperado:\n\n", "An unexpected error occurred:\n\n") + e.Exception.Message,
                                "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            // Utilitários de linha de comandos (build e testes)
            if (args.Length >= 2 && args[0] == "--make-icon") { AppIcon.SaveIco(args[1]); return 0; }
            if (args.Length >= 3 && args[0] == "--capture")
            {
                try
                {
                    using (Bitmap b = args[1] == "window" ? CaptureService.CaptureActiveWindow() : CaptureService.CaptureScreen(CaptureService.VirtualScreen))
                        ImageUtil.Save(b, args[2]);
                    return 0;
                }
                catch (Exception ex)
                {
                    System.IO.File.WriteAllText(args[2] + ".error.txt", ex.ToString() + "\r\nVirtualScreen=" + CaptureService.VirtualScreen);
                    return 1;
                }
            }

            if (args.Length >= 3 && args[0] == "--render")
            {
                try { using (var f = new EditorForm(args[1], null)) f.ExportTo(args[2]); return 0; }
                catch (Exception ex) { System.IO.File.WriteAllText(args[2] + ".error.txt", ex.ToString()); return 1; }
            }

            if (args.Length >= 5 && args[0] == "--preview") previewLog = args[2] + ".error.txt";
            if (args.Length >= 5 && args[0] == "--preview") return Preview(args[1], args[2], args[3], args[4], args.Length > 5 ? args[5] : null);

            bool created;
            using (var mutex = new Mutex(true, @"Local\SnapStudio_SingleInstance", out created))
            {
                if (!created)
                {
                    // já está a correr: pedir à instância existente para mostrar o histórico
                    Native.PostMessage(Native.HWND_BROADCAST, MessageWindow.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                var app = new TrayApp();
                if (args.Length >= 2 && args[0] == "--edit") app.OpenEditor(args[1], null);
                Application.Run(app);
                GC.KeepAlive(mutex);
            }
            return 0;
        }
    }
}
