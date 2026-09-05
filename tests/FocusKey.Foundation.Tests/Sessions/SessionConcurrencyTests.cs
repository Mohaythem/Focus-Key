using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class SessionConcurrencyTests
{
    [Fact]
    public async Task SeparateEngines_StartFromTheSameEmptyRead_OnlyOneInsertWins()
    {
        using var store = new SessionStore();
        var barrier = new ReadBarrier();
        var first = Engine(store, barrier, TestSessions.Anchor);
        var second = Engine(store, barrier, TestSessions.Anchor);
        Task<SessionRecord> a = first.StartAsync(SessionType.Work);
        Task<SessionRecord> b = second.StartAsync(SessionType.Break);

        Exception?[] errors = await Task.WhenAll(
            Record.ExceptionAsync(async () => await a), Record.ExceptionAsync(async () => await b));

        Assert.Single(errors, e => e is null);
        Assert.IsType<ActiveSessionAlreadyExistsException>(Assert.Single(errors, e => e is not null));
        Assert.Equal(1, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeparateEngines_ReadTheSameRunningRow_OnlyOneTerminalWriteWins(bool stop)
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        var barrier = new ReadBarrier();
        var first = Engine(store, barrier, stop ? running.StartedAt.AddMinutes(5) : running.PlannedEndAt);
        var second = Engine(store, barrier, running.PlannedEndAt.AddHours(1));

        Task<SessionOutcome> a = stop ? first.StopAsync() : first.CompleteIfDueAsync();
        Task<SessionOutcome> b = second.CompleteIfDueAsync();
        SessionOutcome[] outcomes = await Task.WhenAll(a, b);

        SessionOutcome winner = Assert.Single(outcomes, result => result.ChangedStoredState);
        SessionOutcome loser = Assert.Single(outcomes, result => !result.ChangedStoredState);
        Assert.Equal(SessionOutcomeKind.Conflict, loser.Kind);
        Assert.Equal(running, loser.Session);
        Assert.Equal(winner.Session, await store.Repository.GetAsync(running.Id));
        Assert.Equal(winner.Kind == SessionOutcomeKind.Completed
            ? running.PlannedEndAt : running.StartedAt.AddMinutes(5), winner.Session!.EndedAt);
        Assert.Equal(1, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
        Assert.Null(await store.Repository.GetRunningAsync());
    }

    [Fact]
    public async Task LosingStop_DoesNotRetryAgainstANewSession()
    {
        using var store = new SessionStore();
        SessionRecord original = TestSessions.Running();
        await store.Repository.AddAsync(original);
        var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = new ReadHookRepository(store.ReopenRepository(), async () =>
        {
            read.SetResult();
            await release.Task;
        });
        Task<SessionOutcome> pending = new SessionEngine(paused,
            new ManualTimeProvider(original.StartedAt.AddMinutes(1))).StopAsync();
        await read.Task;
        var other = new SessionEngine(store.ReopenRepository(), new ManualTimeProvider(original.PlannedEndAt));
        await other.CompleteIfDueAsync();
        SessionRecord next = await other.StartAsync(SessionType.Break);
        release.SetResult();

        Assert.Equal(SessionOutcomeKind.Conflict, (await pending).Kind);
        Assert.Equal(next, await store.Repository.GetRunningAsync());
        Assert.Equal(SessionStatus.Completed, (await store.Repository.GetAsync(original.Id))!.Status);
    }

    private static SessionEngine Engine(SessionStore store, ReadBarrier barrier, DateTimeOffset now) =>
        new(new ReadHookRepository(store.ReopenRepository(), barrier.ArriveAsync), new ManualTimeProvider(now));

    private sealed class ReadBarrier
    {
        private readonly TaskCompletionSource _bothRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _readers;

        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref _readers) == 2) _bothRead.SetResult();
            return _bothRead.Task;
        }
    }

    private sealed class ReadHookRepository(ISessionRepository inner, Func<Task> afterRead) : ISessionRepository
    {
        public async Task<SessionRecord?> GetRunningAsync(CancellationToken cancellationToken = default)
        {
            SessionRecord? observed = await inner.GetRunningAsync(cancellationToken);
            await afterRead();
            return observed;
        }

        public Task AddAsync(SessionRecord session, CancellationToken cancellationToken = default) => inner.AddAsync(session, cancellationToken);
        public Task UpdateAsync(SessionRecord session, CancellationToken cancellationToken = default) => inner.UpdateAsync(session, cancellationToken);
        public Task<bool> TryUpdateAsync(SessionRecord expected, SessionRecord replacement, CancellationToken cancellationToken = default) => inner.TryUpdateAsync(expected, replacement, cancellationToken);
        public Task<SessionRecord?> GetAsync(SessionId id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);
        public Task<IReadOnlyList<SessionRecord>> GetStartedBetweenAsync(DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken = default) => inner.GetStartedBetweenAsync(fromInclusive, toExclusive, cancellationToken);
    }
}
