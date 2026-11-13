using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace SnapStudio
{
    public enum CaptureMode { Region, ActiveWindow, FullScreen }

    static class CaptureService
    {
        public static Rectangle VirtualScreen { get { return SystemInformation.VirtualScreen; } }

        public static Bitmap CaptureScreen(Rectangle r)
        {
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                IntPtr dst = g.GetHdc();
                IntPtr src = Native.GetDC(IntPtr.Zero);
                try
                {
                    // SRCCOPY | CAPTUREBLT (inclui janelas em camadas/transparentes)
                    if (!Native.BitBlt(dst, 0, 0, r.Width, r.Height, src, r.X, r.Y, 0x00CC0020 | 0x40000000))
                        throw new InvalidOperationException("BitBlt failed");
                }
                finally
                {
                    Native.ReleaseDC(IntPtr.Zero, src);
                    g.ReleaseHdc(dst);
                }
            }
            return bmp;
        }

        public static Bitmap CaptureActiveWindow()
        {
            IntPtr h = Native.GetForegroundWindow();
            Rectangle r = h == IntPtr.Zero ? Rectangle.Empty : Native.GetWindowBounds(h);
            r.Intersect(VirtualScreen);
            if (r.Width <= 0 || r.Height <= 0) r = VirtualScreen;
            return CaptureScreen(r);
        }

        /// <summary>Escolher uma zona do ecrã (para gravar vídeo). Devolve coordenadas de ecrã, ou null se cancelado.</summary>
        public static Rectangle? SelectArea(string hint)
        {
            Rectangle vs = VirtualScreen;
            var windows = Native.GetVisibleWindows();
            using (var full = CaptureScreen(vs))
            using (var sel = new RegionSelector(full, windows, vs, hint))
            {
                if (sel.ShowDialog() != DialogResult.OK) return null;
                Rectangle r = sel.Selection;
                r.Intersect(new Rectangle(0, 0, full.Width, full.Height));
                if (r.Width <= 0 || r.Height <= 0) return null;
                r.Offset(vs.X, vs.Y);
                return r;
            }
        }

        /// <summary>Modo captura: ecrã congelado onde se escolhe janela, região ou ecrã inteiro. Devolve null se cancelado.</summary>
        public static Bitmap SelectRegion()
        {
            Rectangle vs = VirtualScreen;
            var windows = Native.GetVisibleWindows();
            using (var full = CaptureScreen(vs))
            using (var sel = new RegionSelector(full, windows, vs))
            {
                if (sel.ShowDialog() != DialogResult.OK) return null;
                Rectangle r = sel.Selection;
                r.Intersect(new Rectangle(0, 0, full.Width, full.Height));
                if (r.Width <= 0 || r.Height <= 0) return null;
                return ImageUtil.Crop(full, r);
            }
        }
    }
}
