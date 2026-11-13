using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SnapStudio
{
    public enum Tool { Select, Arrow, Line, Rect, Ellipse, Highlight, Blur, Pixelate, Text, Step, Pen, Crop }

    /// <summary>Anotação editável (objeto, não pintado diretamente na imagem até guardar).</summary>
    [DataContract]
    [KnownType(typeof(ShapeAnn))]
    [KnownType(typeof(TextAnn))]
    [KnownType(typeof(StepAnn))]
    [KnownType(typeof(PenAnn))]
    public abstract class Ann
    {
        [DataMember] public int Argb;
        [DataMember] public float Width;

        public Color Color { get { return Color.FromArgb(Argb); } set { Argb = value.ToArgb(); } }
        public abstract void Draw(Graphics g, Bitmap baseImage);
        public abstract RectangleF Bounds { get; }
        public virtual bool Hit(PointF p, float tol) { RectangleF b = Bounds; b.Inflate(tol, tol); return b.Contains(p); }
        public abstract void Offset(float dx, float dy);
        public virtual bool UsesFont { get { return false; } }
        public virtual bool UsesWidth { get { return true; } }
        public virtual float FontSize { get { return 0; } set { } }

        // ---------- serialização ----------
        static DataContractJsonSerializer Ser() { return new DataContractJsonSerializer(typeof(List<Ann>)); }

        public static string Serialize(List<Ann> list)
        {
            using (var ms = new MemoryStream())
            {
                Ser().WriteObject(ms, list);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        public static List<Ann> Deserialize(string json)
        {
            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                return (List<Ann>)Ser().ReadObject(ms) ?? new List<Ann>();
        }

        // ---------- utilitários ----------
        public static float Dist(PointF a, PointF b) { float dx = a.X - b.X, dy = a.Y - b.Y; return (float)Math.Sqrt(dx * dx + dy * dy); }

        public static float DistToSegment(PointF p, PointF a, PointF b)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, len2 = dx * dx + dy * dy;
            if (len2 < 0.0001f) return Dist(p, a);
            float t = Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / len2));
            return Dist(p, new PointF(a.X + t * dx, a.Y + t * dy));
        }

        public static RectangleF Norm(float x1, float y1, float x2, float y2)
        {
            return RectangleF.FromLTRB(Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2));
        }

        public static Color Contrast(Color c)
        {
            return (c.R * 0.299 + c.G * 0.587 + c.B * 0.114) > 170 ? Color.FromArgb(40, 40, 40) : Color.White;
        }

        /// <summary>Oculta uma zona: desfoque (reduz muito e amplia suavemente) ou pixelização.</summary>
        public static void Obscure(Graphics g, Bitmap src, RectangleF rf, bool pixelate)
        {
            Rectangle r = Rectangle.Round(rf);
            r.Intersect(new Rectangle(0, 0, src.Width, src.Height));
            if (r.Width < 2 || r.Height < 2) return;
            int block = pixelate ? Math.Max(10, Math.Min(r.Width, r.Height) / 8) : Math.Max(18, Math.Min(r.Width, r.Height) / 3);
            int sw = Math.Max(1, r.Width / block), sh = Math.Max(1, r.Height / block);
            if (!pixelate) { sw = Math.Max(2, sw); sh = Math.Max(2, sh); }
            // margem para o desfoque não escurecer as bordas
            var grab = r;
            if (!pixelate) { grab.Inflate(block, block); grab.Intersect(new Rectangle(0, 0, src.Width, src.Height)); }
            float fx = sw / (float)r.Width, fy = sh / (float)r.Height;
            int gw = Math.Max(1, (int)Math.Round(grab.Width * fx)), gh = Math.Max(1, (int)Math.Round(grab.Height * fy));
            using (var small = new Bitmap(gw, gh, PixelFormat.Format32bppArgb))
            {
                using (var sg = Graphics.FromImage(small))
                {
                    sg.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    sg.DrawImage(src, new Rectangle(0, 0, gw, gh), grab, GraphicsUnit.Pixel);
                }
                var st = g.Save();
                g.SetClip(r, CombineMode.Intersect);
                g.InterpolationMode = pixelate ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.Half;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(small, grab, 0, 0, gw, gh, GraphicsUnit.Pixel, ia);
                }
                g.Restore(st);
            }
        }
    }

    /// <summary>Seta, linha, caixa, elipse, marcador, esbatimento, pixelização.</summary>
    [DataContract]
    public class ShapeAnn : Ann
    {
        [DataMember] public string Kind;   // arrow line rect ellipse highlight blur pixelate
        [DataMember] public float X1, Y1, X2, Y2;

        public PointF P1 { get { return new PointF(X1, Y1); } set { X1 = value.X; Y1 = value.Y; } }
        public PointF P2 { get { return new PointF(X2, Y2); } set { X2 = value.X; Y2 = value.Y; } }
        public bool IsLinear { get { return Kind == "arrow" || Kind == "line"; } }
        public bool IsObscure { get { return Kind == "blur" || Kind == "pixelate"; } }
        public override bool UsesWidth { get { return !IsObscure && Kind != "highlight"; } }

        public override RectangleF Bounds
        {
            get
            {
                var r = Norm(X1, Y1, X2, Y2);
                if (IsLinear) r.Inflate(Width * 2, Width * 2);
                return r;
            }
        }

        public override void Draw(Graphics g, Bitmap baseImage)
        {
            RectangleF r = Norm(X1, Y1, X2, Y2);
            switch (Kind)
            {
                case "arrow":
                    if (Dist(P1, P2) < 1) return;
                    using (var pen = new Pen(Color, Width))
                    {
                        float head = Math.Max(3.2f, 14f / Width);
                        using (var cap = new AdjustableArrowCap(head * 0.85f, head, true))
                        {
                            pen.StartCap = LineCap.Round;
                            pen.CustomEndCap = cap;
                            g.DrawLine(pen, X1, Y1, X2, Y2);
                        }
                    }
                    break;
                case "line":
                    using (var pen = new Pen(Color, Width) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                        g.DrawLine(pen, X1, Y1, X2, Y2);
                    break;
                case "rect":
                    using (var pen = new Pen(Color, Width) { LineJoin = LineJoin.Miter })
                        g.DrawRectangle(pen, r.X, r.Y, r.Width, r.Height);
                    break;
                case "ellipse":
                    using (var pen = new Pen(Color, Width))
                        g.DrawEllipse(pen, r);
                    break;
                case "highlight":
                    using (var b = new SolidBrush(Color.FromArgb(105, Color)))
                        g.FillRectangle(b, r);
                    break;
                case "blur":
                    Obscure(g, baseImage, r, false);
                    break;
                case "pixelate":
                    Obscure(g, baseImage, r, true);
                    break;
            }
        }

        public override bool Hit(PointF p, float tol)
        {
            if (IsLinear) return DistToSegment(p, P1, P2) <= tol + Width / 2;
            return base.Hit(p, tol);
        }

        public override void Offset(float dx, float dy) { X1 += dx; Y1 += dy; X2 += dx; Y2 += dy; }
    }

    [DataContract]
    public class TextAnn : Ann
    {
        [DataMember] public float X, Y;
        [DataMember] public string Text;
        [DataMember] public float Size;

        public override bool UsesFont { get { return true; } }
        public override bool UsesWidth { get { return false; } }
        public override float FontSize { get { return Size; } set { Size = value; } }

        GraphicsPath BuildPath()
        {
            var path = new GraphicsPath();
            using (var ff = new FontFamily("Segoe UI"))
                path.AddString(string.IsNullOrEmpty(Text) ? " " : Text, ff, (int)FontStyle.Bold, Size, new PointF(X, Y), StringFormat.GenericTypographic);
            return path;
        }

        public override RectangleF Bounds
        {
            get
            {
                using (var p = BuildPath())
                {
                    var b = p.GetBounds();
                    if (b.Width <= 0) b = new RectangleF(X, Y, Size, Size);
                    b.Inflate(Size / 8, Size / 8);
                    return b;
                }
            }
        }

        public override void Draw(Graphics g, Bitmap baseImage)
        {
            using (var path = BuildPath())
            using (var halo = new Pen(Contrast(Color), Math.Max(2f, Size / 5f)) { LineJoin = LineJoin.Round })
            using (var b = new SolidBrush(Color))
            {
                g.DrawPath(halo, path);
                g.FillPath(b, path);
            }
        }

        public override void Offset(float dx, float dy) { X += dx; Y += dy; }
    }

    /// <summary>Número de passo (círculo 1, 2, 3…).</summary>
    [DataContract]
    public class StepAnn : Ann
    {
        [DataMember] public float X, Y;
        [DataMember] public int Number;
        [DataMember] public float Size;

        public override bool UsesFont { get { return true; } }
        public override bool UsesWidth { get { return false; } }
        public override float FontSize { get { return Size; } set { Size = value; } }
        float Radius { get { return Size * 0.8f + 4; } }

        public override RectangleF Bounds { get { float r = Radius; return new RectangleF(X - r, Y - r, 2 * r, 2 * r); } }

        public override void Draw(Graphics g, Bitmap baseImage)
        {
            RectangleF r = Bounds;
            Color fg = Contrast(Color);
            using (var b = new SolidBrush(Color)) g.FillEllipse(b, r);
            using (var p = new Pen(fg == Color.White ? Color.White : Color.FromArgb(40, 40, 40), Math.Max(1.5f, Size / 12f))) g.DrawEllipse(p, r);
            using (var f = new Font("Segoe UI", Size * 0.95f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var b = new SolidBrush(fg))
                g.DrawString(Number.ToString(), f, b, new RectangleF(r.X, r.Y + Size * 0.04f, r.Width, r.Height), sf);
        }

        public override bool Hit(PointF p, float tol) { return Dist(p, new PointF(X, Y)) <= Radius + tol; }
        public override void Offset(float dx, float dy) { X += dx; Y += dy; }
    }

    /// <summary>Desenho à mão livre.</summary>
    [DataContract]
    public class PenAnn : Ann
    {
        [DataMember] public List<float> Pts;

        public PenAnn() { Pts = new List<float>(); }

        PointF[] Points()
        {
            var a = new PointF[Pts.Count / 2];
            for (int i = 0; i < a.Length; i++) a[i] = new PointF(Pts[2 * i], Pts[2 * i + 1]);
            return a;
        }

        public override RectangleF Bounds
        {
            get
            {
                if (Pts.Count < 2) return RectangleF.Empty;
                float x1 = float.MaxValue, y1 = float.MaxValue, x2 = float.MinValue, y2 = float.MinValue;
                for (int i = 0; i + 1 < Pts.Count; i += 2)
                {
                    x1 = Math.Min(x1, Pts[i]); x2 = Math.Max(x2, Pts[i]);
                    y1 = Math.Min(y1, Pts[i + 1]); y2 = Math.Max(y2, Pts[i + 1]);
                }
                var r = RectangleF.FromLTRB(x1, y1, x2, y2);
                r.Inflate(Width / 2, Width / 2);
                return r;
            }
        }

        public override void Draw(Graphics g, Bitmap baseImage)
        {
            var pts = Points();
            if (pts.Length == 0) return;
            if (pts.Length == 1)
            {
                using (var b = new SolidBrush(Color)) g.FillEllipse(b, pts[0].X - Width / 2, pts[0].Y - Width / 2, Width, Width);
                return;
            }
            using (var pen = new Pen(Color, Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, pts);
        }

        public override bool Hit(PointF p, float tol)
        {
            var pts = Points();
            for (int i = 0; i + 1 < pts.Length; i++)
                if (DistToSegment(p, pts[i], pts[i + 1]) <= tol + Width / 2) return true;
            return pts.Length == 1 && Dist(p, pts[0]) <= tol + Width;
        }

        public override void Offset(float dx, float dy)
        {
            for (int i = 0; i + 1 < Pts.Count; i += 2) { Pts[i] += dx; Pts[i + 1] += dy; }
        }
    }
}
