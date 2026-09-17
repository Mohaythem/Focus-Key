using FocusKey.Foundation.History;
using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Reports;

public enum ReportPeriod { Weekly, Monthly, Yearly }

public sealed record ReportRange(DateOnly Start, DateOnly End)
{
    public static DateOnly MinimumDate => new(2, 1, 8);
    public static DateOnly MaximumDate => new(9998, 12, 24);

    public static ReportRange For(ReportPeriod period, DateOnly date)
    {
        if (date < MinimumDate || date > MaximumDate) throw new ArgumentOutOfRangeException(nameof(date));
        return period switch
        {
            ReportPeriod.Weekly => new(date.AddDays(-6), date.AddDays(1)),
            ReportPeriod.Monthly => new(new(date.Year, date.Month, 1), new DateOnly(date.Year, date.Month, 1).AddMonths(1)),
            ReportPeriod.Yearly => new(new(date.Year, 1, 1), new DateOnly(date.Year + 1, 1, 1)),
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
            .Where(s => s.Status != SessionStatus.Running && s.Type == type)
            .Aggregate(0L, (sum, s) => checked(sum + s.EffectiveDuration.Ticks)));
        return new(rows.Length, rows.Count(s => s.Type == SessionType.Work), rows.Count(s => s.Type == SessionType.Break),
            rows.Count(s => s.Type == SessionType.Work && s.Status == SessionStatus.Completed),
            rows.Count(s => s.Type == SessionType.Break && s.Status == SessionStatus.Completed),
            rows.Count(s => s.Status == SessionStatus.Stopped), rows.Count(s => s.Status == SessionStatus.Interrupted),
            rows.Count(s => s.Status == SessionStatus.Running), Duration(SessionType.Work), Duration(SessionType.Break));
    }

    public static readonly ReportTotals Empty = From([]);
}

public sealed record ReportBucket(string Label, ReportTotals Totals);
public sealed record FocusPeriod(int StartHour, int CompletedWork);
public sealed record ReportsSnapshot(ReportPeriod Period, ReportRange Range, TimeZoneInfo TimeZone,
    DateTimeOffset ObservedAt, ReportTotals Totals, IReadOnlyList<ReportBucket> Trend,
    IReadOnlyList<FocusPeriod> LeadingFocusPeriods, ReportRange ComparisonWeek,
    TimeSpan WeekFocus, TimeSpan PreviousWeekFocus, StreakStatistics? Streaks = null,
    TimeSpan? PreviousPeriodFocus = null,
    bool IsYearEligible = false,
    DateOnly? EarliestHistoryDate = null)
{
    public StreakStatistics Streaks { get; init; } = Streaks ?? StreakStatistics.Zero;
    public TimeSpan WeekDifference => WeekFocus - PreviousWeekFocus;
    public TimeSpan PreviousPeriodDuration => PreviousPeriodFocus ?? (Period == ReportPeriod.Weekly ? PreviousWeekFocus : TimeSpan.Zero);
    public TimeSpan PeriodDifference => Totals.FocusTime - PreviousPeriodDuration;
}

/// <summary>Read-only report projection; UTC storage, local start-date membership, no lifecycle writes.</summary>
public sealed class ReportsService
{
    private readonly ISessionRepository _repository;
    private readonly IHistoricalFocusRepository? _history;
    private readonly TimeProvider _time;
    private readonly Func<TimeZoneInfo> _zone;

    public ReportsService(
        ISessionRepository repository,
        IHistoricalFocusRepository? historicalFocus,
        TimeProvider? timeProvider = null,
        Func<TimeZoneInfo>? localTimeZone = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _history = historicalFocus;
        _time = timeProvider ?? TimeProvider.System;
        _zone = localTimeZone ?? (() => TimeZoneInfo.Local);
    }

    public ReportsService(
        ISessionRepository repository,
        TimeProvider? timeProvider = null,
        Func<TimeZoneInfo>? localTimeZone = null)
        : this(repository, null, timeProvider, localTimeZone)
    {
    }

    /// <summary>
    /// Computes the dynamic Y-axis ceiling in step-hour increments based on the maximum focus duration.
    /// axisCeiling = ceil(maxHours / stepHours) * stepHours, with a minimum useful ceiling of stepHours.
    /// Defaults to 5-hour increments for weekly reports, and 10-hour increments for monthly reports.
    /// </summary>
    public static int ComputeCeilingHours(double maxSeconds, int stepHours = 5)
    {
        if (stepHours <= 0) stepHours = 5;
        if (maxSeconds <= 0) return stepHours;
        double maxHours = maxSeconds / 3600.0;
        int ceiling = (int)Math.Ceiling(maxHours / (double)stepHours) * stepHours;
        return Math.Max(stepHours, ceiling);
    }

    /// <summary>
    /// Computes an appropriate grid step size in hours for yearly charts based on max monthly focus.
    /// Produces 4 to 6 legible grid intervals regardless of whether focus volume is light or heavy.
    /// </summary>
    public static int ComputeYearlyStepHours(double maxSeconds)
    {
        double maxHours = maxSeconds / 3600.0;
        if (maxHours <= 50) return 10;
        if (maxHours <= 120) return 20;
        if (maxHours <= 250) return 50;
        return 100;
    }

    /// <summary>
    /// Determines if Yearly reports are available. Requires at least one full calendar year
    /// of history (from the earliest recorded native session or imported focus date up to today).
    /// </summary>
    public static bool IsYearEligible(DateOnly? earliestDate, DateOnly today)
    {
        if (earliestDate is null) return false;
        if (earliestDate.Value > today) return false;
        return earliestDate.Value.AddYears(1) <= today;
    }

    public DateOnly CurrentDate() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), _zone()).DateTime);

    public async Task<ReportsSnapshot> ReadAsync(ReportPeriod period, DateOnly date, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var range = ReportRange.For(period, date);
        var week = ReportRange.For(ReportPeriod.Weekly, date);
        var previous = new ReportRange(week.Start.AddDays(-7), week.Start);
        DateOnly prevPeriodStart;
        if (period == ReportPeriod.Yearly)
        {
            prevPeriodStart = range.Start > new DateOnly(2, 1, 1) ? range.Start.AddYears(-1) : range.Start;
        }
        else if (period == ReportPeriod.Monthly)
        {
            prevPeriodStart = range.Start > new DateOnly(1, 2, 1) ? range.Start.AddMonths(-1) : range.Start;
        }
        else
        {
            prevPeriodStart = previous.Start;
        }
        var prevPeriodRange = new ReportRange(prevPeriodStart, range.Start);

        var from = range.Start < prevPeriodStart ? range.Start : prevPeriodStart;
        if (from > previous.Start) from = previous.Start;
        var to = range.End > week.End ? range.End : week.End;
        TimeZoneInfo zone = _zone();
        DateTimeOffset now = _time.GetUtcNow();

        // 1. Read native sessions
        var records = await _repository.GetStartedBetweenAsync(
            new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(-14),
            new DateTimeOffset(to.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)).AddHours(14), cancellationToken).ConfigureAwait(false);
        var localized = records.Select(s => (Session: s, Local: TimeZoneInfo.ConvertTime(s.StartedAt, zone))).ToArray();
        var selected = localized.Where(s => range.Contains(DateOnly.FromDateTime(s.Local.DateTime))).ToArray();
        var totals = ReportTotals.From(selected.Select(s => s.Session));

        // 2. Read imported historical focus
        var historyRecords = _history is not null
            ? await _history.GetBetweenAsync(from, to, cancellationToken).ConfigureAwait(false)
            : [];
        var rangeHistory = historyRecords.Where(h => range.Contains(h.Date)).ToArray();
        var rangeHistoryDuration = rangeHistory.Aggregate(TimeSpan.Zero, (acc, h) => acc + h.Duration);

        // Historical focus adds directly to FocusTime without fabricating native sessions or distorting completion rate
        totals = totals with { FocusTime = totals.FocusTime + rangeHistoryDuration };

        var buckets = new List<ReportBucket>();
        if (period == ReportPeriod.Weekly)
        {
            for (var day = range.Start; day < range.End; day = day.AddDays(1))
            {
                var nativeBucket = ReportTotals.From(selected.Where(s => DateOnly.FromDateTime(s.Local.DateTime) == day).Select(s => s.Session));
                var dayHistoryTime = rangeHistory.Where(h => h.Date == day).Aggregate(TimeSpan.Zero, (acc, h) => acc + h.Duration);
                buckets.Add(new(day.ToString("yyyy-MM-dd"), nativeBucket with { FocusTime = nativeBucket.FocusTime + dayHistoryTime }));
            }
        }
        else if (period == ReportPeriod.Monthly)
        {
            for (var start = range.Start; start < range.End;)
            {
                var nextMonday = ReportRange.WeekStart(start).AddDays(7);
                var end = nextMonday < range.End ? nextMonday : range.End;
                var slice = new ReportRange(start, end);
                var nativeBucket = ReportTotals.From(selected.Where(s => slice.Contains(DateOnly.FromDateTime(s.Local.DateTime))).Select(s => s.Session));
                var sliceHistoryTime = rangeHistory.Where(h => slice.Contains(h.Date)).Aggregate(TimeSpan.Zero, (acc, h) => acc + h.Duration);
                buckets.Add(new($"{start:yyyy-MM-dd} – {end.AddDays(-1):yyyy-MM-dd}", nativeBucket with { FocusTime = nativeBucket.FocusTime + sliceHistoryTime }));
                start = end;
            }
        }
        else // Yearly: 12 monthly buckets across the calendar year (Jan through Dec)
        {
            for (int m = 1; m <= 12; m++)
            {
                var monthStart = new DateOnly(range.Start.Year, m, 1);
                var monthEnd = monthStart.AddMonths(1);
                var slice = new ReportRange(monthStart, monthEnd);
                var nativeBucket = ReportTotals.From(selected.Where(s => slice.Contains(DateOnly.FromDateTime(s.Local.DateTime))).Select(s => s.Session));
                var sliceHistoryTime = rangeHistory.Where(h => slice.Contains(h.Date)).Aggregate(TimeSpan.Zero, (acc, h) => acc + h.Duration);
                buckets.Add(new(monthStart.ToString("yyyy-MM"), nativeBucket with { FocusTime = nativeBucket.FocusTime + sliceHistoryTime }));
            }
        }

        var focus = selected.Where(s => s.Session.Type == SessionType.Work && s.Session.Status == SessionStatus.Completed)
            .GroupBy(s => s.Local.Hour / 3 * 3).Select(g => new FocusPeriod(g.Key, g.Count())).ToArray();
        var leading = focus.Length == 0 ? [] : focus.Where(p => p.CompletedWork == focus.Max(f => f.CompletedWork)).OrderBy(p => p.StartHour).ToArray();

        TimeSpan RangeTime(ReportRange span)
        {
            var native = ReportTotals.From(localized.Where(s => span.Contains(DateOnly.FromDateTime(s.Local.DateTime))).Select(s => s.Session)).FocusTime;
            var hist = historyRecords.Where(h => span.Contains(h.Date)).Aggregate(TimeSpan.Zero, (acc, h) => acc + h.Duration);
            return native + hist;
        }

        TimeSpan weekFocus = RangeTime(week);
        TimeSpan previousWeekFocus = RangeTime(previous);
        TimeSpan previousPeriodFocus = period == ReportPeriod.Weekly ? previousWeekFocus : RangeTime(prevPeriodRange);

        // 3. Streak statistics & Year eligibility across native sessions and imported focus days (hours > 0)
        var allRecords = await _repository.GetStartedBetweenAsync(
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            cancellationToken).ConfigureAwait(false);
        var allHistory = _history is not null
            ? await _history.GetAllAsync(cancellationToken).ConfigureAwait(false)
            : [];
        var qualifyingDates = allHistory.Where(h => h.Duration > TimeSpan.Zero || h.SourceHours > 0).Select(h => h.Date).Distinct();
        var streaks = StreakCalculator.Calculate(allRecords, qualifyingDates, CurrentDate(), zone);

        // Derive earliest history date across native sessions and imported historical data
        DateOnly? earliestNative = allRecords.Count > 0
            ? allRecords.Select(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.StartedAt, zone).DateTime)).Min()
            : null;
        DateOnly? earliestHistorical = allHistory.Count > 0
            ? allHistory.Where(h => h.Duration > TimeSpan.Zero || h.SourceHours > 0).Select(h => (DateOnly?)h.Date).Min()
            : null;

        DateOnly? earliestDate = (earliestNative, earliestHistorical) switch
        {
            ({ } n, { } h) => n < h ? n : h,
            ({ } n, null) => n,
            (null, { } h) => h,
            _ => null
        };

        bool isYearEligible = IsYearEligible(earliestDate, CurrentDate());

        cancellationToken.ThrowIfCancellationRequested();
        return new(period, range, zone, now, totals, buckets.AsReadOnly(), Array.AsReadOnly(leading),
            week, weekFocus, previousWeekFocus, streaks, previousPeriodFocus, isYearEligible, earliestDate);
    }
}
