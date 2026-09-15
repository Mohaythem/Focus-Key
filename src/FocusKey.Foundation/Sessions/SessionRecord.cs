namespace FocusKey.Foundation.Sessions;

/// <summary>
/// One durable focus session: the raw facts, and nothing derived from other sessions.
/// Timestamps are normalized to UTC on construction, so a record always exposes UTC regardless of
/// what the caller passed in.
/// </summary>
/// <remarks>
/// Planned end and actual duration are deliberately computed rather than stored. Both are exact
/// functions of stored facts, and keeping one source of truth per fact removes any way for a
/// database to disagree with itself.
/// </remarks>
public sealed record SessionRecord
{
    private readonly DateTimeOffset _startedAt;
    private readonly DateTimeOffset? _endedAt;
    private readonly DateTimeOffset _createdAt;

    /// <summary>Stable application identity.</summary>
    public required SessionId Id { get; init; }

    public required SessionType Type { get; init; }

    public required SessionStatus Status { get; init; }

    /// <summary>The instant the session began.</summary>
    public required DateTimeOffset StartedAt
    {
        get => _startedAt;
        init => _startedAt = value.ToUniversalTime();
    }

    /// <summary>The length the session was started for. Always greater than zero.</summary>
    public required TimeSpan PlannedDuration { get; init; }

    /// <summary>The instant the session stopped being active. Null exactly while Running.</summary>
    public DateTimeOffset? EndedAt
    {
        get => _endedAt;
        init => _endedAt = value?.ToUniversalTime();
    }

    /// <summary>The instant this record was first written.</summary>
    public required DateTimeOffset CreatedAt
    {
        get => _createdAt;
        init => _createdAt = value.ToUniversalTime();
    }

    /// <summary>When the session was planned to finish: <see cref="StartedAt"/> + <see cref="PlannedDuration"/>.</summary>
    public DateTimeOffset PlannedEndAt => StartedAt + PlannedDuration;

    /// <summary>How long the session actually lasted, or null while it is still Running.</summary>
    public TimeSpan? ActualDuration => EndedAt - StartedAt;

    /// <summary>
    /// The credited focus or break duration for reports and daily accounting.
    /// Returns zero while running or if the session has no valid elapsed time.
    /// Clamped to [0, PlannedDuration] so an interrupted or stopped session cannot credit negative time
    /// or time beyond the planned duration, and completed sessions credit exactly PlannedDuration.
    /// </summary>
    public TimeSpan EffectiveDuration
    {
        get
        {
            if (Status == SessionStatus.Running || EndedAt is null) return TimeSpan.Zero;
            var elapsed = EndedAt.Value - StartedAt;
            if (elapsed <= TimeSpan.Zero) return TimeSpan.Zero;
            return elapsed > PlannedDuration ? PlannedDuration : elapsed;
        }
    }

    /// <summary>True while this record represents the one active session.</summary>
    public bool IsActive => Status == SessionStatus.Running;

    /// <summary>
    /// Checks the invariants that make a record meaningful as stored data. This is shape
    /// validation, not workflow: it says nothing about which status may follow which.
    /// </summary>
    /// <exception cref="ArgumentException">The record could not be stored as-is.</exception>
    public void Validate()
    {
        if (Id.IsEmpty)
        {
            throw new ArgumentException("A session record needs a non-empty id.", nameof(Id));
        }

        if (!Enum.IsDefined(Type))
        {
            throw new ArgumentException($"Session type '{Type}' is not a defined value.", nameof(Type));
        }

        if (!Enum.IsDefined(Status))
        {
            throw new ArgumentException($"Session status '{Status}' is not a defined value.", nameof(Status));
        }

        if (PlannedDuration <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                $"Planned duration must be positive but was {PlannedDuration}.", nameof(PlannedDuration));
        }

        if (PlannedDuration.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            // Storage keeps whole seconds, so anything finer would be silently truncated.
            throw new ArgumentException(
                $"Planned duration must be a whole number of seconds but was {PlannedDuration}.",
                nameof(PlannedDuration));
        }

        if (PlannedDuration.Ticks > DateTimeOffset.MaxValue.UtcTicks - StartedAt.UtcTicks)
        {
            throw new ArgumentException("The planned end must be a representable UTC timestamp.", nameof(PlannedDuration));
        }

        if (Status == SessionStatus.Running && EndedAt is not null)
        {
            throw new ArgumentException(
                "A running session cannot have an end timestamp.", nameof(EndedAt));
        }

        if (Status != SessionStatus.Running && EndedAt is null)
        {
            throw new ArgumentException(
                $"A {Status} session must have an end timestamp.", nameof(EndedAt));
        }

        if (EndedAt is { } endedAt && endedAt < StartedAt)
        {
            throw new ArgumentException(
                $"End timestamp {endedAt:O} is before the start timestamp {StartedAt:O}.", nameof(EndedAt));
        }
    }
}
