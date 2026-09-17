namespace FocusKey.Foundation.Sessions;

/// <summary>What a lifecycle request did.</summary>
public enum SessionOutcomeKind
{
    /// <summary>There was no running session to act on.</summary>
    NoActiveSession = 1,

    /// <summary>The session is running and its planned end has not arrived; nothing was written.</summary>
    StillRunning = 2,

    /// <summary>The user ended the session before its planned end.</summary>
    Stopped = 3,

    /// <summary>The planned end had arrived, so the session finished normally.</summary>
    Completed = 4,

    /// <summary>The observed record changed before this request could write. Nothing was written.</summary>
    Conflict = 5,

    /// <summary>The session was paused.</summary>
    Paused = 6,

    /// <summary>The session was continued.</summary>
    Continued = 7,
}

/// <summary>
/// The result of a lifecycle request. Ordinary conditions — nothing running, not due yet — are
/// results rather than exceptions, because callers will act on them routinely.
/// </summary>
public sealed record SessionOutcome
{
    private SessionOutcome(SessionOutcomeKind kind, SessionRecord? session)
    {
        Kind = kind;
        Session = session;
    }

    public SessionOutcomeKind Kind { get; }

    /// <summary>
    /// The session the outcome is about: the transitioned record for
    /// <see cref="SessionOutcomeKind.Stopped"/> and <see cref="SessionOutcomeKind.Completed"/>, the
    /// untouched record for <see cref="SessionOutcomeKind.StillRunning"/>, and null when there was
    /// nothing to act on. For Conflict, this is the originally observed record, not current state.
    /// </summary>
    public SessionRecord? Session { get; }

    /// <summary>True when this outcome wrote a state change to storage.</summary>
    public bool ChangedStoredState =>
        Kind is SessionOutcomeKind.Stopped or SessionOutcomeKind.Completed or SessionOutcomeKind.Paused or SessionOutcomeKind.Continued;

    internal static SessionOutcome NoActiveSession() =>
        new(SessionOutcomeKind.NoActiveSession, session: null);

    internal static SessionOutcome StillRunning(SessionRecord session) =>
        new(SessionOutcomeKind.StillRunning, session);

    internal static SessionOutcome Stopped(SessionRecord session) =>
        new(SessionOutcomeKind.Stopped, session);

    internal static SessionOutcome Completed(SessionRecord session) =>
        new(SessionOutcomeKind.Completed, session);

    internal static SessionOutcome Conflict(SessionRecord observed) =>
        new(SessionOutcomeKind.Conflict, observed);

    internal static SessionOutcome Paused(SessionRecord session) =>
        new(SessionOutcomeKind.Paused, session);

    internal static SessionOutcome Continued(SessionRecord session) =>
        new(SessionOutcomeKind.Continued, session);
}
