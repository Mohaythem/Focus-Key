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
        DateTimeOffset startedAt,
        TimeSpan plannedDuration,
        DateTimeOffset observedAt)
    {
        Id = id;
        Type = type;
        StartedAt = startedAt;
        PlannedDuration = plannedDuration;
        ObservedAt = observedAt;
    }

    public SessionId Id { get; }

    public SessionType Type { get; }

    public DateTimeOffset StartedAt { get; }

    public TimeSpan PlannedDuration { get; }

    /// <summary>The instant this view describes.</summary>
    public DateTimeOffset ObservedAt { get; }

    public DateTimeOffset PlannedEndAt => StartedAt + PlannedDuration;

    /// <summary><c>max(PlannedEndAt - ObservedAt, 0)</c>. Never negative.</summary>
    public TimeSpan Remaining
    {
        get
        {
            TimeSpan remaining = PlannedEndAt - ObservedAt;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    /// <summary><c>clamp(ObservedAt - StartedAt, 0, PlannedDuration)</c>.</summary>
    public TimeSpan Elapsed
    {
        get
        {
            TimeSpan elapsed = ObservedAt - StartedAt;

            if (elapsed < TimeSpan.Zero)
            {
                return TimeSpan.Zero;
            }

            return elapsed > PlannedDuration ? PlannedDuration : elapsed;
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
            throw new ArgumentException("An active snapshot requires a Running session.", nameof(session));
        }

        return new SessionSnapshot(
            session.Id,
            session.Type,
            session.StartedAt,
            session.PlannedDuration,
            observedAt.ToUniversalTime());
    }
}
