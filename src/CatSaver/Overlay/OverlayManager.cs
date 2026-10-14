using System.Diagnostics;

namespace CatSaver.Overlay;

/// <summary>
/// Shows / hides the reminder overlay (dim layer + cat) on the configured monitors
/// and drives the animation.
/// </summary>
internal sealed class OverlayManager : IDisposable
{
    private const double FadeSeconds = 0.6;

    private readonly AppConfig _config;
    private readonly CatRenderer _renderer;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly List<CatWindow> _cats = new();
    private readonly List<DimForm> _dims = new();
    private readonly List<DismissButton> _buttons = new();
    private readonly Stopwatch _clock = new();

    private DateTime _endsAt;
    private double? _closingStartedAt;
    private int _ticks;

    public OverlayManager(AppConfig config)
    {
        _config = config;
        _renderer = new CatRenderer(config);
        _timer = new System.Windows.Forms.Timer { Interval = 1000 / config.AnimationFps };
        _timer.Tick += (_, _) => Tick();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>Raised when the user clicks the "我在忙，先關掉" button.</summary>
    public event EventHandler? DismissRequested;

    public bool IsShowing => _cats.Count > 0 && _closingStartedAt is null;

    /// <summary>Shows the overlay until <paramref name="endsAt"/> (local time).</summary>
    public void Show(DateTime endsAt)
    {
        _endsAt = endsAt;
        if (IsShowing) return;

        CloseWindows();
        _closingStartedAt = null;

        IEnumerable<Screen> screens = _config.Monitors == "primary"
            ? new[] { Screen.PrimaryScreen ?? Screen.AllScreens[0] }
            : Screen.AllScreens;

        foreach (var screen in screens)
        {
            if (_config.DimScreen && _config.DimOpacity > 0)
            {
                var dim = new DimForm(screen, _config.ClickThrough);
                dim.Show();
                _dims.Add(dim);
            }
            var cat = CatWindow.Create(screen, _renderer, _config.CatScale);
            cat.Show();
            _cats.Add(cat);

            if (_config.ShowDismissButton)
            {
                var button = DismissButton.Create(screen, cat.WindowBounds, _config.DismissButtonText, _renderer.Font);
                button.Clicked += (_, _) => DismissRequested?.Invoke(this, EventArgs.Empty);
                button.Show();
                _buttons.Add(button);
            }
        }

        _clock.Restart();
        _ticks = 0;
        Tick();
        _timer.Start();
    }

    /// <summary>Extends or shortens the current reminder.</summary>
    public void UpdateEnd(DateTime endsAt) => _endsAt = endsAt;

    /// <summary>Fades the overlay out and closes it.</summary>
    public void Hide()
    {
        if (!IsShowing) return;
        _closingStartedAt = _clock.Elapsed.TotalSeconds;
    }

    private void Tick()
    {
        double t = _clock.Elapsed.TotalSeconds;
        float alpha = (float)Math.Min(1.0, t / FadeSeconds);

        if (_closingStartedAt is double closing)
        {
            double k = (t - closing) / FadeSeconds;
            if (k >= 1)
            {
                CloseWindows();
                return;
            }
            alpha = Math.Min(alpha, (float)(1 - k));
        }

        var remaining = _endsAt - DateTime.Now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        var frame = new CatFrame(t, remaining);

        foreach (var dim in _dims)
            dim.Opacity = _config.DimOpacity * alpha;
        foreach (var cat in _cats)
            cat.RenderFrame(frame, alpha);
        foreach (var button in _buttons)
            button.RenderFrame(frame, alpha);

        // Every ~2 seconds make sure nothing has been raised above us.
        if (++_ticks % (_config.AnimationFps * 2) == 0)
        {
            foreach (var dim in _dims) dim.BringToTop();
            foreach (var cat in _cats) cat.BringToTop();
            foreach (var button in _buttons) button.BringToTop();
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        // Monitors were added/removed or resolution changed: rebuild the windows.
        if (!IsShowing) return;
        var end = _endsAt;
        CloseWindows();
        Show(end);
    }

    private void CloseWindows()
    {
        _timer.Stop();
        var windows = _buttons.Cast<Form>().Concat(_cats).Concat(_dims).ToList();
        foreach (var w in windows) w.Close();
        foreach (var w in windows) w.Dispose();
        _buttons.Clear();
        _cats.Clear();
        _dims.Clear();
        _closingStartedAt = null;
        _renderer.ReleaseResources();
    }

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        CloseWindows();
        _timer.Dispose();
        _renderer.Dispose();
    }
}
