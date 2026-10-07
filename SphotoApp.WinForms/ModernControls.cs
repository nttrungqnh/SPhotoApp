using System.Drawing.Drawing2D;

namespace SphotoApp.WinForms;

internal class Surface : Panel
{
    public int Radius { get; set; } = 12;
    public Color Fill { get; set; } = Color.White;
    public Color Stroke { get; set; } = Color.FromArgb(224, 234, 249);
    public Surface() { DoubleBuffered = true; BackColor = Color.Transparent; }
    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        if (Width < 3 || Height < 3) return;
        using var outline = Outline(new RectangleF(0, 0, Width, Height), Radius);
        var previous = Region; Region = new Region(outline); previous?.Dispose();
    }
    internal static GraphicsPath Outline(RectangleF r, float radius)
    {
        var path = new GraphicsPath(); var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        path.AddArc(r.X, r.Y, d, d, 180, 90); path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); path.AddArc(r.X, r.Bottom - d, d, d, 90, 90); path.CloseFigure(); return path;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); if (Width < 3 || Height < 3) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Outline(new RectangleF(.5F, .5F, Width - 1, Height - 1), Radius);
        using var fill = new SolidBrush(Fill); using var pen = new Pen(Stroke);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(pen, path);
    }
}

internal class StyledButton : Button
{
    public bool Accent { get; set; }
    public string Glyph { get; set; } = "";
    public bool Navigation { get; set; }
    public bool Shadow { get; set; }
    private bool hover;
    public StyledButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false; Cursor = Cursors.Hand;
    }
    private Color BackgroundColor()
    {
        // Transparent containers inherit their background; clearing with Transparent paints black corners.
        for (Control? container = Parent; container is not null; container = container.Parent)
            if (container.BackColor.A == 255) return container.BackColor;
        return Color.White;
    }
    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(BackgroundColor());
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackgroundColor()); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        if (Width < 4 || Height < 4) return;
        var bottomInset = Shadow ? 5 : 1;
        using var path = Navigation ? TabOutline() : Surface.Outline(new RectangleF(1, 1, Width - 3, Height - bottomInset - 2), 7);
        if (Shadow && Accent && Enabled)
        {
            using var shadow = Surface.Outline(new RectangleF(2, 5, Width - 4, Height - 7), 7);
            using var shadowBrush = new SolidBrush(Color.FromArgb(224, 232, 251));
            e.Graphics.FillPath(shadowBrush, shadow);
        }
        var start = Accent ? Color.FromArgb(65, 139, 255) : Color.White;
        var end = Accent ? Color.FromArgb(49, 71, 233) : Color.FromArgb(246, 249, 255);
        if (hover) { start = Accent ? Color.FromArgb(48, 117, 246) : Color.FromArgb(235, 243, 255); }
        using var brush = new LinearGradientBrush(ClientRectangle, start, end, 15F);
        e.Graphics.FillPath(brush, path);
        if (!Accent) { using var pen = new Pen(Color.FromArgb(218, 229, 246)); e.Graphics.DrawPath(pen, path); }
        var color = Enabled ? (Accent ? Color.White : ForeColor) : Color.FromArgb(170, 183, 209);
        var bounds = new Rectangle(10, 0, Width - 20, Height - bottomInset);
        if (Glyph.Length > 0)
        {
            using var iconFont = new Font("Segoe MDL2 Assets", 15);
            TextRenderer.DrawText(e.Graphics, Glyph, iconFont, new Rectangle(14, 0, 28, Height - bottomInset), color, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
            bounds = new Rectangle(45, 0, Width - 55, Height - bottomInset);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, bounds, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -5, -5));
    }
    private GraphicsPath TabOutline()
    {
        var path = new GraphicsPath();
        path.AddLine(1, Height - 1, 1, 8); path.AddArc(1, 1, 14, 14, 180, 90);
        path.AddLine(8, 1, Width - 14, 1); path.AddArc(Width - 21, 1, 14, 14, 270, 80);
        path.AddLine(Width - 7, 7, Width - 1, Height - 1); path.CloseFigure(); return path;
    }
}

internal sealed class ProcessingLog : ListBox
{
    public ProcessingLog()
    {
        Dock = DockStyle.Fill; BorderStyle = BorderStyle.None; IntegralHeight = false;
        Font = new Font("Segoe UI", 8.5F); DrawMode = DrawMode.OwnerDrawFixed;
        ItemHeight = 20; BackColor = Color.White; SelectionMode = SelectionMode.None;
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        e.DrawBackground(); if (e.Index < 0) return;
        var text = Convert.ToString(Items[e.Index]) ?? "";
        var split = text.IndexOf("  ", StringComparison.Ordinal);
        var time = split < 0 ? "" : text[..split]; var message = split < 0 ? text : text[(split + 2)..];
        var timeWidth = (int)(76 * DeviceDpi / 96F);
        using var timeFont = new Font(Font, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, time, timeFont, new Rectangle(e.Bounds.X, e.Bounds.Y, timeWidth, e.Bounds.Height), Color.FromArgb(58, 108, 247), TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, message, Font, new Rectangle(e.Bounds.X + timeWidth, e.Bounds.Y, Math.Max(1, e.Bounds.Width - timeWidth), e.Bounds.Height), Color.FromArgb(65, 77, 105), TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
