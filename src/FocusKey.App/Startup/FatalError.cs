using System.Runtime.InteropServices;
using System.Text;
using FocusKey.Foundation;

namespace FocusKey.Startup;

/// <summary>
/// Last-resort reporting for failures that happen before, or outside, normal logging.
/// Writes a local file and tells the user something went wrong instead of exiting silently.
/// </summary>
internal static partial class FatalError
{
    private const string FallbackFileName = "startup-failure.log";
    private const uint MB_OK = 0x0000_0000;
    private const uint MB_ICONERROR = 0x0000_0010;

    internal static void ReportStartupFailure(Exception exception)
    {
        string? reportFile = WriteFallbackReport("Focus Key could not complete startup.", exception);

        var message = new StringBuilder();
        message.AppendLine("Focus Key could not start.");
        message.AppendLine();
        message.AppendLine(exception.Message);

        if (reportFile is not null)
        {
            message.AppendLine();
            message.AppendLine($"Details were written to:{Environment.NewLine}{reportFile}");
        }

        ShowMessage("Focus Key", message.ToString());
    }

    /// <summary>Appends a report next to the normal logs. Returns the file written, if any.</summary>
    internal static string? WriteFallbackReport(string message, Exception? exception)
    {
        try
        {
            string directory = AppPaths.Resolve().LogsDirectory;
            Directory.CreateDirectory(directory);

            string file = Path.Combine(directory, FallbackFileName);
            var report = new StringBuilder();
            report.AppendLine($"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z {message}");

            if (exception is not null)
            {
                report.AppendLine(exception.ToString());
            }

            File.AppendAllText(file, report.ToString(), Encoding.UTF8);
            return file;
        }
        catch (Exception)
        {
            // Reporting must never become the reason the process dies.
            return null;
        }
    }

    private static void ShowMessage(string caption, string text)
    {
        try
        {
            MessageBoxW(IntPtr.Zero, text, caption, MB_OK | MB_ICONERROR);
        }
        catch (Exception)
        {
        }
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
}
