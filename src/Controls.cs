using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SnapStudio
{
    public enum BtnKind { Primary, Secondary, Ghost, Danger, Nav, Swatch, Tool }

    static class Dpi
    {
        static float scale = 0;
        public static float Scale { get { if (scale == 0) using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f; return scale; } }
    }

    /// <summary>Base dos controlos desenhados à mão: escala DPI e repintura quando o tema muda.</summary>
    abstract class ThemedControl : Control
    {
        protected float dpi = 1f;
        protected bool hover, down;

        protected ThemedControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            dpi = Dpi.Scale;
            Theme.Changed += OnThemeChanged;
        }

        void OnThemeChanged() { if (!IsDisposed) Invalidate(); }

        protected int Px(float v) { return (int)Math.Round(v * dpi); }

        protected override void Dispose(bool disposing)
        {
            Theme.Changed -= OnThemeChanged;
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected Color ParentBack
        {
            get
            {
                Control p = Parent;
                while (p != null && p.BackColor.A < 255) p = p.Parent;
                return p != null ? p.BackColor : Theme.Panel;
            }
        }

        protected void PaintFocus(Graphics g, RectangleF r, float rad)
        {
            if (!Focused || !ShowFocusCues) return;
            r.Inflate(-1, -1);
            Theme.StrokeRound(g, Theme.Accent, 2f, r, rad);
        }
    }

    /// <summary>Botão plano com ícone, texto e variantes (primário, secundário, fantasma, navegação, cor…).</summary>
    class FlatButton : ThemedControl
    {
        BtnKind kind = BtnKind.Secondary;
        string icon, badge;
        bool isChecked, vertical, round;
        Color swatch;
        int iconSize = 18;

        public BtnKind Kind { get { return kind; } set { kind = value; Invalidate(); } }
        public string IconName { get { return icon; } set { icon = value; Invalidate(); } }
        public string Badge { get { return badge; } set { badge = value; Invalidate(); } }
        public bool Checked { get { return isChecked; } set { if (isChecked != value) { isChecked = value; Invalidate(); } } }
        public bool Vertical { get { return vertical; } set { vertical = value; Invalidate(); } }
        public bool RoundShape { get { return round; } set { round = value; Invalidate(); } }
        public Color SwatchColor { get { return swatch; } set { swatch = value; Invalidate(); } }
        public int IconSize { get { return iconSize; } set { iconSize = value; Invalidate(); } }
        public float Radius = 8;
        public bool TextOnly;

        public FlatButton()
        {
            SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = Theme.Font(13 * dpi);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (string.IsNullOrEmpty(AccessibleName)) AccessibleName = Text; Invalidate(); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
            base.OnKeyDown(e);
        }

        public int PreferredWidth
        {
            get
            {
                int w = Px(24);
                if (!string.IsNullOrEmpty(icon)) w += Px(iconSize) + (string.IsNullOrEmpty(Text) ? 0 : Px(8));
                if (!string.IsNullOrEmpty(Text)) w += Measure(Text, Font).Width;
                if (!string.IsNullOrEmpty(badge)) w += Px(10) + TextRenderer.MeasureText(badge, Theme.Font(12 * dpi, FontStyle.Bold)).Width + Px(10);
                return w;
            }
        }

        /// <summary>Tamanho do texto sem a margem que o TextRenderer junta por omissão
        /// (o texto é desenhado com NoPadding; medir com margem descentrava o conteúdo).</summary>
        static Size Measure(string text, Font font)
        {
            return TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            float rad = round ? Math.Min(Width, Height) / 2f : Px(Radius);
            Color bg = Color.Empty, fg = Theme.Text, border = Color.Empty;
            bool en = Enabled;

            switch (kind)
            {
                case BtnKind.Primary:
                    bg = !en ? Theme.Border : (down || hover) ? Theme.AccentHover : Theme.Accent;
                    fg = en ? Theme.OnAccent : Theme.Muted;
                    break;
                case BtnKind.Secondary:
                    bg = down ? Theme.Pressed : hover ? Theme.Hover : Theme.Surface2;
                    border = isChecked ? Theme.Accent : Theme.BorderStrong;
                    if (isChecked) { bg = Theme.AccentSoft; fg = Theme.AccentSoftText; }
                    break;
                case BtnKind.Ghost:
                case BtnKind.Tool:
                    if (isChecked) { bg = Theme.AccentSoft; fg = Theme.AccentSoftText; }
                    else if (down) bg = Theme.Pressed;
                    else if (hover) bg = Theme.Hover;
                    if (!en) fg = Theme.Dark ? Color.FromArgb(93, 98, 112) : Color.FromArgb(170, 175, 186);
                    break;
                case BtnKind.Danger:
                    fg = Theme.Danger;
                    if (down) bg = Theme.Pressed; else if (hover) bg = Theme.Hover;
                    break;
                case BtnKind.Nav:
                    if (isChecked) bg = Theme.Dark ? Theme.Surface2 : Theme.Surface;
                    else if (down) bg = Theme.Pressed;
                    else if (hover) bg = Theme.Hover;
                    break;
                case BtnKind.Swatch:
                    PaintSwatch(g, r, rad);
                    PaintFocus(g, r, rad);
                    return;
            }

            if (!bg.IsEmpty) Theme.FillRound(g, bg, r, rad);
            if (!border.IsEmpty) Theme.StrokeRound(g, border, 1f, r, rad);
            if (kind == BtnKind.Nav && isChecked)
                Theme.FillRound(g, Theme.Accent, new RectangleF(0, Px(8), Px(3), Height - Px(16)), Px(1.5f));

            int isz = Px(iconSize);
            Bitmap img = string.IsNullOrEmpty(icon) ? null : ToolIcons.Get(icon, isz, fg);
            Font font = (kind == BtnKind.Nav && isChecked) || kind == BtnKind.Primary ? Theme.Font(Font.Size, FontStyle.Bold) : Font;
            if (kind == BtnKind.Primary) font = Theme.Font(Font.Size, FontStyle.Bold);
            string text = Text;

            if (vertical)
            {
                int th = string.IsNullOrEmpty(text) ? 0 : Measure(text, font).Height;
                int total = (img != null ? isz : 0) + (th > 0 && img != null ? Px(4) : 0) + th;
                int y = (Height - total) / 2;
                if (img != null) { g.DrawImage(img, (Width - isz) / 2, y, isz, isz); y += isz + Px(4); }
                if (th > 0) TextRenderer.DrawText(g, text, font, new Rectangle(0, y, Width, th), fg, TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
            }
            else
            {
                bool left = kind == BtnKind.Nav || !string.IsNullOrEmpty(badge) && kind != BtnKind.Primary;
                int tw = string.IsNullOrEmpty(text) ? 0 : Measure(text, font).Width;
                Font bf = Theme.Font(12 * dpi, kind == BtnKind.Primary ? FontStyle.Bold : FontStyle.Regular);
                int bw = string.IsNullOrEmpty(badge) ? 0 : TextRenderer.MeasureText(badge, bf).Width + (kind == BtnKind.Primary ? Px(12) : 0);
                int content = (img != null ? isz : 0) + (img != null && tw > 0 ? Px(8) : 0) + tw + (kind == BtnKind.Primary && bw > 0 ? Px(10) + bw : 0);
                int x = left ? Px(kind == BtnKind.Nav ? 14 : 12) : (Width - content) / 2;
                if (img != null) { g.DrawImage(img, x, (Height - isz) / 2, isz, isz); x += isz + (tw > 0 ? Px(8) : 0); }
                if (tw > 0)
                {
                    TextRenderer.DrawText(g, text, font, new Rectangle(x, 0, Math.Max(0, Width - x - (left ? bw + Px(12) : 0)), Height), fg,
                        TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    x += tw;
                }
                if (bw > 0)
                {
                    if (kind == BtnKind.Primary)
                    {
                        var kr = new RectangleF(x + Px(10), (Height - Px(22)) / 2f, bw, Px(22));
                        Theme.FillRound(g, Color.FromArgb(50, 255, 255, 255), kr, Px(5));
                        TextRenderer.DrawText(g, badge, bf, Rectangle.Round(kr), fg, TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                    else
                        TextRenderer.DrawText(g, badge, bf, new Rectangle(0, 0, Width - Px(12), Height), Theme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                }
            }
            PaintFocus(g, r, rad);
        }

        void PaintSwatch(Graphics g, RectangleF r, float rad)
        {
            if (isChecked)
            {
                Theme.StrokeRound(g, Theme.Text, Px(2), new RectangleF(Px(1), Px(1), Width - Px(2) - 1, Height - Px(2) - 1), round ? (Width - Px(2)) / 2f : rad);
                var inner = new RectangleF(Px(4), Px(4), Width - Px(8) - 1, Height - Px(8) - 1);
                Theme.FillRound(g, swatch, inner, round ? inner.Width / 2 : Math.Max(2, rad - Px(3)));
            }
            else
            {
                var inner = new RectangleF(Px(3), Px(3), Width - Px(6) - 1, Height - Px(6) - 1);
                if (hover) inner.Inflate(Px(1), Px(1));
                Theme.FillRound(g, swatch, inner, round ? inner.Width / 2 : Math.Max(2, rad - Px(2)));
                Theme.StrokeRound(g, Color.FromArgb(60, 128, 128, 128), 1, inner, round ? inner.Width / 2 : Math.Max(2, rad - Px(2)));
            }
        }
    }

    /// <summary>Interruptor (sim/não) com texto opcional à esquerda.</summary>
    class ToggleRow : ThemedControl
    {
        bool isChecked;
        public string Subtitle;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return isChecked; }
            set { if (isChecked != value) { isChecked = value; Invalidate(); var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty); } }
        }

        public ToggleRow()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = Theme.Font(13.5f * dpi);
            AccessibleRole = AccessibleRole.CheckButton;
        }

        protected override void OnClick(EventArgs e) { Focus(); Checked = !Checked; base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { Checked = !Checked; e.Handled = true; } base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int tw = Px(38), th = Px(20);
            var track = new RectangleF(Width - tw - Px(2), (Height - th) / 2f, tw, th);
            if (isChecked) Theme.FillRound(g, hover ? Theme.AccentHover : Theme.Accent, track, th / 2f);
            else
            {
                Theme.FillRound(g, Theme.Surface2, track, th / 2f);
                Theme.StrokeRound(g, Theme.Muted, 1.2f, track, th / 2f);
            }
            float k = Px(14);
            float kx = isChecked ? track.Right - k - Px(3) : track.X + Px(3);
            using (var b = new SolidBrush(isChecked ? Color.White : Theme.Muted))
                g.FillEllipse(b, kx, track.Y + (th - k) / 2f, k, k);

            var textRect = new Rectangle(0, 0, (int)track.X - Px(12), Height);
            if (string.IsNullOrEmpty(Subtitle))
                TextRenderer.DrawText(g, Text, Font, textRect, Enabled ? Theme.Text : Theme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
            else
            {
                int h1 = TextRenderer.MeasureText(Text, Font).Height;
                Font sf = Theme.Font(12.5f * dpi);
                int h2 = TextRenderer.MeasureText(Subtitle, sf).Height;
                int y = (Height - h1 - h2 - Px(2)) / 2;
                TextRenderer.DrawText(g, Text, Font, new Rectangle(0, y, textRect.Width, h1), Theme.Text, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                TextRenderer.DrawText(g, Subtitle, sf, new Rectangle(0, y + h1 + Px(2), textRect.Width, h2), Theme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }
            if (Focused && ShowFocusCues) Theme.StrokeRound(g, Theme.Accent, 2, new RectangleF(track.X - 2, track.Y - 2, track.Width + 4, track.Height + 4), th / 2f + 2);
        }
    }

    /// <summary>Linha de opção (rádio) com título e subtítulo; as irmãs do mesmo grupo desligam-se.</summary>
    class OptionRow : ThemedControl
    {
        bool isChecked;
        public string Subtitle;
        public string Group = "";
        public object Value;
        public event EventHandler CheckedChanged;
        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked == value) return;
                isChecked = value;
                if (value && Parent != null)
                    foreach (Control c in Parent.Controls)
                    {
                        var o = c as OptionRow;
                        if (o != null && o != this && o.Group == Group) o.Checked = false;
                    }
                Invalidate();
                var h = CheckedChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }

        public OptionRow()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            Font = Theme.Font(13.5f * dpi, FontStyle.Bold);
            AccessibleRole = AccessibleRole.RadioButton;
        }

        protected override void OnClick(EventArgs e) { Focus(); Checked = true; base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { Checked = true; e.Handled = true; } base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
            if (isChecked) { Theme.FillRound(g, Theme.Surface, r, Px(8)); Theme.StrokeRound(g, Theme.Accent, 1.5f, r, Px(8)); }
            else if (hover) Theme.FillRound(g, Theme.Hover, r, Px(8));
            float d = Px(16);
            var c = new RectangleF(Px(12), Px(14), d, d);
            using (var p = new Pen(isChecked ? Theme.Accent : Theme.Muted, 1.6f)) g.DrawEllipse(p, c);
            if (isChecked) { var i = c; i.Inflate(-Px(4), -Px(4)); using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, i); }
            int x = Px(40);
            int h1 = TextRenderer.MeasureText(Text, Font).Height;
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, Px(11), Width - x - Px(10), h1), Theme.Text, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            if (!string.IsNullOrEmpty(Subtitle))
                TextRenderer.DrawText(g, Subtitle, Theme.Font(12.5f * dpi), new Rectangle(x, Px(13) + h1, Width - x - Px(10), Height - Px(13) - h1), Theme.Muted, TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            PaintFocus(g, r, Px(8));
        }
    }

    /// <summary>Cartão de tema com pré-visualização (sistema / claro / escuro).</summary>
    class ThemeCard : OptionRow
    {
        public string PreviewMode = "light";

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(1, 1, Width - 2, Height - 2);
            Theme.FillRound(g, Theme.Surface, r, Px(10));
            Theme.StrokeRound(g, Checked ? Theme.Accent : (hover ? Theme.BorderStrong : Theme.Border), Checked ? 2f : 1.2f, r, Px(10));
            var pv = new RectangleF(Px(10), Px(10), Width - Px(20), Height - Px(48));
            var st = g.Save();
            using (var clip = Theme.Round(pv, Px(6))) g.SetClip(clip);
            if (PreviewMode == "system")
            {
                Mini(g, new RectangleF(pv.X, pv.Y, pv.Width / 2, pv.Height), false);
                Mini(g, new RectangleF(pv.X + pv.Width / 2, pv.Y, pv.Width / 2, pv.Height), true);
            }
            else Mini(g, pv, PreviewMode == "dark");
            g.Restore(st);
            Theme.StrokeRound(g, Theme.Border, 1, pv, Px(6));

            float d = Px(16);
            var c = new RectangleF(Px(12), Height - Px(28), d, d);
            using (var p = new Pen(Checked ? Theme.Accent : Theme.Muted, 1.6f)) g.DrawEllipse(p, c);
            if (Checked) { var i = c; i.Inflate(-Px(4), -Px(4)); using (var b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, i); }
            TextRenderer.DrawText(g, Text, Theme.Font(13.5f * dpi, FontStyle.Bold), new Rectangle(Px(36), Height - Px(34), Width - Px(40), Px(28)), Theme.Text, TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            PaintFocus(g, r, Px(10));
        }

        void Mini(Graphics g, RectangleF r, bool dark)
        {
            Color bg = dark ? Color.FromArgb(32, 34, 41) : Color.FromArgb(246, 247, 249);
            Color line = dark ? Color.FromArgb(74, 78, 90) : Color.FromArgb(201, 207, 218);
            Color acc = Theme.AccentColor(Theme.AccentName, dark);
            Color card = dark ? Color.FromArgb(44, 47, 56) : Color.White;
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, r);
            float p = Px(8);
            using (var b = new SolidBrush(line)) g.FillRectangle(b, r.X + p, r.Y + p, r.Width * 0.5f, Px(6));
            Theme.FillRound(g, acc, new RectangleF(r.X + p, r.Y + p + Px(12), r.Width * 0.7f, Px(14)), Px(3));
            Theme.FillRound(g, card, new RectangleF(r.X + p, r.Y + p + Px(32), r.Width - 2 * p, r.Height - 2 * p - Px(32)), Px(3));
        }
    }

    /// <summary>Barra deslizante simples.</summary>
    class Slider : ThemedControl
    {
        int min = 1, max = 16, val = 4;
        public event EventHandler ValueChanged;
        public int Minimum { get { return min; } set { min = value; Invalidate(); } }
        public int Maximum { get { return max; } set { max = value; Invalidate(); } }
        public int Value
        {
            get { return val; }
            set
            {
                int v = Math.Max(min, Math.Min(max, value));
                if (v == val) return;
                val = v; Invalidate();
                var h = ValueChanged; if (h != null) h(this, EventArgs.Empty);
            }
        }
        /// <summary>Muda o valor sem disparar ValueChanged.</summary>
        public void SetSilently(int v) { val = Math.Max(min, Math.Min(max, v)); Invalidate(); }

        public Slider()
        {
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.Slider;
        }

        float Pad { get { return Px(9); } }

        void SetFromX(int x)
        {
            float t = (x - Pad) / (Width - 2 * Pad);
            Value = (int)Math.Round(min + Math.Max(0, Math.Min(1, t)) * (max - min));
        }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); Focus(); if (e.Button == MouseButtons.Left) SetFromX(e.X); }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (down) SetFromX(e.X); }
        protected override bool IsInputKey(Keys k) { return k == Keys.Left || k == Keys.Right || base.IsInputKey(k); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left) { Value = val - 1; e.Handled = true; }
            else if (e.KeyCode == Keys.Right) { Value = val + 1; e.Handled = true; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(ParentBack);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cy = Height / 2f, th = Px(4);
            float t = max == min ? 0 : (val - min) / (float)(max - min);
            float x = Pad + t * (Width - 2 * Pad);
            Theme.FillRound(g, Theme.BorderStrong, new RectangleF(Pad, cy - th / 2, Width - 2 * Pad, th), th / 2);
            Theme.FillRound(g, Theme.Accent, new RectangleF(Pad, cy - th / 2, x - Pad, th), th / 2);
            float k = Px(16);
            using (var b = new SolidBrush(Color.White)) g.FillEllipse(b, x - k / 2, cy - k / 2, k, k);
            using (var p = new Pen(Theme.Accent, Px(2))) g.DrawEllipse(p, x - k / 2, cy - k / 2, k, k);
            if (Focused && ShowFocusCues) using (var p = new Pen(Color.FromArgb(90, Theme.Accent), Px(3))) g.DrawEllipse(p, x - k / 2 - Px(3), cy - k / 2 - Px(3), k + Px(6), k + Px(6));
        }
    }

    /// <summary>Painel com fundo do tema e linhas de separação opcionais.</summary>
    class ThemedPanel : Panel
    {
        public bool BorderTop, BorderBottom, BorderLeft, BorderRight;
        public Func<Color> Back = () => Theme.Surface;

        public ThemedPanel()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Theme.Changed += OnTheme;
            BackColor = Back();
        }

        void OnTheme() { if (!IsDisposed) { BackColor = Back(); Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var p = new Pen(Theme.Border))
            {
                if (BorderTop) e.Graphics.DrawLine(p, 0, 0, Width, 0);
                if (BorderBottom) e.Graphics.DrawLine(p, 0, Height - 1, Width, Height - 1);
                if (BorderLeft) e.Graphics.DrawLine(p, 0, 0, 0, Height);
                if (BorderRight) e.Graphics.DrawLine(p, Width - 1, 0, Width - 1, Height);
            }
        }

        protected override void Dispose(bool disposing) { Theme.Changed -= OnTheme; base.Dispose(disposing); }
    }

    /// <summary>Etiqueta que segue as cores do tema.</summary>
    class ThemedLabel : Label
    {
        public bool MutedText;
        public ThemedLabel()
        {
            AutoSize = true;
            BackColor = Color.Transparent;
            UseMnemonic = false;
            Theme.Changed += OnTheme;
            OnTheme();
        }
        void OnTheme() { if (!IsDisposed) ForeColor = MutedText ? Theme.Muted : Theme.Text; }
        public ThemedLabel Muted() { MutedText = true; OnTheme(); return this; }
        protected override void Dispose(bool disposing) { Theme.Changed -= OnTheme; base.Dispose(disposing); }
    }

    /// <summary>Pequena caixa de diálogo para pedir um texto (ex.: mudar o nome).</summary>
    static class Prompt
    {
        public static string Show(IWin32Window owner, string title, string label, string value)
        {
            float dpi;
            using (var f = new Form())
            {
                dpi = Dpi.Scale;
                Func<float, int> Sc = v => (int)Math.Round(v * dpi);
                f.Text = title;
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MaximizeBox = false; f.MinimizeBox = false; f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.ClientSize = new Size(Sc(420), Sc(150));
                f.BackColor = Theme.Panel;
                f.Font = Theme.Font(13.5f * dpi);
                f.Icon = AppIcon.FormIcon;
                var lbl = new ThemedLabel { Text = label, Location = new Point(Sc(18), Sc(16)) };
                var tb = new TextBox { Text = value, Location = new Point(Sc(18), Sc(42)), Width = Sc(384), BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface2, ForeColor = Theme.Text };
                var ok = new FlatButton { Kind = BtnKind.Primary, Text = "OK", Size = new Size(Sc(96), Sc(36)), Location = new Point(Sc(306), Sc(96)) };
                var cancel = new FlatButton { Kind = BtnKind.Secondary, Text = S.T("Cancelar", "Cancel"), Size = new Size(Sc(96), Sc(36)), Location = new Point(Sc(200), Sc(96)) };
                ok.Click += delegate { f.DialogResult = DialogResult.OK; };
                cancel.Click += delegate { f.DialogResult = DialogResult.Cancel; };
                f.Controls.AddRange(new Control[] { lbl, tb, ok, cancel });
                f.KeyPreview = true;
                f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { f.DialogResult = DialogResult.OK; e.SuppressKeyPress = true; } else if (e.KeyCode == Keys.Escape) f.DialogResult = DialogResult.Cancel; };
                f.Shown += delegate { Theme.StyleWindow(f); tb.Focus(); tb.SelectAll(); };
                return f.ShowDialog(owner) == DialogResult.OK ? tb.Text : null;
            }
        }
    }
}
