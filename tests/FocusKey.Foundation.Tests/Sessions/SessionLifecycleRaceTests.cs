using FocusKey.Foundation.Sessions;
using Microsoft.Data.Sqlite;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class SessionLifecycleRaceTests
{
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    [Fact]
    public async Task IndependentRecoveriesObservingSameRowHaveExactlyOneWinner()
    {
        using var store = new SessionStore();
        var running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        var bothRead = Signal();
        int reads = 0;
        async Task Barrier()
        {
            if (Interlocked.Increment(ref reads) == 2) bothRead.SetResult();
            await bothRead.Task;
        }
        var first = new LifecycleTestRepository(store.ReopenRepository()) { AfterRead = Barrier };
        var second = new LifecycleTestRepository(store.ReopenRepository()) { AfterRead = Barrier };
        var time = new ManualTimeProvider(running.PlannedEndAt);
        var results = await Task.WhenAll(new SessionCoordinator(first, time).InitializeAsync(),
            new SessionCoordinator(second, time).InitializeAsync());
        Assert.Single(results, result => result.Kind == SessionRecoveryKind.Completed);
        Assert.Single(results, result => result.Kind == SessionRecoveryKind.Conflict);
        Assert.Equal(running with { Status = SessionStatus.Completed, EndedAt = running.PlannedEndAt },
            await store.Repository.GetAsync(running.Id));
        Assert.Equal(1L, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StaleInterruptionCannotOverwriteCompletionOrTouchNewSession(bool startNew)
    {
        using var store = new SessionStore();
        var running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        var read = Signal();
        var release = Signal();
        var repo = new LifecycleTestRepository(store.ReopenRepository())
        {
            AfterRead = async () => { read.SetResult(); await release.Task; }
        };
        var pending = new SessionRecovery(repo, new ManualTimeProvider(running.StartedAt)).RecoverStartupAsync();
        await read.Task;
        var engine = new SessionEngine(store.ReopenRepository(), new ManualTimeProvider(running.PlannedEndAt));
        var completed = await engine.CompleteIfDueAsync();
        SessionRecord? next = startNew ? await engine.StartAsync(SessionType.Break) : null;
        release.SetResult();
        Assert.Equal(SessionRecoveryKind.Conflict, (await pending).Kind);
        Assert.Equal(completed.Session, await store.Repository.GetAsync(running.Id));
        Assert.Equal(next, await store.Repository.GetRunningAsync());
        Assert.Equal(1, repo.ConditionalWrites);
    }

    [Fact]
    public async Task StaleRecoveryCannotOverwriteChangedRunningFields()
    {
        using var store = new SessionStore();
        var running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        var changed = running with { PlannedDuration = TimeSpan.FromMinutes(45) };
        var repo = new LifecycleTestRepository(store.ReopenRepository())
        {
            AfterRead = () => store.Repository.UpdateAsync(changed)
        };
        Assert.Equal(SessionRecoveryKind.Conflict, (await new SessionRecovery(repo,
            new ManualTimeProvider(running.PlannedEndAt)).RecoverStartupAsync()).Kind);
        Assert.Equal(changed, await store.Repository.GetRunningAsync());
    }

    [Fact]
    public async Task InitializationSerializesRepeatAndStartAndAllowsQueuedCancellation()
    {
        using var store = new SessionStore();
        var entered = Signal();
        var release = Signal();
        var repo = new LifecycleTestRepository(store.Repository)
        {
            AfterRead = async () => { entered.TrySetResult(); await release.Task; }
        };
        var coordinator = new SessionCoordinator(repo, new ManualTimeProvider(TestSessions.Anchor));
        var init = coordinator.InitializeAsync();
        await entered.Task;
        var repeat = coordinator.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        var cancelled = coordinator.StartAsync(SessionType.Work, cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var start = coordinator.StartAsync(SessionType.Break);
        Assert.False(start.IsCompleted);
        release.SetResult();
        Assert.Same(await init, await repeat);
        var session = await start;
        Assert.Equal(session, await store.Repository.GetRunningAsync());
        // One startup observation plus the engine's normal Start guard; repeated init adds none.
        Assert.Equal(2, repo.RunningReads);
    }

    [Fact]
    public async Task ConcurrentShutdownRunsOnceAndClosesCoordinator()
    {
        using var store = new SessionStore();
        var repo = new LifecycleTestRepository(store.Repository);
        var coordinator = new SessionCoordinator(repo, new ManualTimeProvider(TestSessions.Anchor));
        await coordinator.InitializeAsync();
        await coordinator.StartAsync(SessionType.Work);
        var entered = Signal();
        var release = Signal();
        repo.AfterRead = async () => { entered.SetResult(); await release.Task; };
        var first = coordinator.ShutdownAsync();
        await entered.Task;
        var second = coordinator.ShutdownAsync();
        release.SetResult();
        Assert.Same(await first, await second);
        Assert.Equal(1, repo.ConditionalWrites);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.InitializeAsync());
    }

    [Fact]
    public async Task CancellationAfterReadLeavesRowRunningAndInitializationRetryable()
    {
        using var store = new SessionStore();
        var running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        using var cancellation = new CancellationTokenSource();
        var repo = new LifecycleTestRepository(store.Repository)
        {
            AfterRead = () => { cancellation.Cancel(); return Task.CompletedTask; }
        };
        var coordinator = new SessionCoordinator(repo, new ManualTimeProvider(running.PlannedEndAt));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.InitializeAsync(cancellation.Token));
        Assert.Equal(running, await store.Repository.GetRunningAsync());
        Assert.Equal(0, repo.ConditionalWrites);
        repo.AfterRead = null;
        Assert.Equal(SessionRecoveryKind.Completed, (await coordinator.InitializeAsync()).Kind);
    }

    [Fact]
    public async Task CancellationAfterCommitPreservesAndCachesSuccessfulRecovery()
    {
        using var store = new SessionStore();
        var running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        using var cancellation = new CancellationTokenSource();
        var repo = new LifecycleTestRepository(store.Repository) { AfterWrite = cancellation.Cancel };
        var coordinator = new SessionCoordinator(repo, new ManualTimeProvider(running.PlannedEndAt));
        var result = await coordinator.InitializeAsync(cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(SessionRecoveryKind.Completed, result.Kind);
        Assert.Same(result, await coordinator.InitializeAsync());
        Assert.Equal(1, repo.ConditionalWrites);
    }

    [Fact]
    public async Task ShutdownWaitsForInFlightStartAndCancelledShutdownRemainsUsable()
    {
        using var store = new SessionStore();
        var repo = new LifecycleTestRepository(store.Repository);
        var coordinator = new SessionCoordinator(repo, new ManualTimeProvider(TestSessions.Anchor));
        await coordinator.InitializeAsync();
        var entered = Signal();
        var release = Signal();
        repo.AfterRead = async () => { entered.TrySetResult(); await release.Task; };
        var start = coordinator.StartAsync(SessionType.Work);
        await entered.Task;
        using var cancellation = new CancellationTokenSource();
        var cancelled = coordinator.ShutdownAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        var shutdown = coordinator.ShutdownAsync();
        Assert.False(shutdown.IsCompleted);
        release.SetResult();
        var session = await start;
        var result = await shutdown;
        Assert.Equal(SessionRecoveryKind.Interrupted, result.Kind);
        Assert.Equal(session.Id, result.Session!.Id);
        Assert.Null(await store.Repository.GetRunningAsync());
    }

    [Fact]
    public async Task SqliteWriteFailureRollsBackAndInitializationCanRetry()
    {
        using var store = new SessionStore();
        var running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        store.ExecuteRaw("CREATE TRIGGER reject_recovery BEFORE UPDATE ON sessions BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        var coordinator = new SessionCoordinator(store.Repository, new ManualTimeProvider(running.PlannedEndAt));
        await Assert.ThrowsAsync<SqliteException>(() => coordinator.InitializeAsync());
        Assert.Equal(running, await store.Repository.GetRunningAsync());
        store.ExecuteRaw("DROP TRIGGER reject_recovery;");
        Assert.Equal(SessionRecoveryKind.Completed, (await coordinator.InitializeAsync()).Kind);
    }
}
