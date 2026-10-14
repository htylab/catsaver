using System.Diagnostics;

namespace CatSaver;

internal static class Program
{
    private const string MutexName = @"Local\CatSaver.SingleInstance";
    /// <summary>A second launch sets this so the running instance opens its settings.</summary>
    private const string ActivateEventName = @"Local\CatSaver.Activate";

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
            // Already running in the tray: ask it to open the settings window, so
            // launching the exe again visibly "does something".
            if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var activate))
            {
                using (activate) activate.Set();
            }
            else
            {
                ApplicationConfiguration.Initialize();
                MessageBox.Show("CatSaver 已經在執行了，請找右下角系統匣的貓咪圖示。", "CatSaver",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return 0;
        }

        bool restartRequested;
        try
        {
            ApplicationConfiguration.Initialize();

            // A background reminder app should never vanish because of one bad event:
            // log the error and keep running.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => Log.Error("UI thread", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unhandled", e.ExceptionObject as Exception);

            // The exe may have been moved since autostart was turned on.
            AutoStart.RefreshPathIfEnabled();

            using var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            var config = AppConfig.Load(out string? configError);
            using var app = new TrayApplication(config, showNow, showSettings, configError, activate);
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
