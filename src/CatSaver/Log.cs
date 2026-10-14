namespace CatSaver;

/// <summary>
/// Append-only error log at %APPDATA%\CatSaver\error.log. When the file passes
/// 512 KB it is rotated to error.old.log. Never throws.
/// </summary>
internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath => Path.Combine(AppConfig.ConfigDirectory, "error.log");

    public static void Error(string context, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppConfig.ConfigDirectory);
                var file = new FileInfo(FilePath);
                if (file.Exists && file.Length > MaxBytes)
                    File.Move(FilePath, Path.Combine(AppConfig.ConfigDirectory, "error.old.log"), overwrite: true);
                File.AppendAllText(FilePath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
