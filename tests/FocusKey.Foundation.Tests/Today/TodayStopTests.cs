using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Today;

public sealed class TodayStopTests
{
    [Fact]
    public async Task StaleDisplayedIdCannotStopReplacementEvenIfReplacementIsDue()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var engine = new SessionEngine(store.Repository, clock);
        var old = await engine.StartAsync(SessionType.Work); await engine.StopAsync();
        var current = await engine.StartAsync(SessionType.Break); clock.Set(current.PlannedEndAt);
        Assert.Equal(SessionOutcomeKind.Conflict, (await engine.StopAsync(old.Id)).Kind);
        Assert.Equal(current, await store.Repository.GetRunningAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StopUsesEngineTimingAndNotifiesOnlyWhenCompleted(bool due)
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock); await sessions.InitializeAsync();
        int notifications = 0;
        using var completion = new CompletionCoordinator(sessions, _ => { notifications++; return Task.CompletedTask; }, _ => { }, clock);
        var started = await completion.StartAsync(SessionType.Work);
        clock.Set(due ? started.PlannedEndAt.AddMinutes(1) : started.StartedAt.AddMinutes(3));
        var outcome = await completion.StopAsync(started.Id);
        Assert.Equal(due ? SessionOutcomeKind.Completed : SessionOutcomeKind.Stopped, outcome.Kind);
        Assert.Equal(due ? 1 : 0, notifications);
        Assert.Equal(due ? started.PlannedEndAt : clock.GetUtcNow(), outcome.Session!.EndedAt);
        clock.Timer.Fire(); await completion.EvaluateAsync(); await completion.StopAsync(started.Id);
        Assert.Equal(due ? 1 : 0, notifications);
        Assert.Equal(Timeout.InfiniteTimeSpan, clock.Timer.LastDueTime);
    }

    [Fact]
    public async Task FailedStopRetainsDeadlineAndRunningRecord()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var repository = new LifecycleTestRepository(store.Repository);
        var sessions = new SessionCoordinator(repository, clock); await sessions.InitializeAsync();
        using var completion = new CompletionCoordinator(sessions, _ => Task.CompletedTask, _ => { }, clock);
        var started = await completion.StartAsync(SessionType.Work); repository.FailWrites = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => completion.StopAsync(started.Id));
        Assert.Equal(started, await store.Repository.GetRunningAsync());
        Assert.Equal(started.PlannedDuration, clock.Timer.LastDueTime);
    }

    [Fact]
    public async Task ConcurrentStopAndDeadlineHaveOneDurableCompletionAndOneNotification()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock); await sessions.InitializeAsync();
        int notifications = 0;
        using var completion = new CompletionCoordinator(sessions, _ => { notifications++; return Task.CompletedTask; }, _ => { }, clock);
        var started = await completion.StartAsync(SessionType.Work); clock.Set(started.PlannedEndAt);
        await Task.WhenAll(Task.Run(() => completion.StopAsync(started.Id)), Task.Run(completion.EvaluateAsync));
        Assert.Equal(1, notifications);
        Assert.Equal(SessionStatus.Completed, (await store.Repository.GetAsync(started.Id))!.Status);
    }
}
