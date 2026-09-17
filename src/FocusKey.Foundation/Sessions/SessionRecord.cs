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
    private readonly DateTimeOffset _resumedAt;
    private readonly DateTimeOffset? _endedAt;
    private readonly DateTimeOffset? _pausedAt;
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

    /// <summary>The instant the current active leg of the session was resumed or started.</summary>
    public DateTimeOffset ResumedAt
    {
        get => _resumedAt == default ? _startedAt : _resumedAt;
        init => _resumedAt = value.ToUniversalTime();
    }

    /// <summary>The length the session was started for. Always greater than zero.</summary>
    public required TimeSpan PlannedDuration { get; init; }

    /// <summary>Accumulated active duration from completed legs before the current resumption.</summary>
    public TimeSpan AccumulatedActiveDuration { get; init; } = TimeSpan.Zero;

    /// <summary>The instant the session was paused, or null while not paused.</summary>
    public DateTimeOffset? PausedAt
    {
        get => _pausedAt;
        init => _pausedAt = value?.ToUniversalTime();
    }

    /// <summary>The instant the session stopped being active. Null exactly while Running or Paused.</summary>
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

    /// <summary>When the session is planned to finish.</summary>
    public DateTimeOffset PlannedEndAt => ResumedAt + (PlannedDuration - AccumulatedActiveDuration);

    /// <summary>How long the session actually lasted from start to end, or null while it is still active.</summary>
    public TimeSpan? ActualDuration => EndedAt is { } ended ? (EndedAt - StartedAt) : null;

    /// <summary>
    /// The credited focus or break duration for reports and daily accounting.
    /// Returns zero while active or if the session has no valid elapsed time.
    /// Clamped to [0, PlannedDuration].
    /// </summary>
    public TimeSpan EffectiveDuration
    {
        get
        {
            if (Status is SessionStatus.Running or SessionStatus.Paused || EndedAt is null)
            {
                return TimeSpan.Zero;
            }

            TimeSpan active;
            if (PausedAt is not null)
            {
                active = AccumulatedActiveDuration;
            }
            else
            {
                var currentLeg = EndedAt.Value - ResumedAt;
                active = AccumulatedActiveDuration + (currentLeg > TimeSpan.Zero ? currentLeg : TimeSpan.Zero);
            }

            if (active <= TimeSpan.Zero) return TimeSpan.Zero;
            return active > PlannedDuration ? PlannedDuration : active;
        }
    }

    /// <summary>True while this record represents the one active session (Running or Paused).</summary>
    public bool IsActive => Status is SessionStatus.Running or SessionStatus.Paused;

    /// <summary>True while the session is paused.</summary>
    public bool IsPaused => Status == SessionStatus.Paused;

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

        if (AccumulatedActiveDuration < TimeSpan.Zero)
        {
            throw new ArgumentException(
                $"Accumulated active duration must not be negative but was {AccumulatedActiveDuration}.",
                nameof(AccumulatedActiveDuration));
        }

        if (AccumulatedActiveDuration.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentException(
                $"Accumulated active duration must be a whole number of seconds but was {AccumulatedActiveDuration}.",
                nameof(AccumulatedActiveDuration));
        }

        if (ResumedAt < StartedAt)
        {
            throw new ArgumentException(
                $"Resumed timestamp {ResumedAt:O} is before start timestamp {StartedAt:O}.", nameof(ResumedAt));
        }

        if ((PlannedDuration - AccumulatedActiveDuration).Ticks > DateTimeOffset.MaxValue.UtcTicks - ResumedAt.UtcTicks)
        {
            throw new ArgumentException("The planned end must be a representable UTC timestamp.", nameof(PlannedDuration));
        }

        if ((Status is SessionStatus.Running or SessionStatus.Paused) && EndedAt is not null)
        {
            throw new ArgumentException(
                "A running or paused session cannot have an end timestamp.", nameof(EndedAt));
        }

        if (Status != SessionStatus.Running && Status != SessionStatus.Paused && EndedAt is null)
        {
            throw new ArgumentException(
                $"A {Status} session must have an end timestamp.", nameof(EndedAt));
        }

        if (Status == SessionStatus.Paused && PausedAt is null)
        {
            throw new ArgumentException("A paused session requires a paused timestamp.", nameof(PausedAt));
        }

        if (Status != SessionStatus.Paused && PausedAt is not null)
        {
            throw new ArgumentException("A non-paused session cannot have a paused timestamp.", nameof(PausedAt));
        }

        if (PausedAt is { } pausedAt && pausedAt < ResumedAt)
        {
            throw new ArgumentException(
                $"Paused timestamp {pausedAt:O} is before the resumed timestamp {ResumedAt:O}.", nameof(PausedAt));
        }

        if (EndedAt is { } endedAt && endedAt < StartedAt)
        {
            throw new ArgumentException(
                $"End timestamp {endedAt:O} is before the start timestamp {StartedAt:O}.", nameof(EndedAt));
        }
    }

    public bool Equals(SessionRecord? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return Id == other.Id &&
               Type == other.Type &&
               Status == other.Status &&
               StartedAt == other.StartedAt &&
               ResumedAt == other.ResumedAt &&
               PlannedDuration == other.PlannedDuration &&
               AccumulatedActiveDuration == other.AccumulatedActiveDuration &&
               PausedAt == other.PausedAt &&
               EndedAt == other.EndedAt &&
               CreatedAt == other.CreatedAt;
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Type);
        hash.Add(Status);
        hash.Add(StartedAt);
        hash.Add(ResumedAt);
        hash.Add(PlannedDuration);
        hash.Add(AccumulatedActiveDuration);
        hash.Add(PausedAt);
        hash.Add(EndedAt);
        hash.Add(CreatedAt);
        return hash.ToHashCode();
    }
}
