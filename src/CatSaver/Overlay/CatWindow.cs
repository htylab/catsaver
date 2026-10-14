namespace CatSaver.Overlay;

/// <summary>The animated cat + speech bubble, centred on a monitor. Always click-through.</summary>
internal sealed class CatWindow : LayeredWindow
{
    private readonly CatRenderer _renderer;
    private readonly float _scale;
    private readonly float _margin; // extra logical width on each side (wide windows)

    private CatWindow(Rectangle bounds, float scale, float margin, CatRenderer renderer)
        : base(bounds, clickThrough: true)
    {
        _renderer = renderer;
        _scale = scale;
        _margin = margin;
    }

    public static CatWindow Create(Screen screen, CatRenderer renderer, double catScale)
    {
        // Fit the logical canvas into ~70% of the screen height / 80% of the width.
        var area = screen.Bounds;
        float fit = Math.Min(area.Height * 0.70f / CatRenderer.CanvasHeight, area.Width * 0.80f / CatRenderer.CanvasWidth);
        float scale = Math.Max(0.1f, fit * (float)catScale);
        int w = (int)Math.Ceiling(CatRenderer.CanvasWidth * scale);
        int h = (int)Math.Ceiling(CatRenderer.CanvasHeight * scale);
        var bounds = new Rectangle(area.Left + (area.Width - w) / 2, area.Top + (area.Height - h) / 2, w, h);
        float margin = 0;
        if (renderer.WideStage)
        {
            // The cat walks across the whole screen: same height, full width.
            margin = (area.Width - w) / 2f / scale;
            bounds = new Rectangle(area.Left, bounds.Top, area.Width, h);
        }
        return new CatWindow(bounds, scale, margin, renderer);
    }

    /// <summary>Where the cat window sits on screen (used to place the buttons, which centre on it).</summary>
    public Rectangle WindowBounds => ScreenBounds;

    /// <summary>Screen rectangle around the cat's body (for <see cref="CatHitArea"/>).</summary>
    public Rectangle CatBodyBounds
    {
        get
        {
            var r = CatRenderer.CatBodyArea;
            return Rectangle.Round(new RectangleF(
                ScreenBounds.Left + (r.X + _margin) * _scale, ScreenBounds.Top + r.Y * _scale,
                r.Width * _scale, r.Height * _scale));
        }
    }

    protected override void Draw(Graphics g, in CatFrame frame)
    {
        g.ScaleTransform(_scale, _scale);
        g.TranslateTransform(_margin, 0);
        _renderer.Draw(g, frame with { SideMargin = _margin });
    }
}
