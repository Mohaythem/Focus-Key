using FocusKey.Foundation.Data;
using FocusKey.Foundation.History;
using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;
using FocusKey.Foundation.Today;

namespace FocusKey.Foundation.Tests.Reports;

public sealed class ReportsPartialSessionAccountingTests
{
    private static readonly DateTimeOffset Anchor = new(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EffectiveDuration_RunningSession_ReturnsZero()
    {
        var session = TestSessions.Running(startedAt: Anchor, plannedDuration: TimeSpan.FromMinutes(40));
        Assert.Equal(TimeSpan.Zero, session.EffectiveDuration);
    }

    [Fact]
    public void EffectiveDuration_CompletedSession_ReturnsExactPlannedDuration()
    {
        var session = TestSessions.Finished(
            SessionStatus.Completed,
            startedAt: Anchor,
            plannedDuration: TimeSpan.FromMinutes(25),
            actualDuration: TimeSpan.FromMinutes(25));

        Assert.Equal(TimeSpan.FromMinutes(25), session.EffectiveDuration);
    }

    [Fact]
    public void EffectiveDuration_StoppedSession_ReturnsElapsedWithinPlanned()
    {
        // 40m planned, stopped after 20m
        var session = TestSessions.Finished(
            SessionStatus.Stopped,
            startedAt: Anchor,
            plannedDuration: TimeSpan.FromMinutes(40),
            actualDuration: TimeSpan.FromMinutes(20));

        Assert.Equal(TimeSpan.FromMinutes(20), session.EffectiveDuration);
    }

    [Fact]
    public void EffectiveDuration_ClampsToPlannedDuration_WhenElapsedExceedsPlanned()
    {
        // Defensive: elapsed is 45m but planned was 40m
        var session = TestSessions.Finished(
            SessionStatus.Stopped,
            startedAt: Anchor,
            plannedDuration: TimeSpan.FromMinutes(40),
            actualDuration: TimeSpan.FromMinutes(45));

        Assert.Equal(TimeSpan.FromMinutes(40), session.EffectiveDuration);
    }

    [Fact]
    public void EffectiveDuration_CrashRecoveryInterruption_ReturnsZero()
    {
        // Crash recovery sets EndedAt == StartedAt
        var session = TestSessions.Finished(
            SessionStatus.Interrupted,
            startedAt: Anchor,
            plannedDuration: TimeSpan.FromMinutes(30),
            actualDuration: TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, session.EffectiveDuration);
    }

    [Fact]
    public void EffectiveDuration_OrderlyShutdownInterruption_ReturnsElapsed()
    {
        // Orderly shutdown interrupted after 15m
        var session = TestSessions.Finished(
            SessionStatus.Interrupted,
            startedAt: Anchor,
            plannedDuration: TimeSpan.FromMinutes(30),
            actualDuration: TimeSpan.FromMinutes(15));

        Assert.Equal(TimeSpan.FromMinutes(15), session.EffectiveDuration);
    }

    [Fact]
    public async Task Today_StoppedWorkAndBreakSessions_CreditTimeWithoutIncrementingCompletedCounts()
    {
        using var store = new SessionStore();

        // 1. Stopped Work session: 40m planned, stopped after 20m
        await store.Repository.AddAsync(TestSessions.Finished(
            SessionStatus.Stopped,
            startedAt: Anchor.AddHours(1),
            type: SessionType.Work,
            plannedDuration: TimeSpan.FromMinutes(40),
            actualDuration: TimeSpan.FromMinutes(20)));

        // 2. Stopped Break session: 10m planned, stopped after 4m
        await store.Repository.AddAsync(TestSessions.Finished(
            SessionStatus.Stopped,
            startedAt: Anchor.AddHours(2),
            type: SessionType.Break,
            plannedDuration: TimeSpan.FromMinutes(10),
            actualDuration: TimeSpan.FromMinutes(4)));

        // 3. Crash recovery interrupted session: 0m credited
        await store.Repository.AddAsync(TestSessions.Finished(
            SessionStatus.Interrupted,
            startedAt: Anchor.AddHours(3),
            type: SessionType.Work,
            plannedDuration: TimeSpan.FromMinutes(25),
            actualDuration: TimeSpan.Zero));

        var todayService = new TodayService(store.Repository, new ManualTimeProvider(Anchor.AddHours(5)), () => TimeZoneInfo.Utc);
        var snapshot = await todayService.ReadAsync();

        // WorkTime and BreakTime must reflect the real elapsed time
        Assert.Equal(TimeSpan.FromMinutes(20), snapshot.WorkTime);
        Assert.Equal(TimeSpan.FromMinutes(4), snapshot.BreakTime);

        // Completed counts MUST remain 0
        Assert.Equal(0, snapshot.CompletedWorkCount);
        Assert.Equal(0, snapshot.CompletedBreakCount);

        // Completion rate must be 0% (0 completed / 3 total)
        Assert.Equal(0.0, snapshot.CompletionRate);
    }

    [Fact]
    public async Task Today_MixOfCompletedAndStopped_CreditsBothAccurately()
    {
        using var store = new SessionStore();

        // 1 Completed Work (25m)
        await store.Repository.AddAsync(TestSessions.Finished(
            SessionStatus.Completed,
            startedAt: Anchor.AddHours(1),
            type: SessionType.Work,
            plannedDuration: TimeSpan.FromMinutes(25),
            actualDuration: TimeSpan.FromMinutes(25)));

        // 1 Stopped Work (20m out of 40m)
        await store.Repository.AddAsync(TestSessions.Finished(
            SessionStatus.Stopped,
            startedAt: Anchor.AddHours(2),
            type: SessionType.Work,
            plannedDuration: TimeSpan.FromMinutes(40),
            actualDuration: TimeSpan.FromMinutes(20)));

        // 1 Completed Break (5m)
        await store.Repository.AddAsync(TestSessions.Finished(
            SessionStatus.Completed,
            startedAt: Anchor.AddHours(3),
            type: SessionType.Break,
            plannedDuration: TimeSpan.FromMinutes(5),
            actualDuration: TimeSpan.FromMinutes(5)));

        // 1 Stopped Break (3m out of 10m)
        await store.Repository.AddAsync(TestSessions.Finished(
            SessionStatus.Stopped,
            startedAt: Anchor.AddHours(4),
            type: SessionType.Break,
            plannedDuration: TimeSpan.FromMinutes(10),
            actualDuration: TimeSpan.FromMinutes(3)));

        var todayService = new TodayService(store.Repository, new ManualTimeProvider(Anchor.AddHours(5)), () => TimeZoneInfo.Utc);
        var snapshot = await todayService.ReadAsync();

        Assert.Equal(TimeSpan.FromMinutes(45), snapshot.WorkTime); // 25 + 20
        Assert.Equal(TimeSpan.FromMinutes(8), snapshot.BreakTime);  // 5 + 3
        Assert.Equal(1, snapshot.CompletedWorkCount);
        Assert.Equal(1, snapshot.CompletedBreakCount);
        Assert.Equal(50.0, snapshot.CompletionRate); // 2 completed / 4 total
    }

    [Fact]
    public async Task WeeklyReports_StoppedSessionsReflectedInTotalsAndTrendBuckets()
    {
        using var store = new SessionStore();
        var wednesday = new DateTimeOffset(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

        // Wednesday: 40m Work stopped after 20m
        await store.Repository.AddAsync(TestSessions.Finished(
            SessionStatus.Stopped,
            startedAt: wednesday,
            type: SessionType.Work,
            plannedDuration: TimeSpan.FromMinutes(40),
            actualDuration: TimeSpan.FromMinutes(20)));

        // Wednesday: 10m Break stopped after 4m
        await store.Repository.AddAsync(TestSessions.Finished(
            SessionStatus.Stopped,
            startedAt: wednesday.AddHours(1),
            type: SessionType.Break,
            plannedDuration: TimeSpan.FromMinutes(10),
            actualDuration: TimeSpan.FromMinutes(4)));

        var service = new ReportsService(store.Repository, new ManualTimeProvider(wednesday.AddHours(2)), () => TimeZoneInfo.Utc);
        var snapshot = await service.ReadAsync(ReportPeriod.Weekly, DateOnly.FromDateTime(wednesday.DateTime));

        // Weekly totals
        Assert.Equal(TimeSpan.FromMinutes(20), snapshot.Totals.FocusTime);
        Assert.Equal(TimeSpan.FromMinutes(4), snapshot.Totals.BreakTime);
        Assert.Equal(2, snapshot.Totals.Started);
        Assert.Equal(0, snapshot.Totals.CompletedWork);
        Assert.Equal(0, snapshot.Totals.CompletedBreak);
        Assert.Equal(0.0, snapshot.Totals.CompletionRate);

        // Wednesday's trend bucket (rightmost, index 6)
        var wednesdayBucket = snapshot.Trend[6];
        Assert.Equal("2026-09-09", wednesdayBucket.Label);
        Assert.Equal(TimeSpan.FromMinutes(20), wednesdayBucket.Totals.FocusTime);
        Assert.Equal(TimeSpan.FromMinutes(4), wednesdayBucket.Totals.BreakTime);
        Assert.Equal(0, wednesdayBucket.Totals.CompletedWork);
    }

    [Fact]
    public async Task MonthlyReports_StoppedSessionsReflectedInWeeklySlices()
    {
        using var store = new SessionStore();
        var date = new DateTimeOffset(2026, 9, 15, 14, 0, 0, TimeSpan.Zero);

        // Stopped Work session: 50m planned, stopped after 35m
        await store.Repository.AddAsync(TestSessions.Finished(
            SessionStatus.Stopped,
            startedAt: date,
            type: SessionType.Work,
            plannedDuration: TimeSpan.FromMinutes(50),
            actualDuration: TimeSpan.FromMinutes(35)));

        var service = new ReportsService(store.Repository, new ManualTimeProvider(date.AddHours(2)), () => TimeZoneInfo.Utc);
        var snapshot = await service.ReadAsync(ReportPeriod.Monthly, DateOnly.FromDateTime(date.DateTime));

        Assert.Equal(TimeSpan.FromMinutes(35), snapshot.Totals.FocusTime);
        Assert.Equal(0, snapshot.Totals.CompletedWork);
        Assert.Equal(0.0, snapshot.Totals.CompletionRate);

        // Ensure at least one weekly slice bucket has the 35m focus time
        var activeSlice = snapshot.Trend.Single(b => b.Totals.FocusTime > TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(35), activeSlice.Totals.FocusTime);
    }

    [Fact]
    public async Task Reports_CoexistenceWithCsvHistory_DoesNotDistortTotalsOrCounts()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key_partial.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var sessionRepo = new SqliteSessionRepository(connections);
        var historyRepo = new SqliteHistoricalFocusRepository(connections);

        var date = new DateOnly(2026, 9, 10);
        var startTime = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);

        // 1 stopped work session: 40m planned, 20m elapsed
        await sessionRepo.AddAsync(new SessionRecord
        {
            Id = SessionId.New(),
            Type = SessionType.Work,
            Status = SessionStatus.Stopped,
            StartedAt = startTime,
            PlannedDuration = TimeSpan.FromMinutes(40),
            EndedAt = startTime.AddMinutes(20),
            CreatedAt = startTime,
        });

        // 1 imported historical focus record: 3 hours on the same date
        await historyRepo.ImportAsync(new List<HistoricalFocusEntry> { new(date, "", 3.0) }, 1, 0);

        var service = new ReportsService(sessionRepo, historyRepo, new ManualTimeProvider(startTime.AddHours(2)), () => TimeZoneInfo.Utc);
        var snapshot = await service.ReadAsync(ReportPeriod.Weekly, date);

        // Total focus time: 20m native stopped + 3h imported = 3h 20m = 200m
        Assert.Equal(TimeSpan.FromMinutes(200), snapshot.Totals.FocusTime);
        Assert.Equal(1, snapshot.Totals.Started);
        Assert.Equal(1, snapshot.Totals.Stopped);
        Assert.Equal(0, snapshot.Totals.CompletedWork);
        Assert.Equal(0.0, snapshot.Totals.CompletionRate);
    }

    [Fact]
    public async Task ExportWebsiteCsvAsync_IncludesStoppedWorkDurationInNativeFocus()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key_export.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var sessionRepo = new SqliteSessionRepository(connections);
        var historyRepo = new SqliteHistoricalFocusRepository(connections);

        var startTime = new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero);

        // Stopped work session: 20m elapsed
        await sessionRepo.AddAsync(new SessionRecord
        {
            Id = SessionId.New(),
            Type = SessionType.Work,
            Status = SessionStatus.Stopped,
            StartedAt = startTime,
            PlannedDuration = TimeSpan.FromMinutes(40),
            EndedAt = startTime.AddMinutes(20),
            CreatedAt = startTime,
        });

        var historyService = new HistoricalFocusService(historyRepo, sessionRepo, () => TimeZoneInfo.Utc);
        string exportedCsv = await historyService.ExportWebsiteCsvAsync();

        // Export format contains: date \t project \t minutes
        // 20 minutes elapsed
        Assert.Contains("20260910", exportedCsv);
        Assert.Contains("\t20\r\n", exportedCsv);
    }
}
