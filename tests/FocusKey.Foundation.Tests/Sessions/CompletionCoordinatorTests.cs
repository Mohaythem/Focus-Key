using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class CompletionCoordinatorTests
{
    [Theory]
    [InlineData(SessionType.Work)]
    [InlineData(SessionType.Break)]
    public async Task StartArmsForPlannedEndAndDueEvaluationCompletes(SessionType type)
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        var notified = new TaskCompletionSource<SessionRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var coordinator = new CompletionCoordinator(sessions, s => { notified.TrySetResult(s); return Task.CompletedTask; }, _ => { }, clock);

        SessionRecord started = await coordinator.StartAsync(type);
        Assert.Equal(started.PlannedEndAt - clock.GetUtcNow(), clock.Timer.LastDueTime);
        clock.Set(started.PlannedEndAt);
        await coordinator.EvaluateAsync();

        Assert.True(notified.Task.IsCompletedSuccessfully);
        SessionRecord completed = await notified.Task;
        Assert.Equal(type, completed.Type);
        Assert.Equal(SessionStatus.Completed, completed.Status);
        Assert.Equal(started.PlannedEndAt, completed.EndedAt);
    }

    [Fact]
    public async Task TimerIsOneShotAndIdleAfterCompletion()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        using var coordinator = NewCoordinator(sessions, clock, out var notifications, out _);
        SessionRecord started = await coordinator.StartAsync(SessionType.Work);
        Assert.Equal(Timeout.InfiniteTimeSpan, clock.Timer.LastPeriod);
        clock.Set(started.PlannedEndAt);
        await coordinator.EvaluateAsync();
        await coordinator.EvaluateAsync();
        Assert.Equal(Timeout.InfiniteTimeSpan, clock.Timer.LastDueTime);
        Assert.Equal(Timeout.InfiniteTimeSpan, clock.Timer.LastPeriod);
        await coordinator.EvaluateAsync();
        Assert.Equal(1, notifications.Count);
    }

    [Fact]
    public async Task RepeatedAndConcurrentEvaluationsNotifyOnce()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        using var coordinator = NewCoordinator(sessions, clock, out var notifications, out _);
        SessionRecord started = await coordinator.StartAsync(SessionType.Work);
        clock.Set(started.PlannedEndAt);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => coordinator.EvaluateAsync()));
        await coordinator.EvaluateAsync();
        Assert.Equal(1, notifications.Count);
        Assert.Equal(SessionStatus.Completed, (await store.Repository.GetAsync(started.Id))!.Status);
    }

    [Fact]
    public async Task StoppedSessionDoesNotNotify()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        using var coordinator = NewCoordinator(sessions, clock, out var notifications, out _);
        SessionRecord started = await coordinator.StartAsync(SessionType.Work);
        Assert.Equal(SessionOutcomeKind.Stopped, (await sessions.StopAsync()).Kind);
        clock.Set(started.PlannedEndAt);
        await coordinator.EvaluateAsync();
        Assert.Equal(0, notifications.Count);
    }

    [Fact]
    public async Task NotificationFailureIsReportedWithoutReplay()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        var reports = new List<Exception>();
        var calls = 0;
        using var coordinator = new CompletionCoordinator(sessions, _ => { calls++; throw new InvalidOperationException("notify"); }, reports.Add, clock);
        SessionRecord started = await coordinator.StartAsync(SessionType.Work);
        clock.Set(started.PlannedEndAt);
        await coordinator.EvaluateAsync();
        await coordinator.EvaluateAsync();
        Assert.Equal(1, calls);
        Assert.Single(reports);
    }

    [Fact]
    public async Task DatabaseFailureReportsAndRetriesOnTimer()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var repo = new LifecycleTestRepository(store.Repository);
        var sessions = new SessionCoordinator(repo, clock);
        await sessions.InitializeAsync();
        using var coordinator = NewCoordinator(sessions, clock, out var notifications, out var reports);
        SessionRecord started = await coordinator.StartAsync(SessionType.Work);
        clock.Set(started.PlannedEndAt);
        repo.FailReads = true;
        await coordinator.EvaluateAsync();
        Assert.NotEmpty(reports);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Timer.LastDueTime);
        repo.FailReads = false;
        clock.Advance(TimeSpan.FromSeconds(10));
        clock.Timer.Fire();
        await coordinator.EvaluateAsync();
        Assert.Equal(1, notifications.Count);
    }

    [Fact]
    public async Task ShutdownDrainsAndDisablesTimerAndFailedShutdownCanResume()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var repo = new LifecycleTestRepository(store.Repository);
        var sessions = new SessionCoordinator(repo, clock);
        await sessions.InitializeAsync();
        using var coordinator = NewCoordinator(sessions, clock, out var notifications, out _);
        SessionRecord started = await coordinator.StartAsync(SessionType.Work);
        repo.FailWrites = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.ShutdownAsync());
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Timer.LastDueTime);
        repo.FailWrites = false;
        SessionRecoveryResult result = await coordinator.ShutdownAsync();
        Assert.Equal(SessionRecoveryKind.Interrupted, result.Kind);
        Assert.Equal(Timeout.InfiniteTimeSpan, clock.Timer.LastDueTime);
        Assert.Equal(0, notifications.Count);
        Assert.Equal(SessionStatus.Interrupted, (await store.Repository.GetAsync(started.Id))!.Status);
    }

    [Fact]
    public async Task CancellationCanAbortStartBeforePersistence()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        using var coordinator = NewCoordinator(sessions, clock, out _, out _);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.StartAsync(SessionType.Work, cancellation.Token));
        Assert.Null(await store.Repository.GetRunningAsync());
    }

    [Fact]
    public async Task StartThatReachesPlannedEndWhilePersistingArmsImmediateEvaluation()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        using var coordinator = NewCoordinator(sessions, clock, out _, out _);
        clock.AdvanceOnNextRead = TestSessions.WorkLength + TimeSpan.FromMilliseconds(1);

        SessionRecord started = await coordinator.StartAsync(SessionType.Work);

        Assert.Equal(TimeSpan.Zero, clock.Timer.LastDueTime);
        Assert.Equal(started.PlannedEndAt, started.StartedAt + started.PlannedDuration);
    }

    [Fact]
    public async Task DelayedTimerCompletesWithPlannedTimestampWithoutAnyWindow()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        using var coordinator = NewCoordinator(sessions, clock, out var notifications, out var errors);
        var started = await coordinator.StartAsync(SessionType.Break);
        clock.Set(started.PlannedEndAt.AddHours(3));
        clock.Timer.Fire();
        await coordinator.EvaluateAsync(); // Drains the callback using the coordinator's gate.
        Assert.Equal(started.PlannedEndAt, (await store.Repository.GetAsync(started.Id))!.EndedAt);
        Assert.Equal(1, notifications.Count);
        Assert.Empty(errors);
        Assert.Equal(Timeout.InfiniteTimeSpan, clock.Timer.LastDueTime);
    }

    [Fact]
    public async Task EarlyWakeAndBackwardClockRearmFromUtc()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        using var coordinator = NewCoordinator(sessions, clock, out var notifications, out _);
        var started = await coordinator.StartAsync(SessionType.Work);
        clock.Set(started.StartedAt.AddMinutes(-20));
        clock.Timer.Fire();
        await coordinator.EvaluateAsync();
        Assert.Equal(TimeSpan.FromMinutes(50), clock.Timer.LastDueTime);
        Assert.Equal(SessionStatus.Running, (await store.Repository.GetAsync(started.Id))!.Status);
        Assert.Equal(0, notifications.Count);
    }

    [Fact]
    public async Task InterruptedAndPreviouslyCompletedHistoryIsNotReplayed()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var previous = new SessionCoordinator(store.Repository, clock);
        await previous.InitializeAsync();
        var interrupted = await previous.StartAsync(SessionType.Work);
        await previous.ShutdownAsync();
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        var completed = await sessions.StartAsync(SessionType.Break);
        clock.Set(completed.PlannedEndAt);
        await sessions.CompleteIfDueAsync();
        using var coordinator = NewCoordinator(sessions, clock, out var notifications, out _);
        await coordinator.EvaluateAsync();
        Assert.Equal(SessionStatus.Interrupted, (await store.Repository.GetAsync(interrupted.Id))!.Status);
        Assert.Equal(0, notifications.Count);
        Assert.Equal(Timeout.InfiniteTimeSpan, clock.Timer.LastDueTime);
    }

    [Fact]
    public async Task TwoSchedulersOnSameStoreStillHaveOneWinner()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var clock2 = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        var sessions2 = new SessionCoordinator(store.ReopenRepository(), clock2);
        await sessions.InitializeAsync();
        await sessions2.InitializeAsync();
        int count = 0;
        Task Notify(SessionRecord _) { Interlocked.Increment(ref count); return Task.CompletedTask; }
        using var first = new CompletionCoordinator(sessions, Notify, _ => { }, clock);
        using var second = new CompletionCoordinator(sessions2, Notify, _ => { }, clock2);
        var started = await first.StartAsync(SessionType.Work);
        clock.Set(started.PlannedEndAt); clock2.Set(started.PlannedEndAt);
        await Task.WhenAll(Task.Run(first.EvaluateAsync), Task.Run(second.EvaluateAsync));
        Assert.Equal(1, count);
        Assert.Equal(started.PlannedEndAt, (await store.Repository.GetAsync(started.Id))!.EndedAt);
    }

    [Fact]
    public async Task CompletionWriteFailureDoesNotNotifyAndRetriesDurably()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var repo = new LifecycleTestRepository(store.Repository);
        var sessions = new SessionCoordinator(repo, clock);
        await sessions.InitializeAsync();
        using var coordinator = NewCoordinator(sessions, clock, out var notifications, out var reports);
        var started = await coordinator.StartAsync(SessionType.Work);
        clock.Set(started.PlannedEndAt); repo.FailWrites = true;
        await coordinator.EvaluateAsync();
        Assert.Equal(0, notifications.Count);
        Assert.Equal(SessionStatus.Running, (await store.Repository.GetAsync(started.Id))!.Status);
        Assert.Single(reports);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Timer.LastDueTime);
        repo.FailWrites = false; clock.Advance(TimeSpan.FromSeconds(10));
        clock.Timer.Fire(); await coordinator.EvaluateAsync();
        Assert.Equal(1, notifications.Count);
    }

    [Fact]
    public async Task ConflictDoesNotNotifyOrBusyPoll()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var repo = new LifecycleTestRepository(store.Repository);
        var sessions = new SessionCoordinator(repo, clock);
        await sessions.InitializeAsync();
        using var coordinator = NewCoordinator(sessions, clock, out var notifications, out _);
        var started = await coordinator.StartAsync(SessionType.Work);
        clock.Set(started.PlannedEndAt); repo.ReturnConflict = true;
        await coordinator.EvaluateAsync();
        Assert.Equal(0, notifications.Count);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Timer.LastDueTime);
    }

    [Fact]
    public async Task ShutdownWaitsForInFlightCompletionAndPreventsLateCallbacks()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        using var coordinator = new CompletionCoordinator(sessions, async _ =>
        { calls++; entered.SetResult(); await release.Task; }, _ => { }, clock);
        var started = await coordinator.StartAsync(SessionType.Work);
        clock.Set(started.PlannedEndAt);
        Task completing = coordinator.EvaluateAsync();
        await entered.Task;
        Task shutdown = coordinator.ShutdownAsync();
        Assert.False(shutdown.IsCompleted);
        release.SetResult(); await completing; await shutdown;
        clock.Timer.Fire(); await coordinator.EvaluateAsync();
        Assert.Equal(1, calls);
        Assert.Equal(Timeout.InfiniteTimeSpan, clock.Timer.LastDueTime);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.StartAsync(SessionType.Break));
        coordinator.Dispose();
        Assert.True(clock.Timer.IsDisposed);
    }

    [Fact]
    public async Task ConcurrentStartsStillRejectSecondAndKeepOriginalDeadline()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        using var coordinator = NewCoordinator(sessions, clock, out _, out _);
        var started = await coordinator.StartAsync(SessionType.Work);
        await Assert.ThrowsAsync<ActiveSessionAlreadyExistsException>(() => coordinator.StartAsync(SessionType.Break));
        Assert.Equal(started.PlannedDuration, clock.Timer.LastDueTime);
        Assert.Equal(1L, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions"));
    }

    private static CompletionCoordinator NewCoordinator(SessionCoordinator sessions, CompletionTestTimeProvider clock,
        out NotificationCounter notifications, out List<Exception> reports)
    {
        notifications = new NotificationCounter();
        reports = new List<Exception>();
        return new CompletionCoordinator(sessions, notifications.NotifyAsync, reports.Add, clock);
    }

    [Fact]
    public async Task DueShutdownNotifiesOnceEvenWhenItsResultIsCached()
    {
        using var store = new SessionStore();
        var clock = new CompletionTestTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        using var coordinator = NewCoordinator(sessions, clock, out var notifications, out _);
        var started = await coordinator.StartAsync(SessionType.Break);
        clock.Set(started.PlannedEndAt.AddMinutes(2));
        Assert.Equal(SessionRecoveryKind.Completed, (await coordinator.ShutdownAsync()).Kind);
        await coordinator.ShutdownAsync();
        clock.Timer.Fire(); await coordinator.EvaluateAsync();
        Assert.Equal(1, notifications.Count);
        Assert.Equal(started.PlannedEndAt, (await store.Repository.GetAsync(started.Id))!.EndedAt);
    }

    private sealed class NotificationCounter
    {
        internal int Count { get; private set; }
        internal Task NotifyAsync(SessionRecord _) { Count++; return Task.CompletedTask; }
    }
}

internal sealed class CompletionTestTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;
    internal CompletionTestTimeProvider(DateTimeOffset utcNow) { _utcNow = utcNow.ToUniversalTime(); Timer = new ControlledTimer(); }
    internal ControlledTimer Timer { get; }
    internal TimeSpan? AdvanceOnNextRead { get; set; }
    public override DateTimeOffset GetUtcNow()
    {
        DateTimeOffset observed = _utcNow;
        if (AdvanceOnNextRead is { } amount) { _utcNow = _utcNow.Add(amount); AdvanceOnNextRead = null; }
        return observed;
    }
    internal void Set(DateTimeOffset value) => _utcNow = value.ToUniversalTime();
    internal void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    { Timer.Initialize(callback, state); return Timer; }
}

internal sealed class ControlledTimer : ITimer
{
    private TimerCallback? _callback;
    private object? _state;
    internal TimeSpan LastDueTime { get; private set; } = Timeout.InfiniteTimeSpan;
    internal TimeSpan LastPeriod { get; private set; } = Timeout.InfiniteTimeSpan;
    internal bool IsDisposed { get; private set; }
    internal void Initialize(TimerCallback callback, object? state) { _callback = callback; _state = state; }
    public bool Change(TimeSpan dueTime, TimeSpan period) { LastDueTime = dueTime; LastPeriod = period; return true; }
    internal void Fire() => _callback?.Invoke(_state);
    public void Dispose() { IsDisposed = true; LastDueTime = Timeout.InfiniteTimeSpan; LastPeriod = Timeout.InfiniteTimeSpan; _callback = null; }
    public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
}
