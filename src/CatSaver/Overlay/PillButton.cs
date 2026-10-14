using System.Drawing.Drawing2D;

namespace CatSaver.Overlay;

/// <summary>
/// Rounded button shown under the cat (「我喝了！」 / 「我在忙，先關掉」).
/// Clickable, but never takes keyboard focus from the user's app.
/// </summary>
internal sealed class PillButton : LayeredWindow
{
    public enum Kind { Drink, Dismiss }

    private const float LogicalWidth = 340, LogicalHeight = 72, LogicalGap = 24;

    private readonly Kind _kind;
    private readonly string _text;
    private readonly FontFamily _font;
    private readonly float _scale;
    private bool _hover;
    private bool _pressed;

    public event EventHandler? Clicked;

    public Kind ButtonKind => _kind;

    private PillButton(Rectangle bounds, float scale, Kind kind, string text, FontFamily font)
        : base(bounds, clickThrough: false)
    {
        _scale = scale;
        _kind = kind;
        _text = text;
        _font = font;
        Cursor = Cursors.Hand;
    }

    /// <summary>Creates a row of buttons centred just below the cat, kept inside the work area.</summary>
    public static List<PillButton> CreateRow(Screen screen, Rectangle catBounds, FontFamily font,
        IReadOnlyList<(Kind Kind, string Text)> buttons)
    {
        var result = new List<PillButton>();
        if (buttons.Count == 0) return result;

        var work = screen.WorkingArea;
        float scale = Math.Max(0.5f, screen.Bounds.Height / 1080f);
        int w = (int)Math.Ceiling(LogicalWidth * scale);
        int h = (int)Math.Ceiling(LogicalHeight * scale);
        int gap = (int)(LogicalGap * scale);
        int total = buttons.Count * w + (buttons.Count - 1) * gap;
        int x = catBounds.Left + (catBounds.Width - total) / 2;
        int y = Math.Min(catBounds.Bottom + (int)(12 * scale), work.Bottom - h - (int)(16 * scale));

        foreach (var (kind, text) in buttons)
        {
            result.Add(new PillButton(new Rectangle(x, y, w, h), scale, kind, text, font));
            x += w + gap;
        }
        return result;
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

        bool drink = _kind == Kind.Drink;
        Color fill = drink
            ? (_hover ? Color.FromArgb(255, 70, 160, 245) : Color.FromArgb(255, 95, 175, 250))
            : (_hover ? Color.FromArgb(255, 255, 236, 222) : Color.FromArgb(240, 255, 252, 245));
        Color border = drink ? Color.FromArgb(255, 40, 120, 210) : Color.FromArgb(255, 255, 140, 90);
        Color ink = drink ? Color.White : Color.FromArgb(255, 110, 85, 75);

        using (var b = new SolidBrush(fill)) g.FillPath(b, pill);
        using (var pen = new Pen(border, 4f)) g.DrawPath(pen, pill);

        float cx = rect.Left + 40, cy = rect.Top + rect.Height / 2;
        if (drink)
        {
            // Water drop icon.
            using var drop = new GraphicsPath();
            drop.AddArc(cx - 12, cy - 6, 24, 24, -30, 240);
            drop.AddLine(cx - 10.4f, cy, cx, cy - 22);
            drop.CloseFigure();
            using var b = new SolidBrush(ink);
            g.FillPath(b, drop);
        }
        else
        {
            // "✕" drawn with lines so it doesn't depend on the font having the glyph.
            const float r = 11;
            using var x = new Pen(ink, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(x, cx - r, cy - r, cx + r, cy + r);
            g.DrawLine(x, cx - r, cy + r, cx + r, cy - r);
        }

        var textBox = new RectangleF(rect.Left + 66, rect.Top + 12, rect.Width - 66 - 28, rect.Height - 24);
        Drawing.DrawFittedText(g, _font, _text, FontStyle.Bold, 30, ink, textBox);
    }
}
