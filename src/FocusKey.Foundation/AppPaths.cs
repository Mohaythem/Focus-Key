namespace FocusKey.Foundation;

/// <summary>
/// Resolves every filesystem location Focus Key uses at runtime.
/// Nothing else in the application is allowed to build these paths by hand.
/// </summary>
public sealed class AppPaths
{
    /// <summary>Folder created directly under the user's local application data.</summary>
    public const string ApplicationFolderName = "FocusKey";

    /// <summary>
    /// Optional override for the runtime data root. Exists so a build can be launched
    /// against an isolated directory (verification, diagnostics) without touching the
    /// real per-user data.
    /// </summary>
    public const string DataRootEnvironmentVariable = "FOCUSKEY_DATA_ROOT";

    public const string DatabaseFileName = "focus_key.db";
    public const string LogsFolderName = "logs";
    public const string LogFileName = "focus_key.log";

    private AppPaths(string rootDirectory) => RootDirectory = rootDirectory;

    /// <summary>Root of all Focus Key runtime data for the current user.</summary>
    public string RootDirectory { get; }

    /// <summary>Directory holding local diagnostic logs.</summary>
    public string LogsDirectory => Path.Combine(RootDirectory, LogsFolderName);

    /// <summary>Full path of the local SQLite database file.</summary>
    public string DatabaseFile => Path.Combine(RootDirectory, DatabaseFileName);

    /// <summary>Full path of the current local log file.</summary>
    public string LogFile => Path.Combine(LogsDirectory, LogFileName);

    /// <summary>
    /// Constructs paths pointing to the standard per-user local application data folder.
    /// </summary>
    public static AppPaths ForLocalApplicationData()
    {
        string localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.DoNotVerify);

        return ForRoot(Path.Combine(localAppData, ApplicationFolderName));
    }

    /// <summary>
    /// The layout the application actually runs against: <see cref="DataRootEnvironmentVariable"/>
    /// when it is set, otherwise the standard per-user location.
    /// </summary>
    public static AppPaths Resolve()
    {
        string? overrideRoot = Environment.GetEnvironmentVariable(DataRootEnvironmentVariable);

        return string.IsNullOrWhiteSpace(overrideRoot)
            ? ForLocalApplicationData()
            : ForRoot(overrideRoot);
    }

    /// <summary>
    /// Roots the layout at an explicit directory. Used by tests and by any future
    /// scenario that needs an isolated data location.
    /// </summary>
    public static AppPaths ForRoot(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return new AppPaths(Path.GetFullPath(rootDirectory));
    }

    /// <summary>
    /// Creates the runtime directories if they do not already exist. Safe to call repeatedly.
    /// </summary>
    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }
}
