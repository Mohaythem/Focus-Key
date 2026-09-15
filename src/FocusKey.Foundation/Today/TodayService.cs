using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Today;

public sealed record TodaySnapshot(DateOnly Date, TimeZoneInfo TimeZone, DateTimeOffset ObservedAt,
    DateTimeOffset NextDayAt, IReadOnlyList<SessionRecord> Sessions, SessionRecord? Running)
{
    public int CompletedWorkCount => Sessions.Count(s => s.Type == SessionType.Work && s.Status == SessionStatus.Completed);
    public int CompletedBreakCount => Sessions.Count(s => s.Type == SessionType.Break && s.Status == SessionStatus.Completed);
    public TimeSpan WorkTime => CreditedTime(SessionType.Work);
    public TimeSpan BreakTime => CreditedTime(SessionType.Break);
    public double? CompletionRate => Sessions.Count == 0 ? null :
        100.0 * Sessions.Count(s => s.Status == SessionStatus.Completed) / Sessions.Count;

    private TimeSpan CreditedTime(SessionType type) => TimeSpan.FromTicks(Sessions
        .Where(s => s.Type == type && s.Status != SessionStatus.Running)
        .Aggregate(0L, (total, session) => checked(total + session.EffectiveDuration.Ticks)));
}

/// <summary>Read-only daily projection. Session membership uses its local start date.</summary>
public sealed class TodayService(ISessionRepository repository, TimeProvider? timeProvider = null,
    Func<TimeZoneInfo>? localTimeZone = null)
{
    private readonly ISessionRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Func<TimeZoneInfo> _zone = localTimeZone ?? (() => TimeZoneInfo.Local);

    public async Task<TodaySnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        DateTimeOffset now = _time.GetUtcNow();
        TimeZoneInfo zone = _zone();
        DateOnly date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        // Query a bounded envelope, then compare actual local dates. This avoids assuming a
        // 24-hour day or forcing ambiguous/invalid local midnight into a single UTC offset.
        // TimeZoneInfo UTC offsets are bounded by +/-14 hours.
        DateTimeOffset nominal = new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        var candidates = await _repository.GetStartedBetweenAsync(nominal.AddHours(-14),
            nominal.AddDays(1).AddHours(14), cancellationToken).ConfigureAwait(false);
        var rows = candidates.Where(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.StartedAt, zone).DateTime) == date)
            .OrderBy(s => s.StartedAt).ThenBy(s => s.Id.ToText()).ToArray();
        SessionRecord? running = await _repository.GetRunningAsync(cancellationToken).ConfigureAwait(false);
        return new(date, zone, now, NextDay(date, zone), Array.AsReadOnly(rows), running);
    }

    public static DateTimeOffset NextDay(DateOnly date, TimeZoneInfo zone)
    {
        DateTime midnight = date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some zones advance at midnight, and historical date-line changes skip an entire day.
        // Find the first valid instant rather than assuming midnight always exists.
        DateTime valid = midnight;
        while (zone.IsInvalidTime(valid)) valid = valid.AddMinutes(1);
        if (valid != midnight)
        {
            long low = valid.AddMinutes(-1).Ticks, high = valid.Ticks;
            while (high - low > 1)
            {
                long middle = low + (high - low) / 2;
                if (zone.IsInvalidTime(new DateTime(middle, DateTimeKind.Unspecified))) low = middle;
                else high = middle;
            }
            valid = new DateTime(high, DateTimeKind.Unspecified);
        }
        TimeSpan offset = zone.IsAmbiguousTime(valid) ? zone.GetAmbiguousTimeOffsets(valid).Max() : zone.GetUtcOffset(valid);
        return new DateTimeOffset(valid, offset).ToUniversalTime();
    }
}
