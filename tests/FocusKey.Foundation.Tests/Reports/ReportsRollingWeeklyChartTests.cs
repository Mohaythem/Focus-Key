using FocusKey.Foundation.Data;
using FocusKey.Foundation.History;
using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Reports;

public sealed class ReportsRollingWeeklyChartTests
{
    [Fact]
    public void ReportPeriod_HasOnlyWeeklyAndMonthly_DailyIsEliminated()
    {
        var periods = Enum.GetValues<ReportPeriod>();
        Assert.Equal(2, periods.Length);
        Assert.Contains(ReportPeriod.Weekly, periods);
        Assert.Contains(ReportPeriod.Monthly, periods);
    }

    [Fact]
    public void ReportRange_Weekly_ProducesRolling7DayRangeEndingOnAnchorDate()
    {
        var anchor = new DateOnly(2026, 9, 12); // Saturday
        var range = ReportRange.For(ReportPeriod.Weekly, anchor);

        Assert.Equal(new DateOnly(2026, 9, 6), range.Start); // Sunday (-6 days)
        Assert.Equal(new DateOnly(2026, 9, 13), range.End);   // Sunday (+1 day, half-open)

        // Verifying range contains exactly 7 days
        int dayCount = 0;
        for (var d = range.Start; d < range.End; d = d.AddDays(1))
        {
            dayCount++;
            Assert.True(range.Contains(d));
        }
        Assert.Equal(7, dayCount);
        Assert.False(range.Contains(new DateOnly(2026, 9, 5)));
        Assert.False(range.Contains(new DateOnly(2026, 9, 13)));
    }

    [Fact]
    public async Task ReadAsync_Weekly_GeneratesExactly7ChronologicalBucketsWithAnchorAsRightmost()
    {
        using var store = new SessionStore();
        var anchor = new DateOnly(2026, 9, 12);
        var service = new ReportsService(store.Repository, new ManualTimeProvider(new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero)), () => TimeZoneInfo.Utc);

        var snapshot = await service.ReadAsync(ReportPeriod.Weekly, anchor);

        Assert.Equal(7, snapshot.Trend.Count);
        // Previous 6 days in chronological order, ending with anchor date on the right
        Assert.Equal("2026-09-06", snapshot.Trend[0].Label);
        Assert.Equal("2026-09-07", snapshot.Trend[1].Label);
        Assert.Equal("2026-09-08", snapshot.Trend[2].Label);
        Assert.Equal("2026-09-09", snapshot.Trend[3].Label);
        Assert.Equal("2026-09-10", snapshot.Trend[4].Label);
        Assert.Equal("2026-09-11", snapshot.Trend[5].Label);
        Assert.Equal("2026-09-12", snapshot.Trend[6].Label); // Rightmost is anchor date
    }

    [Fact]
    public async Task ReadAsync_Weekly_CurrentDateProducesTodayAsRightmostDate()
    {
        using var store = new SessionStore();
        var today = new DateOnly(2026, 9, 15);
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 15, 14, 30, 0, TimeSpan.Zero));
        var service = new ReportsService(store.Repository, clock, () => TimeZoneInfo.Utc);

        var snapshot = await service.ReadAsync(ReportPeriod.Weekly, service.CurrentDate());

        Assert.Equal(7, snapshot.Trend.Count);
        Assert.Equal(today.ToString("yyyy-MM-dd"), snapshot.Trend[6].Label);
        Assert.Equal(today.AddDays(-6).ToString("yyyy-MM-dd"), snapshot.Trend[0].Label);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(30 * 60, 5)]
    [InlineData(60 * 60, 5)]
    [InlineData(120 * 60, 5)]
    [InlineData(180 * 60, 5)] // 3h -> 5
    [InlineData(300 * 60, 5)] // 5h -> 5
    [InlineData(301 * 60, 10)] // 5h 1s -> 10
    [InlineData(480 * 60, 10)] // 8h -> 10
    [InlineData(600 * 60, 10)] // 10h -> 10
    [InlineData(660 * 60, 15)] // 11h -> 15
    [InlineData(1140 * 60, 20)] // 19h -> 20
    [InlineData(1200 * 60, 20)] // 20h -> 20
    public void ComputeCeilingHours_ScalesIn5HourIncrementsWithMinimum5Hours(double maxSeconds, int expectedCeilingHours)
    {
        int ceiling = ReportsService.ComputeCeilingHours(maxSeconds);
        Assert.Equal(expectedCeilingHours, ceiling);
        Assert.True(ceiling % 5 == 0);
        Assert.True(ceiling >= 5);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(30 * 60, 10)] // 30m -> 10
    [InlineData(5 * 3600, 10)] // 5h -> 10
    [InlineData(10 * 3600, 10)] // 10h -> 10
    [InlineData(10 * 3600 + 1, 20)] // 10h 1s -> 20
    [InlineData(18 * 3600, 20)] // 18h -> 20
    [InlineData(20 * 3600, 20)] // 20h -> 20
    [InlineData(21 * 3600, 30)] // 21h -> 30
    [InlineData(30 * 3600, 30)] // 30h -> 30
    [InlineData(35 * 3600, 40)] // 35h -> 40
    [InlineData(50 * 3600, 50)] // 50h -> 50
    public void ComputeCeilingHours_ScalesIn10HourIncrementsWithMinimum10HoursForMonthly(double maxSeconds, int expectedCeilingHours)
    {
        int ceiling = ReportsService.ComputeCeilingHours(maxSeconds, 10);
        Assert.Equal(expectedCeilingHours, ceiling);
        Assert.True(ceiling % 10 == 0);
        Assert.True(ceiling >= 10);
    }

    [Fact]
    public async Task ReadAsync_ZeroValueDays_RemainVisibleWithCleanBaseline()
    {
        using var store = new SessionStore();
        var anchor = new DateOnly(2026, 9, 12);
        // Only seed one session on Friday Sep 11
        var friday = new DateTimeOffset(2026, 9, 11, 10, 0, 0, TimeSpan.Zero);
        await store.Repository.AddAsync(TestSessions.Finished(SessionStatus.Completed, startedAt: friday, plannedDuration: TimeSpan.FromHours(2)));

        var service = new ReportsService(store.Repository, new ManualTimeProvider(new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero)), () => TimeZoneInfo.Utc);
        var snapshot = await service.ReadAsync(ReportPeriod.Weekly, anchor);

        Assert.Equal(7, snapshot.Trend.Count);
        // Days 0-4 (Sep 6 to Sep 10) have zero focus
        for (int i = 0; i <= 4; i++)
        {
            Assert.Equal(TimeSpan.Zero, snapshot.Trend[i].Totals.FocusTime);
            Assert.Equal(0, snapshot.Trend[i].Totals.Started);
        }
        // Day 5 (Friday Sep 11) has 2h focus
        Assert.Equal(TimeSpan.FromHours(2), snapshot.Trend[5].Totals.FocusTime);
        // Day 6 (Saturday Sep 12) has zero focus
        Assert.Equal(TimeSpan.Zero, snapshot.Trend[6].Totals.FocusTime);
    }

    [Fact]
    public async Task ReadAsync_HistoricalFocus_ContributesAccuratelyToRollingDayBuckets()
    {
        using var temp = new TempDirectory();
        string dbPath = Path.Combine(temp.Path, "focus_key.db");
        var connFactory = new SqliteConnectionFactory(dbPath);
        new DatabaseBootstrapper(connFactory).Initialize();
        var sessionRepo = new SqliteSessionRepository(connFactory);
        var histRepo = new SqliteHistoricalFocusRepository(connFactory);

        var anchor = new DateOnly(2026, 9, 12);
        // Import 3.5h on Wednesday Sep 9 and 8.0h on Saturday Sep 12 (anchor date)
        await histRepo.ImportAsync([
            new(new DateOnly(2026, 9, 9), "ProjectA", 3.5),
            new(new DateOnly(2026, 9, 12), "ProjectB", 8.0)
        ], 2, 0);

        var service = new ReportsService(sessionRepo, histRepo, new ManualTimeProvider(new DateTimeOffset(2026, 9, 12, 12, 0, 0, TimeSpan.Zero)), () => TimeZoneInfo.Utc);
        var snapshot = await service.ReadAsync(ReportPeriod.Weekly, anchor);

        Assert.Equal(TimeSpan.FromHours(11.5), snapshot.Totals.FocusTime);
        Assert.Equal(TimeSpan.FromHours(3.5), snapshot.Trend[3].Totals.FocusTime); // Sep 9
        Assert.Equal(TimeSpan.FromHours(8.0), snapshot.Trend[6].Totals.FocusTime); // Sep 12 (rightmost)

        // Verify ceiling calculation for 8h maximum
        int ceiling = ReportsService.ComputeCeilingHours(snapshot.Trend.Max(b => b.Totals.FocusTime.TotalSeconds));
        Assert.Equal(10, ceiling);
    }

    [Fact]
    public void ReportsController_DefaultPeriodIsWeekly()
    {
        using var controller = new ReportsController((_, _, _) => Task.FromResult<ReportsSnapshot>(null!), () => new DateOnly(2026, 9, 12), _ => { });
        Assert.Equal(ReportPeriod.Weekly, controller.Period);
    }

    [Fact]
    public async Task ReportsController_MoveAsync_WeeklyStepsByExactly7Days()
    {
        var anchor = new DateOnly(2026, 9, 12);
        using var controller = new ReportsController(
            (period, date, _) => Task.FromResult(new ReportsSnapshot(period, ReportRange.For(period, date), TimeZoneInfo.Utc,
                DateTimeOffset.UtcNow, new ReportTotals(0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, TimeSpan.Zero), [], [],
                ReportRange.For(ReportPeriod.Weekly, date), TimeSpan.Zero, TimeSpan.Zero)),
            () => anchor, _ => { });

        await controller.OpenAsync();
        Assert.Equal(anchor, controller.Date);

        // Step back by 1 week (-7 days)
        await controller.MoveAsync(-1);
        Assert.Equal(new DateOnly(2026, 9, 5), controller.Date);

        // Step forward by 1 week (+7 days)
        await controller.MoveAsync(1);
        Assert.Equal(anchor, controller.Date);

        // Step forward again (+7 days)
        await controller.MoveAsync(1);
        Assert.Equal(new DateOnly(2026, 9, 19), controller.Date);
    }

    [Fact]
    public async Task ReadAsync_MonthlyMode_WorksWithCalendarWeekBuckets()
    {
        using var store = new SessionStore();
        var date = new DateOnly(2026, 9, 15);
        var service = new ReportsService(store.Repository, new ManualTimeProvider(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)), () => TimeZoneInfo.Utc);

        var snapshot = await service.ReadAsync(ReportPeriod.Monthly, date);

        Assert.Equal(ReportPeriod.Monthly, snapshot.Period);
        Assert.Equal(new DateOnly(2026, 9, 1), snapshot.Range.Start);
        Assert.Equal(new DateOnly(2026, 10, 1), snapshot.Range.End);
        Assert.True(snapshot.Trend.Count >= 4);
    }
}
