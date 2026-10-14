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

    private readonly AppConfig _config;
    private readonly CatRenderer _renderer;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly List<CatWindow> _cats = new();
    private readonly List<DimForm> _dims = new();
    private readonly List<LayeredWindow> _extras = new(); // buttons + hit areas
    private readonly List<ButtonRow> _rows = new();

    /// <summary>
    /// The buttons under one cat. The dismiss button appears only after
    /// <see cref="AppConfig.DismissButtonDelaySeconds"/>; until then the drink button
    /// sits alone in the middle, then slides aside to make room.
    /// </summary>
    private sealed class ButtonRow
    {
        public PillButton? Drink;
        public PillButton? Dismiss;
        public Point Solo;      // centred position while a button is alone
        public Point DrinkFinal;
        public bool DismissShown;
    }

    private const double ButtonSlideSeconds = 0.35;
    private readonly Stopwatch _clock = new();

    private DateTime _endsAt;
    private double? _closingStartedAt;
    private bool _drinkRecorded;
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

    public bool IsShowing => _cats.Count > 0 && _closingStartedAt is null;

    private bool AcceptsClicks => IsShowing;

    /// <summary>True once 「我喝了！」 was used during the reminder on screen.</summary>
    public bool DrinkRecorded => _drinkRecorded;

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
        _drinkRecorded = false;

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

            // Real-cat sets: a second cat strolls along the bottom (below the buttons in z-order).
            if (_renderer.WalkerSheet is { } walkerSheet && _renderer.Catalog is { } catalog)
            {
                var walker = WalkerWindow.Create(screen, catalog, walkerSheet, startPhase: 0);
                walker.Show();
                _extras.Add(walker);
            }

            var hit = new CatHitArea(cat.CatBodyBounds);
            hit.Clicked += (_, _) => { if (AcceptsClicks) _renderer.Poke(_clock.Elapsed.TotalSeconds); };
            hit.Show();
            _extras.Add(hit);

            var row = new ButtonRow();
            foreach (var button in PillButton.CreateRow(screen, cat.WindowBounds, _renderer.Font, buttonSpecs, out row.Solo))
            {
                button.Clicked += (_, _) => OnButtonClicked(button);
                if (button.ButtonKind == PillButton.Kind.Drink)
                {
                    row.Drink = button;
                    row.DrinkFinal = button.ScreenLocation;
                    button.MoveTo(row.Solo);
                    button.Show();
                }
                else
                {
                    row.Dismiss = button; // shown later, see UpdateButtons
                    if (row.Drink is null) button.MoveTo(row.Solo);
                }
                _extras.Add(button);
            }
            _rows.Add(row);
        }
    }

    private void OnButtonClicked(PillButton button)
    {
        if (!AcceptsClicks) return; // already closing
        if (button.ButtonKind == PillButton.Kind.Drink && _drinkRecorded) return;
        var handler = button.ButtonKind == PillButton.Kind.Drink ? DrinkRequested : DismissRequested;
        handler?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Extends or shortens the current reminder.</summary>
    public void UpdateEnd(DateTime endsAt) => _endsAt = endsAt;

    /// <summary>
    /// After 「我喝了！」: the cat jumps with heart eyes, the bubble says
    /// <paramref name="title"/>/<paramref name="sub"/> for a few seconds, and the drink
    /// button turns into <paramref name="badge"/>. The reminder stays until its time is up.
    /// </summary>
    public void Celebrate(string title, string sub, string badge)
    {
        if (!AcceptsClicks) return;
        _drinkRecorded = true;
        _renderer.Celebrate(_clock.Elapsed.TotalSeconds, title, sub);
        foreach (var button in _extras.OfType<PillButton>().Where(b => b.ButtonKind == PillButton.Kind.Drink))
            button.MarkDone(badge);
    }

    /// <summary>Fades the overlay out and closes it.</summary>
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

        float dismissAlpha = UpdateButtons(t);

        var remaining = _endsAt - DateTime.Now;
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        var frame = new CatFrame(t, remaining);

        foreach (var dim in _dims)
            dim.Opacity = _config.DimOpacity * alpha;
        foreach (var cat in _cats)
            cat.RenderFrame(frame, alpha);
        foreach (var extra in _extras)
        {
            if (extra is WalkerWindow walker) walker.Update(t);
            if (!extra.Visible) continue; // dismiss button not shown yet
            bool dismiss = extra is PillButton { ButtonKind: PillButton.Kind.Dismiss };
            extra.RenderFrame(frame, dismiss ? alpha * dismissAlpha : alpha);
        }

        // Every ~2 seconds make sure nothing has been raised above us.
        if (++_ticks % (_config.AnimationFps * 2) == 0)
        {
            foreach (var dim in _dims) dim.BringToTop();
            foreach (var cat in _cats) cat.BringToTop();
            foreach (var extra in _extras) extra.BringToTop();
        }
    }

    /// <summary>
    /// Shows the dismiss button once its delay has passed (sliding the drink button
    /// aside). Returns the dismiss button's fade-in factor.
    /// </summary>
    private float UpdateButtons(double t)
    {
        double since = t - _config.DismissButtonDelaySeconds;
        if (since < 0) return 0;

        float slide = (float)Math.Clamp(since / ButtonSlideSeconds, 0, 1);
        slide = 1 - (1 - slide) * (1 - slide); // ease out
        foreach (var row in _rows)
        {
            if (row.Dismiss is null) continue;
            if (!row.DismissShown)
            {
                row.Dismiss.Show();
                row.DismissShown = true;
            }
            if (row.Drink is not null)
            {
                int x = (int)Math.Round(row.Solo.X + (row.DrinkFinal.X - row.Solo.X) * slide);
                row.Drink.MoveTo(new Point(x, row.DrinkFinal.Y));
            }
        }
        return (float)Math.Clamp(since / 0.4, 0, 1);
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
        _rows.Clear();
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
