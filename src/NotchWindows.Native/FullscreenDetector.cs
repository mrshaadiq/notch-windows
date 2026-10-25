using System.Runtime.InteropServices;

namespace NotchWindows.Native;

public static class FullscreenDetector
{
    // QUERY_USER_NOTIFICATION_STATE (shellapi.h)
    private const int QUNS_NOT_PRESENT = 1;
    private const int QUNS_BUSY = 2;
    private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
    private const int QUNS_PRESENTATION_MODE = 4;

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int pquns);

    /// <summary>True when a fullscreen app, D3D fullscreen game, or presentation is active. False on failure.</summary>
    public static bool IsBusy()
    {
        try
        {
            if (SHQueryUserNotificationState(out int state) != 0)
                return false;
            return state is QUNS_BUSY or QUNS_RUNNING_D3D_FULL_SCREEN or QUNS_PRESENTATION_MODE;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
    }
}
