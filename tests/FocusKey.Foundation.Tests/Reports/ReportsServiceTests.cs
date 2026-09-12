using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Reports;

public sealed class ReportsServiceTests
{
    [Fact]
    public async Task WeeklyTotalsCountStatusesAndOnlyCompletedDurations()
    {
        using var store = new SessionStore();
        var day = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
        var rows = new[]
        {
            Finished(day.AddHours(1), SessionType.Work, SessionStatus.Completed, 25),
            Finished(day.AddHours(2), SessionType.Work, SessionStatus.Completed, 15),
            Finished(day.AddHours(3), SessionType.Break, SessionStatus.Completed, 10),
            Finished(day.AddHours(4), SessionType.Work, SessionStatus.Stopped, 5),
            Finished(day.AddHours(5), SessionType.Break, SessionStatus.Interrupted, 4),
            TestSessions.Running(startedAt: day.AddHours(6))
        };
        foreach (var row in rows) await store.Repository.AddAsync(row);

        var snapshot = await Service(store, TimeZoneInfo.Utc).ReadAsync(ReportPeriod.Weekly, DateOnly.FromDateTime(day.DateTime));
        Assert.Equal(6, snapshot.Totals.Started);
        Assert.Equal(4, snapshot.Totals.WorkStarted);
        Assert.Equal(2, snapshot.Totals.BreakStarted);
        Assert.Equal(2, snapshot.Totals.CompletedWork);
        Assert.Equal(1, snapshot.Totals.CompletedBreak);
        Assert.Equal(1, snapshot.Totals.Stopped);
        Assert.Equal(1, snapshot.Totals.Interrupted);
        Assert.Equal(1, snapshot.Totals.Running);
        Assert.Equal(TimeSpan.FromMinutes(40), snapshot.Totals.FocusTime);
        Assert.Equal(TimeSpan.FromMinutes(10), snapshot.Totals.BreakTime);
        Assert.Equal(7, snapshot.Trend.Count);
        Assert.Equal(6, snapshot.Trend[6].Totals.Started);
        Assert.Equal(0, snapshot.LeadingFocusPeriods.Single().StartHour);
        Assert.Equal(2, snapshot.LeadingFocusPeriods.Single().CompletedWork);
        Assert.Equal(50.0, snapshot.Totals.CompletionRate);
        Assert.Equal(80.0, snapshot.Totals.WorkShare);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(-7)]
    [InlineData(14)]
    [InlineData(-12)]
    public async Task RangesUseLocalHalfOpenDatesAndProduceWeeklyDailyBins(int offset)
    {
        using var store = new SessionStore();
        var zone = FixedZone(offset);
        var localStart = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.FromHours(offset));
        var before = Finished(localStart.ToUniversalTime().AddTicks(-1), SessionType.Work, SessionStatus.Completed, 1);
        var first = Finished(localStart.ToUniversalTime(), SessionType.Work, SessionStatus.Completed, 2);
        var last = Finished(localStart.AddDays(7).ToUniversalTime().AddTicks(-1), SessionType.Work, SessionStatus.Completed, 3);
        var after = Finished(localStart.AddDays(7).ToUniversalTime(), SessionType.Work, SessionStatus.Completed, 4);
        foreach (var row in new[] { before, first, last, after }) await store.Repository.AddAsync(row);

        var snapshot = await Service(store, zone).ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 9, 13));
        Assert.Equal(new DateOnly(2026, 9, 7), snapshot.Range.Start);
        Assert.Equal(7, snapshot.Trend.Count);
        Assert.Equal(1, snapshot.Trend[0].Totals.Started);
        Assert.Equal(1, snapshot.Trend[6].Totals.Started);
        Assert.Equal(2, snapshot.Totals.Started);
    }

    [Fact]
    public async Task MonthlyTrendIsCalendarWeeksClippedToMonth()
    {
        using var store = new SessionStore();
        var zone = TimeZoneInfo.Utc;
        foreach (var date in new[] { new DateTimeOffset(2026, 2, 1, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 2, 2, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero) })
            await store.Repository.AddAsync(Finished(date, SessionType.Work, SessionStatus.Completed, 5));
        var snapshot = await Service(store, zone).ReadAsync(ReportPeriod.Monthly, new DateOnly(2026, 2, 15));
        Assert.Equal(new DateOnly(2026, 2, 1), snapshot.Range.Start);
        Assert.Equal(new DateOnly(2026, 3, 1), snapshot.Range.End);
        Assert.Equal(5, snapshot.Trend.Count);
        Assert.Equal("2026-02-01 – 2026-02-01", snapshot.Trend[0].Label);
        Assert.Equal(1, snapshot.Trend[0].Totals.Started);
        Assert.Equal(1, snapshot.Trend[1].Totals.Started);
        Assert.Equal(2, snapshot.Totals.Started);
    }

    [Fact]
    public async Task WeeklyComparisonExcludesOutsideRowsAndReportsFocusDifference()
    {
        using var store = new SessionStore();
        var monday = new DateTimeOffset(2026, 9, 7, 0, 0, 0, TimeSpan.Zero);
        await store.Repository.AddAsync(Finished(monday.AddDays(-1), SessionType.Work, SessionStatus.Completed, 9));
        await store.Repository.AddAsync(Finished(monday.AddDays(-7), SessionType.Work, SessionStatus.Completed, 7));
        await store.Repository.AddAsync(Finished(monday.AddDays(1), SessionType.Work, SessionStatus.Completed, 5));
        await store.Repository.AddAsync(Finished(monday.AddDays(-7).AddTicks(-1), SessionType.Work, SessionStatus.Completed, 30));
        await store.Repository.AddAsync(Finished(monday.AddDays(7), SessionType.Work, SessionStatus.Completed, 30));
        var snapshot = await Service(store, TimeZoneInfo.Utc).ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 9, 13));
        Assert.Equal(1, snapshot.Totals.Started);
        Assert.Equal(TimeSpan.FromMinutes(5), snapshot.WeekFocus);
        Assert.Equal(TimeSpan.FromMinutes(16), snapshot.PreviousWeekFocus);
        Assert.Equal(TimeSpan.FromMinutes(-11), snapshot.WeekDifference);
    }

    [Fact]
    public async Task EmptyPeriodsHaveZeroBinsAndNoFocusLeaders()
    {
        using var store = new SessionStore();
        var weekly = await Service(store, TimeZoneInfo.Utc).ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 9, 2));
        Assert.All(weekly.Trend, b => Assert.Equal(0, b.Totals.Started));
        Assert.Empty(weekly.LeadingFocusPeriods);
        Assert.Null(weekly.Totals.CompletionRate);
        Assert.Null(weekly.Totals.WorkShare);
    }

    [Theory]
    [InlineData(3, 10, 23)]
    [InlineData(10, 10, 25)]
    public async Task DstRepeatedAndSkippedHoursAreAssignedByLocalStartDate(int month, int day, int hours)
    {
        using var store = new SessionStore();
        var zone = TimeZoneInfo.CreateCustomTimeZone("Report DST", TimeSpan.FromHours(2), "Report DST", "Standard", "Daylight", new[]
        {
            TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 10),
                TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 10, 10))
        });
        var localDate = new DateOnly(2026, month, day);
        var start = FocusKey.Foundation.Today.TodayService.NextDay(localDate.AddDays(-1), zone);
        for (int hour = -1; hour <= hours; hour++)
            await store.Repository.AddAsync(Finished(start.AddHours(hour), SessionType.Work, SessionStatus.Completed, 1));
        var snapshot = await Service(store, zone).ReadAsync(ReportPeriod.Weekly, localDate);
        Assert.Equal(7, snapshot.Trend.Count);
        Assert.Equal(hours, snapshot.Trend[6].Totals.Started);
    }

    [Fact]
    public async Task ReadIsReadOnlyAndCancellationIsHonored()
    {
        using var store = new SessionStore();
        var before = store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service(store, TimeZoneInfo.Utc).ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 9, 2), cts.Token));
        Assert.Equal(before, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions"));
    }

    [Fact]
    public async Task RefreshReflectsRealEngineLifecycleWithoutWritingReports()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var engine = new SessionCoordinator(store.Repository, clock); await engine.InitializeAsync();
        var service = Service(store, TimeZoneInfo.Utc, clock);
        var running = await engine.StartAsync(SessionType.Work);
        Assert.Equal(1, (await service.ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 9, 2))).Totals.Running);
        clock.Advance(TimeSpan.FromMinutes(3)); await engine.StopAsync(running.Id);
        var stopped = await service.ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 9, 2));
        Assert.Equal(1, stopped.Totals.Stopped);
        Assert.Equal(TimeSpan.Zero, stopped.Totals.FocusTime);
        var next = await engine.StartAsync(SessionType.Work);
        clock.Set(next.PlannedEndAt.AddHours(1));
        var overdue = await service.ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 9, 2));
        Assert.Equal(1, overdue.Totals.Running);
        Assert.Equal(next, await store.Repository.GetAsync(next.Id));
        await engine.CompleteIfDueAsync();
        var completed = await service.ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 9, 2));
        Assert.Equal(TimeSpan.FromMinutes(30), completed.Totals.FocusTime);
        Assert.Equal(50.0, completed.Totals.CompletionRate);
        var brk = await engine.StartAsync(SessionType.Break);
        clock.Set(brk.PlannedEndAt); await engine.CompleteIfDueAsync();
        Assert.Equal(TimeSpan.FromMinutes(10), (await service.ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 9, 2))).Totals.BreakTime);
    }

    [Theory]
    [InlineData(SessionType.Work, SessionStatus.Stopped)]
    [InlineData(SessionType.Work, SessionStatus.Interrupted)]
    [InlineData(SessionType.Break, SessionStatus.Stopped)]
    [InlineData(SessionType.Break, SessionStatus.Interrupted)]
    public async Task NonCompletedActualTimeNeverCountsAsCompletedTime(SessionType type, SessionStatus status)
    {
        using var store = new SessionStore();
        await store.Repository.AddAsync(TestSessions.Finished(status, type: type, actualDuration: TimeSpan.FromMinutes(12)));
        var report = await Service(store, TimeZoneInfo.Utc).ReadAsync(ReportPeriod.Weekly, new(2026, 9, 2));
        Assert.Equal(TimeSpan.Zero, report.Totals.FocusTime);
        Assert.Equal(TimeSpan.Zero, report.Totals.BreakTime);
        Assert.Equal(0.0, report.Totals.CompletionRate);
        Assert.Null(report.Totals.WorkShare);
        Assert.Equal(1, report.Totals.Started);
        Assert.Empty(report.LeadingFocusPeriods);
    }

    [Fact]
    public async Task OvernightDurationBelongsWhollyToStartDayAndFocusTiesAreHonest()
    {
        using var store = new SessionStore();
        var start = new DateTimeOffset(2026, 9, 2, 23, 50, 0, TimeSpan.Zero);
        await store.Repository.AddAsync(Finished(start, SessionType.Work, SessionStatus.Completed, 30));
        await store.Repository.AddAsync(Finished(start.AddHours(-12), SessionType.Work, SessionStatus.Completed, 30));
        var report = await Service(store, TimeZoneInfo.Utc).ReadAsync(ReportPeriod.Weekly, new(2026, 9, 2));
        Assert.Equal(TimeSpan.FromHours(1), report.Totals.FocusTime);
        Assert.Equal(new[] { 9, 21 }, report.LeadingFocusPeriods.Select(p => p.StartHour));
        Assert.All(report.LeadingFocusPeriods, p => Assert.Equal(1, p.CompletedWork));
        Assert.Equal(TimeSpan.FromHours(1), report.Trend[6].Totals.FocusTime);
        Assert.Equal(TimeSpan.Zero, (await Service(store, TimeZoneInfo.Utc).ReadAsync(ReportPeriod.Weekly, new(2026, 9, 10))).Totals.FocusTime);
    }

    [Fact]
    public async Task LeapMonthWeeklyBinsIncludeEveryDayOnceAndExcludeAdjacentMonths()
    {
        using var store = new SessionStore();
        var first = new DateTimeOffset(2028, 2, 1, 12, 0, 0, TimeSpan.Zero);
        for (int day = -1; day <= 29; day++)
            await store.Repository.AddAsync(Finished(first.AddDays(day), SessionType.Work, SessionStatus.Completed, 1));
        var report = await Service(store, TimeZoneInfo.Utc).ReadAsync(ReportPeriod.Monthly, new(2028, 2, 29));
        Assert.Equal(29, report.Totals.CompletedWork);
        Assert.Equal(29, report.Trend.Sum(b => b.Totals.CompletedWork));
        Assert.Equal(new[] { 6, 7, 7, 7, 2 }, report.Trend.Select(b => b.Totals.CompletedWork));
        Assert.Equal(new DateOnly(2028, 3, 1), report.Range.End);
    }

    [Fact]
    public async Task WeekAcrossYearAndMonthlyComparisonUseEntireSelectedWeek()
    {
        using var store = new SessionStore();
        await store.Repository.AddAsync(Finished(new(2025, 12, 31, 12, 0, 0, TimeSpan.Zero), SessionType.Work, SessionStatus.Completed, 30));
        await store.Repository.AddAsync(Finished(new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero), SessionType.Work, SessionStatus.Completed, 10));
        var report = await Service(store, TimeZoneInfo.Utc).ReadAsync(ReportPeriod.Monthly, new(2026, 1, 1));
        Assert.Equal(TimeSpan.FromMinutes(10), report.Totals.FocusTime);
        Assert.Equal(TimeSpan.FromMinutes(40), report.WeekFocus);
        Assert.Equal(new DateOnly(2025, 12, 26), report.ComparisonWeek.Start);
        Assert.Equal(new DateOnly(2026, 1, 2), report.ComparisonWeek.End);
    }

    [Fact]
    public async Task EachReadResolvesZoneAndCurrentDateFromInjectedClock()
    {
        using var store = new SessionStore();
        var now = new DateTimeOffset(2026, 9, 2, 1, 0, 0, TimeSpan.Zero);
        await store.Repository.AddAsync(Finished(now, SessionType.Work, SessionStatus.Completed, 30));
        TimeZoneInfo zone = FixedZone(3);
        var service = new ReportsService(store.Repository, new ManualTimeProvider(now), () => zone);
        Assert.Equal(new DateOnly(2026, 9, 2), service.CurrentDate());
        Assert.Equal(1, (await service.ReadAsync(ReportPeriod.Weekly, service.CurrentDate())).Totals.Started);
        zone = FixedZone(-7);
        Assert.Equal(new DateOnly(2026, 9, 1), service.CurrentDate());
        Assert.Equal(1, (await service.ReadAsync(ReportPeriod.Weekly, service.CurrentDate())).Totals.Started);
        Assert.Equal(0, (await service.ReadAsync(ReportPeriod.Weekly, new(2026, 8, 20))).Totals.Started);
    }

    [Theory]
    [InlineData(ReportPeriod.Weekly)]
    [InlineData(ReportPeriod.Monthly)]
    public async Task SupportedDateLimitsDoNotOverflowQueryEnvelopeOrComparison(ReportPeriod period)
    {
        using var store = new SessionStore();
        var service = Service(store, TimeZoneInfo.Utc);
        Assert.Equal(0, (await service.ReadAsync(period, ReportRange.MinimumDate)).Totals.Started);
        Assert.Equal(0, (await service.ReadAsync(period, ReportRange.MaximumDate)).Totals.Started);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReadAsync(period, DateOnly.MinValue));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.ReadAsync(period, DateOnly.MaxValue));
    }

    private static ReportsService Service(SessionStore store, TimeZoneInfo zone, TimeProvider? clock = null) => new(store.Repository, clock ?? new ManualTimeProvider(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)), () => zone);
    private static TimeZoneInfo FixedZone(int hours) => TimeZoneInfo.CreateCustomTimeZone($"UTC{hours:+0;-0}", TimeSpan.FromHours(hours), "Test", "Test");
    private static SessionRecord Finished(DateTimeOffset start, SessionType type, SessionStatus status, int minutes) => TestSessions.Finished(status, startedAt: start, type: type, plannedDuration: TimeSpan.FromMinutes(minutes));
}
