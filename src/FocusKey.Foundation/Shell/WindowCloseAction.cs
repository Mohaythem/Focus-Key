namespace FocusKey.Foundation.Shell;

/// <summary>
/// User decision when requesting main window closure.
/// </summary>
public enum WindowCloseAction
{
    /// <summary>Cancel / dismiss close request and keep main window open.</summary>
    Cancel = 0,

    /// <summary>Hide main window to system tray while keeping process and sessions running.</summary>
    Hide = 1,

    /// <summary>Quit application completely via canonical graceful shutdown.</summary>
    Quit = 2
}
