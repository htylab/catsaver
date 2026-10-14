using CatSaver.Overlay;

namespace CatSaver;

/// <summary>
/// Background app: a tray icon plus a once-per-second check of the schedule that
/// shows/hides the cat overlay.
/// </summary>
internal sealed class TrayApplication : ApplicationContext
{
    private AppConfig _config;
    private ReminderSchedule _schedule;
    private OverlayManager _overlay;
    private SettingsForm? _settingsForm;
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _scheduleTimer;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private readonly Icon _icon;

    private DateTime? _dismissedSlot;
    private DateTime? _manualUntil;
    private bool _paused;

    /// <summary>Set when the user picks "重新啟動" so Program can relaunch after exit.</summary>
    public bool RestartRequested { get; private set; }

    public TrayApplication(AppConfig config, bool showImmediately, bool showSettings, string? configError)
    {
        _config = config;
        _schedule = config.CreateSchedule();
        _overlay = CreateOverlay(config);
        _icon = LoadIcon();

        _pauseItem = new ToolStripMenuItem("暫停提醒", null, (_, _) => TogglePause()) { CheckOnClick = false };
        _autoStartItem = new ToolStripMenuItem("開機時自動啟動", null, (_, _) => ToggleAutoStart());

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("現在顯示貓咪", null, (_, _) => ShowNow()));
        menu.Items.Add(new ToolStripMenuItem("關閉這次提醒", null, (_, _) => DismissCurrent()));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripMenuItem("設定…", null, (_, _) => OpenSettings()));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("重新啟動", null, (_, _) => Restart()));
        menu.Items.Add(new ToolStripMenuItem("結束", null, (_, _) => ExitThread()));
        menu.Opening += (_, _) =>
        {
            _autoStartItem.Checked = SafeIsAutoStartEnabled();
            _pauseItem.Checked = _paused;
        };

        _tray = new NotifyIcon
        {
            Icon = _icon,
            Text = "CatSaver",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

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

    private OverlayManager CreateOverlay(AppConfig config)
    {
        var overlay = new OverlayManager(config);
        overlay.DismissRequested += (_, _) => DismissCurrent();
        return overlay;
    }

    /// <summary>The settings currently in effect (a copy, safe to edit).</summary>
    public AppConfig CurrentConfig => _config.Clone();

    /// <summary>
    /// Switches to new settings immediately (no restart). If a reminder is on screen
    /// it is rebuilt with the new look / timing.
    /// </summary>
    public void ApplyConfig(AppConfig config)
    {
        config.Normalize();
        _config = config;
        _schedule = config.CreateSchedule();
        _overlay.Dispose();
        _overlay = CreateOverlay(config);
        if (_manualUntil is not null) _manualUntil = DateTime.Now.AddMinutes(config.DurationMinutes);
        CheckSchedule();
    }

    /// <summary>Shows the cat right now for the configured duration.</summary>
    public void ShowNow()
    {
        _manualUntil = DateTime.Now.AddMinutes(_config.DurationMinutes);
        CheckSchedule();
    }

    /// <summary>Ends a "show now" preview (scheduled reminders are unaffected).</summary>
    public void StopPreview()
    {
        _manualUntil = null;
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

    private void CheckSchedule()
    {
        var now = DateTime.Now;
        DateTime? until = null;

        if (_manualUntil is DateTime manual && now < manual)
        {
            until = manual;
        }
        else
        {
            _manualUntil = null;
            if (!_paused && _schedule.ActiveUntil(now) is DateTime end && _dismissedSlot != _schedule.SlotStart(now))
                until = end;
        }

        if (until is DateTime u)
        {
            if (_overlay.IsShowing) _overlay.UpdateEnd(u);
            else _overlay.Show(u);
        }
        else if (_overlay.IsShowing)
        {
            _overlay.Hide();
        }

        UpdateTooltip(now);
    }

    private void UpdateTooltip(DateTime now)
    {
        string text = _paused
            ? "CatSaver – 已暫停"
            : $"CatSaver – 下次提醒 {_schedule.NextStart(now):HH:mm}";
        // NotifyIcon.Text is limited to 127 characters.
        if (_tray.Text != text) _tray.Text = text.Length > 127 ? text[..127] : text;
    }

    /// <summary>Hides the current reminder; the next one comes at the next slot as usual.</summary>
    public void DismissCurrent()
    {
        var now = DateTime.Now;
        _manualUntil = null;
        _dismissedSlot = _schedule.SlotStart(now);
        CheckSchedule();
    }

    private void TogglePause()
    {
        _paused = !_paused;
        if (_paused) _manualUntil = null;
        CheckSchedule();
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
