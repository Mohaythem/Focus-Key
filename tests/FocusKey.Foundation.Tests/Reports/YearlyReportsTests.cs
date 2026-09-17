using FocusKey.Foundation.Data;
using FocusKey.Foundation.History;
using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Reports;

public sealed class YearlyReportsTests
{
    [Fact]
    public void IsYearEligible_ReturnsFalse_WhenNoHistoryExists()
    {
        var today = new DateOnly(2026, 9, 15);
        Assert.False(ReportsService.IsYearEligible(null, today));
    }

    [Fact]
    public void IsYearEligible_ReturnsFalse_WhenHistoryLessThanOneYear()
    {
        var today = new DateOnly(2026, 9, 15);
        // Earliest date is 6 months ago
        var earliest = new DateOnly(2026, 3, 15);
        Assert.False(ReportsService.IsYearEligible(earliest, today));

        // Earliest date is 364 days ago (day before 1 full year)
        var almostYear = today.AddYears(-1).AddDays(1);
        Assert.False(ReportsService.IsYearEligible(almostYear, today));
    }

    [Fact]
    public void IsYearEligible_ReturnsTrue_WhenHistoryAtLeastOneYear()
    {
        var today = new DateOnly(2026, 9, 15);
        // Exactly 1 year ago
        var exactlyOneYear = today.AddYears(-1);
        Assert.True(ReportsService.IsYearEligible(exactlyOneYear, today));

        // 2 years ago
        var twoYearsAgo = today.AddYears(-2);
        Assert.True(ReportsService.IsYearEligible(twoYearsAgo, today));
    }

    [Fact]
    public void IsYearEligible_ReturnsFalse_WhenEarliestDateInFuture()
    {
        var today = new DateOnly(2026, 9, 15);
        var future = new DateOnly(2027, 1, 1);
        Assert.False(ReportsService.IsYearEligible(future, today));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(3600 * 20, 10)]
    [InlineData(3600 * 50, 10)]
    [InlineData(3600 * 51, 20)]
    [InlineData(3600 * 120, 20)]
    [InlineData(3600 * 121, 50)]
    [InlineData(3600 * 250, 50)]
    [InlineData(3600 * 251, 100)]
    [InlineData(3600 * 1000, 100)]
    public void ComputeYearlyStepHours_ProducesExpectedGridSteps(double maxSeconds, int expectedStep)
    {
        int step = ReportsService.ComputeYearlyStepHours(maxSeconds);
        Assert.Equal(expectedStep, step);
    }

    [Fact]
    public void Formatting_YearlyMonthsAndDurations()
    {
        Assert.Equal("Jan", ReportsFormatting.FormatYearMonth(1));
        Assert.Equal("Dec", ReportsFormatting.FormatYearMonth(12));
        Assert.Equal("January", ReportsFormatting.FormatYearMonthLong(1));
        Assert.Equal("December 2026", ReportsFormatting.FormatYearMonthLong(12, 2026));

        Assert.Equal(string.Empty, ReportsFormatting.FormatYearlyBarDuration(TimeSpan.Zero));
        Assert.Equal("45m", ReportsFormatting.FormatYearlyBarDuration(TimeSpan.FromMinutes(45)));
        Assert.Equal("5h", ReportsFormatting.FormatYearlyBarDuration(TimeSpan.FromHours(5)));
        Assert.Equal("5h 30m", ReportsFormatting.FormatYearlyBarDuration(TimeSpan.FromHours(5) + TimeSpan.FromMinutes(30)));
        Assert.Equal("10h", ReportsFormatting.FormatYearlyBarDuration(TimeSpan.FromHours(10)));
        Assert.Equal("124h", ReportsFormatting.FormatYearlyBarDuration(TimeSpan.FromHours(124) + TimeSpan.FromMinutes(15)));
    }

    [Fact]
    public async Task ReadAsync_YearlyProducesExactly12MonthlyBuckets()
    {
        using var store = new SessionStore();
        var service = new ReportsService(store.Repository, new ManualTimeProvider(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)), () => TimeZoneInfo.Utc);

        var snapshot = await service.ReadAsync(ReportPeriod.Yearly, new DateOnly(2026, 5, 10));

        Assert.Equal(ReportPeriod.Yearly, snapshot.Period);
        Assert.Equal(new DateOnly(2026, 1, 1), snapshot.Range.Start);
        Assert.Equal(new DateOnly(2027, 1, 1), snapshot.Range.End);
        Assert.Equal(12, snapshot.Trend.Count);

        for (int m = 1; m <= 12; m++)
        {
            Assert.Equal($"2026-{m:00}", snapshot.Trend[m - 1].Label);
            Assert.Equal(TimeSpan.Zero, snapshot.Trend[m - 1].Totals.FocusTime);
        }
    }

    [Fact]
    public async Task ReadAsync_YearlyAggregatesSessionsIntoCorrectMonthlyBuckets()
    {
        using var store = new SessionStore();
        // Session in March 2026 (45 mins work)
        var march = new DateTimeOffset(2026, 3, 10, 10, 0, 0, TimeSpan.Zero);
        await store.Repository.AddAsync(Finished(march, SessionType.Work, SessionStatus.Completed, 45));

        // Session in July 2026 (90 mins work)
        var july = new DateTimeOffset(2026, 7, 20, 14, 0, 0, TimeSpan.Zero);
        await store.Repository.AddAsync(Finished(july, SessionType.Work, SessionStatus.Completed, 90));

        // Session in November 2026 (30 mins break - does NOT count as focus)
        var nov = new DateTimeOffset(2026, 11, 5, 9, 0, 0, TimeSpan.Zero);
        await store.Repository.AddAsync(Finished(nov, SessionType.Break, SessionStatus.Completed, 30));

        // Session outside 2026 (in 2025)
        var pastYear = new DateTimeOffset(2025, 12, 31, 23, 0, 0, TimeSpan.Zero);
        await store.Repository.AddAsync(Finished(pastYear, SessionType.Work, SessionStatus.Completed, 60));

        var service = new ReportsService(store.Repository, new ManualTimeProvider(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)), () => TimeZoneInfo.Utc);
        var snapshot = await service.ReadAsync(ReportPeriod.Yearly, new DateOnly(2026, 9, 15));

        Assert.Equal(TimeSpan.FromMinutes(135), snapshot.Totals.FocusTime);
        Assert.Equal(2, snapshot.Totals.CompletedWork);
        Assert.Equal(1, snapshot.Totals.CompletedBreak);

        // March is index 2 (month 3)
        Assert.Equal(TimeSpan.FromMinutes(45), snapshot.Trend[2].Totals.FocusTime);
        Assert.Equal(1, snapshot.Trend[2].Totals.CompletedWork);

        // July is index 6 (month 7)
        Assert.Equal(TimeSpan.FromMinutes(90), snapshot.Trend[6].Totals.FocusTime);
        Assert.Equal(1, snapshot.Trend[6].Totals.CompletedWork);

        // November is index 10 (month 11) - 0 focus time, 1 break
        Assert.Equal(TimeSpan.Zero, snapshot.Trend[10].Totals.FocusTime);
        Assert.Equal(TimeSpan.FromMinutes(30), snapshot.Trend[10].Totals.BreakTime);

        // Other months zero
        Assert.Equal(TimeSpan.Zero, snapshot.Trend[0].Totals.FocusTime); // Jan
        Assert.Equal(TimeSpan.Zero, snapshot.Trend[1].Totals.FocusTime); // Feb
    }

    [Fact]
    public async Task ReadAsync_YearlyIntegratesImportedHistoryWithoutFabricatingSessions()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var sessionRepo = new SqliteSessionRepository(connections);
        var historyRepo = new SqliteHistoricalFocusRepository(connections);

        // Native completed work session in Feb 2026: 1 hour
        var febTime = new DateTimeOffset(2026, 2, 10, 10, 0, 0, TimeSpan.Zero);
        await sessionRepo.AddAsync(Finished(febTime, SessionType.Work, SessionStatus.Completed, 60));

        // Imported historical focus entries in 2026
        var historyEntries = new List<HistoricalFocusEntry>
        {
            new(new DateOnly(2026, 2, 15), "", 3.0), // 3 hours in Feb
            new(new DateOnly(2026, 8, 20), "", 5.0), // 5 hours in Aug
        };
        await historyRepo.ImportAsync(historyEntries, 1, 0);

        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        var reportsService = new ReportsService(sessionRepo, historyRepo, timeProvider, () => TimeZoneInfo.Utc);

        var snapshot = await reportsService.ReadAsync(ReportPeriod.Yearly, new DateOnly(2026, 9, 15));

        // Focus time: 1h native + 3h + 5h imported = 9 hours
        Assert.Equal(TimeSpan.FromHours(9), snapshot.Totals.FocusTime);

        // Session count remains strictly 1 native session — NO fake session records fabricated
        Assert.Equal(1, snapshot.Totals.Started);
        Assert.Equal(1, snapshot.Totals.WorkStarted);
        Assert.Equal(1, snapshot.Totals.CompletedWork);
        Assert.Equal(100.0, snapshot.Totals.CompletionRate);

        // Feb bucket (index 1): 1h native + 3h imported = 4 hours
        Assert.Equal(TimeSpan.FromHours(4), snapshot.Trend[1].Totals.FocusTime);
        Assert.Equal(1, snapshot.Trend[1].Totals.CompletedWork);

        // Aug bucket (index 7): 5h imported = 5 hours
        Assert.Equal(TimeSpan.FromHours(5), snapshot.Trend[7].Totals.FocusTime);
        Assert.Equal(0, snapshot.Trend[7].Totals.CompletedWork);
    }

    [Fact]
    public async Task ReadAsync_YearlyComparesWithPreviousCalendarYear()
    {
        using var store = new SessionStore();
        // Session in 2025: 10 hours
        var time2025 = new DateTimeOffset(2025, 6, 15, 10, 0, 0, TimeSpan.Zero);
        await store.Repository.AddAsync(Finished(time2025, SessionType.Work, SessionStatus.Completed, 600));

        // Session in 2026: 15 hours
        var time2026 = new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero);
        await store.Repository.AddAsync(Finished(time2026, SessionType.Work, SessionStatus.Completed, 900));

        var service = new ReportsService(store.Repository, new ManualTimeProvider(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero)), () => TimeZoneInfo.Utc);
        var snapshot = await service.ReadAsync(ReportPeriod.Yearly, new DateOnly(2026, 9, 15));

        Assert.Equal(TimeSpan.FromHours(15), snapshot.Totals.FocusTime);
        Assert.Equal(TimeSpan.FromHours(10), snapshot.PreviousPeriodDuration);
        Assert.Equal(TimeSpan.FromHours(5), snapshot.PeriodDifference);
    }

    [Fact]
    public async Task ReadAsync_DerivesYearEligibilityFromEarliestHistoryDate()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var sessionRepo = new SqliteSessionRepository(connections);
        var historyRepo = new SqliteHistoricalFocusRepository(connections);

        var today = new DateOnly(2026, 9, 15);
        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero));
        var reportsService = new ReportsService(sessionRepo, historyRepo, timeProvider, () => TimeZoneInfo.Utc);

        // Case 1: Empty database -> Not eligible
        var snapEmpty = await reportsService.ReadAsync(ReportPeriod.Weekly, today);
        Assert.False(snapEmpty.IsYearEligible);
        Assert.Null(snapEmpty.EarliestHistoryDate);

        // Case 2: Session 6 months ago -> Not eligible
        var recentSession = Finished(new DateTimeOffset(2026, 3, 15, 10, 0, 0, TimeSpan.Zero), SessionType.Work, SessionStatus.Completed, 30);
        await sessionRepo.AddAsync(recentSession);
        var snapRecent = await reportsService.ReadAsync(ReportPeriod.Weekly, today);
        Assert.False(snapRecent.IsYearEligible);
        Assert.Equal(new DateOnly(2026, 3, 15), snapRecent.EarliestHistoryDate);

        // Case 3: Imported history from 2 years ago -> Eligible!
        var oldHistory = new List<HistoricalFocusEntry> { new(new DateOnly(2024, 5, 1), "", 2.0) };
        await historyRepo.ImportAsync(oldHistory, 1, 0);

        var snapOld = await reportsService.ReadAsync(ReportPeriod.Weekly, today);
        Assert.True(snapOld.IsYearEligible);
        Assert.Equal(new DateOnly(2024, 5, 1), snapOld.EarliestHistoryDate);
    }

    [Fact]
    public async Task ReportsController_YearlyNavigation_ClampsAtCurrentYear()
    {
        var currentDate = new DateOnly(2026, 9, 15);
        var controller = new ReportsController(
            (period, date, token) => Task.FromResult(new ReportsSnapshot(
                period, ReportRange.For(period, date), TimeZoneInfo.Utc, DateTimeOffset.UtcNow,
                ReportTotals.Empty, Array.Empty<ReportBucket>(), Array.Empty<FocusPeriod>(),
                ReportRange.For(ReportPeriod.Weekly, date), TimeSpan.Zero, TimeSpan.Zero)),
            () => currentDate,
            ex => { });

        await controller.OpenAsync();
        await controller.SelectAsync(ReportPeriod.Yearly, new DateOnly(2025, 1, 1));
        Assert.Equal(ReportPeriod.Yearly, controller.Period);
        Assert.Equal(new DateOnly(2025, 1, 1), controller.Date);

        // Move forward 1 year -> 2026
        await controller.MoveAsync(1);
        Assert.Equal(new DateOnly(2026, 1, 1), controller.Date);

        // Attempt to move forward again -> Clamped, remains 2026
        await controller.MoveAsync(1);
        Assert.Equal(new DateOnly(2026, 1, 1), controller.Date);

        // Move backward 1 year -> 2025
        await controller.MoveAsync(-1);
        Assert.Equal(new DateOnly(2025, 1, 1), controller.Date);

        // Attempting to select a future year directly clamps to current year
        await controller.SelectAsync(ReportPeriod.Yearly, new DateOnly(2030, 6, 1));
        Assert.Equal(2026, controller.Date.Year);
    }

    private static SessionRecord Finished(DateTimeOffset start, SessionType type, SessionStatus status, int minutes) =>
        TestSessions.Finished(status, startedAt: start, type: type, plannedDuration: TimeSpan.FromMinutes(minutes));
}
