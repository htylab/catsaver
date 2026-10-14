using CatSaver.Overlay;

namespace CatSaver;

/// <summary>
/// Background app: a tray icon plus a once-per-second check (via
/// <see cref="ReminderState"/>) that shows/hides the cat overlay.
/// </summary>
internal sealed class TrayApplication : ApplicationContext
{
    private AppConfig _config;
    private OverlayManager _overlay;
    private SettingsForm? _settingsForm;
    private readonly ReminderState _state;
    private readonly WaterLog _water;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _scheduleTimer;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _waterItem;
    private readonly ToolStripMenuItem _resumeItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly Icon _icon;
    private readonly SynchronizationContext _ui;
    private RegisteredWaitHandle? _activationWait;

    /// <summary>Set when the user picks "重新啟動" so Program can relaunch after exit.</summary>
    public bool RestartRequested { get; private set; }

    /// <param name="activateSignal">Set by a second launch of the exe; we respond by opening the settings.</param>
    public TrayApplication(AppConfig config, bool showImmediately, bool showSettings, string? configError,
        EventWaitHandle? activateSignal)
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _config = config;
        _state = new ReminderState(config.CreateSchedule()) { PostponeWhenBusy = config.PostponeWhenBusy };
        _water = WaterLog.Load();
        _overlay = CreateOverlay(config);
        _icon = LoadIcon();

        _statusItem = new ToolStripMenuItem { Enabled = false };
        _waterItem = new ToolStripMenuItem { Enabled = false };
        _resumeItem = new ToolStripMenuItem("恢復提醒", null, (_, _) => Resume());
        _autoStartItem = new ToolStripMenuItem("開機時自動啟動", null, (_, _) => ToggleAutoStart());

        var pauseItem = new ToolStripMenuItem("暫停提醒");
        pauseItem.DropDownItems.Add(new ToolStripMenuItem("1 小時", null, (_, _) => Pause(DateTime.Now.AddHours(1))));
        pauseItem.DropDownItems.Add(new ToolStripMenuItem("2 小時", null, (_, _) => Pause(DateTime.Now.AddHours(2))));
        pauseItem.DropDownItems.Add(new ToolStripMenuItem("今天都不要提醒", null, (_, _) => Pause(DateTime.Today.AddDays(1))));

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(_waterItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("現在顯示貓咪", null, (_, _) => ShowNow()));
        menu.Items.Add(new ToolStripMenuItem("關閉這次提醒", null, (_, _) => DismissCurrent()));
        menu.Items.Add(new ToolStripMenuItem("我喝了一杯水（+1）", null, (_, _) => Drink()));
        menu.Items.Add(pauseItem);
        menu.Items.Add(_resumeItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("設定…", null, (_, _) => OpenSettings()));
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("重新啟動", null, (_, _) => Restart()));
        menu.Items.Add(new ToolStripMenuItem("結束", null, (_, _) => ExitThread()));
        menu.Opening += (_, _) =>
        {
            var now = DateTime.Now;
            _statusItem.Text = StatusText(now);
            _waterItem.Text = $"今天喝了 {_water.CupsOn(now)} 杯水";
            _resumeItem.Visible = _state.IsPaused(now);
            _autoStartItem.Checked = SafeIsAutoStartEnabled();
        };

        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = "CatSaver",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

        if (activateSignal is not null)
        {
            _activationWait = ThreadPool.RegisterWaitForSingleObject(activateSignal,
                (_, _) => _ui.Post(_ => OpenSettings(), null), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        _scheduleTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _scheduleTimer.Tick += (_, _) => CheckSchedule();
        _scheduleTimer.Start();

        if (showSettings) OpenSettings();

        if (configError is not null)
            _tray.ShowBalloonTip(8000, "CatSaver 設定錯誤", configError, ToolTipIcon.Warning);
        else if (!showImmediately)
            _tray.ShowBalloonTip(4000, "CatSaver 在背景執行中 🐱",
                $"每 {_config.IntervalMinutes} 分鐘提醒你起來喝水。雙擊系統匣圖示可開啟設定，右鍵可暫停或結束。",
                ToolTipIcon.Info);

        if (showImmediately) ShowNow();
        CheckSchedule();
    }

    private OverlayManager CreateOverlay(AppConfig config, bool preview = false)
    {
        var overlay = new OverlayManager(config, preview);
        overlay.DismissRequested += (_, _) => DismissCurrent();
        overlay.DrinkRequested += (_, _) => Drink();
        return overlay;
    }

    // ------------------------------------------------------------ schedule

    private void CheckSchedule()
    {
        var now = DateTime.Now;
        bool showing = _overlay.IsShowing;
        // Only ask Windows when it matters: before a reminder would pop up.
        bool busy = !showing && _config.PostponeWhenBusy && UserActivity.IsBusy();
        var until = _state.Evaluate(now, showing, busy);

        if (until is DateTime u)
        {
            if (showing) _overlay.UpdateEnd(u);
            else _overlay.Show(u);
        }
        else if (showing)
        {
            _overlay.Hide();
        }

        UpdateTooltip(now);
    }

    private string StatusText(DateTime now)
    {
        if (_state.PausedUntil is DateTime p && now < p)
            return p >= now.Date.AddDays(1) ? "今天不再提醒（已暫停）" : $"已暫停到 {p:HH:mm}";
        if (_overlay.IsShowing)
            return "提醒中：起來喝水吧！";
        if (_state.IsDeferred)
            return "等你忙完（簡報/全螢幕）再提醒";
        return $"下次提醒 {_state.Schedule.NextStart(now):HH:mm}";
    }

    private void UpdateTooltip(DateTime now)
    {
        string text = $"CatSaver – {StatusText(now)}\n今天喝了 {_water.CupsOn(now)} 杯水";
        // NotifyIcon.Text is limited to 127 characters.
        if (text.Length > 127) text = text[..127];
        if (_tray.Text != text) _tray.Text = text;
    }

    // ------------------------------------------------------------- actions

    /// <summary>The settings currently in effect (a copy, safe to edit).</summary>
    public AppConfig CurrentConfig => _config.Clone();

    /// <summary>
    /// Switches to new settings immediately (no restart). If a reminder is on screen
    /// it is rebuilt with the new look / timing.
    /// </summary>
    public void ApplyConfig(AppConfig config)
    {
        config.Normalize();
        var now = DateTime.Now;
        bool manual = _state.IsManual(now);
        _config = config;
        _state.Schedule = config.CreateSchedule();
        _state.PostponeWhenBusy = config.PostponeWhenBusy;
        _overlay.Dispose();
        _overlay = CreateOverlay(config);
        if (manual) _state.ShowNow(now);
        CheckSchedule();
    }

    /// <summary>Shows the cat right now for the configured duration.</summary>
    public void ShowNow()
    {
        _state.ShowNow(DateTime.Now);
        CheckSchedule();
    }

    /// <summary>
    /// Settings preview: shows the cat with <paramref name="config"/> (not saved) in a
    /// non-blocking way (no dimming or buttons, click-through) until <see cref="EndPreview"/>.
    /// </summary>
    public void StartPreview(AppConfig config)
    {
        config.Normalize();
        _config = config;
        _state.Schedule = config.CreateSchedule();
        _state.PostponeWhenBusy = config.PostponeWhenBusy;
        _overlay.Dispose();
        _overlay = CreateOverlay(config, preview: true);
        _state.ShowUntil(DateTime.Now.AddHours(1));
        CheckSchedule();
    }

    /// <summary>Closes the settings preview and switches to <paramref name="config"/> (saved or original).</summary>
    public void EndPreview(AppConfig config)
    {
        _state.StopManual();
        ApplyConfig(config); // rebuilds a normal overlay; a scheduled reminder shows again if due
    }

    /// <summary>Hides the current reminder; the next one comes at the next slot as usual.</summary>
    public void DismissCurrent()
    {
        _state.Dismiss(DateTime.Now);
        CheckSchedule();
    }

    /// <summary>
    /// Records a cup of water. If the cat is up it celebrates but stays until the
    /// reminder ends (only 「我在忙，先關掉」 closes it early).
    /// </summary>
    private void Drink()
    {
        var now = DateTime.Now;
        int cups = _water.AddCup(now);
        if (_overlay.IsShowing)
            _overlay.Celebrate("好棒！", $"今天第 {cups} 杯水，再休息一下吧～", $"已記錄第 {cups} 杯");
        else
            _tray.ShowBalloonTip(3000, "CatSaver", $"記錄好了！今天第 {cups} 杯水 💧", ToolTipIcon.Info);
        UpdateTooltip(now);
    }

    private void Pause(DateTime until)
    {
        _state.Pause(until);
        CheckSchedule();
    }

    private void Resume()
    {
        _state.Resume();
        CheckSchedule();
    }

    public void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.WindowState = FormWindowState.Normal;
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(this, _icon);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void ToggleAutoStart()
    {
        try
        {
            AutoStart.Set(!SafeIsAutoStartEnabled());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"無法變更開機自動啟動設定：\n{ex.Message}", "CatSaver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal static bool SafeIsAutoStartEnabled()
    {
        try { return AutoStart.IsEnabled; }
        catch { return false; }
    }

    private void Restart()
    {
        RestartRequested = true;
        ExitThread();
    }

    private static Icon LoadIcon()
    {
        using var stream = typeof(TrayApplication).Assembly.GetManifestResourceStream("CatSaver.catsaver.ico");
        return stream is not null ? new Icon(stream, SystemInformation.SmallIconSize) : SystemIcons.Application;
    }

    protected override void ExitThreadCore()
    {
        _scheduleTimer.Stop();
        _activationWait?.Unregister(null);
        _activationWait = null;
        _settingsForm?.Close();
        _tray.Visible = false;
        _overlay.Dispose();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scheduleTimer.Dispose();
            _tray.Dispose();
            _icon.Dispose();
        }
        base.Dispose(disposing);
    }
}
