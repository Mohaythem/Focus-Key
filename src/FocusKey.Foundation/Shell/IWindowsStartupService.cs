namespace FocusKey.Foundation.Shell;

/// <summary>
/// Controls automatic launch of Focus Key when the Windows user signs in.
/// </summary>
public interface IWindowsStartupService
{
    /// <summary>
    /// Returns true if Focus Key is currently registered to launch at Windows login.
    /// Queries the actual Windows registry state directly.
    /// </summary>
    bool IsEnabled();

    /// <summary>
    /// Enables or disables automatic launch of Focus Key at Windows login.
    /// When enabled, writes the safely quoted executable path with --startup.
    /// When disabled, cleanly removes the registry value.
    /// </summary>
    void SetEnabled(bool enabled);

    /// <summary>
    /// Returns the raw command line currently stored in the registry, or null if absent.
    /// </summary>
    string? GetConfiguredCommandLine();

    /// <summary>
    /// Returns the expected canonical command line for the current executable:
    /// &quot;&lt;ExecutablePath&gt;&quot; --startup
    /// </summary>
    string GetExpectedCommandLine();
}
