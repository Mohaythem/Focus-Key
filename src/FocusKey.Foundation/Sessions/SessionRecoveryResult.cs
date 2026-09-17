namespace FocusKey.Foundation.Sessions;

/// <summary>The outcome of one application-lifecycle persistence decision.</summary>
public enum SessionRecoveryKind
{
    NoActiveSession = 1,
    Completed = 2,
    Interrupted = 3,
    Conflict = 4,
    StillPaused = 5,
}

/// <summary>
/// A historical operation result, never a mutable copy of active state. Conflict carries the
/// originally observed record, not the current database state, and means this request wrote nothing.
/// </summary>
public sealed record SessionRecoveryResult
{
    private SessionRecoveryResult(SessionRecoveryKind kind, SessionRecord? session)
    {
        Kind = kind;
        Session = session;
    }

    public SessionRecoveryKind Kind { get; }
    public SessionRecord? Session { get; }
    public bool ChangedStoredState => Kind is SessionRecoveryKind.Completed or SessionRecoveryKind.Interrupted;

    internal static SessionRecoveryResult NoActiveSession() => new(SessionRecoveryKind.NoActiveSession, null);
    internal static SessionRecoveryResult Completed(SessionRecord session) => new(SessionRecoveryKind.Completed, session);
    internal static SessionRecoveryResult Interrupted(SessionRecord session) => new(SessionRecoveryKind.Interrupted, session);
    internal static SessionRecoveryResult Conflict(SessionRecord observed) => new(SessionRecoveryKind.Conflict, observed);
    internal static SessionRecoveryResult StillPaused(SessionRecord session) => new(SessionRecoveryKind.StillPaused, session);
}
