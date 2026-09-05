using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class SessionCoordinatorTests
{
    [Fact]
    public async Task OperationsRequireInitialization()
    {
        using var store = new SessionStore();
        var coordinator = new SessionCoordinator(store.Repository, new ManualTimeProvider(TestSessions.Anchor));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartAsync(SessionType.Work));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.GetActiveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StopAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CompleteIfDueAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ShutdownAsync());
    }

    [Fact]
    public async Task InitializationFailureAndCancellationCanRetry()
    {
        using var store = new SessionStore();
        var repo = new LifecycleTestRepository(store.Repository) { FailReads = true };
        var coordinator = new SessionCoordinator(repo, new ManualTimeProvider(TestSessions.Anchor));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.InitializeAsync());
        repo.FailReads = false;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.InitializeAsync(cancellation.Token));
        SessionRecoveryResult result = await coordinator.InitializeAsync();
        Assert.Equal(SessionRecoveryKind.NoActiveSession, result.Kind);
        Assert.Equal(2, repo.RunningReads);
    }

    [Fact]
    public async Task RepeatedInitializationDoesNotRecoverNewlyStartedSession()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var coordinator = new SessionCoordinator(store.Repository, clock);
        Assert.Equal(SessionRecoveryKind.NoActiveSession, (await coordinator.InitializeAsync()).Kind);
        SessionRecord started = await coordinator.StartAsync(SessionType.Work);
        SessionRecoveryResult again = await coordinator.InitializeAsync();
        Assert.Equal(SessionRecoveryKind.NoActiveSession, again.Kind);
        Assert.Equal(started, await store.Repository.GetRunningAsync());
    }

    [Fact]
    public async Task InitializedCoordinatorForwardsLifecycleAndDurations()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var durations = new SessionDurations(TimeSpan.FromSeconds(9), TimeSpan.FromSeconds(4));
        var coordinator = new SessionCoordinator(store.Repository, clock, durations);
        await coordinator.InitializeAsync();
        SessionRecord started = await coordinator.StartAsync(SessionType.Break);
        Assert.Equal(TimeSpan.FromSeconds(4), started.PlannedDuration);
        Assert.Equal(started.Id, (await coordinator.GetActiveAsync())!.Id);
        Assert.Equal(SessionOutcomeKind.Stopped, (await coordinator.StopAsync()).Kind);
    }

    [Fact]
    public async Task ShutdownBeforeEndInterruptsAndClosesCoordinator()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var coordinator = new SessionCoordinator(store.Repository, clock);
        await coordinator.InitializeAsync();
        SessionRecord started = await coordinator.StartAsync(SessionType.Work);
        clock.Advance(TimeSpan.FromMinutes(2));
        SessionRecoveryResult result = await coordinator.ShutdownAsync();
        Assert.Equal(SessionRecoveryKind.Interrupted, result.Kind);
        Assert.Equal(clock.GetUtcNow(), result.Session!.EndedAt);
        Assert.Equal(result.Session, await store.Repository.GetAsync(started.Id));
        Assert.Equal(result, await coordinator.ShutdownAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartAsync(SessionType.Work));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.GetActiveAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StopAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.CompleteIfDueAsync());
    }

    [Fact]
    public async Task ShutdownAtExpiryCompletesAtPlannedEnd()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var coordinator = new SessionCoordinator(store.Repository, clock);
        await coordinator.InitializeAsync();
        SessionRecord started = await coordinator.StartAsync(SessionType.Work);
        clock.Set(started.PlannedEndAt);
        SessionRecoveryResult result = await coordinator.ShutdownAsync();
        Assert.Equal(SessionRecoveryKind.Completed, result.Kind);
        Assert.Equal(started.PlannedEndAt, result.Session!.EndedAt);
    }

    [Fact]
    public async Task ShutdownFailureLeavesCoordinatorOpenAndCanRetry()
    {
        using var store = new SessionStore();
        var repo = new LifecycleTestRepository(store.Repository);
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var coordinator = new SessionCoordinator(repo, clock);
        await coordinator.InitializeAsync();
        SessionRecord started = await coordinator.StartAsync(SessionType.Work);
        clock.Advance(TimeSpan.FromMinutes(1));
        repo.FailWrites = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ShutdownAsync());
        repo.FailWrites = false;
        Assert.Equal(SessionStatus.Running, (await store.Repository.GetAsync(started.Id))!.Status);
        Assert.Equal(SessionRecoveryKind.Interrupted, (await coordinator.ShutdownAsync()).Kind);
        Assert.Equal(2, repo.ConditionalWrites);
        Assert.Equal(4, repo.RunningReads);
    }
}
