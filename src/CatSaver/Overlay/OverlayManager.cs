using System.Diagnostics;

namespace CatSaver.Overlay;

/// <summary>
/// Shows / hides the reminder overlay on the configured monitors: dimming layer,
/// cat, an invisible click target over the cat, and the 「我喝了！」/「先關掉」
/// buttons. Drives the animation and fades.
/// </summary>
internal sealed class OverlayManager : IDisposable
{
    private const double FadeSeconds = 0.6;
    /// <summary>How long the "好棒！" celebration stays before fading out.</summary>
    private const double CelebrateSeconds = 1.8;

    private readonly AppConfig _config;
    private readonly CatRenderer _renderer;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly List<CatWindow> _cats = new();
    private readonly List<DimForm> _dims = new();
    private readonly List<LayeredWindow> _extras = new(); // buttons + hit areas
    private readonly Stopwatch _clock = new();

    private DateTime _endsAt;
    private double? _closingStartedAt;
    private double? _closeAt;
    private DateTime? _failedEndsAt;
    private int _ticks;

    public OverlayManager(AppConfig config)
    {
        _config = config;
        _renderer = new CatRenderer(config);
        _timer = new System.Windows.Forms.Timer { Interval = 1000 / config.AnimationFps };
        _timer.Tick += (_, _) => Tick();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    /// <summary>Raised when the user clicks 「我在忙，先關掉」.</summary>
    public event EventHandler? DismissRequested;

    /// <summary>Raised when the user clicks 「我喝了！」.</summary>
    public event EventHandler? DrinkRequested;

    public bool IsShowing => _cats.Count > 0 && _closingStartedAt is null && _closeAt is null;

    private bool AcceptsClicks => _cats.Count > 0 && _closingStartedAt is null && _closeAt is null;

    /// <summary>Shows the overlay until <paramref name="endsAt"/> (local time).</summary>
    public void Show(DateTime endsAt)
    {
        _endsAt = endsAt;
        if (IsShowing) return;
        // If drawing failed for this reminder, don't retry every second.
        if (_failedEndsAt == endsAt) return;

        CloseWindows();
        try
        {
            CreateWindows();
        }
        catch (Exception ex)
        {
            Fail("Creating overlay windows", ex);
            return;
        }

        _clock.Restart();
        _ticks = 0;
        Tick();
        _timer.Start();
    }

    private void CreateWindows()
    {
        _renderer.ResetInteraction();

        IEnumerable<Screen> screens = _config.Monitors == "primary"
            ? new[] { Screen.PrimaryScreen ?? Screen.AllScreens[0] }
            : Screen.AllScreens;

        var buttonSpecs = new List<(PillButton.Kind, string)>();
        if (_config.ShowDrinkButton) buttonSpecs.Add((PillButton.Kind.Drink, _config.DrinkButtonText));
        if (_config.ShowDismissButton) buttonSpecs.Add((PillButton.Kind.Dismiss, _config.DismissButtonText));

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

            var hit = new CatHitArea(cat.CatBodyBounds);
            hit.Clicked += (_, _) => { if (AcceptsClicks) _renderer.Poke(_clock.Elapsed.TotalSeconds); };
            hit.Show();
            _extras.Add(hit);

            foreach (var button in PillButton.CreateRow(screen, cat.WindowBounds, _renderer.Font, buttonSpecs))
            {
                button.Clicked += (_, _) => OnButtonClicked(button);
                button.Show();
                _extras.Add(button);
            }
        }
    }

    private void OnButtonClicked(PillButton button)
    {
        if (!AcceptsClicks) return; // already closing / celebrating
        var handler = button.ButtonKind == PillButton.Kind.Drink ? DrinkRequested : DismissRequested;
        handler?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Extends or shortens the current reminder.</summary>
    public void UpdateEnd(DateTime endsAt) => _endsAt = endsAt;

    /// <summary>
    /// Shows a short "好棒！" message (the cat jumps with heart eyes), then fades out.
    /// </summary>
    public void Celebrate(string title, string sub)
    {
        if (!AcceptsClicks) return;
        double t = _clock.Elapsed.TotalSeconds;
        _renderer.Celebrate(t, title, sub);
        _closeAt = t + CelebrateSeconds;
    }

    /// <summary>Fades the overlay out and closes it (waits for a running celebration).</summary>
    public void Hide()
    {
        if (!IsShowing) return;
        _closingStartedAt = _clock.Elapsed.TotalSeconds;
    }

    private void Tick()
    {
        try
        {
            RenderTick();
        }
        catch (Exception ex)
        {
            Fail("Rendering overlay", ex);
        }
    }

    private void RenderTick()
    {
        double t = _clock.Elapsed.TotalSeconds;
        float alpha = (float)Math.Min(1.0, t / FadeSeconds);

        if (_closeAt is double closeAt && t >= closeAt && _closingStartedAt is null)
            _closingStartedAt = t;

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
        foreach (var extra in _extras)
            extra.RenderFrame(frame, alpha);

        // Every ~2 seconds make sure nothing has been raised above us.
        if (++_ticks % (_config.AnimationFps * 2) == 0)
        {
            foreach (var dim in _dims) dim.BringToTop();
            foreach (var cat in _cats) cat.BringToTop();
            foreach (var extra in _extras) extra.BringToTop();
        }
    }

    /// <summary>Logs the error and closes this reminder; the tray app keeps running.</summary>
    private void Fail(string context, Exception ex)
    {
        Log.Error(context, ex);
        _failedEndsAt = _endsAt;
        try { CloseWindows(); } catch (Exception closeEx) { Log.Error("Closing overlay after error", closeEx); }
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
        var windows = _extras.Cast<Form>().Concat(_cats).Concat(_dims).ToList();
        foreach (var w in windows) w.Close();
        foreach (var w in windows) w.Dispose();
        _extras.Clear();
        _cats.Clear();
        _dims.Clear();
        _closingStartedAt = null;
        _closeAt = null;
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
