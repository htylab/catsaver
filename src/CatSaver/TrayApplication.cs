using System.Diagnostics;
using CatSaver.Overlay;

namespace CatSaver;

/// <summary>
/// Background app: a tray icon plus a once-per-second check of the schedule that
/// shows/hides the cat overlay.
/// </summary>
internal sealed class TrayApplication : ApplicationContext
{
    private readonly AppConfig _config;
    private readonly ReminderSchedule _schedule;
    private readonly OverlayManager _overlay;
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

    public TrayApplication(AppConfig config, bool showImmediately, string? configError)
    {
        _config = config;
        _schedule = config.CreateSchedule();
        _overlay = new OverlayManager(config);
        _icon = LoadIcon();

        _pauseItem = new ToolStripMenuItem("暫停提醒", null, (_, _) => TogglePause()) { CheckOnClick = false };
        _autoStartItem = new ToolStripMenuItem("開機時自動啟動", null, (_, _) => ToggleAutoStart());

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("現在顯示貓咪", null, (_, _) => ShowNow()));
        menu.Items.Add(new ToolStripMenuItem("關閉這次提醒", null, (_, _) => DismissCurrent()));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripMenuItem("開啟設定檔…", null, (_, _) => OpenConfig()));
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
        _tray.DoubleClick += (_, _) => ShowNow();

        _scheduleTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _scheduleTimer.Tick += (_, _) => CheckSchedule();
        _scheduleTimer.Start();

        if (configError is not null)
            _tray.ShowBalloonTip(8000, "CatSaver 設定錯誤", configError, ToolTipIcon.Warning);
        else if (!showImmediately)
            _tray.ShowBalloonTip(4000, "CatSaver 在背景執行中 🐱",
                $"每 {_config.IntervalMinutes} 分鐘提醒你起來喝水。右鍵點選系統匣圖示可以暫停或結束。",
                ToolTipIcon.Info);

        if (showImmediately) ShowNow();
        CheckSchedule();
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

    private void ShowNow()
    {
        _manualUntil = DateTime.Now.AddMinutes(_config.DurationMinutes);
        CheckSchedule();
    }

    private void DismissCurrent()
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

    private static bool SafeIsAutoStartEnabled()
    {
        try { return AutoStart.IsEnabled; }
        catch { return false; }
    }

    private void OpenConfig()
    {
        try
        {
            if (!File.Exists(AppConfig.ConfigPath)) _config.Save();
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{AppConfig.ConfigPath}\"") { UseShellExecute = true });
            _tray.ShowBalloonTip(4000, "CatSaver", "修改設定並存檔後，請從選單選「重新啟動」套用。", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "CatSaver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
