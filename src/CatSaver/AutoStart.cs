using Microsoft.Win32;

namespace CatSaver;

/// <summary>Start-with-Windows via HKCU\...\Run (no admin rights needed).</summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    // The installer writes the same value name, so both stay in sync.
    private const string ValueName = "CatSaver";

    private static string Command => $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\"";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string s && s.Length > 0;
        }
    }

    /// <summary>
    /// If autostart is on but points at another path (the exe was moved), point it
    /// at this exe instead.
    /// </summary>
    public static void RefreshPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string current && current.Length > 0
                && !string.Equals(current, Command, StringComparison.OrdinalIgnoreCase))
                key.SetValue(ValueName, Command);
        }
        catch (Exception ex)
        {
            Log.Error("Refreshing autostart path", ex);
        }
    }

    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
            key.SetValue(ValueName, Command);
        else if (key.GetValue(ValueName) is not null)
            key.DeleteValue(ValueName);
    }
}
