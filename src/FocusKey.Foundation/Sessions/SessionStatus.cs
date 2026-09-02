namespace FocusKey.Foundation.Sessions;

/// <summary>
/// The four states a recorded session can be in, as defined by the product specification.
/// Phase 1 stores and reads these values; it does not implement transitions between them.
/// </summary>
public enum SessionStatus
{
    /// <summary>The session is currently active. At most one stored session may be Running.</summary>
    Running = 1,

    /// <summary>The planned time elapsed normally.</summary>
    Completed = 2,

    /// <summary>The user ended the session early.</summary>
    Stopped = 3,

    /// <summary>The session ended without completing or being stopped by the user.</summary>
    Interrupted = 4,
}

/// <summary>
/// Maps <see cref="SessionStatus"/> to and from the exact text stored in SQLite.
/// Explicit in both directions: an unrecognized stored value fails instead of quietly becoming
/// a valid status.
/// </summary>
public static class SessionStatusText
{
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Stopped = "stopped";
    public const string Interrupted = "interrupted";

    public static string Format(SessionStatus status) => status switch
    {
        SessionStatus.Running => Running,
        SessionStatus.Completed => Completed,
        SessionStatus.Stopped => Stopped,
        SessionStatus.Interrupted => Interrupted,
        _ => throw new ArgumentOutOfRangeException(
            nameof(status), status, "Unknown session status cannot be persisted."),
    };

    /// <exception cref="FormatException">The stored text is not a known session status.</exception>
    public static SessionStatus Parse(string? text) =>
        TryParse(text, out SessionStatus status)
            ? status
            : throw new FormatException($"'{text}' is not a valid stored session status.");

    public static bool TryParse(string? text, out SessionStatus status)
    {
        switch (text)
        {
            case Running:
                status = SessionStatus.Running;
                return true;
            case Completed:
                status = SessionStatus.Completed;
                return true;
            case Stopped:
                status = SessionStatus.Stopped;
                return true;
            case Interrupted:
                status = SessionStatus.Interrupted;
                return true;
            default:
                status = default;
                return false;
        }
    }
}
