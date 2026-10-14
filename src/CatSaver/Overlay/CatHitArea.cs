namespace CatSaver.Overlay;

/// <summary>
/// An invisible, clickable ellipse over the cat's body. The cat window itself is
/// click-through, so only clicks on the cat (not the bubble or the space around
/// it) are caught. Pixels with alpha 1/255 are invisible but still hit-testable.
/// </summary>
internal sealed class CatHitArea : LayeredWindow
{
    public event EventHandler? Clicked;

    public CatHitArea(Rectangle bounds) : base(bounds, clickThrough: false)
    {
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left) Clicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void Draw(Graphics g, in CatFrame frame)
    {
        using var brush = new SolidBrush(Color.FromArgb(1, 0, 0, 0));
        g.FillEllipse(brush, 0, 0, ScreenBounds.Width - 1, ScreenBounds.Height - 1);
    }
}
