using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatSaver;

/// <summary>
/// User-editable settings, stored as JSON in %APPDATA%\CatSaver\config.json.
/// Missing keys fall back to the defaults below.
/// </summary>
public sealed class AppConfig
{
    /// <summary>Minutes between reminders. 60 = every exact hour (aligned to midnight).</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>How long the cat stays on screen, in minutes.</summary>
    public int DurationMinutes { get; set; } = 10;

    /// <summary>Main text in the speech bubble.</summary>
    public string Message { get; set; } = "起來喝水了！";

    /// <summary>Smaller second line. The countdown is appended after it.</summary>
    public string SubMessage { get; set; } = "離開電腦，伸展一下，喝杯水吧～";

    /// <summary>Show the remaining time ("還剩 15:42") in the bubble.</summary>
    public bool ShowCountdown { get; set; } = true;

    /// <summary>Show a "我在忙，先關掉" button under the cat that dismisses this reminder.</summary>
    public bool ShowDismissButton { get; set; } = true;

    /// <summary>
    /// The dismiss button only appears this many seconds after the cat (minimum 30),
    /// so the reminder can't be clicked away by reflex.
    /// </summary>
    public int DismissButtonDelaySeconds { get; set; } = 30;

    /// <summary>Text on the dismiss button.</summary>
    public string DismissButtonText { get; set; } = "我在忙，先關掉";

    /// <summary>Show a 「我喝了！」 button that records a cup of water and dismisses the reminder.</summary>
    public bool ShowDrinkButton { get; set; } = true;

    /// <summary>Text on the drink button.</summary>
    public string DrinkButtonText { get; set; } = "我喝了！";

    /// <summary>
    /// While presenting or in a full-screen app (game, video), hold the reminder and
    /// show it once you're done.
    /// </summary>
    public bool PostponeWhenBusy { get; set; } = true;

    /// <summary>"all" = cat + dimming on every monitor, "primary" = primary monitor only.</summary>
    public string Monitors { get; set; } = "all";

    /// <summary>Dim the rest of the screen behind the cat.</summary>
    public bool DimScreen { get; set; } = true;

    /// <summary>0.0 – 0.9. How dark the dimming layer is.</summary>
    public double DimOpacity { get; set; } = 0.35;

    /// <summary>
    /// true  = mouse clicks pass through the overlay to the apps underneath.
    /// false = the dimmed area blocks mouse clicks (keyboard focus is never stolen).
    /// </summary>
    public bool ClickThrough { get; set; } = true;

    /// <summary>Size multiplier for the cat (1.0 ≈ 70% of screen height).</summary>
    public double CatScale { get; set; } = 1.0;

    /// <summary>Fur colour as #RRGGBB (hand-drawn "classic" cat only).</summary>
    public string CatColor { get; set; } = "#F6A55A";

    /// <summary>
    /// Cat style: "random" (a different Noto cat set each reminder), a set id from
    /// Assets/Noto/sprites.json ("happy", "drama", "cool", "bigcat") or Assets/RealCats/realcats.json
    /// ("realcat", "realcat2", "realcat3", "realcat4"), or "classic"
    /// (hand-drawn cat that waves and holds a glass of water; uses catColor).
    /// </summary>
    public string Animation { get; set; } = "random";

    /// <summary>Frames per second for the animation.</summary>
    public int AnimationFps { get; set; } = 30;

    /// <summary>Font for the bubble text. Falls back automatically if not installed.</summary>
    public string FontFamily { get; set; } = "Microsoft JhengHei UI";

    /// <summary>Format version of config.json, used to migrate old defaults.</summary>
    public int ConfigVersion { get; set; } = CurrentVersion;

    /// <summary>2: default duration changed from 16 to 10 minutes.</summary>
    private const int CurrentVersion = 2;

    [JsonIgnore]
    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CatSaver");

    [JsonIgnore]
    public static string ConfigPath => Path.Combine(ConfigDirectory, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        // Keep Chinese characters readable in the file instead of \uXXXX escapes.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Loads the config, creating the file with defaults if it does not exist.</summary>
    public static AppConfig Load(out string? error)
    {
        error = null;
        AppConfig config;
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
                if (Migrate(config, json)) config.Save();
            }
            else
            {
                config = new AppConfig();
                config.Save();
            }
        }
        catch (Exception ex)
        {
            error = $"無法讀取設定檔，已使用預設值。\n{ConfigPath}\n{ex.Message}";
            config = new AppConfig();
        }

        config.Normalize();
        return config;
    }

    /// <summary>Updates settings that still hold an old default. Returns true if anything changed.</summary>
    private static bool Migrate(AppConfig config, string json)
    {
        int version = 1; // files written before configVersion existed
        using (var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
        {
            foreach (var property in doc.RootElement.EnumerateObject())
                if (string.Equals(property.Name, "configVersion", StringComparison.OrdinalIgnoreCase) && property.Value.TryGetInt32(out int v))
                    version = v;
        }
        if (version >= CurrentVersion) return false;

        // 16 minutes was the old default; anything else was the user's own choice.
        if (version < 2 && config.DurationMinutes == 16) config.DurationMinutes = 10;
        config.ConfigVersion = CurrentVersion;
        return true;
    }

    public void Save()
    {
        Directory.CreateDirectory(ConfigDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(this, JsonOptions));
    }

    /// <summary>Clamps values into sane ranges so a typo can't break the app.</summary>
    public void Normalize()
    {
        IntervalMinutes = Math.Clamp(IntervalMinutes, 1, 24 * 60);
        DurationMinutes = Math.Clamp(DurationMinutes, 1, IntervalMinutes);
        DimOpacity = Math.Clamp(DimOpacity, 0.0, 0.9);
        DismissButtonDelaySeconds = Math.Clamp(DismissButtonDelaySeconds, 30, 600);
        CatScale = Math.Clamp(CatScale, 0.2, 2.0);
        AnimationFps = Math.Clamp(AnimationFps, 5, 60);
        Message ??= "";
        SubMessage ??= "";
        if (string.IsNullOrWhiteSpace(DismissButtonText)) DismissButtonText = "我在忙，先關掉";
        if (string.IsNullOrWhiteSpace(DrinkButtonText)) DrinkButtonText = "我喝了！";
        Monitors = string.Equals(Monitors, "primary", StringComparison.OrdinalIgnoreCase) ? "primary" : "all";
        if (string.IsNullOrWhiteSpace(FontFamily)) FontFamily = "Microsoft JhengHei UI";
        Animation = Animation?.Trim().ToLowerInvariant() ?? "";
        // "noto"/"wave" were earlier names; unknown ids fall back to random in the renderer too.
        if (Animation is "" or "noto" or "wave") Animation = "random";
    }

    /// <summary>Copy (all members are values/strings), so the settings window can edit without touching the live config.</summary>
    public AppConfig Clone() => (AppConfig)MemberwiseClone();

    public ReminderSchedule CreateSchedule() =>
        new(TimeSpan.FromMinutes(IntervalMinutes), TimeSpan.FromMinutes(DurationMinutes));

    public Color GetCatColor()
    {
        try
        {
            return ColorTranslator.FromHtml(CatColor);
        }
        catch
        {
            return ColorTranslator.FromHtml("#F6A55A");
        }
    }
}
