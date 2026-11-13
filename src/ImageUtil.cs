using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace SnapStudio
{
    static class ImageUtil
    {
        /// <summary>Carrega uma imagem sem bloquear o ficheiro.</summary>
        public static Bitmap LoadNoLock(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes))
            using (var img = Image.FromStream(ms))
            {
                var bmp = new Bitmap(img.Width, img.Height, PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height));
                }
                return bmp;
            }
        }

        public static Bitmap Crop(Bitmap src, Rectangle r)
        {
            var bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(src, new Rectangle(0, 0, r.Width, r.Height), r, GraphicsUnit.Pixel);
            }
            return bmp;
        }

        public static void Save(Image img, string path)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string ext = (Path.GetExtension(path) ?? "").ToLowerInvariant();
            if (ext == ".jpg" || ext == ".jpeg")
            {
                var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using (var p = new EncoderParameters(1))
                {
                    p.Param[0] = new EncoderParameter(Encoder.Quality, 92L);
                    // JPEG não suporta transparência: achatar sobre branco
                    using (var flat = new Bitmap(img.Width, img.Height, PixelFormat.Format24bppRgb))
                    {
                        using (var g = Graphics.FromImage(flat)) { g.Clear(Color.White); g.DrawImage(img, new Rectangle(0, 0, img.Width, img.Height)); }
                        flat.Save(path, codec, p);
                    }
                }
            }
            else if (ext == ".bmp") img.Save(path, ImageFormat.Bmp);
            else img.Save(path, ImageFormat.Png);
        }

        /// <summary>Miniatura que cobre a área pedida (recorte central), e as dimensões originais.</summary>
        public static Bitmap ThumbCover(string path, Size size, out Size original)
        {
            if (VideoFile.Is(path)) return VideoThumb(path, size, out original);
            byte[] bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes))
            using (var img = Image.FromStream(ms, false, false))
            {
                original = new Size(img.Width, img.Height);
                var th = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                // mostrar o topo da imagem (onde costuma estar a informação), centrado na horizontal
                float sc = Math.Max(size.Width / (float)img.Width, size.Height / (float)img.Height);
                if (sc > 1) sc = Math.Max(1f, Math.Min(sc, 2f));
                float w = img.Width * sc, h = img.Height * sc;
                using (var g = Graphics.FromImage(th))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.Clear(Color.FromArgb(230, 232, 236));
                    g.DrawImage(img, new RectangleF((size.Width - w) / 2f, h > size.Height ? 0 : (size.Height - h) / 2f, w, h));
                }
                return th;
            }
        }

        static Bitmap VideoThumb(string path, Size size, out Size original)
        {
            original = Size.Empty;
            var th = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(th))
            {
                g.Clear(Color.FromArgb(40, 42, 48));
                using (var src = VideoFile.Thumbnail(path, Math.Max(size.Width, size.Height)))
                {
                    if (src != null)
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        float sc = Math.Max(size.Width / (float)src.Width, size.Height / (float)src.Height);
                        float w = src.Width * sc, h = src.Height * sc;
                        g.DrawImage(src, new RectangleF((size.Width - w) / 2f, (size.Height - h) / 2f, w, h));
                    }
                }
                VideoFile.DrawPlayBadge(g, new RectangleF(0, 0, size.Width, size.Height));
            }
            return th;
        }

        public static Bitmap Thumbnail(string path, Size size)
        {
            byte[] bytes = File.ReadAllBytes(path);
            using (var ms = new MemoryStream(bytes))
            using (var img = Image.FromStream(ms, false, false))
            {
                var th = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppArgb);
                float sc = Math.Min((size.Width - 6f) / img.Width, (size.Height - 6f) / img.Height);
                if (sc > 1) sc = 1;
                int w = Math.Max(1, (int)(img.Width * sc)), h = Math.Max(1, (int)(img.Height * sc));
                var r = new Rectangle((size.Width - w) / 2, (size.Height - h) / 2, w, h);
                using (var g = Graphics.FromImage(th))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    using (var sh = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                        g.FillRectangle(sh, r.X + 2, r.Y + 2, r.Width, r.Height);
                    g.DrawImage(img, r);
                    using (var pen = new Pen(Color.FromArgb(70, 0, 0, 0)))
                        g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
                }
                return th;
            }
        }
    }
}
