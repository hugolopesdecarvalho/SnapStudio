using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace SnapStudio
{
    /// <summary>Ícone da aplicação desenhado em código (gera também o app.ico no build).</summary>
    static class AppIcon
    {
        public static Bitmap Draw(int n)
        {
            var bmp = new Bitmap(n, n, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float m = n * 0.04f;
                var r = new RectangleF(m, m, n - 2 * m, n - 2 * m);
                using (var path = RoundRect(r, n * 0.22f))
                using (var br = new LinearGradientBrush(r, Color.FromArgb(59, 130, 246), Color.FromArgb(109, 40, 217), 45f))
                    g.FillPath(br, path);
                float w = Math.Max(1.5f, n * 0.085f);
                float a = n * 0.25f, b = n * 0.75f, l = n * 0.17f;
                using (var p = new Pen(Color.White, w) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    g.DrawLines(p, new[] { new PointF(a, a + l), new PointF(a, a), new PointF(a + l, a) });
                    g.DrawLines(p, new[] { new PointF(b - l, a), new PointF(b, a), new PointF(b, a + l) });
                    g.DrawLines(p, new[] { new PointF(b, b - l), new PointF(b, b), new PointF(b - l, b) });
                    g.DrawLines(p, new[] { new PointF(a + l, b), new PointF(a, b), new PointF(a, b - l) });
                }
                float c = n * 0.11f;
                g.FillEllipse(Brushes.White, n / 2f - c, n / 2f - c, 2 * c, 2 * c);
            }
            return bmp;
        }

        public static Icon Create(int n)
        {
            using (var bmp = Draw(n))
            {
                IntPtr h = bmp.GetHicon();
                var ic = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return ic;
            }
        }

        static Icon formIcon;
        public static Icon FormIcon
        {
            get
            {
                if (formIcon == null)
                {
                    try { formIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
                    if (formIcon == null) formIcon = Create(32);
                }
                return formIcon;
            }
        }

        /// <summary>Grava um .ico multi-tamanho com entradas PNG.</summary>
        public static void SaveIco(string path)
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            var pngs = new List<byte[]>();
            foreach (int s in sizes)
                using (var bmp = Draw(s))
                using (var ms = new MemoryStream()) { bmp.Save(ms, ImageFormat.Png); pngs.Add(ms.ToArray()); }
            using (var fs = File.Create(path))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(pngs[i].Length); w.Write(offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
        }

        public static GraphicsPath RoundRect(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>Ícones das barras de ferramentas, desenhados numa grelha 20x20 e escalados.</summary>
    static class ToolIcons
    {
        static readonly Color Ink = Color.FromArgb(45, 45, 52);
        static readonly Color Acc = Color.FromArgb(37, 99, 235);

        public static Bitmap Swatch(Color c, int s)
        {
            var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                var r = new RectangleF(1.5f, 1.5f, s - 3, s - 3);
                using (var path = AppIcon.RoundRect(r, s * 0.18f))
                using (var b = new SolidBrush(c))
                using (var p = new Pen(Color.FromArgb(120, 0, 0, 0), 1))
                {
                    g.FillPath(b, path);
                    g.DrawPath(p, path);
                }
            }
            return bmp;
        }

        static readonly Dictionary<string, Bitmap> cache = new Dictionary<string, Bitmap>();

        /// <summary>Ícone partilhado (em cache): não fazer Dispose.</summary>
        public static Bitmap Get(string name, int s) { return Get(name, s, Ink); }

        /// <summary>Ícone monocromático na cor indicada (em cache): não fazer Dispose.</summary>
        public static Bitmap Get(string name, int s, Color ink)
        {
            string key = name + "|" + s + "|" + ink.ToArgb();
            Bitmap bmp;
            if (cache.TryGetValue(key, out bmp)) return bmp;
            bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.ScaleTransform(s / 20f, s / 20f);
                using (var p = new Pen(ink, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                using (var b = new SolidBrush(ink))
                using (var acc = new Pen(ink, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                    Paint(g, name, p, b, acc, ink);
            }
            cache[key] = bmp;
            return bmp;
        }

        static void Paint(Graphics g, string name, Pen p, Brush b, Pen acc, Color ink)
        {
            switch (name)
            {
                case "moon":
                    using (var outer = new GraphicsPath())
                    using (var inner = new GraphicsPath())
                    {
                        outer.AddEllipse(3, 3, 14, 14);
                        inner.AddEllipse(7.5f, 0.5f, 13, 13);
                        using (var rg = new Region(outer)) { rg.Exclude(inner); g.FillRegion(b, rg); }
                    }
                    break;
                case "sun":
                    g.DrawEllipse(p, 6.5f, 6.5f, 7, 7);
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        g.DrawLine(p, 10 + (float)Math.Cos(a) * 6f, 10 + (float)Math.Sin(a) * 6f, 10 + (float)Math.Cos(a) * 8.3f, 10 + (float)Math.Sin(a) * 8.3f);
                    }
                    break;
                case "close":
                    g.DrawLine(p, 5, 5, 15, 15); g.DrawLine(p, 15, 5, 5, 15);
                    break;
                case "expand":
                    g.DrawLines(p, new[] { new PointF(12, 3), new PointF(17, 3), new PointF(17, 8) });
                    g.DrawLines(p, new[] { new PointF(8, 17), new PointF(3, 17), new PointF(3, 12) });
                    g.DrawLine(p, 17, 3, 11.5f, 8.5f); g.DrawLine(p, 3, 17, 8.5f, 11.5f);
                    break;
                case "edit":
                    g.DrawPolygon(p, new[] { new PointF(4, 16), new PointF(4, 13), new PointF(13, 4), new PointF(16, 7), new PointF(7, 16) });
                    break;
                case "search":
                    g.DrawEllipse(p, 3.5f, 3.5f, 10, 10); g.DrawLine(p, 12, 12, 16.5f, 16.5f);
                    break;
                case "plus":
                    g.DrawLine(p, 10, 4, 10, 16); g.DrawLine(p, 4, 10, 16, 10);
                    break;
                case "clipboard":
                    g.DrawRectangle(p, 7, 2.5f, 6, 3);
                    g.DrawLines(p, new[] { new PointF(13, 4), new PointF(15.5f, 4), new PointF(15.5f, 17.5f), new PointF(4.5f, 17.5f), new PointF(4.5f, 4), new PointF(7, 4) });
                    g.DrawLines(p, new[] { new PointF(7.5f, 11.5f), new PointF(9.3f, 13.3f), new PointF(12.8f, 9.5f) });
                    break;
                case "save":
                    g.DrawLines(p, new[] { new PointF(3, 3), new PointF(14, 3), new PointF(17, 6), new PointF(17, 17), new PointF(3, 17), new PointF(3, 3) });
                    g.DrawRectangle(p, 6.5f, 3, 6, 4.5f);
                    g.DrawRectangle(p, 6, 11, 8, 6);
                    break;
                case "saveas":
                    g.DrawLines(p, new[] { new PointF(3, 3), new PointF(12, 3), new PointF(15, 6), new PointF(15, 10) });
                    g.DrawLines(p, new[] { new PointF(10, 17), new PointF(3, 17), new PointF(3, 3) });
                    g.DrawRectangle(p, 6, 3, 5, 4);
                    g.DrawLine(acc, 15, 12, 15, 19); g.DrawLine(acc, 11.5f, 15.5f, 18.5f, 15.5f);
                    break;
                case "copy":
                    g.DrawRectangle(p, 7, 7, 10, 10);
                    g.DrawLines(p, new[] { new PointF(4, 13), new PointF(4, 4), new PointF(13, 4) });
                    break;
                case "drag":
                    g.DrawLine(p, 10, 2.5f, 10, 17.5f); g.DrawLine(p, 2.5f, 10, 17.5f, 10);
                    g.DrawLines(p, new[] { new PointF(7.5f, 5), new PointF(10, 2.5f), new PointF(12.5f, 5) });
                    g.DrawLines(p, new[] { new PointF(7.5f, 15), new PointF(10, 17.5f), new PointF(12.5f, 15) });
                    g.DrawLines(p, new[] { new PointF(5, 7.5f), new PointF(2.5f, 10), new PointF(5, 12.5f) });
                    g.DrawLines(p, new[] { new PointF(15, 7.5f), new PointF(17.5f, 10), new PointF(15, 12.5f) });
                    break;
                case "redo":
                    g.TranslateTransform(20, 0); g.ScaleTransform(-1, 1);
                    goto case "undo";
                case "undo":
                    g.DrawLine(p, 4, 7, 11, 7);
                    g.DrawArc(p, 7, 7, 8, 8, -90, 180);
                    g.DrawLine(p, 11, 15, 7, 15);
                    g.DrawLines(p, new[] { new PointF(7, 4), new PointF(4, 7), new PointF(7, 10) });
                    break;
                case "select":
                    {
                        var pts = new[] { new PointF(5, 2.5f), new PointF(5, 16), new PointF(8.5f, 12.5f), new PointF(11, 17.5f), new PointF(13.2f, 16.5f), new PointF(10.8f, 11.5f), new PointF(15, 11.5f) };
                        g.FillPolygon(Brushes.White, pts);
                        g.DrawPolygon(p, pts);
                    }
                    break;
                case "arrow":
                    using (var rp = new Pen(ink, 1.8f) { StartCap = LineCap.Round })
                        g.DrawLine(rp, 4, 16, 13, 7);
                    g.FillPolygon(b, new[] { new PointF(17, 3), new PointF(9.5f, 5.5f), new PointF(14.5f, 10.5f) });
                    break;
                case "line":
                    g.DrawLine(p, 4, 16, 16, 4);
                    break;
                case "rect":
                    using (var rp = new Pen(ink, 1.8f))
                        g.DrawRectangle(rp, 3, 5, 14, 10);
                    break;
                case "ellipse":
                    g.DrawEllipse(p, 3, 4, 14, 12);
                    break;
                case "highlight":
                    using (var yb = new SolidBrush(Color.FromArgb(200, 250, 204, 21))) g.FillRectangle(yb, 2, 12, 16, 5);
                    g.DrawPolygon(p, new[] { new PointF(8, 12), new PointF(14, 3), new PointF(17, 5), new PointF(11, 14) });
                    break;
                case "blur":
                    using (var gb = new LinearGradientBrush(new RectangleF(3, 3, 14, 14), Color.FromArgb(150, 150, 165), Color.FromArgb(225, 225, 235), 45f))
                    using (var path = AppIcon.RoundRect(new RectangleF(3, 3, 14, 14), 3))
                    {
                        g.FillPath(gb, path);
                        using (var dp = new Pen(ink, 1.2f) { DashStyle = DashStyle.Dash }) g.DrawPath(dp, path);
                    }
                    g.FillEllipse(Brushes.White, 6, 6, 5, 5);
                    using (var wb = new SolidBrush(Color.FromArgb(160, 255, 255, 255))) g.FillEllipse(wb, 9, 9, 6, 6);
                    break;
                case "pixelate":
                    for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 4; x++)
                        {
                            int v = ((x + y) % 2 == 0) ? 110 : 190;
                            v += (x * 7 + y * 13) % 30;
                            using (var cb = new SolidBrush(Color.FromArgb(v, v, v + 10 > 255 ? 255 : v + 10)))
                                g.FillRectangle(cb, 3 + x * 3.5f, 3 + y * 3.5f, 3.5f, 3.5f);
                        }
                    g.DrawRectangle(p, 3, 3, 14, 14);
                    break;
                case "text":
                    g.DrawLines(p, new[] { new PointF(4, 17), new PointF(10, 3), new PointF(16, 17) });
                    g.DrawLine(p, 6.5f, 12, 13.5f, 12);
                    break;
                case "step":
                    g.DrawEllipse(p, 2.5f, 2.5f, 15, 15);
                    g.DrawLines(p, new[] { new PointF(8.5f, 7.5f), new PointF(10.5f, 6.3f), new PointF(10.5f, 14) });
                    break;
                case "pen":
                    g.DrawBezier(p, 3, 15, 7, 2, 12, 19, 17, 5);
                    break;
                case "crop":
                    g.DrawLines(p, new[] { new PointF(6, 2), new PointF(6, 14), new PointF(18, 14) });
                    g.DrawLines(p, new[] { new PointF(2, 6), new PointF(14, 6), new PointF(14, 18) });
                    break;
                case "delete":
                    g.DrawLine(p, 3, 5.5f, 17, 5.5f);
                    g.DrawLines(p, new[] { new PointF(8, 5.5f), new PointF(8, 3), new PointF(12, 3), new PointF(12, 5.5f) });
                    g.DrawLines(p, new[] { new PointF(5, 5.5f), new PointF(6, 17), new PointF(14, 17), new PointF(15, 5.5f) });
                    g.DrawLine(p, 8.5f, 8.5f, 8.5f, 14); g.DrawLine(p, 11.5f, 8.5f, 11.5f, 14);
                    break;
                case "region":
                    using (var dp = new Pen(ink, 1.6f) { DashStyle = DashStyle.Dash }) g.DrawRectangle(dp, 3, 4, 14, 11);
                    g.DrawLine(p, 12, 12, 18, 18); g.DrawLine(p, 15, 12, 12, 12); g.DrawLine(p, 12, 12, 12, 15);
                    break;
                case "window":
                    g.DrawRectangle(p, 2.5f, 3.5f, 15, 13);
                    g.FillRectangle(b, 2.5f, 3.5f, 15, 3.2f);
                    break;
                case "full":
                    g.DrawRectangle(p, 2.5f, 3, 15, 10.5f);
                    g.DrawLine(p, 10, 13.5f, 10, 16.5f); g.DrawLine(p, 6.5f, 17, 13.5f, 17);
                    break;
                case "timer":
                    g.DrawEllipse(p, 3, 4, 14, 14);
                    g.DrawLine(p, 10, 11, 10, 7); g.DrawLine(p, 10, 11, 13, 13);
                    g.DrawLine(p, 8, 2, 12, 2);
                    break;
                case "record":
                    g.DrawEllipse(p, 2.5f, 2.5f, 15, 15);
                    using (var rb = new SolidBrush(Color.FromArgb(232, 64, 64))) g.FillEllipse(rb, 6, 6, 8, 8);
                    break;
                case "stop":
                    using (var path = new GraphicsPath()) { path.AddRectangle(new RectangleF(5, 5, 10, 10)); g.FillPath(b, path); }
                    break;
                case "pause":
                    g.FillRectangle(b, 5, 4, 3.4f, 12); g.FillRectangle(b, 11.6f, 4, 3.4f, 12);
                    break;
                case "play":
                    g.FillPolygon(b, new[] { new PointF(6, 4), new PointF(6, 16), new PointF(16, 10) });
                    break;
                case "folder":
                    g.DrawLines(p, new[] { new PointF(2.5f, 16), new PointF(2.5f, 4), new PointF(8, 4), new PointF(10, 6.5f), new PointF(17.5f, 6.5f), new PointF(17.5f, 16), new PointF(2.5f, 16) });
                    break;
                case "settings":
                    for (int i = 0; i < 8; i++)
                    {
                        double a = i * Math.PI / 4;
                        g.DrawLine(p, 10 + (float)Math.Cos(a) * 5.5f, 10 + (float)Math.Sin(a) * 5.5f, 10 + (float)Math.Cos(a) * 8f, 10 + (float)Math.Sin(a) * 8f);
                    }
                    g.DrawEllipse(p, 4.5f, 4.5f, 11, 11);
                    g.DrawEllipse(p, 8, 8, 4, 4);
                    break;
                case "history":
                    g.DrawRectangle(p, 3, 3, 6, 6); g.DrawRectangle(p, 11, 3, 6, 6);
                    g.DrawRectangle(p, 3, 11, 6, 6); g.DrawRectangle(p, 11, 11, 6, 6);
                    break;
                case "open":
                    g.DrawRectangle(p, 3, 4, 14, 12);
                    g.DrawLines(p, new[] { new PointF(5, 14), new PointF(9, 9), new PointF(12, 12), new PointF(14, 10), new PointF(16, 13) });
                    g.FillEllipse(b, 12, 6, 2.5f, 2.5f);
                    break;
            }
        }
    }
}
