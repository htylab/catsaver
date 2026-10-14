using System.Diagnostics;

namespace CatSaver;

/// <summary>
/// Settings window. While it is open the cat is previewed live (without blocking
/// the screen) and follows every change; closing the window ends the preview.
/// "儲存" writes config.json and applies it immediately; "取消" keeps the old settings.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly TrayApplication _app;
    private readonly AppConfig _original;
    private AppConfig? _saved;
    private readonly System.Windows.Forms.Timer _previewDebounce = new() { Interval = 400 };

    private readonly NumericUpDown _interval = new() { Minimum = 1, Maximum = 24 * 60, Width = 80 };
    private readonly NumericUpDown _duration = new() { Minimum = 1, Maximum = 60, Width = 80 };
    private readonly TextBox _message = new() { Width = 300 };
    private readonly TextBox _subMessage = new() { Width = 300 };
    private readonly CheckBox _countdown = new() { Text = "在對話框顯示倒數計時", AutoSize = true };
    private readonly CheckBox _drinkButton = new() { Text = "「我喝了！」（記錄杯數）", AutoSize = true };
    private readonly TextBox _drinkText = new() { Width = 150 };
    private readonly CheckBox _postpone = new() { Text = "簡報、全螢幕影片或遊戲時先不跳出，結束後再提醒", AutoSize = true };
    private readonly CheckBox _dismissButton = new() { Text = "「先關掉」", AutoSize = true };
    private readonly TextBox _dismissText = new() { Width = 150 };
    private readonly NumericUpDown _dismissDelay = new() { Minimum = 30, Maximum = 600, Width = 60 };
    private readonly ComboBox _style = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly List<(string Id, string Label)> _styles = new();
    private string SelectedStyle => _style.SelectedIndex >= 0 ? _styles[_style.SelectedIndex].Id : "random";
    private readonly ComboBox _monitors = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly CheckBox _dim = new() { Text = "讓背景變暗", AutoSize = true };
    private readonly TrackBar _dimOpacity = new() { Minimum = 0, Maximum = 90, TickFrequency = 10, Width = 220, AutoSize = false, Height = 30, TickStyle = TickStyle.None };
    private readonly Label _dimValue = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox _clickThrough = new() { Text = "滑鼠可以點到後面的視窗（建議開啟）", AutoSize = true };
    private readonly TrackBar _catScale = new() { Minimum = 20, Maximum = 200, TickFrequency = 20, Width = 220, AutoSize = false, Height = 30, TickStyle = TickStyle.None };
    private readonly Label _catScaleValue = new() { AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Button _catColor = new() { Width = 80, Height = 26, FlatStyle = FlatStyle.Flat };
    private readonly CheckBox _autoStart = new() { Text = "開機時自動啟動 CatSaver", AutoSize = true };

    public SettingsForm(TrayApplication app, Icon icon)
    {
        _app = app;
        _original = app.CurrentConfig;

        Text = "CatSaver 設定";
        Icon = icon;
        Font = new Font(Overlay.Drawing.ResolveFont(_original.FontFamily), 10f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        // Left side of the screen, above the previewed cat.
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ShowInTaskbar = true;

        _monitors.Items.AddRange(new object[] { "所有螢幕", "只有主螢幕" });
        _styles.Add(("random", "隨機（每次換一種）"));
        foreach (var set in Overlay.SpriteCatalog.Instance?.Sets ?? (IReadOnlyList<Overlay.SpriteCatalog.CatSet>)Array.Empty<Overlay.SpriteCatalog.CatSet>())
            _styles.Add((set.Id, set.Name));
        _styles.Add(("classic", "手繪貓（揮手拿水杯）"));
        _style.Items.AddRange(_styles.Select(s => (object)s.Label).ToArray());
        // Fur colour only applies to the hand-drawn cat.
        _style.SelectedIndexChanged += (_, _) => _catColor.Enabled = SelectedStyle == "classic";
        _interval.ValueChanged += (_, _) => _duration.Maximum = _interval.Value;
        _dim.CheckedChanged += (_, _) => _dimOpacity.Enabled = _dim.Checked;
        _dismissButton.CheckedChanged += (_, _) => _dismissText.Enabled = _dismissDelay.Enabled = _dismissButton.Checked;
        _drinkButton.CheckedChanged += (_, _) => _drinkText.Enabled = _drinkButton.Checked;
        _dimOpacity.ValueChanged += (_, _) => _dimValue.Text = $"{_dimOpacity.Value}%";
        _catScale.ValueChanged += (_, _) => _catScaleValue.Text = $"{_catScale.Value}%";
        _catColor.Click += (_, _) => PickColor();

        BuildLayout();
        LoadFrom(_original);
        _autoStart.Checked = TrayApplication.SafeIsAutoStartEnabled();

        // Live preview: refresh shortly after anything that changes the look.
        _previewDebounce.Tick += (_, _) => { _previewDebounce.Stop(); Preview(); };
        void Changed(object? _, EventArgs __) { _previewDebounce.Stop(); _previewDebounce.Start(); }
        foreach (var c in new Control[] { _message, _subMessage, _drinkText, _dismissText })
            c.TextChanged += Changed;
        foreach (var c in new[] { _countdown, _drinkButton, _dismissButton })
            c.CheckedChanged += Changed;
        _style.SelectedIndexChanged += Changed;
        _monitors.SelectedIndexChanged += Changed;
        _catScale.ValueChanged += Changed;
        _catColor.BackColorChanged += Changed;
    }

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(16, 12, 16, 8),
            Dock = DockStyle.Fill,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        void Section(string title)
        {
            var label = new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                ForeColor = Color.FromArgb(220, 100, 50),
                Margin = new Padding(0, grid.RowCount == 0 ? 0 : 14, 0, 4),
            };
            grid.Controls.Add(label, 0, grid.RowCount);
            grid.SetColumnSpan(label, 2);
            grid.RowCount++;
        }

        void Row(string? caption, params Control[] controls)
        {
            if (caption is not null)
                grid.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) }, 0, grid.RowCount);

            Control content;
            if (controls.Length == 1)
            {
                content = controls[0];
            }
            else
            {
                var flow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
                foreach (var c in controls)
                {
                    c.Anchor = AnchorStyles.Left;
                    flow.Controls.Add(c);
                }
                content = flow;
            }
            content.Anchor = AnchorStyles.Left;
            grid.Controls.Add(content, 1, grid.RowCount);
            if (caption is null)
            {
                grid.SetColumn(content, 0);
                grid.SetColumnSpan(content, 2);
            }
            grid.RowCount++;
        }

        Label Hint(string text) => new() { Text = text, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(4, 6, 0, 0) };

        Section("提醒時間");
        Row("每隔", _interval, Hint("分鐘提醒一次（60 = 每個整點）"));
        Row("貓咪停留", _duration, Hint("分鐘後自動消失"));
        Row(null, _postpone);

        Section("文字");
        Row("主訊息", _message);
        Row("副訊息", _subMessage);
        Row(null, _countdown);

        Section("貓咪下方的按鈕");
        Row("顯示", _drinkButton, _drinkText);
        Row("顯示", _dismissButton, _dismissText, _dismissDelay, Hint("秒後才出現"));

        Section("外觀");
        Row("貓咪樣式", _style);
        Row("顯示在", _monitors);
        Row("背景", _dim, _dimOpacity, _dimValue);
        Row(null, _clickThrough);
        Row("貓咪大小", _catScale, _catScaleValue);
        Row("毛色", _catColor, Hint("（手繪貓）"));

        Section("其他");
        var openFolder = new LinkLabel { Text = "開啟設定檔資料夾", AutoSize = true, Margin = new Padding(16, 6, 0, 0) };
        openFolder.LinkClicked += (_, _) => OpenConfigFolder();
        Row(null, _autoStart, openFolder);

        // Buttons
        var preview = new Button { Text = "換一隻貓", AutoSize = true };
        var defaults = new Button { Text = "還原預設值", AutoSize = true };
        var save = new Button { Text = "儲存", AutoSize = true, DialogResult = DialogResult.None };
        var cancel = new Button { Text = "取消", AutoSize = true };
        preview.Click += (_, _) => Preview();
        defaults.Click += (_, _) => LoadFrom(new AppConfig());
        save.Click += (_, _) => Save();
        cancel.Click += (_, _) => Close();
        AcceptButton = save;
        CancelButton = cancel;

        var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        left.Controls.AddRange(new Control[] { preview, defaults });
        var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty, Anchor = AnchorStyles.Right };
        right.Controls.AddRange(new Control[] { save, cancel });

        var buttons = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 16, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.Controls.Add(left, 0, 0);
        buttons.Controls.Add(right, 1, 0);
        grid.Controls.Add(buttons, 0, grid.RowCount);
        grid.SetColumnSpan(buttons, 2);
        grid.RowCount++;

        Controls.Add(grid);
        _grid = grid;
    }

    private TableLayoutPanel? _grid;

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        // On small screens, cap the height and let the contents scroll.
        var work = (Screen.PrimaryScreen ?? Screen.FromControl(this)).WorkingArea;
        if (Height > work.Height && _grid is not null)
        {
            AutoSize = false;
            _grid.Dock = DockStyle.Top;
            AutoScroll = true;
            Size = new Size(Width + SystemInformation.VerticalScrollBarWidth, work.Height - 20);
        }
        Location = new Point(work.Left + 24, work.Top + Math.Max(10, (work.Height - Height) / 2));
    }

    // ------------------------------------------------------------- data flow

    private void LoadFrom(AppConfig c)
    {
        _interval.Value = c.IntervalMinutes;
        _duration.Maximum = c.IntervalMinutes;
        _duration.Value = Math.Min(c.DurationMinutes, c.IntervalMinutes);
        _message.Text = c.Message;
        _subMessage.Text = c.SubMessage;
        _countdown.Checked = c.ShowCountdown;
        _dismissButton.Checked = c.ShowDismissButton;
        _dismissText.Text = c.DismissButtonText;
        _dismissText.Enabled = c.ShowDismissButton;
        _dismissDelay.Value = c.DismissButtonDelaySeconds;
        _dismissDelay.Enabled = c.ShowDismissButton;
        _drinkButton.Checked = c.ShowDrinkButton;
        _drinkText.Text = c.DrinkButtonText;
        _drinkText.Enabled = c.ShowDrinkButton;
        _postpone.Checked = c.PostponeWhenBusy;
        _monitors.SelectedIndex = c.Monitors == "primary" ? 1 : 0;
        int style = _styles.FindIndex(s => s.Id == c.Animation);
        _style.SelectedIndex = style >= 0 ? style : 0;
        _catColor.Enabled = SelectedStyle == "classic";
        _dim.Checked = c.DimScreen;
        _dimOpacity.Value = (int)Math.Round(c.DimOpacity * 100);
        _dimOpacity.Enabled = c.DimScreen;
        _dimValue.Text = $"{_dimOpacity.Value}%";
        _clickThrough.Checked = c.ClickThrough;
        _catScale.Value = (int)Math.Round(c.CatScale * 100);
        _catScaleValue.Text = $"{_catScale.Value}%";
        _catColor.BackColor = c.GetCatColor();
    }

    /// <summary>Builds a config from the form, keeping settings the form doesn't show.</summary>
    private AppConfig ReadForm()
    {
        var c = _original.Clone();
        c.IntervalMinutes = (int)_interval.Value;
        c.DurationMinutes = (int)_duration.Value;
        c.Message = _message.Text;
        c.SubMessage = _subMessage.Text;
        c.ShowCountdown = _countdown.Checked;
        c.ShowDismissButton = _dismissButton.Checked;
        c.DismissButtonText = _dismissText.Text;
        c.DismissButtonDelaySeconds = (int)_dismissDelay.Value;
        c.ShowDrinkButton = _drinkButton.Checked;
        c.DrinkButtonText = _drinkText.Text;
        c.PostponeWhenBusy = _postpone.Checked;
        c.Monitors = _monitors.SelectedIndex == 1 ? "primary" : "all";
        c.Animation = SelectedStyle;
        c.DimScreen = _dim.Checked;
        c.DimOpacity = _dimOpacity.Value / 100.0;
        c.ClickThrough = _clickThrough.Checked;
        c.CatScale = _catScale.Value / 100.0;
        c.CatColor = ColorTranslator.ToHtml(Color.FromArgb(_catColor.BackColor.R, _catColor.BackColor.G, _catColor.BackColor.B));
        c.Normalize();
        return c;
    }

    private void PickColor()
    {
        using var dialog = new ColorDialog { Color = _catColor.BackColor, FullOpen = true };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _catColor.BackColor = dialog.Color;
    }

    private void Preview()
    {
        _app.StartPreview(ReadForm());
        // The new overlay windows are topmost too; stay above them.
        BringToFront();
    }

    private void Save()
    {
        var config = ReadForm();
        try
        {
            config.Save();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"無法儲存設定：\n{ex.Message}", "CatSaver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            if (_autoStart.Checked != TrayApplication.SafeIsAutoStartEnabled())
                AutoStart.Set(_autoStart.Checked);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"無法變更開機自動啟動設定：\n{ex.Message}", "CatSaver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        _saved = config;
        Close();
    }

    private static void OpenConfigFolder()
    {
        try
        {
            Directory.CreateDirectory(AppConfig.ConfigDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppConfig.ConfigDirectory}\"") { UseShellExecute = true });
        }
        catch
        {
            // Not important enough to bother the user.
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Preview(); // show the cat as soon as the settings open
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _previewDebounce.Dispose();
        // Close the preview and use the saved settings (or the original ones on 取消).
        _app.EndPreview(_saved ?? _original);
        base.OnFormClosed(e);
    }
}
