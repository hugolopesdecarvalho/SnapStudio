using System;
using System.IO;
using Microsoft.Win32;

namespace SnapStudio
{
    /// <summary>
    /// Regista em %AppData%\SnapStudio\exit.log como é que a aplicação terminou
    /// (o utilizador saiu, o Windows encerrou a sessão, erro inesperado), para se perceber
    /// porque é que ela "desaparece". Se uma sessão não deixou registo de fim, foi morta
    /// (Gestor de Tarefas, falta de memória, …) e isso é anotado no arranque seguinte.
    /// </summary>
    static class ExitLog
    {
        const string StartTag = "START", EndTag = "END";
        static readonly object gate = new object();
        static bool ended;

        static string FilePath { get { return Path.Combine(Settings.AppDir, "exit.log"); } }

        public static void Started()
        {
            try
            {
                if (LastSessionLineIsStart())
                    Write("END   unknown — the previous session ended without notice (killed by Windows/Task Manager or a hard crash)");
            }
            catch { }
            Write(StartTag + " pid=" + System.Diagnostics.Process.GetCurrentProcess().Id + " version=" + typeof(ExitLog).Assembly.GetName().Version);

            SystemEvents.SessionEnding += (s, e) => Ended("Windows session ending (" + e.Reason + ")");
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                Ended("crash — unhandled exception: " + e.ExceptionObject);
        }

        /// <summary>Anota um erro que não fechou a aplicação (para contexto).</summary>
        public static void Error(Exception ex)
        {
            Write("ERROR " + ex);
        }

        public static void Ended(string reason)
        {
            lock (gate) { if (ended) return; ended = true; }
            Write(EndTag + "   " + reason);
        }

        static void Write(string text)
        {
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(Settings.AppDir);
                    var fi = new FileInfo(FilePath);
                    if (fi.Exists && fi.Length > 256 * 1024)
                    {
                        // manter só a metade mais recente
                        string all = File.ReadAllText(FilePath);
                        File.WriteAllText(FilePath, all.Substring(all.Length / 2));
                    }
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + text.Replace("\r\n", "\n").Replace("\n", "\r\n    ") + "\r\n");
                }
                catch { }
            }
        }

        static bool LastSessionLineIsStart()
        {
            if (!File.Exists(FilePath)) return false;
            string[] lines = File.ReadAllLines(FilePath);
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (lines[i].Contains(" " + StartTag + " ")) return true;
                if (lines[i].Contains(" " + EndTag + " ")) return false;
            }
            return false;
        }
    }
}
