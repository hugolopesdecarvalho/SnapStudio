using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SnapStudio
{
    /// <summary>Moldura vermelha à volta da zona gravada (não recebe cliques e não aparece no vídeo).</summary>
    class RecordingFrame : Form
    {
        const int B = 3;
        public bool Paused { get { return paused; } set { paused = value; Invalidate(); } }
        bool paused;

        public RecordingFrame(Rectangle area)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.Magenta;
            TransparencyKey = Color.Magenta;
            Bounds = Rectangle.Inflate(area, B, B);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x80000 | 0x20 | 0x80 | 0x8000000; // LAYERED | TRANSPARENT | TOOLWINDOW | NOACTIVATE
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            VideoRecorder.ExcludeFromCapture(Handle);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color c = paused ? Color.FromArgb(240, 170, 40) : Color.FromArgb(232, 64, 64);
            using (var p = new Pen(c, B) { Alignment = PenAlignment.Inset })
                e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>Barra flutuante durante a gravação: tempo, pausa, parar, cancelar.</summary>
    class RecordingBar : Form
    {
        readonly float dpi;
        readonly FlatButton btnPause, btnStop, btnCancel;
        readonly ToolTip tips = new ToolTip();
        public string Status = "";
        public bool Live, Paused;
        Point dragStart;
        bool dragging;

        public event Action PauseClicked, StopClicked, CancelClicked;

        int Px(float v) { return (int)Math.Round(v * dpi); }

        public RecordingBar()
        {
            dpi = Dpi.Scale;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            Text = S.T("SnapStudio — a gravar", "SnapStudio — recording");
            Font = Theme.Font(13 * dpi);
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            BackColor = Theme.Panel;

            btnPause = new FlatButton { Kind = BtnKind.Ghost, IconName = "pause", Size = new Size(Px(36), Px(36)) };
            btnPause.Click += delegate { Raise(PauseClicked); };
            btnStop = new FlatButton { Kind = BtnKind.Primary, IconName = "stop", IconSize = 14, Text = S.T("Parar", "Stop"), Font = Theme.Font(13 * dpi), Radius = 7 };
            btnStop.Size = new Size(btnStop.PreferredWidth, Px(34));
            btnStop.Click += delegate { Raise(StopClicked); };
            btnCancel = new FlatButton { Kind = BtnKind.Ghost, IconName = "close", Size = new Size(Px(36), Px(36)) };
            btnCancel.Click += delegate { Raise(CancelClicked); };
            tips.SetToolTip(btnPause, S.T("Pausar / continuar", "Pause / resume"));
            tips.SetToolTip(btnStop, S.T("Parar e guardar o vídeo", "Stop and save the video"));
            tips.SetToolTip(btnCancel, S.T("Cancelar (não guarda)", "Cancel (doesn't save)"));
            Controls.AddRange(new Control[] { btnPause, btnStop, btnCancel });

            int x = Px(128);
            btnPause.Location = new Point(x, Px(6)); x += btnPause.Width + Px(4);
            btnStop.Location = new Point(x, Px(7)); x += btnStop.Width + Px(4);
            btnCancel.Location = new Point(x, Px(6)); x += btnCancel.Width + Px(8);
            ClientSize = new Size(x, Px(48));
        }

        static void Raise(Action a) { if (a != null) a(); }

        public void UpdateState(string status, bool live, bool paused)
        {
            Status = status; Live = live; Paused = paused;
            btnPause.IconName = paused ? "play" : "pause";
            btnPause.Enabled = live;
            Invalidate(new Rectangle(0, 0, Px(128), Height));
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;            // CS_DROPSHADOW
                cp.ExStyle |= 0x80 | 0x8000000;      // TOOLWINDOW | NOACTIVATE
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.RoundCorners(this);
            VideoRecorder.ExcludeFromCapture(Handle);
        }

        // arrastar a barra pelo fundo
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { dragging = true; dragStart = e.Location; Cursor = Cursors.SizeAll; } }
        protected override void OnMouseMove(MouseEventArgs e) { if (dragging) Location = new Point(Left + e.X - dragStart.X, Top + e.Y - dragStart.Y); }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Cursor = Cursors.Default; }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color dot = !Live ? Theme.Muted : Paused ? Color.FromArgb(240, 170, 40) : Color.FromArgb(232, 64, 64);
            bool blinkOff = Live && !Paused && DateTime.Now.Millisecond >= 500;
            int d = Px(12);
            using (var b = new SolidBrush(blinkOff ? Color.FromArgb(90, dot) : dot)) g.FillEllipse(b, Px(16), (Height - d) / 2, d, d);
            TextRenderer.DrawText(g, Status, Theme.Font(14.5f * dpi, FontStyle.Bold), new Rectangle(Px(36), 0, Px(92), Height), Theme.Text,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            using (var p = new Pen(Theme.BorderStrong)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tips.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Uma gravação em curso: contagem decrescente, barra, moldura e o gravador.</summary>
    class ScreenRecording
    {
        public readonly VideoRecorder Recorder;
        public event Action<ScreenRecording> Ended;
        public TimeSpan Duration { get; private set; }

        readonly RecordingFrame frame;
        readonly RecordingBar bar;
        readonly Timer timer;
        int countdown = 3;
        bool started, ended;

        public ScreenRecording(Rectangle area, string path, int fps, bool cursor)
        {
            Recorder = new VideoRecorder(path, area, fps, cursor);
            frame = new RecordingFrame(area);
            bar = new RecordingBar();
            bar.PauseClicked += TogglePause;
            bar.StopClicked += Stop;
            bar.CancelClicked += Cancel;
            PlaceBar(area);
            timer = new Timer { Interval = 1000 };
            timer.Tick += OnTick;
        }

        void PlaceBar(Rectangle area)
        {
            Rectangle wa = Screen.FromRectangle(area).WorkingArea;
            int m = (int)(10 * Dpi.Scale), w = bar.Width, h = bar.Height;
            int x = Math.Max(wa.Left + m, Math.Min(wa.Right - w - m, area.X + (area.Width - w) / 2));
            int y;
            if (area.Bottom + m + h <= wa.Bottom) y = area.Bottom + m;
            else if (area.Top - m - h >= wa.Top) y = area.Top - m - h;
            else y = Math.Min(area.Bottom, wa.Bottom) - h - 3 * m; // dentro da zona (a barra não aparece no vídeo)
            bar.Location = new Point(x, y);
        }

        public void Begin()
        {
            frame.Show();
            bar.Show();
            bar.UpdateState(S.T("Em ", "In ") + countdown + "…", false, false);
            timer.Start();
        }

        void OnTick(object sender, EventArgs e)
        {
            if (!started)
            {
                countdown--;
                if (countdown > 0) { bar.UpdateState(S.T("Em ", "In ") + countdown + "…", false, false); return; }
                started = true;
                timer.Interval = 250;
                Recorder.Finished += () => { try { bar.BeginInvoke((Action)Finish); } catch { } };
                Recorder.Start();
            }
            TimeSpan t = Recorder.Elapsed;
            bar.UpdateState(Format(t), true, Recorder.Paused);
        }

        public static string Format(TimeSpan t)
        {
            return t.TotalHours >= 1 ? ((int)t.TotalHours).ToString() + ":" + t.ToString(@"mm\:ss") : t.ToString(@"mm\:ss");
        }

        public void TogglePause()
        {
            if (!started) return;
            Recorder.Paused = !Recorder.Paused;
            frame.Paused = Recorder.Paused;
            bar.UpdateState(Format(Recorder.Elapsed), true, Recorder.Paused);
        }

        public void Stop()
        {
            if (!started) { Cancel(); return; }
            Duration = Recorder.Elapsed;
            Recorder.Paused = false;
            bar.UpdateState(S.T("A guardar…", "Saving…"), false, false);
            Recorder.Stop();
        }

        public void Cancel()
        {
            if (!started) { started = true; Recorder.Cancel(); Finish(); return; }
            Recorder.Cancel();
        }

        /// <summary>Ao sair da aplicação: parar e esperar que o ficheiro fique fechado.</summary>
        public void StopAndWait()
        {
            if (!started) { Cancel(); return; }
            Stop();
            Recorder.Wait(10000);
        }

        void Finish()
        {
            if (ended) return;
            ended = true;
            timer.Stop();
            timer.Dispose();
            frame.Close();
            bar.Close();
            var h = Ended;
            if (h != null) h(this);
        }
    }
}
