using System.Text.Json;

namespace CatSaver;

/// <summary>
/// Cups of water per day, stored in %APPDATA%\CatSaver\water.json as
/// { "2026-10-14": 3, ... }. Keeps the last 366 days.
/// </summary>
internal sealed class WaterLog
{
    private const int KeepDays = 366;
    private readonly Dictionary<string, int> _days;

    private static string FilePath => Path.Combine(AppConfig.ConfigDirectory, "water.json");
    private static string Key(DateTime day) => day.ToString("yyyy-MM-dd");

    private WaterLog(Dictionary<string, int> days) => _days = days;

    public static WaterLog Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var days = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FilePath));
                if (days is not null) return new WaterLog(days);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Reading water.json", ex);
        }
        return new WaterLog(new Dictionary<string, int>());
    }

    public int CupsOn(DateTime day) => _days.GetValueOrDefault(Key(day));

    /// <summary>Records one cup for today and returns today's total.</summary>
    public int AddCup(DateTime now)
    {
        int cups = CupsOn(now) + 1;
        _days[Key(now)] = cups;
        foreach (var old in _days.Keys.OrderByDescending(k => k, StringComparer.Ordinal).Skip(KeepDays).ToList())
            _days.Remove(old);
        try
        {
            Directory.CreateDirectory(AppConfig.ConfigDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_days, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Error("Writing water.json", ex);
        }
        return cups;
    }
}
