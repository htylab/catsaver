namespace CatSaver.Overlay;

/// <summary>The animated cat + speech bubble, centred on a monitor. Always click-through.</summary>
internal sealed class CatWindow : LayeredWindow
{
    private readonly CatRenderer _renderer;
    private readonly float _scale;

    private CatWindow(Rectangle bounds, float scale, CatRenderer renderer)
        : base(bounds, clickThrough: true)
    {
        _renderer = renderer;
        _scale = scale;
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
        return new CatWindow(bounds, scale, renderer);
    }

    /// <summary>Where the cat window sits on screen (used to place the dismiss button).</summary>
    public Rectangle WindowBounds => ScreenBounds;

    protected override void Draw(Graphics g, in CatFrame frame)
    {
        g.ScaleTransform(_scale, _scale);
        _renderer.Draw(g, frame);
    }
}
