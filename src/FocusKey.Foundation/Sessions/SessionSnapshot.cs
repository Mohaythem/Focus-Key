namespace FocusKey.Foundation.Sessions;

/// <summary>
/// A read-only view of a session at one instant: the stored facts plus the two values derived from
/// comparing them against a clock. Producing a snapshot never touches storage.
/// </summary>
/// <remarks>
/// Nothing here is persisted. <see cref="Elapsed"/> and <see cref="Remaining"/> exist only as
/// functions of <see cref="ObservedAt"/>, which is why a stale snapshot is obviously stale rather
/// than quietly wrong.
/// </remarks>
public sealed record SessionSnapshot
{
    private SessionSnapshot(
        SessionId id,
        SessionType type,
        SessionStatus status,
        DateTimeOffset startedAt,
        DateTimeOffset resumedAt,
        TimeSpan plannedDuration,
        TimeSpan accumulatedActiveDuration,
        DateTimeOffset observedAt)
    {
        Id = id;
        Type = type;
        Status = status;
        StartedAt = startedAt;
        ResumedAt = resumedAt;
        PlannedDuration = plannedDuration;
        AccumulatedActiveDuration = accumulatedActiveDuration;
        ObservedAt = observedAt;
    }

    public SessionId Id { get; }

    public SessionType Type { get; }

    public SessionStatus Status { get; }

    public DateTimeOffset StartedAt { get; }

    public DateTimeOffset ResumedAt { get; }

    public TimeSpan PlannedDuration { get; }

    public TimeSpan AccumulatedActiveDuration { get; }

    /// <summary>The instant this view describes.</summary>
    public DateTimeOffset ObservedAt { get; }

    public bool IsPaused => Status == SessionStatus.Paused;

    public DateTimeOffset PlannedEndAt => ResumedAt + (PlannedDuration - AccumulatedActiveDuration);

    /// <summary>Remaining active time until planned end. Never negative.</summary>
    public TimeSpan Remaining
    {
        get
        {
            if (IsPaused)
            {
                TimeSpan rem = PlannedDuration - AccumulatedActiveDuration;
                return rem > TimeSpan.Zero ? rem : TimeSpan.Zero;
            }

            TimeSpan remaining = PlannedEndAt - ObservedAt;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary>Total active time elapsed so far.</summary>
    public TimeSpan Elapsed
    {
        get
        {
            if (IsPaused)
            {
                return AccumulatedActiveDuration;
            }

            TimeSpan leg = ObservedAt - ResumedAt;
            if (leg < TimeSpan.Zero) leg = TimeSpan.Zero;
            TimeSpan remainingCap = PlannedDuration - AccumulatedActiveDuration;
            if (leg > remainingCap) leg = remainingCap;
            return AccumulatedActiveDuration + leg;
        }
    }

    /// <summary>True once the planned end has arrived. Says nothing about what was stored.</summary>
    public bool HasReachedPlannedEnd => ObservedAt >= PlannedEndAt;

    /// <summary>Describes <paramref name="session"/> as seen at <paramref name="observedAt"/>.</summary>
    public static SessionSnapshot For(SessionRecord session, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Validate();
        if (!session.IsActive)
        {
            throw new ArgumentException("An active snapshot requires a Running or Paused session.", nameof(session));
        }

        DateTimeOffset effectiveObserved = session.Status == SessionStatus.Paused && session.PausedAt is { } p
            ? p
            : observedAt.ToUniversalTime();

        return new SessionSnapshot(
            session.Id,
            session.Type,
            session.Status,
            session.StartedAt,
            session.ResumedAt,
            session.PlannedDuration,
            session.AccumulatedActiveDuration,
            effectiveObserved);
    }
}
