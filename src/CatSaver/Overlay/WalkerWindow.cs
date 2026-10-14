namespace CatSaver.Overlay;

/// <summary>
/// A real cat strolling back and forth along the bottom of the screen (on top of
/// the taskbar): it walks in from the left, off the right edge, turns around and
/// comes back. Click-through; the walk clip is mirrored when heading left.
/// </summary>
internal sealed class WalkerWindow : LayeredWindow
{
    private readonly SpritePlayer _player;
    private readonly Rectangle _work;
    private readonly int _width, _height;
    private readonly double _speed;     // px per second
    private readonly double _startPhase;
    private bool _headingLeft;

    private WalkerWindow(Rectangle bounds, Rectangle work, SpritePlayer player, double speed, double startPhase)
        : base(bounds, clickThrough: true)
    {
        _player = player;
        _work = work;
        _width = bounds.Width;
        _height = bounds.Height;
        _speed = speed;
        _startPhase = startPhase;
    }

    public static WalkerWindow Create(Screen screen, SpriteCatalog catalog, SpriteCatalog.SheetInfo sheet, double startPhase)
    {
        var work = screen.WorkingArea;
        int h = Math.Max(60, (int)(screen.Bounds.Height * 0.16));
        int w = (int)Math.Round(h * (double)sheet.FrameWidth / sheet.FrameHeight);
        var bounds = new Rectangle(work.Left - w, work.Bottom - h, w, h);
        double speed = Math.Max(0.1, sheet.Speed) * w;
        return new WalkerWindow(bounds, work, new SpritePlayer(catalog, new[] { sheet.Name }), speed, startPhase);
    }

    /// <summary>Moves the cat for overlay time <paramref name="t"/> (seconds).</summary>
    public void Update(double t)
    {
        // Out of the left edge → off the right edge, then back again.
        double distance = _work.Width + _width;
        double p = (_startPhase * distance + t * _speed) % (2 * distance);
        _headingLeft = p >= distance;
        double x = _headingLeft ? _work.Right - (p - distance) : _work.Left - _width + p;
        MoveTo(new Point((int)Math.Round(x), _work.Bottom - _height));
    }

    protected override void Draw(Graphics g, in CatFrame frame) =>
        _player.Draw(g, frame.Time, new RectangleF(0, 0, _width, _height), mirror: _headingLeft);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _player.Dispose();
        base.Dispose(disposing);
    }
}
