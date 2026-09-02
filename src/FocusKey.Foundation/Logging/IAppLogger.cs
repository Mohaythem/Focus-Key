namespace FocusKey.Foundation.Logging;

/// <summary>
/// Minimal local diagnostic log. Local file only: no telemetry, no network, no analytics.
/// </summary>
public interface IAppLogger
{
    void Info(string message);

    void Warning(string message);

    void Error(string message, Exception? exception = null);
}
