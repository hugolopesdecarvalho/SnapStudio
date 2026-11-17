using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualBasic.FileIO;

namespace SnapStudio
{
    class Canvas : Control
    {
        public Canvas()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        }
    }

    class Viewport : Panel
    {
        public event Action<int, Point> CtrlWheel;
        public Viewport() { DoubleBuffered = true; AutoScroll = true; }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((ModifierKeys & Keys.Control) != 0)
            {
                var h = CtrlWheel;
                if (h != null) h(e.Delta, PointToClient(Cursor.Position));
                return;
            }
            base.OnMouseWheel(e);
        }
        protected override Point ScrollToControl(Control activeControl) { return DisplayRectangle.Location; }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); Theme.ScrollbarTheme(this); }
    }

    /// <summary>
    /// Editor de capturas. Dois modos: pop-up rápido junto ao relógio (compact) e editor completo.
    /// </summary>
    class EditorForm : Form
    {
        class Snap { public Bitmap Base; public string Json; }
        enum Drag { None, Create, Move, Handle1, Handle2, Crop }

        class LayerItem
        {
            public Ann A;
            public override string ToString() { return EditorForm.AnnName(A); }
        }

        readonly bool compact;
        string filePath;
        Bitmap baseImg;
        List<Ann> anns = new List<Ann>();
        readonly Stack<Snap> undo = new Stack<Snap>(), redo = new Stack<Snap>();

        float zoom = 1f, dpi = 1f;
        Tool tool = Tool.Arrow;
        Color curColor = Color.FromArgb(229, 32, 41);
        float curWidth = 4, curFont = 26;
        bool curRounded;                 // cantos arredondados nas novas caixas/setas/marcador/esbater
        float curRadius = 12;
        bool curFillOn;                  // preencher caixas e elipses
        float curFill = 0.25f;           // opacidade do preenchimento
        int curOutline;                  // contorno do texto: 0 auto, 1 branco, 2 preto, 3 nenhum

        /// <summary>Grupo de controlos do painel de propriedades (posições relativas ao topo do grupo).</summary>
        class PropGroup
        {
            public string Key;
            public int Height;
            public readonly List<Control> Items = new List<Control>();
            public readonly List<int> Dy = new List<int>();
            public void Add(Control c, int dy) { Items.Add(c); Dy.Add(dy); }
        }
        readonly List<PropGroup> propGroups = new List<PropGroup>();
        // imagem anterior / seguinte (capturas da mesma pasta, da mais antiga para a mais recente)
        List<string> siblings = new List<string>();
        FlatButton btnPrev, btnNext;
        ThemedLabel lblNav;
        readonly List<FlatButton> outlineButtons = new List<FlatButton>();
        ThemedPanel propsPanel;
        ThemedLabel lblPropsHint, lblLayersTitle, lblFillVal;
        ToggleRow tglFill;
        Slider sldFill;
        int propsDivY;
        static readonly Color HighlightColor = Color.FromArgb(255, 230, 0);
        static readonly Color SelectionColor = Color.FromArgb(99, 102, 241);

        Ann selected, live;
        bool modified, syncing, syncingLayers, quietClose, dialogOpen;
        Drag drag;
        PointF dragStart, lastPt;
        Snap pendingUndo;
        bool moved;
        RectangleF cropRect;

        TextBox textBox;
        TextAnn editingText;
        PointF textPos;
        float textSize;
        Color textColor;

        Bitmap cache;
        bool cacheDirty = true;

        Viewport viewport;
        Canvas canvas;
        readonly Dictionary<Tool, FlatButton> toolButtons = new Dictionary<Tool, FlatButton>();
        readonly List<FlatButton> swatches = new List<FlatButton>();
        FlatButton btnUndo, btnRedo, btnDelete, btnTheme, btnMoreColor;
        Slider sldWidth, sldFont, sldRadius;
        ToggleRow tglRounded;
        ThemedLabel lblFile, lblStatus, lblPath, lblZoom, lblSection, lblWidthVal, lblFontVal, lblRadiusVal;
        ListBox layers;
        readonly List<int> railSeps = new List<int>();

        public string FilePath { get { return filePath; } }
        public bool Compact { get { return compact; } }

        int Px(float v) { return (int)Math.Round(v * dpi); }

        public EditorForm(string path, Bitmap initial) : this(path, initial, false) { }

        public EditorForm(string path, Bitmap initial, bool compactMode)
        {
            compact = compactMode;
            filePath = path;
            dpi = Dpi.Scale;
            LoadLastStyle();
            Icon = AppIcon.FormIcon;
            KeyPreview = true;
            AutoScaleMode = AutoScaleMode.None;
            Font = Theme.Font(13 * dpi);
            LoadImage(path, initial);

            if (compact) BuildCompactUi(); else BuildFullUi();

            Theme.Changed += ApplyTheme;
            ApplyTheme();
            SetTool(Tool.Arrow);
            SetColor(curColor, false);
            UpdateTitle();
            UpdateStatus();
            UpdateUndoButtons();
            RefreshLayers();
            SyncSliders();
            if (!compact) { ReloadSiblings(); UpdateNav(); }
        }

        // ================= imagem anterior / seguinte =================

        void ReloadSiblings()
        {
            siblings = new List<string>();
            try
            {
                string dir = Path.GetDirectoryName(filePath);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
                string[] exts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif" };
                var files = new List<FileInfo>();
                foreach (var f in new DirectoryInfo(dir).GetFiles())
                    if (Array.IndexOf(exts, f.Extension.ToLowerInvariant()) >= 0) files.Add(f);
                files.Sort((a, b) => a.CreationTime.CompareTo(b.CreationTime));
                foreach (var f in files) siblings.Add(f.FullName);
            }
            catch { }
        }

        int SiblingIndex()
        {
            for (int i = 0; i < siblings.Count; i++)
                if (string.Equals(siblings[i], filePath, StringComparison.OrdinalIgnoreCase)) return i;
            return -1;
        }

        void UpdateNav()
        {
            if (btnPrev == null) return;
            int i = SiblingIndex();
            bool show = i >= 0 && siblings.Count > 1;
            btnPrev.Visible = btnNext.Visible = lblNav.Visible = show;
            if (!show) return;
            lblNav.Text = (i + 1) + " / " + siblings.Count;
            btnPrev.Enabled = i > 0;
            btnNext.Enabled = i < siblings.Count - 1;
        }

        /// <summary>Abre a imagem anterior (-1) ou seguinte (+1) nesta janela; as alterações são guardadas antes.</summary>
        void Navigate(int delta)
        {
            if (compact) return;
            CommitText();
            ReloadSiblings();
            int i = SiblingIndex(), t = i + delta;
            if (i < 0 || t < 0 || t >= siblings.Count) { System.Media.SystemSounds.Beep.Play(); return; }
            if (modified && !Save()) return;
            string target = siblings[t];
            try
            {
                anns = new List<Ann>();
                LoadImage(target, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, S.T("Não foi possível abrir a imagem: ", "Could not open the image: ") + ex.Message, "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Error);
                LoadImage(filePath, null);
                return;
            }
            filePath = target;
            undo.Clear(); redo.Clear();
            selected = null; live = null; editingText = null; pendingUndo = null;
            drag = Drag.None; cropRect = RectangleF.Empty;
            modified = false;
            ZoomFit();
            Dirty();
            UpdateTitle();
            UpdateStatus();
            UpdateUndoButtons();
            RefreshLayers();
            SelectionChanged();
            UpdateNav();
        }

        // ================= carregamento =================

        void LoadImage(string path, Bitmap initial)
        {
            string json = Sidecar.Json(path), basep = Sidecar.Base(path);
            try
            {
                if (File.Exists(path) && File.Exists(json) && File.Exists(basep) &&
                    File.GetLastWriteTimeUtc(json) >= File.GetLastWriteTimeUtc(path).AddSeconds(-5))
                {
                    baseImg = ImageUtil.LoadNoLock(basep);
                    anns = Ann.Deserialize(File.ReadAllText(json));
                    return;
                }
            }
            catch { anns = new List<Ann>(); }
            baseImg = initial != null ? ImageUtil.Crop(initial, new Rectangle(0, 0, initial.Width, initial.Height)) : ImageUtil.LoadNoLock(path);
        }

        // ================= interface: comum =================

        FlatButton Btn(Control parent, BtnKind kind, string icon, string text, string tip, EventHandler click)
        {
            var b = new FlatButton { Kind = kind, IconName = icon, Text = text ?? "", Font = Theme.Font(13 * dpi) };
            if (!string.IsNullOrEmpty(tip)) { b.AccessibleName = tip; tips.SetToolTip(b, tip); }
            if (click != null) b.Click += click;
            parent.Controls.Add(b);
            return b;
        }

        readonly ToolTip tips = new ToolTip();

        void BuildCanvas()
        {
            viewport = new Viewport { Dock = DockStyle.Fill };
            viewport.CtrlWheel += (d, p) => ZoomBy(d > 0 ? 1.15f : 1 / 1.15f, p);
            viewport.Resize += delegate { LayoutCanvas(); };
            viewport.Scroll += delegate { if (textBox != null) CommitText(); };

            canvas = new Canvas { BackColor = Color.White };
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseMove += Canvas_MouseMove;
            canvas.MouseUp += Canvas_MouseUp;
            canvas.MouseDoubleClick += Canvas_MouseDoubleClick;
            viewport.Controls.Add(canvas);
        }

        static readonly Color[] FullPalette = {
            Color.FromArgb(229, 32, 41), Color.FromArgb(245, 124, 0), Color.FromArgb(255, 214, 0), Color.FromArgb(46, 160, 67),
            Color.FromArgb(37, 99, 235), Color.FromArgb(124, 58, 237), Color.FromArgb(17, 17, 17), Color.White };
        static readonly Color[] QuickPalette = {
            Color.FromArgb(229, 32, 41), Color.FromArgb(255, 214, 0), Color.FromArgb(37, 99, 235), Color.FromArgb(17, 17, 17) };

        FlatButton SwatchButton(Control parent, Color c, int size, bool round)
        {
            Color cc = c;
            var b = new FlatButton { Kind = BtnKind.Swatch, SwatchColor = c, RoundShape = round, Size = new Size(size, size), Radius = 7, AccessibleName = ColorName(c) };
            tips.SetToolTip(b, ColorName(c));
            b.Click += delegate { SetColor(cc); };
            parent.Controls.Add(b);
            swatches.Add(b);
            return b;
        }

        static string ColorName(Color c)
        {
            int v = c.ToArgb();
            if (v == Color.FromArgb(229, 32, 41).ToArgb()) return S.T("Vermelho", "Red");
            if (v == Color.FromArgb(245, 124, 0).ToArgb()) return S.T("Laranja", "Orange");
            if (v == Color.FromArgb(255, 214, 0).ToArgb()) return S.T("Amarelo", "Yellow");
            if (v == Color.FromArgb(46, 160, 67).ToArgb()) return S.T("Verde", "Green");
            if (v == Color.FromArgb(37, 99, 235).ToArgb()) return S.T("Azul", "Blue");
            if (v == Color.FromArgb(124, 58, 237).ToArgb()) return S.T("Roxo", "Purple");
            if (v == Color.FromArgb(17, 17, 17).ToArgb()) return S.T("Preto", "Black");
            if (v == Color.White.ToArgb()) return S.T("Branco", "White");
            return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
        }

        static string ToolName(Tool t)
        {
            switch (t)
            {
                case Tool.Select: return S.T("Selecionar", "Select");
                case Tool.Arrow: return S.T("Seta", "Arrow");
                case Tool.Line: return S.T("Linha", "Line");
                case Tool.Rect: return S.T("Caixa", "Box");
                case Tool.Ellipse: return S.T("Elipse", "Ellipse");
                case Tool.Highlight: return S.T("Marcador", "Highlighter");
                case Tool.Blur: return S.T("Esbater", "Blur");
                case Tool.Pixelate: return S.T("Pixelizar", "Pixelate");
                case Tool.Text: return S.T("Texto", "Text");
                case Tool.Step: return S.T("Passo numerado", "Step number");
                case Tool.Pen: return S.T("Desenho livre", "Freehand");
                case Tool.Crop: return S.T("Recortar", "Crop");
            }
            return "";
        }

        public static string AnnName(Ann a)
        {
            var sh = a as ShapeAnn;
            if (sh != null)
            {
                switch (sh.Kind)
                {
                    case "arrow": return S.T("Seta", "Arrow");
                    case "line": return S.T("Linha", "Line");
                    case "rect": return S.T("Caixa", "Box");
                    case "ellipse": return S.T("Elipse", "Ellipse");
                    case "highlight": return S.T("Marcador", "Highlight");
                    case "blur": return S.T("Esbater", "Blur");
                    case "pixelate": return S.T("Pixelizar", "Pixelate");
                }
            }
            var t = a as TextAnn;
            if (t != null)
            {
                string s = (t.Text ?? "").Replace("\n", " ");
                if (s.Length > 18) s = s.Substring(0, 17) + "…";
                return S.T("Texto “", "Text “") + s + "”";
            }
            var st = a as StepAnn;
            if (st != null) return S.T("Passo ", "Step ") + st.Number;
            return S.T("Desenho", "Drawing");
        }

        static string ToolIcon(Tool t)
        {
            switch (t)
            {
                case Tool.Select: return "select";
                case Tool.Arrow: return "arrow";
                case Tool.Line: return "line";
                case Tool.Rect: return "rect";
                case Tool.Ellipse: return "ellipse";
                case Tool.Highlight: return "highlight";
                case Tool.Blur: return "blur";
                case Tool.Pixelate: return "pixelate";
                case Tool.Text: return "text";
                case Tool.Step: return "step";
                case Tool.Pen: return "pen";
                default: return "crop";
            }
        }

        static string ToolKey(Tool t)
        {
            switch (t)
            {
                case Tool.Select: return "V";
                case Tool.Arrow: return "A";
                case Tool.Line: return "L";
                case Tool.Rect: return "R";
                case Tool.Ellipse: return "E";
                case Tool.Highlight: return "H";
                case Tool.Blur: return "B";
                case Tool.Pixelate: return "X";
                case Tool.Text: return "T";
                case Tool.Step: return "N";
                case Tool.Pen: return "P";
                default: return "C";
            }
        }

        FlatButton ToolButton(Control parent, Tool t, int size)
        {
            Tool tt = t;
            var b = Btn(parent, BtnKind.Tool, ToolIcon(t), null, ToolName(t) + " (" + ToolKey(t) + ")", delegate { SetTool(tt); });
            b.Size = new Size(size, size);
            b.IconSize = 20;
            toolButtons[t] = b;
            return b;
        }

        // ================= interface: editor completo =================

        void BuildFullUi()
        {
            SuspendLayout();
            StartPosition = FormStartPosition.CenterScreen;

            // --- cabeçalho
            var header = new ThemedPanel { Dock = DockStyle.Top, Height = Px(56), BorderBottom = true };
            header.Paint += (s, e) => { using (var logo = AppIcon.Draw(Px(24))) e.Graphics.DrawImage(logo, Px(16), Px(16)); };
            lblFile = new ThemedLabel { Font = Theme.Font(14 * dpi, FontStyle.Bold), Location = new Point(Px(52), Px(9)) };
            lblStatus = new ThemedLabel { Font = Theme.Font(12 * dpi), Location = new Point(Px(52), Px(30)) }.Muted();
            header.Controls.Add(lblFile); header.Controls.Add(lblStatus);
            btnUndo = Btn(header, BtnKind.Ghost, "undo", null, S.T("Anular (Ctrl+Z)", "Undo (Ctrl+Z)"), delegate { Undo(); });
            btnRedo = Btn(header, BtnKind.Ghost, "redo", null, S.T("Refazer (Ctrl+Y)", "Redo (Ctrl+Y)"), delegate { Redo(); });
            btnUndo.Size = btnRedo.Size = new Size(Px(40), Px(40));
            btnPrev = Btn(header, BtnKind.Ghost, null, "‹", S.T("Imagem anterior (Ctrl+←)", "Previous image (Ctrl+←)"), delegate { Navigate(-1); });
            btnNext = Btn(header, BtnKind.Ghost, null, "›", S.T("Imagem seguinte (Ctrl+→)", "Next image (Ctrl+→)"), delegate { Navigate(1); });
            btnPrev.Font = btnNext.Font = Theme.Font(20 * dpi);
            btnPrev.Size = btnNext.Size = new Size(Px(36), Px(40));
            lblNav = new ThemedLabel { Font = Theme.Font(12.5f * dpi), AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Size = new Size(Px(64), Px(40)) }.Muted();
            header.Controls.Add(lblNav);
            var btnCopy = Btn(header, BtnKind.Primary, "copy", S.T("Copiar e fechar", "Copy and close"), S.T("Guardar as alterações, copiar a imagem e fechar (Ctrl+Enter)", "Save changes, copy the image and close (Ctrl+Enter)"), delegate { CopyAndClose(); });
            var btnSaveAs = Btn(header, BtnKind.Secondary, null, S.T("Guardar como…", "Save as…"), S.T("Guardar como (Ctrl+Shift+S)", "Save as (Ctrl+Shift+S)"), delegate { SaveAs(); });
            var btnDrag = Btn(header, BtnKind.Secondary, "drag", S.T("Arrastar", "Drag"), S.T("Arraste para o Outlook, Teams ou Explorador", "Drag into Outlook, Teams or Explorer"), null);
            btnDrag.MouseDown += DragButton_MouseDown;
            btnDrag.Cursor = Cursors.SizeAll;
            btnTheme = Btn(header, BtnKind.Ghost, "moon", null, S.T("Mudar tema", "Toggle theme"), delegate { if (TrayApp.Instance != null) TrayApp.Instance.ToggleTheme(); });
            btnTheme.Size = new Size(Px(40), Px(40));
            btnTheme.Visible = TrayApp.Instance != null;
            header.Layout += delegate
            {
                int x0 = Px(52) + Math.Max(lblFile.Width, lblStatus.Width) + Px(20);
                btnUndo.Location = new Point(x0, Px(8));
                btnRedo.Location = new Point(x0 + Px(42), Px(8));
                btnPrev.Location = new Point(x0 + Px(98), Px(8));
                lblNav.Location = new Point(btnPrev.Right, Px(8));
                btnNext.Location = new Point(lblNav.Right, Px(8));
                int x = header.Width - Px(12);
                foreach (var b in new[] { btnCopy, btnSaveAs, btnDrag })
                {
                    b.Size = new Size(b.PreferredWidth, Px(40));
                    x -= b.Width;
                    b.Location = new Point(x, Px(8));
                    x -= Px(8);
                }
                if (btnTheme.Visible) btnTheme.Location = new Point(x - Px(40), Px(8));
            };

            // --- barra de ferramentas vertical
            var rail = new ThemedPanel { Dock = DockStyle.Left, Width = Px(64), BorderRight = true };
            int y = Px(10);
            Tool[][] groups = {
                new[] { Tool.Select, Tool.Arrow, Tool.Rect, Tool.Text },
                new[] { Tool.Blur, Tool.Pixelate, Tool.Highlight, Tool.Step },
                new[] { Tool.Ellipse, Tool.Line, Tool.Pen },
                new[] { Tool.Crop } };
            for (int gi = 0; gi < groups.Length; gi++)
            {
                if (gi > 0) { railSeps.Add(y + Px(4)); y += Px(10); }
                foreach (var t in groups[gi])
                {
                    var b = ToolButton(rail, t, Px(44));
                    b.Location = new Point(Px(10), y);
                    y += Px(48);
                }
            }
            btnDelete = Btn(rail, BtnKind.Ghost, "delete", null, S.T("Apagar anotação (Delete)", "Delete annotation (Delete)"), delegate { DeleteSelected(); });
            btnDelete.Size = new Size(Px(44), Px(44));
            rail.Resize += delegate { btnDelete.Location = new Point(Px(10), Math.Max(y + Px(10), rail.Height - Px(54))); };
            rail.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) foreach (int sy in railSeps) e.Graphics.DrawLine(p, Px(18), sy, Px(46), sy); };

            // --- propriedades
            var props = new ThemedPanel { Dock = DockStyle.Right, Width = Px(272), BorderLeft = true };
            int px = Px(16), pw = Px(272) - 2 * Px(16);
            lblSection = new ThemedLabel { Font = Theme.Font(12 * dpi, FontStyle.Bold), Location = new Point(px, Px(18)) }.Muted();
            props.Controls.Add(lblSection);
            // As propriedades estão em grupos; LayoutProps mostra só os que têm efeito no elemento
            // selecionado (ou no que a ferramenta ativa cria) e empilha-os. Posições relativas ao grupo.
            propsPanel = props;
            lblPropsHint = new ThemedLabel { Font = Theme.Font(12.5f * dpi), AutoSize = false, Size = new Size(pw, Px(52)), Location = new Point(px, Px(48)) }.Muted();
            props.Controls.Add(lblPropsHint);

            // Cor
            var gColor = NewGroup("color");
            gColor.Add(PropLabel(props, S.T("Cor", "Color"), px), 0);
            int cell = (pw - 5 * Px(8)) / 6;
            for (int i = 0; i < FullPalette.Length; i++)
            {
                var b = SwatchButton(props, FullPalette[i], cell, false);
                b.Left = px + (i % 6) * (cell + Px(8));
                gColor.Add(b, Px(24) + (i / 6) * (cell + Px(8)));
            }
            btnMoreColor = Btn(props, BtnKind.Secondary, "plus", null, S.T("Outra cor…", "More colors…"), delegate { PickColor(); });
            btnMoreColor.Size = new Size(cell, cell);
            btnMoreColor.Left = px + 2 * (cell + Px(8));
            gColor.Add(btnMoreColor, Px(24) + cell + Px(8));
            gColor.Height = Px(24) + 2 * cell + Px(8) + Px(20);

            // Espessura
            var gWidth = NewGroup("width");
            gWidth.Add(PropLabel(props, S.T("Espessura", "Width"), px), 0);
            lblWidthVal = ValueLabel(props, px, pw); gWidth.Add(lblWidthVal, 0);
            sldWidth = new Slider { Minimum = 1, Maximum = 16, Size = new Size(pw, Px(28)), Left = px, AccessibleName = S.T("Espessura", "Width") };
            sldWidth.ValueChanged += delegate { if (!syncing) SetWidth(sldWidth.Value); };
            props.Controls.Add(sldWidth); gWidth.Add(sldWidth, Px(22));
            gWidth.Height = Px(64);

            // Tamanho do texto
            var gFont = NewGroup("font");
            gFont.Add(PropLabel(props, S.T("Tamanho do texto", "Text size"), px), 0);
            lblFontVal = ValueLabel(props, px, pw); gFont.Add(lblFontVal, 0);
            sldFont = new Slider { Minimum = 12, Maximum = 80, Size = new Size(pw, Px(28)), Left = px, AccessibleName = S.T("Tamanho do texto", "Text size") };
            sldFont.ValueChanged += delegate { if (!syncing) SetFontSize(sldFont.Value); };
            props.Controls.Add(sldFont); gFont.Add(sldFont, Px(22));
            gFont.Height = Px(64);

            // Contorno do texto: automático / branco / preto / nenhum
            var gOutline = NewGroup("outline");
            gOutline.Add(PropLabel(props, S.T("Contorno", "Outline"), px), 0);
            string[] outlineNames = { S.T("Auto", "Auto"), S.T("Branco", "White"), S.T("Preto", "Black"), S.T("Nenhum", "None") };
            int ow = (pw - 3 * Px(4)) / 4;
            for (int i = 0; i < 4; i++)
            {
                int value = i;
                var ob = new FlatButton { Kind = BtnKind.Secondary, Text = outlineNames[i], Font = Theme.Font(12 * dpi), Radius = 6, Size = new Size(ow, Px(30)), Left = px + i * (ow + Px(4)), Tag = i };
                ob.Click += delegate { SetTextOutline(value); };
                props.Controls.Add(ob); outlineButtons.Add(ob); gOutline.Add(ob, Px(24));
            }
            gOutline.Height = Px(66);

            // Preencher (caixas e elipses)
            var gFill = NewGroup("fill");
            tglFill = new ToggleRow { Text = S.T("Preencher", "Fill"), Font = Theme.Font(13 * dpi), Checked = curFillOn, Size = new Size(pw, Px(34)), Left = px };
            tglFill.CheckedChanged += delegate { if (!syncing) SetFill(tglFill.Checked); };
            props.Controls.Add(tglFill); gFill.Add(tglFill, 0);
            gFill.Height = Px(40);
            var gFillOpacity = NewGroup("fillOpacity");
            gFillOpacity.Add(PropLabel(props, S.T("Opacidade", "Opacity"), px), 0);
            lblFillVal = ValueLabel(props, px, pw); gFillOpacity.Add(lblFillVal, 0);
            sldFill = new Slider { Minimum = 10, Maximum = 100, Size = new Size(pw, Px(28)), Left = px, AccessibleName = S.T("Opacidade do preenchimento", "Fill opacity") };
            sldFill.ValueChanged += delegate { if (!syncing) SetFillOpacity(sldFill.Value / 100f); };
            props.Controls.Add(sldFill); gFillOpacity.Add(sldFill, Px(22));
            gFillOpacity.Height = Px(64);

            // Cantos arredondados (caixas, setas, marcador, esbater, pixelizar)
            var gRounded = NewGroup("rounded");
            tglRounded = new ToggleRow { Text = S.T("Cantos arredondados", "Rounded corners"), Font = Theme.Font(13 * dpi), Checked = curRounded, Size = new Size(pw, Px(34)), Left = px };
            tglRounded.CheckedChanged += delegate { if (!syncing) SetRounded(tglRounded.Checked); };
            props.Controls.Add(tglRounded); gRounded.Add(tglRounded, 0);
            gRounded.Height = Px(40);
            var gRadius = NewGroup("radius");
            gRadius.Add(PropLabel(props, S.T("Raio", "Radius"), px), 0);
            lblRadiusVal = ValueLabel(props, px, pw); gRadius.Add(lblRadiusVal, 0);
            sldRadius = new Slider { Minimum = 2, Maximum = 40, Size = new Size(pw, Px(28)), Left = px, AccessibleName = S.T("Raio dos cantos", "Corner radius") };
            sldRadius.ValueChanged += delegate { if (!syncing) SetRadius(sldRadius.Value); };
            props.Controls.Add(sldRadius); gRadius.Add(sldRadius, Px(22));
            gRadius.Height = Px(64);

            props.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, px, propsDivY, px + pw, propsDivY); };
            lblLayersTitle = new ThemedLabel { Text = S.T("ANOTAÇÕES", "ANNOTATIONS"), Font = Theme.Font(12 * dpi, FontStyle.Bold), Location = new Point(px, Px(300)) }.Muted();
            props.Controls.Add(lblLayersTitle);
            y = Px(326);
            layers = new ListBox
            {
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = Px(34),
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                Font = Theme.Font(13 * dpi),
                Location = new Point(px - Px(6), y),
                Size = new Size(pw + Px(12), Math.Max(Px(80), Px(600) - y)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
            };
            layers.DrawItem += Layers_DrawItem;
            layers.SelectedIndexChanged += Layers_SelectedIndexChanged;
            layers.HandleCreated += delegate { Theme.ScrollbarTheme(layers); };
            props.Controls.Add(layers);
            props.Resize += delegate { layers.Height = Math.Max(Px(60), props.Height - layers.Top - Px(12)); };

            // --- área principal e rodapé
            var main = new Panel { Dock = DockStyle.Fill };
            BuildCanvas();
            var footer = new ThemedPanel { Dock = DockStyle.Bottom, Height = Px(40), BorderTop = true };
            lblPath = new ThemedLabel { Font = Theme.Font(12.5f * dpi), AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft, Location = new Point(Px(16), Px(10)), Height = Px(20) }.Muted();
            lblZoom = new ThemedLabel { Font = Theme.Font(12.5f * dpi), AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, Size = new Size(Px(48), Px(40)) };
            var bOut = Btn(footer, BtnKind.Ghost, null, "−", S.T("Reduzir (Ctrl+−)", "Zoom out (Ctrl+−)"), delegate { ZoomBy(1 / 1.25f); });
            var bIn = Btn(footer, BtnKind.Ghost, null, "+", S.T("Ampliar (Ctrl++)", "Zoom in (Ctrl++)"), delegate { ZoomBy(1.25f); });
            var bFit = Btn(footer, BtnKind.Secondary, null, S.T("Ajustar", "Fit"), S.T("Ajustar à janela (Ctrl+9)", "Fit to window (Ctrl+9)"), delegate { ZoomFit(); });
            var b100 = Btn(footer, BtnKind.Ghost, null, "1:1", S.T("Tamanho real (Ctrl+0)", "Actual size (Ctrl+0)"), delegate { SetZoom(1f); });
            bOut.Size = bIn.Size = new Size(Px(32), Px(30));
            bFit.Size = new Size(bFit.PreferredWidth, Px(28));
            b100.Size = new Size(b100.PreferredWidth, Px(30));
            footer.Controls.Add(lblPath); footer.Controls.Add(lblZoom);
            footer.Layout += delegate
            {
                int x = footer.Width - Px(12) - bFit.Width;
                bFit.Location = new Point(x, Px(6));
                x -= Px(6) + b100.Width; b100.Location = new Point(x, Px(5));
                x -= Px(4) + bIn.Width; bIn.Location = new Point(x, Px(5));
                x -= lblZoom.Width; lblZoom.Location = new Point(x, 0);
                x -= bOut.Width; bOut.Location = new Point(x, Px(5));
                lblPath.Width = Math.Max(Px(40), x - Px(28));
            };
            main.Controls.Add(viewport);
            main.Controls.Add(footer);

            Controls.Add(main);
            Controls.Add(props);
            Controls.Add(rail);
            Controls.Add(header);
            ResumeLayout(true);

            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            int w = Math.Min((int)(wa.Width * 0.92), Math.Max(Px(1100), baseImg.Width + Px(380)));
            int h = Math.Min((int)(wa.Height * 0.92), Math.Max(Px(720), baseImg.Height + Px(140)));
            Size = new Size(w, h);
            MinimumSize = new Size(Px(860), Px(560));
        }

        // ================= interface: editor rápido (pop-up) =================

        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        void BuildCompactUi()
        {
            SuspendLayout();
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            Padding = new Padding(1);
            int W = Px(700);

            // --- cabeçalho (arrastável)
            var header = new ThemedPanel { Dock = DockStyle.Top, Height = Px(58), BorderBottom = true, Back = () => Theme.Panel };
            header.Paint += (s, e) => { using (var logo = AppIcon.Draw(Px(22))) e.Graphics.DrawImage(logo, Px(16), Px(18)); };
            DateTime t = File.Exists(filePath) ? File.GetCreationTime(filePath) : DateTime.Now;
            lblFile = new ThemedLabel { Font = Theme.Font(14 * dpi, FontStyle.Bold), Location = new Point(Px(48), Px(10)), Text = S.T("Captura de ", "Capture at ") + t.ToString("HH:mm") };
            lblStatus = new ThemedLabel { Font = Theme.Font(12 * dpi), Location = new Point(Px(48), Px(31)) }.Muted();
            header.Controls.Add(lblFile); header.Controls.Add(lblStatus);
            var btnClose = Btn(header, BtnKind.Ghost, "close", null, S.T("Fechar (Esc) — guarda as alterações", "Close (Esc) — keeps your changes"), delegate { SaveAndClose(); });
            btnClose.Size = new Size(Px(36), Px(36));
            header.Resize += delegate { btnClose.Location = new Point(header.Width - Px(48), Px(11)); };
            MouseEventHandler dragWin = (s, e) => { if (e.Button == MouseButtons.Left) { ReleaseCapture(); SendMessage(Handle, 0xA1, (IntPtr)2, IntPtr.Zero); } };
            header.MouseDown += dragWin; lblFile.MouseDown += dragWin; lblStatus.MouseDown += dragWin;

            // --- ferramentas
            var bar = new ThemedPanel { Dock = DockStyle.Top, Height = Px(56), BorderBottom = true, Back = () => Theme.Panel };
            int x = Px(12);
            foreach (var tl in new[] { Tool.Select, Tool.Arrow, Tool.Rect, Tool.Text, Tool.Blur, Tool.Step, Tool.Crop })
            {
                var b = ToolButton(bar, tl, Px(40));
                b.Location = new Point(x, Px(8));
                x += Px(44);
            }
            int sepX = x + Px(4);
            bar.Paint += (s, e) => { using (var p = new Pen(Theme.Border)) e.Graphics.DrawLine(p, sepX, Px(16), sepX, Px(40)); };
            x += Px(14);
            foreach (var c in QuickPalette)
            {
                var b = SwatchButton(bar, c, Px(30), true);
                b.Location = new Point(x, Px(13));
                x += Px(34);
            }
            btnUndo = Btn(bar, BtnKind.Ghost, "undo", null, S.T("Anular (Ctrl+Z)", "Undo (Ctrl+Z)"), delegate { Undo(); });
            btnUndo.Size = new Size(Px(40), Px(40));
            bar.Resize += delegate { btnUndo.Location = new Point(bar.Width - Px(52), Px(8)); };

            // --- rodapé
            var footer = new ThemedPanel { Dock = DockStyle.Bottom, Height = Px(64), BorderTop = true, Back = () => Theme.Panel };
            var bFull = Btn(footer, BtnKind.Secondary, "expand", S.T("Editor completo", "Full editor"), S.T("Abrir no editor completo", "Open in the full editor"), delegate { OpenFullEditor(); });
            var bDiscard = Btn(footer, BtnKind.Danger, null, S.T("Descartar", "Discard"), S.T("Enviar esta captura para a Reciclagem", "Send this capture to the Recycle Bin"), delegate { Discard(); });
            var bCopy = Btn(footer, BtnKind.Primary, "copy", S.T("Copiar e fechar", "Copy and close"), S.T("Copiar a imagem anotada e fechar", "Copy the annotated image and close"), delegate { CopyAndClose(); });
            var bSaveAs = Btn(footer, BtnKind.Secondary, null, S.T("Guardar como…", "Save as…"), null, delegate { SaveAs(); });
            foreach (var b in new[] { bFull, bDiscard, bCopy, bSaveAs }) b.Size = new Size(b.PreferredWidth, Px(40));
            bFull.Location = new Point(Px(14), Px(12));
            bDiscard.Location = new Point(bFull.Right + Px(6), Px(12));
            bCopy.Location = new Point(W - 2 - Px(14) - bCopy.Width, Px(12));
            bSaveAs.Location = new Point(bCopy.Left - Px(8) - bSaveAs.Width, Px(12));

            BuildCanvas();
            Controls.Add(viewport);
            Controls.Add(footer);
            Controls.Add(bar);
            Controls.Add(header);
            ResumeLayout(true);

            // tamanho: a imagem cabe na largura do pop-up
            float z = Math.Min(1f, (W - Px(40)) / (float)baseImg.Width);
            int vh = Math.Max(Px(200), Math.Min(Px(460), (int)(baseImg.Height * z) + Px(32)));
            ClientSize = new Size(W, Px(58) + Px(56) + vh + Px(64) + 2);
            Rectangle wa = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(wa.Right - Width - Px(12), wa.Bottom - Height - Px(12));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                if (compact) cp.ClassStyle |= 0x20000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Theme.StyleWindow(this);
            if (compact) Theme.RoundCorners(this);
        }

        void ApplyTheme()
        {
            if (IsDisposed) return;
            BackColor = compact ? Theme.BorderStrong : Theme.Bg;
            viewport.BackColor = Theme.Canvas;
            if (layers != null) { layers.BackColor = Theme.Surface; layers.ForeColor = Theme.Text; Theme.ScrollbarTheme(layers); }
            if (btnTheme != null) btnTheme.IconName = Theme.Dark ? "sun" : "moon";
            Theme.ScrollbarTheme(viewport);
            Theme.StyleWindow(this);
            Invalidate(true);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ZoomFit();
            Activate();
            Native.SetForegroundWindow(Handle);
            canvas.Focus();
        }

        void UpdateTitle()
        {
            Text = (modified ? "• " : "") + Path.GetFileName(filePath) + " — SnapStudio";
            if (!compact && lblFile != null) { lblFile.Text = Path.GetFileName(filePath); PerformLayout(); }
        }

        void UpdateStatus()
        {
            string dims = baseImg.Width + " × " + baseImg.Height;
            if (compact)
            {
                bool copied = TrayApp.Instance == null || TrayApp.Instance.Settings.CopyToClipboard;
                lblStatus.Text = dims + (copied ? S.T(" · copiada para a área de transferência", " · copied to the clipboard") : "");
            }
            else
            {
                lblStatus.Text = (modified ? S.T("Alterações por guardar", "Unsaved changes") : S.T("Guardado", "Saved")) + " · " + dims;
                lblZoom.Text = (int)Math.Round(zoom * 100) + "%";
                lblPath.Text = filePath;
                if (lblStatus.Parent != null) lblStatus.Parent.PerformLayout();
            }
        }

        void UpdateUndoButtons()
        {
            btnUndo.Enabled = undo.Count > 0;
            if (btnRedo != null) btnRedo.Enabled = redo.Count > 0;
            if (btnDelete != null) btnDelete.Enabled = selected != null && anns.Contains(selected);
        }

        void UpdateSection()
        {
            if (lblSection == null) return;
            string s = selected != null && anns.Contains(selected)
                ? AnnName(selected) + S.T(" selecionada", " selected")
                : S.T("Ferramenta: ", "Tool: ") + ToolName(tool);
            lblSection.Text = s.ToUpperInvariant();
        }

        void SelectionChanged()
        {
            UpdateUndoButtons();
            UpdateSection();
            SyncLayerSelection();
            LayoutProps();
            canvas.Invalidate();
        }

        // ================= painel de propriedades contextual =================

        PropGroup NewGroup(string key) { var g = new PropGroup { Key = key }; propGroups.Add(g); return g; }

        ThemedLabel PropLabel(Control parent, string text, int x)
        {
            var l = new ThemedLabel { Text = text, Font = Theme.Font(13 * dpi), Left = x };
            parent.Controls.Add(l);
            return l;
        }

        ThemedLabel ValueLabel(Control parent, int x, int w)
        {
            var l = new ThemedLabel { Font = Theme.Font(13 * dpi), AutoSize = false, TextAlign = ContentAlignment.TopRight, Size = new Size(Px(80), Px(20)), Left = x + w - Px(80) }.Muted();
            parent.Controls.Add(l);
            return l;
        }

        /// <summary>Tipo cujas propriedades o painel mostra: o da anotação selecionada ou o que a ferramenta cria (null: nenhum).</summary>
        string PropKind()
        {
            if (selected != null && anns.Contains(selected))
            {
                var sh = selected as ShapeAnn;
                if (sh != null) return sh.Kind;
                if (selected is TextAnn) return "text";
                if (selected is StepAnn) return "step";
                if (selected is PenAnn) return "pen";
            }
            switch (tool)
            {
                case Tool.Select: case Tool.Crop: return null;
                case Tool.Text: return "text";
                case Tool.Step: return "step";
                case Tool.Pen: return "pen";
                default: return KindFor(tool);
            }
        }

        bool GroupVisible(string key, string k)
        {
            if (k == null) return false;
            switch (key)
            {
                case "color": return k != "blur" && k != "pixelate";
                case "width": return k == "arrow" || k == "line" || k == "rect" || k == "ellipse" || k == "pen";
                case "font": return k == "text" || k == "step";
                case "outline": return k == "text";
                case "fill": return k == "rect" || k == "ellipse";
                case "fillOpacity": return (k == "rect" || k == "ellipse") && curFillOn;
                case "rounded": return k == "arrow" || k == "rect" || k == "highlight" || k == "blur" || k == "pixelate";
                case "radius": return (k == "rect" || k == "highlight" || k == "blur" || k == "pixelate") && curRounded;
            }
            return false;
        }

        /// <summary>Mostra só as propriedades que têm efeito e empilha-as; a lista de anotações fica por baixo.</summary>
        void LayoutProps()
        {
            if (propsPanel == null) return;
            string k = PropKind();
            int y = Px(48);
            lblPropsHint.Visible = k == null;
            if (k == null)
            {
                lblPropsHint.Text = tool == Tool.Crop
                    ? S.T("Arraste sobre a imagem para escolher a zona a manter.", "Drag over the image to choose the area to keep.")
                    : S.T("Selecione uma anotação para a alterar, ou escolha uma ferramenta à esquerda.", "Select an annotation to change it, or pick a tool on the left.");
                lblPropsHint.Top = y;
                y += Px(60);
            }
            propsPanel.SuspendLayout();
            foreach (var g in propGroups)
            {
                bool vis = GroupVisible(g.Key, k);
                for (int i = 0; i < g.Items.Count; i++)
                {
                    g.Items[i].Visible = vis;
                    if (vis) g.Items[i].Top = y + g.Dy[i];
                }
                if (vis) y += g.Height;
            }
            tglRounded.Text = k == "arrow" ? S.T("Ponta arredondada", "Rounded tip") : S.T("Cantos arredondados", "Rounded corners");
            tglRounded.Invalidate();
            propsDivY = y + Px(2);
            y += Px(18);
            lblLayersTitle.Top = y;
            y += Px(26);
            layers.Top = y;
            layers.Height = Math.Max(Px(60), propsPanel.Height - y - Px(12));
            propsPanel.ResumeLayout(true);
            propsPanel.Invalidate();
        }

        void MarkModified()
        {
            modified = true;
            UpdateTitle();
            UpdateStatus();
            UpdateUndoButtons();
            UpdateSection();
            RefreshLayers();
        }

        // ================= lista de anotações =================

        void RefreshLayers()
        {
            if (layers == null) return;
            syncingLayers = true;
            try
            {
                layers.BeginUpdate();
                layers.Items.Clear();
                for (int i = anns.Count - 1; i >= 0; i--) layers.Items.Add(new LayerItem { A = anns[i] });
                layers.EndUpdate();
                SyncLayerSelectionCore();
            }
            finally { syncingLayers = false; }
        }

        void SyncLayerSelection()
        {
            if (layers == null) return;
            syncingLayers = true;
            try { SyncLayerSelectionCore(); } finally { syncingLayers = false; }
        }

        void SyncLayerSelectionCore()
        {
            int idx = -1;
            for (int i = 0; i < layers.Items.Count; i++) if (((LayerItem)layers.Items[i]).A == selected) { idx = i; break; }
            if (layers.SelectedIndex != idx) layers.SelectedIndex = idx;
            layers.Invalidate();
        }

        void Layers_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (syncingLayers) return;
            var li = layers.SelectedItem as LayerItem;
            if (li == null) return;
            if (tool != Tool.Select) SetTool(Tool.Select);
            selected = li.A;
            SyncPropsFromSelection();
            UpdateUndoButtons();
            UpdateSection();
            canvas.Invalidate();
        }

        void Layers_DrawItem(object sender, DrawItemEventArgs e)
        {
            Graphics g = e.Graphics;
            using (var b = new SolidBrush(Theme.Surface)) g.FillRectangle(b, e.Bounds);
            if (e.Index < 0 || e.Index >= layers.Items.Count) return;
            var a = ((LayerItem)layers.Items[e.Index]).A;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var r = new RectangleF(e.Bounds.X + 2, e.Bounds.Y + 1, e.Bounds.Width - 4, e.Bounds.Height - 2);
            if (sel) Theme.FillRound(g, Theme.AccentSoft, r, Px(6));
            var sh = a as ShapeAnn;
            Color dot = sh != null && sh.IsObscure ? Color.FromArgb(154, 160, 174) : a.Color;
            Theme.FillRound(g, dot, new RectangleF(e.Bounds.X + Px(12), e.Bounds.Y + (e.Bounds.Height - Px(10)) / 2f, Px(10), Px(10)), Px(3));
            TextRenderer.DrawText(g, AnnName(a), layers.Font, new Rectangle(e.Bounds.X + Px(32), e.Bounds.Y, e.Bounds.Width - Px(36), e.Bounds.Height),
                sel ? Theme.AccentSoftText : Theme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        // ================= zoom / layout =================

        void LayoutCanvas()
        {
            if (canvas == null || baseImg == null) return;
            int cw = Math.Max(1, (int)Math.Round(baseImg.Width * zoom)), ch = Math.Max(1, (int)Math.Round(baseImg.Height * zoom));
            if (canvas.Size != new Size(cw, ch)) canvas.Size = new Size(cw, ch);
            Point sp = viewport.AutoScrollPosition;
            int x = Math.Max(0, (viewport.ClientSize.Width - cw) / 2), y = Math.Max(0, (viewport.ClientSize.Height - ch) / 2);
            canvas.Location = new Point(x + sp.X, y + sp.Y);
        }

        void SetZoom(float z) { SetZoom(z, new Point(viewport.ClientSize.Width / 2, viewport.ClientSize.Height / 2)); }

        void SetZoom(float z, Point anchor)
        {
            CommitText();
            z = Math.Max(0.05f, Math.Min(16f, z));
            var imgPt = new PointF((anchor.X - canvas.Left) / zoom, (anchor.Y - canvas.Top) / zoom);
            zoom = z;
            cacheDirty = true;
            LayoutCanvas();
            int sx = (int)(imgPt.X * zoom - anchor.X), sy = (int)(imgPt.Y * zoom - anchor.Y);
            viewport.AutoScrollPosition = new Point(Math.Max(0, sx), Math.Max(0, sy));
            LayoutCanvas();
            canvas.Invalidate();
            UpdateStatus();
        }

        void ZoomBy(float f) { SetZoom(zoom * f); }
        void ZoomBy(float f, Point anchor) { SetZoom(zoom * f, anchor); }

        void ZoomFit()
        {
            float pad = (compact ? 16 : 24) * dpi;
            float vw = viewport.ClientSize.Width - pad, vh = viewport.ClientSize.Height - pad;
            float z = Math.Min(1f, Math.Min(vw / baseImg.Width, vh / baseImg.Height));
            SetZoom(Math.Max(0.05f, z));
        }

        PointF ToImg(Point p) { return new PointF(p.X / zoom, p.Y / zoom); }
        RectangleF ToScreen(RectangleF r) { return new RectangleF(r.X * zoom, r.Y * zoom, r.Width * zoom, r.Height * zoom); }
        PointF ToScreen(PointF p) { return new PointF(p.X * zoom, p.Y * zoom); }

        // ================= desenho =================

        void Dirty() { cacheDirty = true; canvas.Invalidate(); }

        void RenderScene(Graphics g, Ann exclude, float scale)
        {
            g.InterpolationMode = scale < 1 ? InterpolationMode.HighQualityBicubic : InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            using (var ia = new ImageAttributes())
            {
                ia.SetWrapMode(WrapMode.TileFlipXY);
                g.DrawImage(baseImg, new Rectangle(0, 0, baseImg.Width, baseImg.Height), 0, 0, baseImg.Width, baseImg.Height, GraphicsUnit.Pixel, ia);
            }
            g.PixelOffsetMode = PixelOffsetMode.Default;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            foreach (var a in anns)
                if (a != exclude && a != editingText) a.Draw(g, baseImg);
        }

        Bitmap Render()
        {
            var bmp = new Bitmap(baseImg.Width, baseImg.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                RenderScene(g, null, 1f);
            }
            return bmp;
        }

        void EnsureCache()
        {
            if (!cacheDirty && (cache == null || cache.Size == canvas.Size)) return;
            long px = (long)canvas.Width * canvas.Height;
            if (px > 5000L * 5000L)
            {
                if (cache != null) { cache.Dispose(); cache = null; }
                cacheDirty = false;
                return;
            }
            if (cache == null || cache.Size != canvas.Size)
            {
                if (cache != null) cache.Dispose();
                cache = new Bitmap(canvas.Width, canvas.Height, PixelFormat.Format32bppPArgb);
            }
            using (var g = Graphics.FromImage(cache))
            {
                g.Clear(Color.Transparent);
                g.ScaleTransform(zoom, zoom);
                RenderScene(g, live, zoom);
            }
            cacheDirty = false;
        }

        void Canvas_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Color.White);
            EnsureCache();
            if (cache != null)
                g.DrawImage(cache, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);
            else
            {
                var st0 = g.Save();
                g.ScaleTransform(zoom, zoom);
                RenderScene(g, live, zoom);
                g.Restore(st0);
            }

            if (live != null)
            {
                var st = g.Save();
                g.ScaleTransform(zoom, zoom);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                live.Draw(g, baseImg);
                g.Restore(st);
            }

            if (drag == Drag.Crop)
            {
                RectangleF r = ToScreen(cropRect);
                using (var region = new Region(new Rectangle(0, 0, canvas.Width, canvas.Height)))
                using (var b = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
                {
                    region.Exclude(r);
                    g.FillRegion(b, region);
                }
                using (var p = new Pen(Color.White, 1) { DashStyle = DashStyle.Dash })
                    g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
                string t = (int)cropRect.Width + " × " + (int)cropRect.Height;
                TextRenderer.DrawText(g, t, Font, new Point((int)r.X + 4, (int)r.Y + 4), Color.White);
            }

            if (selected != null && selected != editingText && anns.Contains(selected))
                DrawSelection(g);
        }

        void DrawSelection(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.None;
            RectangleF r = ToScreen(selected.Bounds);
            r.Inflate(4, 4);
            using (var p = new Pen(SelectionColor, 1) { DashStyle = DashStyle.Dash })
                g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
            var sh = selected as ShapeAnn;
            if (sh != null && tool == Tool.Select)
            {
                DrawHandle(g, ToScreen(sh.P1));
                DrawHandle(g, ToScreen(sh.P2));
            }
        }

        void DrawHandle(Graphics g, PointF p)
        {
            float s = 4 * dpi;
            g.FillRectangle(Brushes.White, p.X - s, p.Y - s, 2 * s, 2 * s);
            using (var pen = new Pen(SelectionColor, 1.5f)) g.DrawRectangle(pen, p.X - s, p.Y - s, 2 * s, 2 * s);
        }

        // ================= rato =================

        Ann HitTest(PointF p, float tol)
        {
            for (int i = anns.Count - 1; i >= 0; i--)
                if (anns[i].Hit(p, tol)) return anns[i];
            return null;
        }

        static string KindFor(Tool t)
        {
            switch (t)
            {
                case Tool.Arrow: return "arrow";
                case Tool.Line: return "line";
                case Tool.Rect: return "rect";
                case Tool.Ellipse: return "ellipse";
                case Tool.Highlight: return "highlight";
                case Tool.Blur: return "blur";
                case Tool.Pixelate: return "pixelate";
            }
            return "rect";
        }

        int NextStep()
        {
            int n = 0;
            foreach (var a in anns) { var s = a as StepAnn; if (s != null && s.Number > n) n = s.Number; }
            return n + 1;
        }

        Snap MakeSnap() { return new Snap { Base = baseImg, Json = Ann.Serialize(anns) }; }

        void PushUndo() { PushUndo(MakeSnap()); }
        void PushUndo(Snap s)
        {
            undo.Push(s);
            redo.Clear();
            if (undo.Count > 200)
            {
                var keep = undo.ToArray().Take(200).Reverse().ToList();
                undo.Clear();
                foreach (var k in keep) undo.Push(k);
            }
            UpdateUndoButtons();
        }

        void BeginEdit(Ann a)
        {
            pendingUndo = MakeSnap();
            live = a;
            cacheDirty = true;
        }

        void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (textBox != null) { CommitText(); return; }
            if (e.Button != MouseButtons.Left) return;
            canvas.Focus();
            PointF p = ToImg(e.Location);
            dragStart = p; lastPt = p; moved = false;
            float tol = 6 * dpi / zoom;

            switch (tool)
            {
                case Tool.Select:
                    {
                        var sh = selected as ShapeAnn;
                        if (sh != null && anns.Contains(sh))
                        {
                            if (Ann.Dist(p, sh.P1) <= tol * 1.5f) { BeginEdit(sh); drag = Drag.Handle1; canvas.Invalidate(); return; }
                            if (Ann.Dist(p, sh.P2) <= tol * 1.5f) { BeginEdit(sh); drag = Drag.Handle2; canvas.Invalidate(); return; }
                        }
                        selected = HitTest(p, tol);
                        if (selected != null) { BeginEdit(selected); drag = Drag.Move; }
                        SyncPropsFromSelection();
                        SelectionChanged();
                        Dirty();
                    }
                    break;
                case Tool.Text:
                    BeginTextEdit(HitTest(p, tol) as TextAnn, p);
                    break;
                case Tool.Step:
                    {
                        PushUndo();
                        var st = new StepAnn { X = p.X, Y = p.Y, Number = NextStep(), Size = curFont, Color = curColor };
                        anns.Add(st);
                        selected = st;
                        MarkModified();
                        Dirty();
                    }
                    break;
                case Tool.Pen:
                    {
                        var pa = new PenAnn { Color = curColor, Width = curWidth };
                        pa.Pts.Add(p.X); pa.Pts.Add(p.Y);
                        live = pa; drag = Drag.Create;
                        Dirty();
                    }
                    break;
                case Tool.Crop:
                    drag = Drag.Crop;
                    cropRect = new RectangleF(p, SizeF.Empty);
                    break;
                default:
                    {
                        var sa = new ShapeAnn { Kind = KindFor(tool), X1 = p.X, Y1 = p.Y, X2 = p.X, Y2 = p.Y, Width = curWidth, Color = tool == Tool.Highlight ? HighlightColor : curColor };
                        if (curRounded && sa.IsRoundable) sa.Radius = curRadius;
                        if (curFillOn && (sa.Kind == "rect" || sa.Kind == "ellipse")) sa.Fill = curFill;
                        live = sa; drag = Drag.Create;
                        Dirty();
                    }
                    break;
            }
        }

        static PointF Constrain(PointF a, PointF p, bool linear)
        {
            float dx = p.X - a.X, dy = p.Y - a.Y;
            if (linear)
            {
                double ang = Math.Atan2(dy, dx), step = Math.PI / 4;
                ang = Math.Round(ang / step) * step;
                double len = Math.Sqrt(dx * dx + dy * dy);
                return new PointF(a.X + (float)(Math.Cos(ang) * len), a.Y + (float)(Math.Sin(ang) * len));
            }
            float d = Math.Max(Math.Abs(dx), Math.Abs(dy));
            return new PointF(a.X + Math.Sign(dx) * d, a.Y + Math.Sign(dy) * d);
        }

        PointF ClampToImage(PointF p)
        {
            return new PointF(Math.Max(0, Math.Min(baseImg.Width, p.X)), Math.Max(0, Math.Min(baseImg.Height, p.Y)));
        }

        void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            PointF p = ToImg(e.Location);
            if (drag == Drag.None) { UpdateCursor(p); return; }
            bool shift = (ModifierKeys & Keys.Shift) != 0;

            switch (drag)
            {
                case Drag.Create:
                    {
                        var sa = live as ShapeAnn;
                        if (sa != null)
                        {
                            PointF q = shift ? Constrain(sa.P1, p, sa.IsLinear) : p;
                            sa.X2 = q.X; sa.Y2 = q.Y;
                        }
                        var pa = live as PenAnn;
                        if (pa != null)
                        {
                            int n = pa.Pts.Count;
                            if (Ann.Dist(p, new PointF(pa.Pts[n - 2], pa.Pts[n - 1])) > 1.2f / zoom) { pa.Pts.Add(p.X); pa.Pts.Add(p.Y); }
                        }
                    }
                    break;
                case Drag.Move:
                    {
                        float dx = p.X - lastPt.X, dy = p.Y - lastPt.Y;
                        if (!moved && Ann.Dist(p, dragStart) * zoom < 2) return;
                        if (!moved) { moved = true; PushUndo(pendingUndo); }
                        selected.Offset(dx, dy);
                        lastPt = p;
                    }
                    break;
                case Drag.Handle1:
                case Drag.Handle2:
                    {
                        if (!moved) { moved = true; PushUndo(pendingUndo); }
                        var sh = (ShapeAnn)selected;
                        PointF other = drag == Drag.Handle1 ? sh.P2 : sh.P1;
                        PointF q = shift ? Constrain(other, p, sh.IsLinear) : p;
                        if (drag == Drag.Handle1) sh.P1 = q; else sh.P2 = q;
                    }
                    break;
                case Drag.Crop:
                    {
                        PointF a = ClampToImage(dragStart), b = ClampToImage(p);
                        cropRect = Ann.Norm(a.X, a.Y, b.X, b.Y);
                    }
                    break;
            }
            canvas.Invalidate();
        }

        void Canvas_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            switch (drag)
            {
                case Drag.Create:
                    {
                        Ann a = live;
                        live = null;
                        bool valid = true;
                        var sa = a as ShapeAnn;
                        if (sa != null)
                        {
                            float w = Math.Abs(sa.X2 - sa.X1) * zoom, h = Math.Abs(sa.Y2 - sa.Y1) * zoom;
                            valid = sa.IsLinear ? (w + h) >= 6 : (w >= 4 && h >= 4);
                        }
                        if (valid)
                        {
                            PushUndo();
                            anns.Add(a);
                            selected = a;
                            MarkModified();
                            // depois de colocar um elemento, volta ao ponteiro com ele selecionado;
                            // o desenho livre mantém-se para traçar vários seguidos
                            if (!(a is PenAnn)) SetTool(Tool.Select);
                        }
                        Dirty();
                    }
                    break;
                case Drag.Move:
                case Drag.Handle1:
                case Drag.Handle2:
                    live = null;
                    if (moved) MarkModified();
                    Dirty();
                    break;
                case Drag.Crop:
                    drag = Drag.None;
                    if (cropRect.Width >= 4 && cropRect.Height >= 4) ApplyCrop(Rectangle.Round(cropRect));
                    canvas.Invalidate();
                    break;
            }
            drag = Drag.None;
            pendingUndo = null;
            SelectionChanged();
        }

        void Canvas_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (tool != Tool.Select || e.Button != MouseButtons.Left) return;
            var t = HitTest(ToImg(e.Location), 6 * dpi / zoom) as TextAnn;
            if (t != null) BeginTextEdit(t, new PointF(t.X, t.Y));
        }

        void UpdateCursor(PointF p)
        {
            Cursor c = Cursors.Cross;
            if (tool == Tool.Select)
            {
                c = Cursors.Default;
                float tol = 6 * dpi / zoom;
                var sh = selected as ShapeAnn;
                if (sh != null && (Ann.Dist(p, sh.P1) <= tol * 1.5f || Ann.Dist(p, sh.P2) <= tol * 1.5f)) c = Cursors.Cross;
                else if (HitTest(p, tol) != null) c = Cursors.SizeAll;
            }
            else if (tool == Tool.Text) c = Cursors.IBeam;
            if (canvas.Cursor != c) canvas.Cursor = c;
        }

        void ApplyCrop(Rectangle r)
        {
            r.Intersect(new Rectangle(0, 0, baseImg.Width, baseImg.Height));
            if (r.Width < 2 || r.Height < 2) return;
            PushUndo();
            var nb = ImageUtil.Crop(baseImg, r);
            foreach (var a in anns) a.Offset(-r.X, -r.Y);
            baseImg = nb;
            selected = null;
            MarkModified();
            SetTool(Tool.Select);
            ZoomFit();
            Dirty();
            UpdateStatus();
        }

        // ================= texto =================

        void BeginTextEdit(TextAnn existing, PointF p)
        {
            CommitText();
            editingText = existing;
            textSize = existing != null ? existing.Size : curFont;
            textColor = existing != null ? existing.Color : curColor;
            textPos = existing != null ? new PointF(existing.X, existing.Y) : p;

            textBox = new TextBox
            {
                Multiline = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", Math.Max(6f, textSize * zoom), FontStyle.Bold, GraphicsUnit.Pixel),
                ForeColor = textColor,
                BackColor = Ann.Contrast(textColor) == Color.White ? Color.White : Color.FromArgb(60, 60, 60),
                Text = existing != null ? existing.Text.Replace("\n", "\r\n") : "",
                Location = new Point((int)(textPos.X * zoom) - 3, (int)(textPos.Y * zoom) - 3),
                ScrollBars = ScrollBars.None,
                WordWrap = false,
            };
            textBox.TextChanged += delegate { FitTextBox(); };
            textBox.KeyDown += TextBox_KeyDown;
            textBox.LostFocus += delegate { CommitText(); };
            canvas.Controls.Add(textBox);
            FitTextBox();
            textBox.Focus();
            textBox.SelectAll();
            if (existing != null) Dirty();
        }

        void FitTextBox()
        {
            if (textBox == null) return;
            Size s = TextRenderer.MeasureText(textBox.Text + "W", textBox.Font);
            textBox.Size = new Size(s.Width + 12, s.Height + 8);
        }

        void TextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; CommitText(); canvas.Focus(); }
        }

        void CancelText()
        {
            if (textBox == null) return;
            textBox.Text = editingText != null ? editingText.Text : "";
            CommitText();
            canvas.Focus();
        }

        void CommitText()
        {
            if (textBox == null) return;
            var tb = textBox;
            textBox = null;
            string txt = tb.Text.Replace("\r\n", "\n").TrimEnd();
            canvas.Controls.Remove(tb);
            BeginInvoke((Action)tb.Dispose);

            if (editingText != null)
            {
                if (txt != editingText.Text)
                {
                    PushUndo();
                    if (txt.Length == 0) { anns.Remove(editingText); if (selected == editingText) selected = null; }
                    else editingText.Text = txt;
                    MarkModified();
                }
            }
            else if (txt.Length > 0)
            {
                PushUndo();
                var t = new TextAnn { X = textPos.X, Y = textPos.Y, Text = txt, Size = textSize, Color = textColor, Outline = curOutline };
                anns.Add(t);
                selected = t;
                MarkModified();
            }
            editingText = null;
            Dirty();
            SelectionChanged();
            // texto terminado: volta ao ponteiro (se vier de SetTool(x), o SetTool exterior define x a seguir)
            if (tool == Tool.Text) SetTool(Tool.Select);
        }

        // ================= ferramentas e propriedades =================

        void SetTool(Tool t)
        {
            CommitText();
            tool = t;
            foreach (var kv in toolButtons) kv.Value.Checked = kv.Key == t;
            if (t != Tool.Select && selected != null) selected = null;
            canvas.Cursor = t == Tool.Select ? Cursors.Default : t == Tool.Text ? Cursors.IBeam : Cursors.Cross;
            SelectionChanged();
        }

        void UpdateSwatches()
        {
            bool any = false;
            foreach (var b in swatches)
            {
                bool on = b.SwatchColor.ToArgb() == curColor.ToArgb();
                b.Checked = on;
                any |= on;
            }
            if (btnMoreColor != null) btnMoreColor.Checked = !any;
        }

        /// <summary>O editor abre com a última cor, espessura e tamanho de texto que o utilizador escolheu.</summary>
        void LoadLastStyle()
        {
            var st = TrayApp.Instance != null ? TrayApp.Instance.Settings : null;
            if (st == null) return;
            if (st.EditorColor != 0) curColor = Color.FromArgb(st.EditorColor);
            if (st.EditorWidth >= 1 && st.EditorWidth <= 16) curWidth = st.EditorWidth;
            if (st.EditorFont >= 12 && st.EditorFont <= 80) curFont = st.EditorFont;
            curRounded = st.EditorRounded;
            if (st.EditorRadius >= 2 && st.EditorRadius <= 40) curRadius = st.EditorRadius;
            curFillOn = st.EditorFill;
            if (st.EditorFillOpacity >= 10 && st.EditorFillOpacity <= 100) curFill = st.EditorFillOpacity / 100f;
            if (st.EditorTextOutline >= 0 && st.EditorTextOutline <= 3) curOutline = st.EditorTextOutline;
        }

        /// <summary>Memoriza a escolha (gravada no disco ao fechar o editor).</summary>
        void RememberStyle()
        {
            var st = TrayApp.Instance != null && !TrayApp.Instance.Headless ? TrayApp.Instance.Settings : null;
            if (st == null) return;
            int c = curColor.ToArgb(), w = (int)curWidth, f = (int)curFont, rr = (int)curRadius, fo = (int)Math.Round(curFill * 100);
            if (st.EditorColor == c && st.EditorWidth == w && st.EditorFont == f && st.EditorRounded == curRounded && st.EditorRadius == rr
                && st.EditorFill == curFillOn && st.EditorFillOpacity == fo && st.EditorTextOutline == curOutline) return;
            st.EditorColor = c; st.EditorWidth = w; st.EditorFont = f;
            st.EditorRounded = curRounded; st.EditorRadius = rr;
            st.EditorFill = curFillOn; st.EditorFillOpacity = fo; st.EditorTextOutline = curOutline;
            styleChanged = true;
        }

        bool styleChanged;

        void SetColor(Color c) { SetColor(c, true); }

        void SetColor(Color c, bool remember)
        {
            curColor = c;
            if (remember) RememberStyle();
            UpdateSwatches();
            if (textBox != null) { textColor = c; textBox.ForeColor = c; return; }
            if (selected != null && anns.Contains(selected) && selected.Argb != c.ToArgb())
            {
                PushUndo();
                selected.Color = c;
                MarkModified();
                Dirty();
            }
        }

        void PickColor()
        {
            dialogOpen = true;
            try
            {
                using (var d = new ColorDialog { Color = curColor, FullOpen = true, AnyColor = true })
                    if (d.ShowDialog(this) == DialogResult.OK) SetColor(d.Color);
            }
            finally { dialogOpen = false; }
        }

        void SetWidth(float w)
        {
            curWidth = w;
            RememberStyle();
            if (lblWidthVal != null) lblWidthVal.Text = (int)w + " px";
            if (selected != null && anns.Contains(selected) && selected.UsesWidth && selected.Width != w)
            {
                PushUndo();
                selected.Width = w;
                MarkModified();
                Dirty();
            }
        }

        void SetFontSize(float s)
        {
            curFont = s;
            RememberStyle();
            if (lblFontVal != null) lblFontVal.Text = (int)s + " px";
            if (textBox != null)
            {
                textSize = s;
                var old = textBox.Font;
                textBox.Font = new Font("Segoe UI", Math.Max(6f, s * zoom), FontStyle.Bold, GraphicsUnit.Pixel);
                old.Dispose();
                FitTextBox();
                return;
            }
            if (selected != null && anns.Contains(selected) && selected.UsesFont && selected.FontSize != s)
            {
                PushUndo();
                selected.FontSize = s;
                MarkModified();
                Dirty();
            }
        }

        void SyncSliders()
        {
            if (sldWidth == null) return;
            syncing = true;
            try
            {
                sldWidth.SetSilently((int)curWidth); lblWidthVal.Text = (int)curWidth + " px";
                sldFont.SetSilently((int)curFont); lblFontVal.Text = (int)curFont + " px";
                if (tglRounded != null)
                {
                    tglRounded.Checked = curRounded;
                    sldRadius.SetSilently((int)curRadius); lblRadiusVal.Text = (int)curRadius + " px";
                    sldRadius.Enabled = curRounded;
                }
                if (tglFill != null)
                {
                    tglFill.Checked = curFillOn;
                    sldFill.SetSilently((int)Math.Round(curFill * 100)); lblFillVal.Text = (int)Math.Round(curFill * 100) + "%";
                }
                foreach (var b in outlineButtons) b.Checked = (int)b.Tag == curOutline;
            }
            finally { syncing = false; }
        }

        /// <summary>Liga/desliga o preenchimento (e aplica à caixa/elipse selecionada).</summary>
        void SetFill(bool on)
        {
            curFillOn = on;
            RememberStyle();
            LayoutProps();
            var sh = selected as ShapeAnn;
            if (sh != null && anns.Contains(sh) && (sh.Kind == "rect" || sh.Kind == "ellipse") && (sh.Fill > 0) != on)
            {
                PushUndo();
                sh.Fill = on ? curFill : 0;
                MarkModified();
                Dirty();
            }
        }

        void SetFillOpacity(float o)
        {
            curFill = Math.Max(0.1f, Math.Min(1f, o));
            RememberStyle();
            if (lblFillVal != null) lblFillVal.Text = (int)Math.Round(curFill * 100) + "%";
            var sh = selected as ShapeAnn;
            if (curFillOn && sh != null && anns.Contains(sh) && (sh.Kind == "rect" || sh.Kind == "ellipse") && sh.Fill != curFill)
            {
                PushUndo();
                sh.Fill = curFill;
                MarkModified();
                Dirty();
            }
        }

        /// <summary>Contorno do texto: 0 automático, 1 branco, 2 preto, 3 nenhum.</summary>
        void SetTextOutline(int o)
        {
            curOutline = o;
            RememberStyle();
            foreach (var b in outlineButtons) b.Checked = (int)b.Tag == o;
            var t = selected as TextAnn;
            if (t != null && anns.Contains(t) && t.Outline != o)
            {
                PushUndo();
                t.Outline = o;
                MarkModified();
                Dirty();
            }
        }

        /// <summary>Liga/desliga os cantos arredondados (e aplica à anotação selecionada).</summary>
        void SetRounded(bool on)
        {
            curRounded = on;
            RememberStyle();
            LayoutProps();
            if (sldRadius != null) sldRadius.Enabled = on;
            var sh = selected as ShapeAnn;
            if (sh != null && anns.Contains(sh) && sh.IsRoundable && (sh.Radius > 0) != on)
            {
                PushUndo();
                sh.Radius = on ? curRadius : 0;
                MarkModified();
                Dirty();
            }
        }

        void SetRadius(float r)
        {
            curRadius = r;
            RememberStyle();
            if (lblRadiusVal != null) lblRadiusVal.Text = (int)r + " px";
            var sh = selected as ShapeAnn;
            if (curRounded && sh != null && anns.Contains(sh) && sh.IsRoundable && sh.Radius != r)
            {
                PushUndo();
                sh.Radius = r;
                MarkModified();
                Dirty();
            }
        }

        void SyncPropsFromSelection()
        {
            if (selected == null) { SyncSliders(); return; }
            curColor = selected.Color;
            UpdateSwatches();
            if (selected.UsesWidth) curWidth = selected.Width;
            if (selected.UsesFont) curFont = selected.FontSize;
            var sh = selected as ShapeAnn;
            if (sh != null && sh.IsRoundable)
            {
                curRounded = sh.Radius > 0;
                if (sh.Radius > 0) curRadius = sh.Radius;
            }
            if (sh != null && (sh.Kind == "rect" || sh.Kind == "ellipse"))
            {
                curFillOn = sh.Fill > 0;
                if (sh.Fill > 0) curFill = sh.Fill;
            }
            var ta = selected as TextAnn;
            if (ta != null) curOutline = ta.Outline;
            SyncSliders();
            LayoutProps();
        }

        void DeleteSelected()
        {
            if (selected == null || !anns.Contains(selected)) return;
            PushUndo();
            anns.Remove(selected);
            selected = null;
            MarkModified();
            Dirty();
            SelectionChanged();
        }

        void Nudge(float dx, float dy)
        {
            if (selected == null || !anns.Contains(selected)) return;
            PushUndo();
            selected.Offset(dx, dy);
            MarkModified();
            Dirty();
        }

        void Undo()
        {
            CommitText();
            if (undo.Count == 0) return;
            redo.Push(MakeSnap());
            Restore(undo.Pop());
        }

        void Redo()
        {
            CommitText();
            if (redo.Count == 0) return;
            undo.Push(MakeSnap());
            Restore(redo.Pop());
        }

        void Restore(Snap s)
        {
            bool baseChanged = s.Base != baseImg;
            baseImg = s.Base;
            anns = Ann.Deserialize(s.Json);
            selected = null; live = null;
            if (baseChanged) ZoomFit();
            MarkModified();
            Dirty();
            UpdateStatus();
            SelectionChanged();
        }

        // ================= ficheiros =================

        public bool Save()
        {
            CommitText();
            try
            {
                using (var bmp = Render()) ImageUtil.Save(bmp, filePath);
                SaveSidecar();
                modified = false;
                UpdateTitle();
                UpdateStatus();
                return true;
            }
            catch (Exception ex)
            {
                dialogOpen = true;
                MessageBox.Show(this, S.T("Não foi possível guardar:\n", "Could not save:\n") + ex.Message, "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Error);
                dialogOpen = false;
                return false;
            }
        }

        /// <summary>Exporta a imagem com anotações (usado pelo modo de teste --render).</summary>
        public void ExportTo(string path)
        {
            using (var bmp = Render()) ImageUtil.Save(bmp, path);
        }

        void SaveSidecar()
        {
            if (anns.Count == 0) { Sidecar.Delete(filePath); return; }
            Sidecar.EnsureDir();
            baseImg.Save(Sidecar.Base(filePath), ImageFormat.Png);
            File.WriteAllText(Sidecar.Json(filePath), Ann.Serialize(anns));
        }

        void SaveAs()
        {
            CommitText();
            dialogOpen = true;
            try
            {
                using (var d = new SaveFileDialog())
                {
                    d.Filter = "PNG (*.png)|*.png|JPEG (*.jpg)|*.jpg|BMP (*.bmp)|*.bmp";
                    d.FileName = Path.GetFileNameWithoutExtension(filePath);
                    d.InitialDirectory = Path.GetDirectoryName(filePath);
                    string ext = Path.GetExtension(filePath).ToLowerInvariant();
                    d.FilterIndex = ext == ".jpg" || ext == ".jpeg" ? 2 : ext == ".bmp" ? 3 : 1;
                    if (d.ShowDialog(this) != DialogResult.OK) return;
                    filePath = d.FileName;
                    Save();
                }
            }
            finally { dialogOpen = false; }
        }

        void CopyToClipboard(bool notify)
        {
            CommitText();
            try
            {
                using (var bmp = Render())
                    Clipboard.SetDataObject(bmp, true, 5, 100);
                if (notify && lblStatus != null) lblStatus.Text = S.T("Imagem copiada para a área de transferência", "Image copied to the clipboard");
            }
            catch (Exception ex)
            {
                dialogOpen = true;
                MessageBox.Show(this, ex.Message, "SnapStudio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                dialogOpen = false;
            }
        }

        void DragButton_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (modified && !Save()) return;
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { filePath });
            data.SetImage(Render());
            DoDragDrop(data, DragDropEffects.Copy);
        }

        // ---------- ações do pop-up ----------

        void SaveAndClose()
        {
            CommitText();
            if (modified) Save();
            quietClose = true;
            Close();
        }

        void CopyAndClose()
        {
            CommitText();
            CopyToClipboard(false);
            if (modified && !Save() && !compact) return; // no editor completo, não fechar se não conseguiu guardar
            quietClose = true;
            Close();
        }

        void OpenFullEditor()
        {
            CommitText();
            if (modified && !Save()) return;
            string p = filePath;
            quietClose = true;
            Close();
            if (TrayApp.Instance != null) TrayApp.Instance.OpenEditor(p, null);
        }

        void Discard()
        {
            CommitText();
            modified = false;
            quietClose = true;
            try
            {
                if (File.Exists(filePath)) FileSystem.DeleteFile(filePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                Sidecar.Delete(filePath);
            }
            catch { }
            Close();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            // o pop-up fecha-se (e guarda) ao clicar fora
            bool headless = TrayApp.Instance != null && TrayApp.Instance.Headless;
            if (compact && !headless && !dialogOpen && !quietClose && Visible && !IsDisposed)
                BeginInvoke((Action)delegate { if (!IsDisposed && !quietClose && !dialogOpen && Form.ActiveForm != this) SaveAndClose(); });
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            CommitText();
            // as alterações são sempre guardadas ao fechar (sem perguntar);
            // no editor completo, se não for possível guardar, a janela fica aberta
            if (modified && !Save() && !compact && !quietClose && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; return; }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Theme.Changed -= ApplyTheme;
            if (styleChanged) try { TrayApp.Instance.Settings.Save(); } catch { }
            if (cache != null) cache.Dispose();
            tips.Dispose();
            base.OnFormClosed(e);
        }

        // ================= teclado =================

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (textBox != null && textBox.Focused)
            {
                if (keyData == Keys.Escape) { CancelText(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            Keys code = keyData & Keys.KeyCode;
            bool arrow = code == Keys.Left || code == Keys.Right || code == Keys.Up || code == Keys.Down;
            if (arrow && (ActiveControl is Slider || ActiveControl == layers)) return base.ProcessCmdKey(ref msg, keyData);

            float step = (keyData & Keys.Shift) != 0 ? 10 / zoom : 1 / zoom;
            switch (keyData)
            {
                case Keys.Control | Keys.S: Save(); return true;
                case Keys.Control | Keys.Shift | Keys.S: SaveAs(); return true;
                case Keys.Control | Keys.C: CopyToClipboard(true); return true;
                case Keys.Control | Keys.Enter: CopyAndClose(); return true;
                case Keys.Control | Keys.Z: Undo(); return true;
                case Keys.Control | Keys.Y:
                case Keys.Control | Keys.Shift | Keys.Z: Redo(); return true;
                case Keys.Delete:
                case Keys.Back: DeleteSelected(); return true;
                case Keys.Escape:
                    if (drag != Drag.None) { drag = Drag.None; live = null; Dirty(); }
                    else if (compact) SaveAndClose();
                    else if (selected != null) { selected = null; SelectionChanged(); }
                    else SetTool(Tool.Select);
                    return true;
                case Keys.Control | Keys.Oemplus:
                case Keys.Control | Keys.Add: ZoomBy(1.25f); return true;
                case Keys.Control | Keys.OemMinus:
                case Keys.Control | Keys.Subtract: ZoomBy(1 / 1.25f); return true;
                case Keys.Control | Keys.D0:
                case Keys.Control | Keys.NumPad0: SetZoom(1f); return true;
                case Keys.Control | Keys.D9: ZoomFit(); return true;
                case Keys.Control | Keys.Left: if (!compact) { Navigate(-1); return true; } break;
                case Keys.Control | Keys.Right: if (!compact) { Navigate(1); return true; } break;
                case Keys.Left: case Keys.Shift | Keys.Left: Nudge(-step, 0); return true;
                case Keys.Right: case Keys.Shift | Keys.Right: Nudge(step, 0); return true;
                case Keys.Up: case Keys.Shift | Keys.Up: Nudge(0, -step); return true;
                case Keys.Down: case Keys.Shift | Keys.Down: Nudge(0, step); return true;
            }
            if ((keyData & (Keys.Control | Keys.Alt)) == 0)
            {
                foreach (var kv in toolButtons)
                    if (code.ToString() == ToolKey(kv.Key) && (keyData & Keys.Shift) == 0) { SetTool(kv.Key); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
