namespace FocusKey.Foundation.Shell;

/// <summary>
/// Manages registration of Focus Key in HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// Operates exclusively per-user without requiring administrator privileges.
/// </summary>
public sealed class WindowsStartupService : IWindowsStartupService
{
    /// <summary>
    /// Standard per-user Run subkey under HKCU.
    /// </summary>
    public const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Registry value name for Focus Key startup registration.
    /// </summary>
    public const string ValueName = "Focus Key";

    /// <summary>
    /// Startup command-line argument.
    /// </summary>
    public const string StartupFlag = StartupArguments.StartupFlag;

    private readonly IRegistryAccessor _registry;
    private readonly Func<string> _executablePathResolver;

    public WindowsStartupService(
        IRegistryAccessor registry,
        Func<string>? executablePathResolver = null)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _executablePathResolver = executablePathResolver ?? (() => ResolveExecutablePath());
    }

    /// <inheritdoc/>
    public bool IsEnabled()
    {
        string? value = _registry.GetValue(RunSubKey, ValueName);
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <inheritdoc/>
    public string? GetConfiguredCommandLine()
    {
        return _registry.GetValue(RunSubKey, ValueName);
    }

    /// <inheritdoc/>
    public string GetExpectedCommandLine()
    {
        string exePath = _executablePathResolver();
        return FormatCommandLine(exePath);
    }

    /// <inheritdoc/>
    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            string commandLine = GetExpectedCommandLine();
            _registry.SetValue(RunSubKey, ValueName, commandLine);
        }
        else
        {
            _registry.DeleteValue(RunSubKey, ValueName);
        }
    }

    /// <summary>
    /// Formats an executable path into a safely quoted command line with the --startup flag.
    /// </summary>
    public static string FormatCommandLine(string executablePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        string cleanPath = executablePath.Trim().Trim('"');
        return $"\"{cleanPath}\" {StartupFlag}";
    }

    /// <summary>
    /// Resolves the canonical executable path for Focus Key.
    /// Prefers the installed per-user location (%LOCALAPPDATA%\Programs\Focus Key\FocusKey.exe)
    /// if present, otherwise uses the running process path if it is FocusKey.exe.
    /// </summary>
    public static string ResolveExecutablePath(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return explicitPath;
        }

        string localAppProgramsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "Focus Key", "FocusKey.exe");

        if (File.Exists(localAppProgramsPath))
        {
            return localAppProgramsPath;
        }

        string? currentProcess = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(currentProcess) &&
            Path.GetFileName(currentProcess).Equals("FocusKey.exe", StringComparison.OrdinalIgnoreCase))
        {
            return currentProcess;
        }

        return localAppProgramsPath;
    }
}
