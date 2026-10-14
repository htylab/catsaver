using System.Drawing.Drawing2D;

namespace CatSaver.Overlay;

/// <summary>
/// "我在忙，先關掉" pill button shown under the cat. Clicking it dismisses the
/// current reminder without taking keyboard focus from the user's app.
/// </summary>
internal sealed class DismissButton : LayeredWindow
{
    private const float LogicalWidth = 360, LogicalHeight = 72;

    private readonly string _text;
    private readonly FontFamily _font;
    private readonly float _scale;
    private bool _hover;
    private bool _pressed;

    public event EventHandler? Clicked;

    private DismissButton(Rectangle bounds, float scale, string text, FontFamily font)
        : base(bounds, clickThrough: false)
    {
        _scale = scale;
        _text = text;
        _font = font;
        Cursor = Cursors.Hand;
    }

    /// <summary>Places the button just below the cat, kept inside the monitor's work area.</summary>
    public static DismissButton Create(Screen screen, Rectangle catBounds, string text, FontFamily font)
    {
        var work = screen.WorkingArea;
        float scale = Math.Max(0.5f, screen.Bounds.Height / 1080f);
        int w = (int)Math.Ceiling(LogicalWidth * scale);
        int h = (int)Math.Ceiling(LogicalHeight * scale);
        int x = catBounds.Left + (catBounds.Width - w) / 2;
        int y = catBounds.Bottom + (int)(12 * scale);
        y = Math.Min(y, work.Bottom - h - (int)(16 * scale));
        return new DismissButton(new Rectangle(x, y, w, h), scale, text, font);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; base.OnMouseLeave(e); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) _pressed = true;
        base.OnMouseDown(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        bool click = _pressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
        _pressed = false;
        base.OnMouseUp(e);
        if (click) Clicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void Draw(Graphics g, in CatFrame frame)
    {
        g.ScaleTransform(_scale, _scale);

        float inset = _pressed ? 6 : 4;
        var rect = new RectangleF(inset, inset, LogicalWidth - inset * 2, LogicalHeight - inset * 2);
        using var pill = Drawing.RoundedRect(rect, rect.Height / 2);

        var fill = _hover ? Color.FromArgb(255, 255, 236, 222) : Color.FromArgb(240, 255, 252, 245);
        using (var b = new SolidBrush(fill)) g.FillPath(b, pill);
        using (var pen = new Pen(Color.FromArgb(255, 255, 140, 90), 4f)) g.DrawPath(pen, pill);

        // "✕" drawn with lines so it doesn't depend on the font having the glyph.
        float cx = rect.Left + 40, cy = rect.Top + rect.Height / 2, r = 11;
        var ink = Color.FromArgb(255, 110, 85, 75);
        using (var x = new Pen(ink, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.DrawLine(x, cx - r, cy - r, cx + r, cy + r);
            g.DrawLine(x, cx - r, cy + r, cx + r, cy - r);
        }

        var textBox = new RectangleF(rect.Left + 66, rect.Top + 12, rect.Width - 66 - 28, rect.Height - 24);
        Drawing.DrawFittedText(g, _font, _text, FontStyle.Bold, 30, ink, textBox);
    }
}
