using System.Runtime.InteropServices;

namespace CatSaver;

/// <summary>Asks Windows whether now is a bad moment for a pop-up.</summary>
internal static class UserActivity
{
    // QUERY_USER_NOTIFICATION_STATE
    private const int QUNS_BUSY = 2;                    // a full-screen app is in the foreground
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3; // exclusive full-screen game/video
    private const int QUNS_PRESENTATION_MODE = 4;       // PowerPoint slide show, presentation settings

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    /// <summary>True while presenting or running a full-screen app.</summary>
    public static bool IsBusy()
    {
        try
        {
            return SHQueryUserNotificationState(out int state) == 0
                && state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
        }
        catch
        {
            return false;
        }
    }
}
