using System.Diagnostics;

namespace CatSaver;

internal static class Program
{
    private const string MutexName = @"Local\CatSaver.SingleInstance";

    /// <summary>
    /// Command line:
    ///   --show       show the cat immediately (handy for testing the look)
    ///   --restarted  internal: wait for the previous instance to exit first
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        bool showNow = args.Any(a => a.Equals("--show", StringComparison.OrdinalIgnoreCase));
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

            var config = AppConfig.Load(out string? configError);
            using var app = new TrayApplication(config, showNow, configError);
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
}
