using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Reports;

public enum ReportPeriod { Daily, Weekly, Monthly }

public sealed record ReportRange(DateOnly Start, DateOnly End)
{
    public static DateOnly MinimumDate => new(2, 1, 8);
    public static DateOnly MaximumDate => new(9998, 12, 24);

    public static ReportRange For(ReportPeriod period, DateOnly date)
    {
        if (date < MinimumDate || date > MaximumDate) throw new ArgumentOutOfRangeException(nameof(date));
        return period switch
        {
            ReportPeriod.Daily => new(date, date.AddDays(1)),
            ReportPeriod.Weekly => new(WeekStart(date), WeekStart(date).AddDays(7)),
            ReportPeriod.Monthly => new(new(date.Year, date.Month, 1), new DateOnly(date.Year, date.Month, 1).AddMonths(1)),
            _ => throw new ArgumentOutOfRangeException(nameof(period))
        };
    }

    public static DateOnly WeekStart(DateOnly date) => date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
    public bool Contains(DateOnly date) => date >= Start && date < End;
}

public sealed record ReportTotals(int Started, int WorkStarted, int BreakStarted, int CompletedWork,
    int CompletedBreak, int Stopped, int Interrupted, int Running, TimeSpan FocusTime, TimeSpan BreakTime)
{
    public int Completed => CompletedWork + CompletedBreak;
    public double? CompletionRate => Started == 0 ? null : 100.0 * Completed / Started;
    public double? WorkShare => FocusTime == TimeSpan.Zero && BreakTime == TimeSpan.Zero ? null :
        100.0 * FocusTime.Ticks / (FocusTime.Ticks + (double)BreakTime.Ticks);

    public static ReportTotals From(IEnumerable<SessionRecord> sessions)
    {
        var rows = sessions.ToArray();
        TimeSpan Duration(SessionType type) => TimeSpan.FromTicks(rows
            .Where(s => s.Status == SessionStatus.Completed && s.Type == type)
            .Aggregate(0L, (sum, s) => checked(sum + s.ActualDuration!.Value.Ticks)));
        return new(rows.Length, rows.Count(s => s.Type == SessionType.Work), rows.Count(s => s.Type == SessionType.Break),
            rows.Count(s => s.Type == SessionType.Work && s.Status == SessionStatus.Completed),
            rows.Count(s => s.Type == SessionType.Break && s.Status == SessionStatus.Completed),
            rows.Count(s => s.Status == SessionStatus.Stopped), rows.Count(s => s.Status == SessionStatus.Interrupted),
            rows.Count(s => s.Status == SessionStatus.Running), Duration(SessionType.Work), Duration(SessionType.Break));
    }
}

public sealed record ReportBucket(string Label, ReportTotals Totals);
public sealed record FocusPeriod(int StartHour, int CompletedWork);
public sealed record ReportsSnapshot(ReportPeriod Period, ReportRange Range, TimeZoneInfo TimeZone,
    DateTimeOffset ObservedAt, ReportTotals Totals, IReadOnlyList<ReportBucket> Trend,
    IReadOnlyList<FocusPeriod> LeadingFocusPeriods, ReportRange ComparisonWeek,
    TimeSpan WeekFocus, TimeSpan PreviousWeekFocus)
{
    public TimeSpan WeekDifference => WeekFocus - PreviousWeekFocus;
}

/// <summary>Read-only report projection; UTC storage, local start-date membership, no lifecycle writes.</summary>
public sealed class ReportsService(ISessionRepository repository, TimeProvider? timeProvider = null,
    Func<TimeZoneInfo>? localTimeZone = null)
{
    private readonly ISessionRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Func<TimeZoneInfo> _zone = localTimeZone ?? (() => TimeZoneInfo.Local);

    public DateOnly CurrentDate() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone()).DateTime);

    public async Task<ReportsSnapshot> ReadAsync(ReportPeriod period, DateOnly date, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var range = ReportRange.For(period, date);
        var week = ReportRange.For(ReportPeriod.Weekly, date);
        var previous = new ReportRange(week.Start.AddDays(-7), week.Start);
        var from = range.Start < previous.Start ? range.Start : previous.Start;
        var to = range.End > week.End ? range.End : week.End;
        TimeZoneInfo zone = _zone();
        DateTimeOffset now = _time.GetUtcNow();
        // One repository read gives totals, trends and comparisons the same persisted snapshot.
        // The +/-14h envelope includes DST/ambiguous midnight; actual local dates decide membership.
        var records = await _repository.GetStartedBetweenAsync(
            new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(-14),
            new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(14), cancellationToken).ConfigureAwait(false);
        var localized = records.Select(s => (Session: s, Local: TimeZoneInfo.ConvertTime(s.StartedAt, zone))).ToArray();
        var selected = localized.Where(s => range.Contains(DateOnly.FromDateTime(s.Local.DateTime))).ToArray();
        var totals = ReportTotals.From(selected.Select(s => s.Session));
        var buckets = new List<ReportBucket>();
        if (period == ReportPeriod.Daily)
        {
            for (int hour = 0; hour < 24; hour++)
                buckets.Add(new($"{hour:00}:00", ReportTotals.From(selected.Where(s => s.Local.Hour == hour).Select(s => s.Session))));
        }
        else if (period == ReportPeriod.Weekly)
        {
            for (var day = range.Start; day < range.End; day = day.AddDays(1))
                buckets.Add(new(day.ToString("yyyy-MM-dd"), ReportTotals.From(selected
                    .Where(s => DateOnly.FromDateTime(s.Local.DateTime) == day).Select(s => s.Session))));
        }
        else
        {
            // Calendar weeks clipped to the selected month; never borrow adjacent-month data.
            for (var start = range.Start; start < range.End;)
            {
                var nextMonday = ReportRange.WeekStart(start).AddDays(7);
                var end = nextMonday < range.End ? nextMonday : range.End;
                var slice = new ReportRange(start, end);
                buckets.Add(new($"{start:yyyy-MM-dd} – {end.AddDays(-1):yyyy-MM-dd}", ReportTotals.From(selected
                    .Where(s => slice.Contains(DateOnly.FromDateTime(s.Local.DateTime))).Select(s => s.Session))));
                start = end;
            }
        }
        var focus = selected.Where(s => s.Session.Type == SessionType.Work && s.Session.Status == SessionStatus.Completed)
            .GroupBy(s => s.Local.Hour / 3 * 3).Select(g => new FocusPeriod(g.Key, g.Count())).ToArray();
        var leading = focus.Length == 0 ? [] : focus.Where(p => p.CompletedWork == focus.Max(f => f.CompletedWork)).OrderBy(p => p.StartHour).ToArray();
        TimeSpan WeekTime(ReportRange span) => ReportTotals.From(localized
            .Where(s => span.Contains(DateOnly.FromDateTime(s.Local.DateTime))).Select(s => s.Session)).FocusTime;
        cancellationToken.ThrowIfCancellationRequested();
        return new(period, range, zone, now, totals, buckets.AsReadOnly(), Array.AsReadOnly(leading),
            week, WeekTime(week), WeekTime(previous));
    }
}
