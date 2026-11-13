using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SnapStudio
{
    /// <summary>Tema claro/escuro e cor de destaque, partilhados por todas as janelas.</summary>
    static class Theme
    {
        public static bool Dark { get; private set; }
        public static Color Accent, AccentHover, AccentSoft, AccentSoftText;
        public static Color Bg, Surface, Panel, Surface2, Border, BorderStrong, Text, Muted, Hover, Pressed, Danger, Canvas;
        public static readonly Color OnAccent = Color.White;

        public static event Action Changed;

        static string mode = "system", accent = "indigo";
        public static string Mode { get { return mode; } }
        public static string AccentName { get { return accent; } }

        static Theme() { Recompute(); }

        public static void Configure(string newMode, string newAccent)
        {
            mode = string.IsNullOrEmpty(newMode) ? "system" : newMode;
            accent = string.IsNullOrEmpty(newAccent) ? "indigo" : newAccent;
            Recompute();
            var h = Changed;
            if (h != null) h();
        }

        public static void Refresh() { Configure(mode, accent); }

        public static bool SystemDark
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    {
                        object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                        return v != null && Convert.ToInt32(v) == 0;
                    }
                }
                catch { return false; }
            }
        }

        static Color Hex(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff); }

        public static Color Blend(Color a, Color b, double t)
        {
            return Color.FromArgb(255,
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        static double Lum(Color c)
        {
            Func<double, double> f = v => { v /= 255.0; return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); };
            return 0.2126 * f(c.R) + 0.7152 * f(c.G) + 0.0722 * f(c.B);
        }

        static void Recompute()
        {
            Dark = mode == "dark" || (mode == "system" && SystemDark);
            if (Dark)
            {
                Bg = Hex(0x16171C); Surface = Hex(0x1D1F25); Panel = Hex(0x202229); Surface2 = Hex(0x272A33);
                Border = Hex(0x2E3139); BorderStrong = Hex(0x3A3E49); Text = Hex(0xECEDF1); Muted = Hex(0xA1A6B4);
                Hover = Hex(0x2C2F38); Pressed = Hex(0x353945); Danger = Hex(0xF97066); Canvas = Hex(0x16171C);
            }
            else
            {
                Bg = Hex(0xF3F4F7); Surface = Hex(0xFFFFFF); Panel = Hex(0xFFFFFF); Surface2 = Hex(0xF6F7F9);
                Border = Hex(0xE1E4EA); BorderStrong = Hex(0xD3D7DF); Text = Hex(0x1B1D24); Muted = Hex(0x545A69);
                Hover = Hex(0xEEF0F3); Pressed = Hex(0xE3E6EB); Danger = Hex(0xB42318); Canvas = Hex(0xE6E9EF);
            }
            Accent = AccentColor(accent, Dark);
            AccentHover = Blend(Accent, Dark ? Color.White : Color.Black, 0.12);
            AccentSoft = Dark ? Blend(Panel, Accent, 0.30) : Blend(Color.White, Accent, 0.10);
            AccentSoftText = Dark ? Blend(Accent, Color.White, 0.6) : Blend(Accent, Color.Black, 0.35);
        }

        public static Color AccentColor(string name, bool dark)
        {
            switch (name)
            {
                case "blue": return dark ? Hex(0x2B88D8) : Hex(0x0F6CBD);
                case "teal": return dark ? Hex(0x14917F) : Hex(0x0E7A6B);
                case "orange": return dark ? Hex(0xD9541A) : Hex(0xC2410C);
                case "windows":
                    {
                        Color? w = WindowsAccent;
                        if (w.HasValue)
                        {
                            Color c = w.Value;
                            // garantir contraste com texto branco
                            int guard = 0;
                            while (Lum(c) > 0.2 && guard++ < 10) c = Blend(c, Color.Black, 0.12);
                            return c;
                        }
                        goto default;
                    }
                default: return dark ? Hex(0x5B5FEF) : Hex(0x4F46E5);
            }
        }

        public static Color? WindowsAccent
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM"))
                    {
                        object v = k == null ? null : k.GetValue("AccentColor");
                        if (v == null) return null;
                        int abgr = unchecked((int)Convert.ToUInt32(v));
                        return Color.FromArgb(255, abgr & 0xff, (abgr >> 8) & 0xff, (abgr >> 16) & 0xff);
                    }
                }
                catch { return null; }
            }
        }

        // ---------- fontes ----------
        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();
        public static Font Font(float px) { return Font(px, FontStyle.Regular); }
        public static Font Font(float px, FontStyle style)
        {
            string key = px.ToString("0.0") + "|" + (int)style;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font("Segoe UI", px, style, GraphicsUnit.Pixel);
                fonts[key] = f;
            }
            return f;
        }

        // ---------- janela ----------
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

        /// <summary>Barra de título escura/clara conforme o tema.</summary>
        public static void StyleWindow(Form f)
        {
            if (f == null || !f.IsHandleCreated) return;
            try
            {
                int v = Dark ? 1 : 0;
                if (DwmSetWindowAttribute(f.Handle, 20, ref v, 4) != 0) DwmSetWindowAttribute(f.Handle, 19, ref v, 4);
                SetWindowPos(f.Handle, IntPtr.Zero, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0004 | 0x0010 | 0x0020);
            }
            catch { }
        }

        /// <summary>Cantos arredondados do Windows 11 numa janela sem moldura.</summary>
        public static void RoundCorners(Form f)
        {
            if (f == null || !f.IsHandleCreated) return;
            try { int pref = 2; DwmSetWindowAttribute(f.Handle, 33, ref pref, 4); } catch { }
        }

        /// <summary>Barras de deslocamento escuras (Windows 10 1809+).</summary>
        public static void ScrollbarTheme(Control c)
        {
            if (c == null || !c.IsHandleCreated) return;
            try { SetWindowTheme(c.Handle, Dark ? "DarkMode_Explorer" : "Explorer", null); } catch { }
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            rad = Math.Min(rad, Math.Min(r.Width, r.Height) / 2f);
            if (rad <= 0.5f) { var p0 = new GraphicsPath(); p0.AddRectangle(r); return p0; }
            return AppIcon.RoundRect(r, rad);
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        public static void StrokeRound(Graphics g, Color c, float w, RectangleF r, float rad)
        {
            using (var p = Round(r, rad)) using (var pen = new Pen(c, w)) g.DrawPath(pen, p);
        }
    }

    /// <summary>Renderizador dos menus (bandeja, botão direito) com as cores do tema.</summary>
    class ThemedRenderer : ToolStripRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Panel)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            var r = new Rectangle(0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            using (var p = new Pen(Theme.BorderStrong)) e.Graphics.DrawRectangle(p, r);
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!e.Item.Selected || !e.Item.Enabled) return;
            var r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(e.Graphics, Theme.Hover, r, 4);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Muted;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Text;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var r = e.ImageRectangle;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = new Pen(Theme.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                e.Graphics.DrawLines(p, new[] {
                    new PointF(r.X + r.Width * 0.2f, r.Y + r.Height * 0.55f),
                    new PointF(r.X + r.Width * 0.42f, r.Y + r.Height * 0.75f),
                    new PointF(r.X + r.Width * 0.8f, r.Y + r.Height * 0.28f) });
        }
    }
}
