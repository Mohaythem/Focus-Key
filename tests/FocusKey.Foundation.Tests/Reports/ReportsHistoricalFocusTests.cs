using FocusKey.Foundation.Data;
using FocusKey.Foundation.History;
using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Reports;

public sealed class ReportsHistoricalFocusTests
{
    [Fact]
    public async Task ReadAsync_CombinesNativeSessionsAndHistoricalFocus()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var sessionRepo = new SqliteSessionRepository(connections);
        var historyRepo = new SqliteHistoricalFocusRepository(connections);

        var date = new DateOnly(2026, 9, 10);
        var startTime = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);

        // 1 native completed work session: 30 minutes
        var nativeSession = new SessionRecord
        {
            Id = SessionId.New(),
            Type = SessionType.Work,
            Status = SessionStatus.Completed,
            StartedAt = startTime,
            PlannedDuration = TimeSpan.FromMinutes(30),
            EndedAt = startTime.AddMinutes(30),
            CreatedAt = startTime,
        };
        await sessionRepo.AddAsync(nativeSession);

        // 1 imported historical focus record: 5.5 hours on the same date
        var historyEntries = new List<HistoricalFocusEntry>
        {
            new(date, "", 5.5)
        };
        await historyRepo.ImportAsync(historyEntries, 1, 0);

        var timeProvider = new ManualTimeProvider(startTime.AddHours(2));
        var reportsService = new ReportsService(sessionRepo, historyRepo, timeProvider, () => TimeZoneInfo.Utc);

        var snapshot = await reportsService.ReadAsync(ReportPeriod.Weekly, date);

        // Totals: native (30m) + imported (5.5h = 330m) = 360m = 6 hours
        Assert.Equal(TimeSpan.FromHours(6), snapshot.Totals.FocusTime);

        // Native session counts must remain strictly 1 — NO fake session records fabricated
        Assert.Equal(1, snapshot.Totals.Started);
        Assert.Equal(1, snapshot.Totals.WorkStarted);
        Assert.Equal(1, snapshot.Totals.CompletedWork);
        Assert.Equal(0, snapshot.Totals.BreakStarted);
        Assert.Equal(100.0, snapshot.Totals.CompletionRate);
    }

    [Fact]
    public async Task ReadAsync_HistoricalFocusWithoutNativeSessions_DoesNotDistortSessionCounts()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var sessionRepo = new SqliteSessionRepository(connections);
        var historyRepo = new SqliteHistoricalFocusRepository(connections);

        var date = new DateOnly(2026, 9, 10);
        var historyEntries = new List<HistoricalFocusEntry>
        {
            new(date, "", 4.0)
        };
        await historyRepo.ImportAsync(historyEntries, 1, 0);

        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var reportsService = new ReportsService(sessionRepo, historyRepo, timeProvider, () => TimeZoneInfo.Utc);

        var snapshot = await reportsService.ReadAsync(ReportPeriod.Weekly, date);

        Assert.Equal(TimeSpan.FromHours(4), snapshot.Totals.FocusTime);
        Assert.Equal(0, snapshot.Totals.Started);
        Assert.Equal(0, snapshot.Totals.CompletedWork);
        Assert.Null(snapshot.Totals.CompletionRate); // 0 started = null completion rate, NOT corrupted
    }

    [Fact]
    public async Task ReadAsync_WeeklyTrend_IncludesHistoricalHoursInDayBuckets()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var sessionRepo = new SqliteSessionRepository(connections);
        var historyRepo = new SqliteHistoricalFocusRepository(connections);

        var date = new DateOnly(2026, 9, 10);
        var historyEntries = new List<HistoricalFocusEntry>
        {
            new(date, "", 12.0)
        };
        await historyRepo.ImportAsync(historyEntries, 1, 0);

        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var reportsService = new ReportsService(sessionRepo, historyRepo, timeProvider, () => TimeZoneInfo.Utc);

        var snapshot = await reportsService.ReadAsync(ReportPeriod.Weekly, date);

        // Weekly trend has 7 rolling day buckets ending on date (index 6 is 2026-09-10)
        Assert.Equal(7, snapshot.Trend.Count);
        Assert.Equal("2026-09-10", snapshot.Trend[6].Label);
        Assert.Equal(TimeSpan.FromHours(12), snapshot.Trend[6].Totals.FocusTime);
        var totalTrendFocus = snapshot.Trend.Aggregate(TimeSpan.Zero, (acc, b) => acc + b.Totals.FocusTime);
        Assert.Equal(TimeSpan.FromHours(12), totalTrendFocus);
    }

    [Fact]
    public async Task ReadAsync_WeeklyAndMonthly_IncludesHistoricalFocusInBuckets()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var sessionRepo = new SqliteSessionRepository(connections);
        var historyRepo = new SqliteHistoricalFocusRepository(connections);

        // Add 3 consecutive days of imported focus
        var historyEntries = new List<HistoricalFocusEntry>
        {
            new(new DateOnly(2026, 9, 7), "", 5.0),
            new(new DateOnly(2026, 9, 8), "", 6.0),
            new(new DateOnly(2026, 9, 9), "", 7.0),
        };
        await historyRepo.ImportAsync(historyEntries, 3, 0);

        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));
        var reportsService = new ReportsService(sessionRepo, historyRepo, timeProvider, () => TimeZoneInfo.Utc);

        // Weekly test
        var weeklySnapshot = await reportsService.ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 9, 9));
        Assert.Equal(TimeSpan.FromHours(18), weeklySnapshot.Totals.FocusTime);
        Assert.Equal(TimeSpan.FromHours(18), weeklySnapshot.WeekFocus);

        // Monthly test
        var monthlySnapshot = await reportsService.ReadAsync(ReportPeriod.Monthly, new DateOnly(2026, 9, 9));
        Assert.Equal(TimeSpan.FromHours(18), monthlySnapshot.Totals.FocusTime);
    }

    [Fact]
    public async Task ReadAsync_StreakCalculation_IncludesHistoricalFocusDays()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var sessionRepo = new SqliteSessionRepository(connections);
        var historyRepo = new SqliteHistoricalFocusRepository(connections);

        // Historical focus on 2026-09-08 and 2026-09-09
        var historyEntries = new List<HistoricalFocusEntry>
        {
            new(new DateOnly(2026, 9, 8), "", 4.0),
            new(new DateOnly(2026, 9, 9), "", 5.0),
        };
        await historyRepo.ImportAsync(historyEntries, 2, 0);

        // Native completed work session today: 2026-09-10
        var startTime = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);
        var nativeSession = new SessionRecord
        {
            Id = SessionId.New(),
            Type = SessionType.Work,
            Status = SessionStatus.Completed,
            StartedAt = startTime,
            PlannedDuration = TimeSpan.FromMinutes(25),
            EndedAt = startTime.AddMinutes(25),
            CreatedAt = startTime,
        };
        await sessionRepo.AddAsync(nativeSession);

        var timeProvider = new ManualTimeProvider(startTime.AddHours(1));
        var reportsService = new ReportsService(sessionRepo, historyRepo, timeProvider, () => TimeZoneInfo.Utc);

        var snapshot = await reportsService.ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 9, 10));

        // 3 consecutive days (Sep 8, Sep 9, Sep 10) => Current Streak = 3!
        Assert.Equal(3, snapshot.Streaks.CurrentStreak);
        Assert.True(snapshot.Streaks.LongestStreak >= 3);
    }

    [Fact]
    public async Task ReadAsync_15ConsecutiveImportedPositiveFocusDates_Produces15DayStreak()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var sessionRepo = new SqliteSessionRepository(connections);
        var historyRepo = new SqliteHistoricalFocusRepository(connections);

        // 15 consecutive days with imported minutes focus (e.g. 2026-09-01 to 2026-09-15)
        var entries = new List<HistoricalFocusEntry>();
        for (int i = 1; i <= 15; i++)
        {
            entries.Add(HistoricalFocusEntry.FromMinutes(new DateOnly(2026, 9, i), "", 120)); // 2 hours each day
        }
        await historyRepo.ImportAsync(entries, 15, 0);

        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 9, 15, 18, 0, 0, TimeSpan.Zero));
        var reportsService = new ReportsService(sessionRepo, historyRepo, timeProvider, () => TimeZoneInfo.Utc);

        var snapshot = await reportsService.ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 9, 15));

        Assert.Equal(15, snapshot.Streaks.CurrentStreak);
        Assert.True(snapshot.Streaks.LongestStreak >= 15);
        Assert.Equal(TimeSpan.FromHours(14), snapshot.Totals.FocusTime);
    }

    [Fact]
    public async Task ReadAsync_OverlappingImports_DoesNotInflateStreakOrFocusTime()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var sessionRepo = new SqliteSessionRepository(connections);
        var historyRepo = new SqliteHistoricalFocusRepository(connections);

        var date = new DateOnly(2026, 9, 10);

        // First import via hours schema: 11.5 hours
        var hoursEntries = new List<HistoricalFocusEntry>
        {
            HistoricalFocusEntry.FromHours(date, "", 11.5)
        };
        var res1 = await historyRepo.ImportAsync(hoursEntries, 1, 0);
        Assert.Equal(1, res1.NewRecords);

        // Second import via minutes schema: 690 minutes (11.5 hours)
        var minutesEntries = new List<HistoricalFocusEntry>
        {
            HistoricalFocusEntry.FromMinutes(date, "", 690)
        };
        var res2 = await historyRepo.ImportAsync(minutesEntries, 1, 0);
        Assert.Equal(0, res2.NewRecords);
        Assert.Equal(1, res2.DuplicateRecords); // Already exists with identical duration

        var timeProvider = new ManualTimeProvider(new DateTimeOffset(2026, 9, 10, 18, 0, 0, TimeSpan.Zero));
        var reportsService = new ReportsService(sessionRepo, historyRepo, timeProvider, () => TimeZoneInfo.Utc);

        var snapshot = await reportsService.ReadAsync(ReportPeriod.Weekly, date);

        // Total focus time must remain exactly 11.5 hours (690 minutes), NOT duplicated to 23 hours
        Assert.Equal(TimeSpan.FromMinutes(690), snapshot.Totals.FocusTime);
        Assert.Equal(1, snapshot.Streaks.CurrentStreak);
    }

    [Fact]
    public async Task ReadAsync_ExportWebsiteCsv_DoesNotModifyInternalReportDurationWithLeftoverSeconds()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var sessionRepo = new SqliteSessionRepository(connections);
        var historyRepo = new SqliteHistoricalFocusRepository(connections);
        var historyService = new HistoricalFocusService(historyRepo, sessionRepo, () => TimeZoneInfo.Utc);

        var date = new DateOnly(2026, 9, 10);
        var startTime = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);

        // Native completed work session of 1829 seconds (30m 29s)
        var nativeSession = new SessionRecord
        {
            Id = SessionId.New(),
            Type = SessionType.Work,
            Status = SessionStatus.Completed,
            StartedAt = startTime,
            PlannedDuration = TimeSpan.FromSeconds(1829),
            EndedAt = startTime.AddSeconds(1829),
            CreatedAt = startTime,
        };
        await sessionRepo.AddAsync(nativeSession);

        var timeProvider = new ManualTimeProvider(startTime.AddHours(2));
        var reportsService = new ReportsService(sessionRepo, historyRepo, timeProvider, () => TimeZoneInfo.Utc);

        // Internal report snapshot before export: exactly 1829 seconds
        var snapshotBefore = await reportsService.ReadAsync(ReportPeriod.Weekly, date);
        Assert.Equal(TimeSpan.FromSeconds(1829), snapshotBefore.Totals.FocusTime);

        // Export to website CSV (1829s rounds to 30 minutes via MidpointRounding.AwayFromZero)
        string exportedCsv = await historyService.ExportWebsiteCsvAsync();
        Assert.Contains("20260910\t\"\"\t30\r\n", exportedCsv);

        // Internal report snapshot after export: MUST STILL BE exactly 1829 seconds (unmodified by export rounding)
        var snapshotAfter = await reportsService.ReadAsync(ReportPeriod.Weekly, date);
        Assert.Equal(TimeSpan.FromSeconds(1829), snapshotAfter.Totals.FocusTime);
        Assert.Equal(TimeSpan.FromSeconds(1829), snapshotBefore.Totals.FocusTime);
    }
}

