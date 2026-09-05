using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class SessionRecoveryTests
{
    [Fact]
    public async Task Startup_NoActiveSessionDoesNotWrite()
    {
        using var store = new SessionStore();
        var repo = new LifecycleTestRepository(store.Repository);
        var result = await new SessionRecovery(repo, new ManualTimeProvider(TestSessions.Anchor)).RecoverStartupAsync();
        Assert.Equal(SessionRecoveryKind.NoActiveSession, result.Kind);
        Assert.Null(result.Session);
        Assert.False(result.ChangedStoredState);
        Assert.Equal(0, repo.ConditionalWrites);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Startup_AtOrAfterPlannedEndCompletesAtPlannedEnd(int extraSeconds)
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        var clock = new ManualTimeProvider(running.PlannedEndAt.AddSeconds(extraSeconds));
        SessionRecoveryResult result = await new SessionRecovery(store.Repository, clock).RecoverStartupAsync();
        Assert.Equal(SessionRecoveryKind.Completed, result.Kind);
        Assert.Equal(SessionStatus.Completed, result.Session!.Status);
        Assert.Equal(running.PlannedEndAt, result.Session.EndedAt);
        Assert.True(result.ChangedStoredState);
        Assert.Equal(result.Session, await store.Repository.GetAsync(running.Id));
    }

    [Fact]
    public async Task Startup_BeforeEndInterruptsAtStartAndPreservesFields()
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running(type: SessionType.Break, plannedDuration: TimeSpan.FromSeconds(17));
        await store.Repository.AddAsync(running);
        var result = await new SessionRecovery(store.Repository,
            new ManualTimeProvider(running.StartedAt.AddSeconds(16))).RecoverStartupAsync();
        Assert.Equal(SessionRecoveryKind.Interrupted, result.Kind);
        Assert.Equal(running with { Status = SessionStatus.Interrupted, EndedAt = running.StartedAt }, result.Session);
        Assert.True(result.ChangedStoredState);
    }

    [Fact]
    public async Task Startup_BackwardsClockStillInterruptsAtStart()
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        SessionRecoveryResult result = await new SessionRecovery(store.Repository,
            new ManualTimeProvider(running.StartedAt.AddMinutes(-1))).RecoverStartupAsync();
        Assert.Equal(SessionRecoveryKind.Interrupted, result.Kind);
        Assert.Equal(running.StartedAt, result.Session!.EndedAt);
    }

    [Fact]
    public async Task Shutdown_BeforeEndUsesCurrentTimeAndAfterEndUsesPlannedEnd()
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        var clock = new ManualTimeProvider(running.StartedAt.AddMinutes(3));
        SessionRecoveryResult early = await new SessionRecovery(store.Repository, clock).FinishShutdownAsync();
        Assert.Equal(SessionRecoveryKind.Interrupted, early.Kind);
        Assert.Equal(clock.GetUtcNow(), early.Session!.EndedAt);

        SessionRecord next = TestSessions.Running(startedAt: running.StartedAt.AddHours(1));
        await store.Repository.AddAsync(next);
        clock.Set(next.PlannedEndAt.AddSeconds(1));
        SessionRecoveryResult late = await new SessionRecovery(store.Repository, clock).FinishShutdownAsync();
        Assert.Equal(SessionRecoveryKind.Completed, late.Kind);
        Assert.Equal(next.PlannedEndAt, late.Session!.EndedAt);
    }

    [Fact]
    public async Task ConflictReturnsOriginalObservationWithoutRetry()
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        var repo = new LifecycleTestRepository(store.Repository) { ReturnConflict = true };
        SessionRecoveryResult result = await new SessionRecovery(repo,
            new ManualTimeProvider(running.PlannedEndAt)).RecoverStartupAsync();
        Assert.Equal(SessionRecoveryKind.Conflict, result.Kind);
        Assert.Equal(running, result.Session);
        Assert.False(result.ChangedStoredState);
        Assert.Equal(1, repo.ConditionalWrites);
    }

    [Fact]
    public async Task ReadAndWriteFailuresCanBeRetried()
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        var repo = new LifecycleTestRepository(store.Repository) { FailReads = true };
        var recovery = new SessionRecovery(repo, new ManualTimeProvider(running.PlannedEndAt));
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.RecoverStartupAsync());
        repo.FailReads = false;
        repo.FailWrites = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => recovery.RecoverStartupAsync());
        repo.FailWrites = false;
        Assert.Equal(SessionRecoveryKind.Completed, (await recovery.RecoverStartupAsync()).Kind);
    }

    [Fact]
    public async Task CancellationDoesNotWriteAndCanBeRetried()
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        var recovery = new SessionRecovery(store.Repository, new ManualTimeProvider(running.PlannedEndAt));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => recovery.RecoverStartupAsync(cancellation.Token));
        Assert.Equal(SessionStatus.Running, (await store.Repository.GetAsync(running.Id))!.Status);
        Assert.Equal(SessionRecoveryKind.Completed, (await recovery.RecoverStartupAsync()).Kind);
    }

    [Fact]
    public async Task RepeatedStartupAfterSuccessFindsNoActiveSession()
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        var recovery = new SessionRecovery(store.Repository, new ManualTimeProvider(running.PlannedEndAt));
        Assert.Equal(SessionRecoveryKind.Completed, (await recovery.RecoverStartupAsync()).Kind);
        Assert.Equal(SessionRecoveryKind.NoActiveSession, (await recovery.RecoverStartupAsync()).Kind);
    }
}
