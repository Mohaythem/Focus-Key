using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;
using FocusKey.Foundation.Today;

namespace FocusKey.Foundation.Tests.Today;

public sealed class TodayServiceTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(-7)]
    [InlineData(14)]
    [InlineData(-12)]
    public async Task UsesLocalStartDateAndHalfOpenMidnightBoundaries(int offsetHours)
    {
        using var store = new SessionStore();
        var zone = FixedZone(offsetHours);
        var midnight = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.FromHours(offsetHours)).ToUniversalTime();
        var before = Completed(midnight.AddTicks(-1));
        var first = Completed(midnight);
        var last = Completed(midnight.AddDays(1).AddTicks(-1));
        var after = Completed(midnight.AddDays(1));
        foreach (var row in new[] { before, first, last, after }) await store.Repository.AddAsync(row);
        var service = new TodayService(store.Repository, new ManualTimeProvider(midnight.AddHours(12)), () => zone);
        var today = await service.ReadAsync();
        Assert.Equal(new DateOnly(2026, 9, 2), today.Date);
        Assert.Equal(new[] { first.Id, last.Id }, today.Sessions.Select(s => s.Id));
        Assert.Equal(midnight.AddDays(1), today.NextDayAt);
    }

    [Fact]
    public async Task CountsOnlyCompletedTypesAndSumsCreditedDurations()
    {
        using var store = new SessionStore();
        var rows = new[]
        {
            Completed(TestSessions.Anchor, SessionType.Work, 30),
            Completed(TestSessions.Anchor.AddHours(1), SessionType.Work, 15),
            Completed(TestSessions.Anchor.AddHours(2), SessionType.Break, 10),
            TestSessions.Finished(SessionStatus.Stopped, startedAt: TestSessions.Anchor.AddHours(3), actualDuration: TimeSpan.FromMinutes(5)),
            TestSessions.Finished(SessionStatus.Interrupted, startedAt: TestSessions.Anchor.AddHours(4), actualDuration: TimeSpan.FromMinutes(10)),
            TestSessions.Running(startedAt: TestSessions.Anchor.AddHours(5))
        };
        foreach (var row in rows) await store.Repository.AddAsync(row);
        var today = await new TodayService(store.Repository, new ManualTimeProvider(TestSessions.Anchor.AddHours(6)), () => TimeZoneInfo.Utc).ReadAsync();
        Assert.Equal(2, today.CompletedWorkCount);
        Assert.Equal(1, today.CompletedBreakCount);
        Assert.Equal(TimeSpan.FromMinutes(60), today.WorkTime);
        Assert.Equal(TimeSpan.FromMinutes(10), today.BreakTime);
        Assert.Equal(50.0, today.CompletionRate);
        Assert.Equal(rows[^1], today.Running);
        Assert.Equal(6, today.Sessions.Count);
    }

    [Fact]
    public async Task EmptyDayHasZeroTotalsAndNoCompletionPercentage()
    {
        using var store = new SessionStore();
        var today = await new TodayService(store.Repository, new ManualTimeProvider(TestSessions.Anchor), () => TimeZoneInfo.Utc).ReadAsync();
        Assert.Empty(today.Sessions);
        Assert.Null(today.Running);
        Assert.Null(today.CompletionRate);
        Assert.Equal(0, today.CompletedWorkCount);
        Assert.Equal(0, today.CompletedBreakCount);
        Assert.Equal(TimeSpan.Zero, today.WorkTime);
        Assert.Equal(TimeSpan.Zero, today.BreakTime);
    }

    [Fact]
    public async Task OvernightRunningIsVisibleWithoutCountingItAsTodaysSession()
    {
        using var store = new SessionStore();
        var midnight = new DateTimeOffset(2026, 9, 2, 0, 0, 0, TimeSpan.Zero);
        var running = TestSessions.Running(startedAt: midnight.AddMinutes(-10));
        await store.Repository.AddAsync(running);
        var today = await new TodayService(store.Repository, new ManualTimeProvider(midnight.AddMinutes(5)), () => TimeZoneInfo.Utc).ReadAsync();
        Assert.Equal(running, today.Running);
        Assert.Empty(today.Sessions);
        Assert.Equal(TimeSpan.FromMinutes(15), SessionSnapshot.For(today.Running!, today.ObservedAt).Remaining);
    }

    [Fact]
    public async Task ReadDoesNotCompleteAnOverdueSession()
    {
        using var store = new SessionStore();
        var running = TestSessions.Running(); await store.Repository.AddAsync(running);
        var today = await new TodayService(store.Repository, new ManualTimeProvider(running.PlannedEndAt.AddHours(1)), () => TimeZoneInfo.Utc).ReadAsync();
        Assert.Equal(running, today.Running);
        Assert.Equal(running, await store.Repository.GetAsync(running.Id));
        Assert.Equal(0, today.CompletedWorkCount);
    }

    [Theory]
    [InlineData(3, 10, 23)]
    [InlineData(10, 10, 25)]
    public async Task MidnightDstTransitionsIncludeBothRepeatedInstantsAndSkipNoRecords(int month, int day, int hours)
    {
        using var store = new SessionStore();
        var zone = MidnightDstZone();
        var date = new DateOnly(2026, month, day);
        var start = TodayService.NextDay(date.AddDays(-1), zone);
        var end = TodayService.NextDay(date, zone);
        Assert.Equal(TimeSpan.FromHours(hours), end - start);
        var expected = new List<SessionId>();
        for (var instant = start.AddHours(-1); instant <= end; instant = instant.AddHours(1))
        {
            var row = Completed(instant);
            await store.Repository.AddAsync(row);
            if (instant >= start && instant < end) expected.Add(row.Id);
        }
        var today = await new TodayService(store.Repository, new ManualTimeProvider(start.AddHours(12)), () => zone).ReadAsync();
        Assert.Equal(expected, today.Sessions.Select(s => s.Id));
        Assert.Equal(end, today.NextDayAt);
    }

    [Fact]
    public async Task TimeZoneIsResolvedOnEachRead()
    {
        using var store = new SessionStore();
        var now = new DateTimeOffset(2026, 9, 2, 1, 0, 0, TimeSpan.Zero);
        var row = Completed(now); await store.Repository.AddAsync(row);
        TimeZoneInfo zone = FixedZone(3);
        var service = new TodayService(store.Repository, new ManualTimeProvider(now), () => zone);
        Assert.Equal(new DateOnly(2026, 9, 2), (await service.ReadAsync()).Date);
        zone = FixedZone(-7);
        var changed = await service.ReadAsync();
        Assert.Equal(new DateOnly(2026, 9, 1), changed.Date);
        Assert.Single(changed.Sessions);
    }

    [Fact]
    public async Task RefreshReflectsStartStopAndCompletionFromRealEngine()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock); await sessions.InitializeAsync();
        var service = new TodayService(store.Repository, clock, () => TimeZoneInfo.Utc);
        var started = await sessions.StartAsync(SessionType.Work);
        Assert.Equal(started, (await service.ReadAsync()).Running);
        clock.Advance(TimeSpan.FromMinutes(3)); await sessions.StopAsync(started.Id);
        var stopped = await service.ReadAsync();
        Assert.Null(stopped.Running); Assert.Equal(SessionStatus.Stopped, Assert.Single(stopped.Sessions).Status);
        Assert.Equal(TimeSpan.FromMinutes(3), stopped.WorkTime);
        var second = await sessions.StartAsync(SessionType.Break);
        clock.Set(second.PlannedEndAt); await sessions.CompleteIfDueAsync();
        var completed = await service.ReadAsync();
        Assert.Equal(1, completed.CompletedBreakCount); Assert.Equal(TimeSpan.FromMinutes(10), completed.BreakTime);
        Assert.Equal(50.0, completed.CompletionRate);
        clock.Set(new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero));
        Assert.Empty((await service.ReadAsync()).Sessions);
    }

    [Fact]
    public async Task CancelledReadDoesNotReturnPartialData()
    {
        using var store = new SessionStore();
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new TodayService(store.Repository).ReadAsync(cancel.Token));
    }

    internal static TimeZoneInfo FixedZone(int hours) => TimeZoneInfo.CreateCustomTimeZone($"UTC{hours:+0;-0}", TimeSpan.FromHours(hours), "Test zone", "Test zone");
    private static SessionRecord Completed(DateTimeOffset start, SessionType type = SessionType.Work, int minutes = 30) =>
        TestSessions.Finished(SessionStatus.Completed, startedAt: start, type: type, plannedDuration: TimeSpan.FromMinutes(minutes));
    private static TimeZoneInfo MidnightDstZone()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 10),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 10, 10));
        return TimeZoneInfo.CreateCustomTimeZone("Midnight DST", TimeSpan.FromHours(2), "Midnight DST", "Standard", "Daylight", [rule]);
    }
}
