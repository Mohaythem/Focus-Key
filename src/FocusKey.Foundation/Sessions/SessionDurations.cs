namespace FocusKey.Foundation.Sessions;

/// <summary>
/// How long a Work session and a Break session run for. The engine asks this for a duration; it
/// does not decide durations itself, and it does not read or write user preferences.
/// </summary>
public sealed record SessionDurations
{
    /// <summary>The product defaults: Work 30 minutes, Break 10 minutes.</summary>
    public static SessionDurations Default { get; } =
        new(TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10));

    public SessionDurations(TimeSpan work, TimeSpan @break)
    {
        Validate(work, nameof(work));
        Validate(@break, nameof(@break));

        Work = work;
        Break = @break;
    }

    public TimeSpan Work { get; }

    public TimeSpan Break { get; }

    public TimeSpan For(SessionType type) => type switch
    {
        SessionType.Work => Work,
        SessionType.Break => Break,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type), type, "Unknown session type has no duration."),
    };

    /// <summary>
    /// The same rules a stored record must satisfy, checked here so the engine can never build an
    /// unstorable session out of a bad duration.
    /// </summary>
    private static void Validate(TimeSpan duration, string parameterName)
    {
        if (duration <= TimeSpan.Zero)
        {
            throw new ArgumentException(
                $"A session duration must be positive but was {duration}.", parameterName);
        }

        if (duration.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentException(
                $"A session duration must be a whole number of seconds but was {duration}.",
                parameterName);
        }

        if (duration.Ticks > DateTimeOffset.MaxValue.UtcTicks)
        {
            throw new ArgumentException("A session duration cannot exceed the UTC timestamp range.", parameterName);
        }
    }
}
