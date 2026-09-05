using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Data;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class SessionAtomicUpdateTests
{
    [Fact]
    public async Task ActualSqliteWriteFailure_RollsBackAndCanBeRetried()
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        store.ExecuteRaw("""
            CREATE TRIGGER reject_completion BEFORE UPDATE ON sessions
            WHEN NEW.status = 'completed'
            BEGIN SELECT RAISE(ABORT, 'injected write failure'); END;
            """);
        var engine = new SessionEngine(store.Repository, new ManualTimeProvider(running.PlannedEndAt));
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() => engine.CompleteIfDueAsync());
        Assert.Equal(running, await store.Repository.GetRunningAsync());
        store.ExecuteRaw("DROP TRIGGER reject_completion;");
        Assert.Equal(SessionOutcomeKind.Completed, (await engine.CompleteIfDueAsync()).Kind);
    }

    [Fact]
    public async Task MatchingRecord_UpdatesOnce_AndRejectsTheStaleRetry()
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        SessionRecord completed = running with { Status = SessionStatus.Completed, EndedAt = running.PlannedEndAt };

        Assert.True(await store.Repository.TryUpdateAsync(running, completed));
        Assert.False(await store.ReopenRepository().TryUpdateAsync(running,
            running with { Status = SessionStatus.Stopped, EndedAt = running.StartedAt }));
        Assert.Equal(completed, await store.Repository.GetAsync(running.Id));
    }

    [Theory]
    [InlineData("type")]
    [InlineData("start")]
    [InlineData("duration")]
    [InlineData("created")]
    [InlineData("status")]
    [InlineData("end")]
    public async Task Comparison_IncludesEveryStoredFact(string field)
    {
        using var store = new SessionStore();
        SessionRecord expected = TestSessions.Finished(SessionStatus.Stopped);
        await store.Repository.AddAsync(expected);
        SessionRecord changed = field switch
        {
            "type" => expected with { Type = SessionType.Break },
            "start" => expected with { StartedAt = expected.StartedAt.AddTicks(-1) },
            "duration" => expected with { PlannedDuration = expected.PlannedDuration.Add(TimeSpan.FromSeconds(1)) },
            "created" => expected with { CreatedAt = expected.CreatedAt.AddTicks(1) },
            "status" => expected with { Status = SessionStatus.Completed },
            _ => expected with { EndedAt = expected.EndedAt!.Value.AddTicks(1) },
        };
        await store.Repository.UpdateAsync(changed);

        Assert.False(await store.Repository.TryUpdateAsync(expected, expected));
        Assert.Equal(changed, await store.Repository.GetAsync(expected.Id));
        Assert.True(await store.Repository.TryUpdateAsync(changed, expected));
    }

    [Fact]
    public async Task MissingRecord_ReturnsFalse_AndCannotChangeIdentity()
    {
        using var store = new SessionStore();
        SessionRecord record = TestSessions.Running();
        Assert.False(await store.Repository.TryUpdateAsync(record, record));
        await Assert.ThrowsAsync<ArgumentException>(() => store.Repository.TryUpdateAsync(record, record with { Id = SessionId.New() }));
        Assert.Equal(0, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public async Task CancelledUpdate_LeavesTheRecordUntouched()
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        await store.Repository.AddAsync(running);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.Repository.TryUpdateAsync(running,
            running with { Status = SessionStatus.Completed, EndedAt = running.PlannedEndAt }, cancelled.Token));
        Assert.Equal(running, await store.Repository.GetRunningAsync());
    }

    [Fact]
    public async Task OverflowingPlannedEnd_IsRejectedOnWriteAndRead()
    {
        using var store = new SessionStore();
        SessionRecord invalid = TestSessions.Running(startedAt: DateTimeOffset.MaxValue.AddSeconds(-1));
        await Assert.ThrowsAsync<ArgumentException>(() => store.Repository.AddAsync(invalid));
        Assert.Equal(0, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
        SessionRecord valid = TestSessions.Running();
        await store.Repository.AddAsync(valid);
        store.ExecuteRaw("UPDATE sessions SET started_at_utc = $start WHERE id = $id;",
            ("$start", UtcTimestamp.Format(invalid.StartedAt)), ("$id", valid.Id.ToText()));
        await Assert.ThrowsAsync<ArgumentException>(() => store.Repository.GetAsync(valid.Id));
    }

    [Fact]
    public async Task DurationBeyondTimeSpanRange_FailsLoudlyOnRead()
    {
        using var store = new SessionStore();
        SessionRecord valid = TestSessions.Running();
        await store.Repository.AddAsync(valid);
        store.ExecuteRaw("UPDATE sessions SET planned_duration_seconds = $seconds WHERE id = $id;",
            ("$seconds", long.MaxValue), ("$id", valid.Id.ToText()));
        await Assert.ThrowsAsync<OverflowException>(() => store.Repository.GetAsync(valid.Id));
    }
}
