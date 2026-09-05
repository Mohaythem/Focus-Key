using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class SessionEngineTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAroundTheWrite_DoesNotMisreportCommittedState(bool afterCommit)
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        using var cancellation = new CancellationTokenSource();
        var repository = new EngineTestRepository(store.Repository);
        var engine = new SessionEngine(repository, clock);
        SessionRecord started = await engine.StartAsync(SessionType.Work);
        clock.Set(started.PlannedEndAt);
        if (afterCommit) repository.AfterUpdate = cancellation.Cancel;
        else repository.BeforeUpdate = cancellation.Cancel;

        if (afterCommit)
        {
            SessionOutcome outcome = await engine.CompleteIfDueAsync(cancellation.Token);
            Assert.Equal(SessionOutcomeKind.Completed, outcome.Kind);
            Assert.True(outcome.ChangedStoredState);
            Assert.Equal(outcome.Session, await store.Repository.GetAsync(started.Id));
        }
        else
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.CompleteIfDueAsync(cancellation.Token));
            Assert.Equal(started, await store.Repository.GetRunningAsync());
            repository.BeforeUpdate = null;
            Assert.Equal(SessionOutcomeKind.Completed, (await engine.CompleteIfDueAsync()).Kind);
        }
    }

    [Fact]
    public async Task NaturalCompletion_ExactlyAtDeadlinePersistsTheOriginalFacts()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor.AddTicks(1234567));
        var engine = new SessionEngine(store.Repository, clock);
        SessionRecord started = await engine.StartAsync(SessionType.Break);
        clock.Set(started.PlannedEndAt.AddTicks(-1));
        Assert.Equal(SessionOutcomeKind.StillRunning, (await engine.CompleteIfDueAsync()).Kind);
        Assert.Equal(started, await store.Repository.GetRunningAsync());
        clock.Set(started.PlannedEndAt);
        SessionOutcome result = await engine.CompleteIfDueAsync();
        Assert.Equal(SessionOutcomeKind.Completed, result.Kind);
        Assert.Equal(started with { Status = SessionStatus.Completed, EndedAt = started.PlannedEndAt }, result.Session);
        Assert.Equal(result.Session, await store.ReopenRepository().GetAsync(started.Id));
    }

    [Theory]
    [InlineData(SessionType.Work, 1800)]
    [InlineData(SessionType.Break, 600)]
    public async Task Start_UsesTheExpectedDefaultDuration(SessionType type, int seconds)
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var engine = new SessionEngine(store.Repository, clock);

        SessionRecord started = await engine.StartAsync(type);

        Assert.Equal(type, started.Type);
        Assert.Equal(TimeSpan.FromSeconds(seconds), started.PlannedDuration);
        Assert.Equal(TestSessions.Anchor, started.StartedAt);
        Assert.Equal(SessionStatus.Running, (await store.Repository.GetAsync(started.Id))!.Status);
    }

    [Fact]
    public async Task Start_AllowsValidCustomDurations()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var engine = new SessionEngine(store.Repository, clock, new SessionDurations(
            TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(11)));

        Assert.Equal(TimeSpan.FromSeconds(7), (await engine.StartAsync(SessionType.Work)).PlannedDuration);
    }

    [Fact]
    public async Task Start_RejectsDurationWhosePlannedEndWouldOverflowWithoutWriting()
    {
        using var store = new SessionStore();
        DateTimeOffset nearMaximum = DateTimeOffset.MaxValue.AddSeconds(-5);
        var clock = new ManualTimeProvider(nearMaximum);
        var engine = new SessionEngine(store.Repository, clock, new SessionDurations(
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)));

        await Assert.ThrowsAsync<ArgumentException>(() => engine.StartAsync(SessionType.Work));
        Assert.Equal(0, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public async Task Start_RejectsWhenAnotherSessionIsRunning()
    {
        using var store = new SessionStore();
        var engine = new SessionEngine(store.Repository, new ManualTimeProvider(TestSessions.Anchor));
        SessionRecord first = await engine.StartAsync(SessionType.Work);

        ActiveSessionAlreadyExistsException error = await Assert.ThrowsAsync<ActiveSessionAlreadyExistsException>(
            () => engine.StartAsync(SessionType.Break));
        Assert.Equal(first.Id, error.ExistingId);
        Assert.Equal(first, await store.Repository.GetRunningAsync());
    }

    [Theory]
    [InlineData((SessionType)0)]
    [InlineData((SessionType)99)]
    public async Task Start_RejectsUndefinedType(SessionType type)
    {
        using var store = new SessionStore();
        var engine = new SessionEngine(store.Repository, new ManualTimeProvider(TestSessions.Anchor));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => engine.StartAsync(type));
        Assert.Equal(0, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public void Start_RejectsInvalidBreakDuration()
    {
        using var store = new SessionStore();
        Assert.Throws<ArgumentException>(() => new SessionDurations(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(0)));
        Assert.Equal(0, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public async Task GetActive_ComputesTimeWithoutWritingWhenDue()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var engine = new SessionEngine(store.Repository, clock);
        SessionRecord started = await engine.StartAsync(SessionType.Work);
        clock.Advance(TimeSpan.FromMinutes(12));

        SessionSnapshot snapshot = (await engine.GetActiveAsync())!;
        Assert.Equal(TimeSpan.FromMinutes(12), snapshot.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(18), snapshot.Remaining);

        clock.Advance(TimeSpan.FromMinutes(18));
        snapshot = (await engine.GetActiveAsync())!;
        Assert.True(snapshot.HasReachedPlannedEnd);
        Assert.Equal(SessionStatus.Running, (await store.Repository.GetAsync(started.Id))!.Status);
    }

    [Fact]
    public async Task Lifecycle_ReturnsNoActiveSessionWhenNothingIsRunning()
    {
        using var store = new SessionStore();
        var engine = new SessionEngine(store.Repository, new ManualTimeProvider(TestSessions.Anchor));
        Assert.Equal(SessionOutcomeKind.NoActiveSession, (await engine.StopAsync()).Kind);
        Assert.Equal(SessionOutcomeKind.NoActiveSession, (await engine.CompleteIfDueAsync()).Kind);
        Assert.Null(await engine.GetActiveAsync());
    }

    [Fact]
    public async Task Stop_BeforeExpiryPersistsActualStopTime()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var engine = new SessionEngine(store.Repository, clock);
        SessionRecord started = await engine.StartAsync(SessionType.Work);
        clock.Advance(TimeSpan.FromMinutes(5));

        SessionOutcome outcome = await engine.StopAsync();

        Assert.Equal(SessionOutcomeKind.Stopped, outcome.Kind);
        Assert.Equal(clock.GetUtcNow(), outcome.Session!.EndedAt);
        Assert.Equal(outcome.Session, await store.Repository.GetAsync(started.Id));
    }

    [Fact]
    public async Task Stop_WhenClockMovesBeforeStart_ClampsEndToStart()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var engine = new SessionEngine(store.Repository, clock);
        SessionRecord started = await engine.StartAsync(SessionType.Work);
        clock.Set(started.StartedAt.AddMinutes(-1));
        SessionOutcome outcome = await engine.StopAsync();
        Assert.Equal(started.StartedAt, outcome.Session!.EndedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Stop_AtOrAfterExpiryCompletesAtPlannedEnd(int extraSeconds)
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var engine = new SessionEngine(store.Repository, clock);
        SessionRecord started = await engine.StartAsync(SessionType.Work);
        clock.Advance(started.PlannedDuration + TimeSpan.FromSeconds(extraSeconds));

        SessionOutcome outcome = await engine.StopAsync();

        Assert.Equal(SessionOutcomeKind.Completed, outcome.Kind);
        Assert.Equal(started.PlannedEndAt, outcome.Session!.EndedAt);
        Assert.Equal(SessionStatus.Completed, (await store.Repository.GetAsync(started.Id))!.Status);
    }

    [Fact]
    public async Task CompleteIfDue_IsStillRunningBeforeEnd_AndCompletesExactlyOnce()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var engine = new SessionEngine(store.Repository, clock);
        SessionRecord started = await engine.StartAsync(SessionType.Work);

        Assert.Equal(SessionOutcomeKind.StillRunning, (await engine.CompleteIfDueAsync()).Kind);
        clock.Set(started.PlannedEndAt.AddHours(2));
        SessionOutcome completed = await engine.CompleteIfDueAsync();
        SessionOutcome again = await engine.CompleteIfDueAsync();

        Assert.Equal(SessionOutcomeKind.Completed, completed.Kind);
        Assert.Equal(started.PlannedEndAt, completed.Session!.EndedAt);
        Assert.Equal(SessionOutcomeKind.NoActiveSession, again.Kind);
        Assert.Equal(1, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions WHERE status = 'completed';"));
    }

    [Fact]
    public async Task CompletionFailure_ReleasesGateAndCanBeRetried()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        SessionRecord started = await new SessionEngine(store.Repository, clock).StartAsync(SessionType.Work);
        clock.Set(started.PlannedEndAt);
        var repository = new EngineTestRepository(store.Repository) { FailUpdates = true };
        var engine = new SessionEngine(repository, clock);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.CompleteIfDueAsync());
        repository.FailUpdates = false;
        Assert.Equal(SessionOutcomeKind.Completed, (await engine.CompleteIfDueAsync()).Kind);
    }

    [Fact]
    public async Task ReadFailure_ReleasesGateForLaterRequest()
    {
        using var store = new SessionStore();
        var repository = new EngineTestRepository(store.Repository) { FailReads = true };
        var engine = new SessionEngine(repository, new ManualTimeProvider(TestSessions.Anchor));
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StopAsync());
        repository.FailReads = false;
        Assert.Equal(SessionOutcomeKind.NoActiveSession, (await engine.StopAsync()).Kind);
    }

    [Fact]
    public async Task Start_ConcurrentRequestsOnOneEngineProduceOneSession()
    {
        using var store = new SessionStore();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allow = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new EngineTestRepository(store.Repository) { AddEntered = entered, AllowAdd = allow };
        var engine = new SessionEngine(repository, new ManualTimeProvider(TestSessions.Anchor));
        Task<SessionRecord> first = engine.StartAsync(SessionType.Work);
        await entered.Task;
        Task<SessionRecord> second = engine.StartAsync(SessionType.Break);
        Assert.False(second.IsCompleted);
        allow.SetResult(true);
        await first;
        await Assert.ThrowsAsync<ActiveSessionAlreadyExistsException>(() => second);
        Assert.Equal(1, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public async Task CancellationWhileWaitingForLifecycleGate_DoesNotRunTheQueuedRequest()
    {
        using var store = new SessionStore();
        var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var allow = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new EngineTestRepository(store.Repository) { AddEntered = entered, AllowAdd = allow };
        var engine = new SessionEngine(repository, new ManualTimeProvider(TestSessions.Anchor));
        Task<SessionRecord> first = engine.StartAsync(SessionType.Work);
        await entered.Task;
        using var cancellation = new CancellationTokenSource();
        Task<SessionRecord> queued = engine.StartAsync(SessionType.Break, cancellation.Token);
        await cancellation.CancelAsync();
        allow.SetResult(true);
        await first;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
        Assert.Equal(1, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public async Task Cancellation_IsHonouredBeforeOperationAndDoesNotWrite()
    {
        using var store = new SessionStore();
        var engine = new SessionEngine(store.Repository, new ManualTimeProvider(TestSessions.Anchor));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.StartAsync(SessionType.Work, cancellation.Token));
        Assert.Equal(0, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public async Task Cancellation_IsHonouredForAllReadOperations()
    {
        using var store = new SessionStore();
        var engine = new SessionEngine(store.Repository, new ManualTimeProvider(TestSessions.Anchor));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.GetActiveAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.StopAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.CompleteIfDueAsync(cancellation.Token));
    }

    [Fact]
    public async Task Start_PropagatesPersistenceFailureWithoutPretendingItStarted()
    {
        using var store = new SessionStore();
        var repository = new EngineTestRepository(store.Repository) { FailAdds = true };
        var engine = new SessionEngine(repository, new ManualTimeProvider(TestSessions.Anchor));

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync(SessionType.Work));
        Assert.Equal(0, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public async Task Stop_PropagatesPersistenceFailureAndLeavesRunningRecord()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        SessionRecord started = await new SessionEngine(store.Repository, clock).StartAsync(SessionType.Work);
        var repository = new EngineTestRepository(store.Repository) { FailUpdates = true };
        clock.Advance(TimeSpan.FromMinutes(1));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SessionEngine(repository, clock).StopAsync());
        Assert.Equal(SessionStatus.Running, (await store.Repository.GetAsync(started.Id))!.Status);
    }
}
