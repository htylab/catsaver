using System.Diagnostics;

namespace CatSaver;

internal static class Program
{
    private const string MutexName = @"Local\CatSaver.SingleInstance";

    /// <summary>
    /// Command line:
    ///   --show       show the cat immediately (handy for testing the look)
    ///   --settings   open the settings window on start
    ///   --restarted  internal: wait for the previous instance to exit first
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        bool showNow = args.Any(a => a.Equals("--show", StringComparison.OrdinalIgnoreCase));
        bool showSettings = args.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase));
        bool restarted = args.Any(a => a.Equals("--restarted", StringComparison.OrdinalIgnoreCase));

        using var mutex = new Mutex(false, MutexName);
        bool owned;
        try
        {
            owned = mutex.WaitOne(restarted ? TimeSpan.FromSeconds(10) : TimeSpan.Zero);
        }
        catch (AbandonedMutexException)
        {
            owned = true; // previous instance crashed; take over
        }

        if (!owned)
        {
            // Already running in the tray – nothing to do.
            return 0;
        }

        bool restartRequested;
        try
        {
            ApplicationConfiguration.Initialize();

            // A background reminder app should never vanish because of one bad event:
            // log the error and keep running.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => LogError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => LogError(e.ExceptionObject as Exception);

            var config = AppConfig.Load(out string? configError);
            using var app = new TrayApplication(config, showNow, showSettings, configError);
            Application.Run(app);
            restartRequested = app.RestartRequested;
        }
        finally
        {
            mutex.ReleaseMutex();
        }

        if (restartRequested)
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath ?? Application.ExecutablePath, "--restarted")
            {
                UseShellExecute = false,
            });
        }
        return 0;
    }

    /// <summary>Appends to %APPDATA%\CatSaver\error.log (best effort).</summary>
    private static void LogError(Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(AppConfig.ConfigDirectory);
            File.AppendAllText(Path.Combine(AppConfig.ConfigDirectory, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Nothing sensible left to do.
        }
    }
}
