namespace FocusKey.Foundation.Shell;

/// <summary>
/// Command-line argument parser for Focus Key startup modes.
/// </summary>
public static class StartupArguments
{
    /// <summary>
    /// Standard command-line argument appended when launching automatically at Windows login.
    /// </summary>
    public const string StartupFlag = "--startup";

    /// <summary>
    /// Returns true if the command-line arguments indicate a quiet Windows startup launch.
    /// Supports standard '--startup' as well as common variants ('-startup', '/startup').
    /// </summary>
    public static bool IsStartupLaunch(string[]? commandLineArgs)
    {
        if (commandLineArgs is null || commandLineArgs.Length <= 1)
        {
            return false;
        }

        for (int i = 1; i < commandLineArgs.Length; i++)
        {
            string arg = commandLineArgs[i];
            if (string.Equals(arg, StartupFlag, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-startup", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "/startup", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
