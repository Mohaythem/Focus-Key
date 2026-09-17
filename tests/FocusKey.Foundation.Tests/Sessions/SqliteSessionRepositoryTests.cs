using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class SqliteSessionRepositoryTests
{
    [Theory]
    [InlineData(SessionType.Work)]
    [InlineData(SessionType.Break)]
    public async Task Add_ThenGet_RoundTripsEveryFieldOfARunningSession(SessionType type)
    {
        using var store = new SessionStore();
        SessionRecord session = TestSessions.Running(
            type: type,
            plannedDuration: type == SessionType.Work ? TestSessions.WorkLength : TestSessions.BreakLength);

        await store.Repository.AddAsync(session);
        SessionRecord? stored = await store.Repository.GetAsync(session.Id);

        Assert.NotNull(stored);
        Assert.Equal(session.Id, stored.Id);
        Assert.Equal(type, stored.Type);
        Assert.Equal(SessionStatus.Running, stored.Status);
        Assert.Equal(session.StartedAt, stored.StartedAt);
        Assert.Equal(session.PlannedDuration, stored.PlannedDuration);
        Assert.Null(stored.EndedAt);
        Assert.Null(stored.ActualDuration);
        Assert.Equal(session.CreatedAt, stored.CreatedAt);
        Assert.Equal(session.PlannedEndAt, stored.PlannedEndAt);
        Assert.Equal(session, stored);
    }

    [Theory]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Stopped)]
    [InlineData(SessionStatus.Interrupted)]
    public async Task Add_ThenGet_RoundTripsEveryFinishedStatus(SessionStatus status)
    {
        using var store = new SessionStore();
        SessionRecord session = TestSessions.Finished(status, actualDuration: TimeSpan.FromMinutes(7));

        await store.Repository.AddAsync(session);
        SessionRecord? stored = await store.Repository.GetAsync(session.Id);

        Assert.NotNull(stored);
        Assert.Equal(status, stored.Status);
        Assert.Equal(session.EndedAt, stored.EndedAt);
        Assert.Equal(TimeSpan.FromMinutes(7), stored.ActualDuration);
    }

    [Fact]
    public async Task Add_PreservesTimestampsToTheTick()
    {
        using var store = new SessionStore();
        DateTimeOffset start = TestSessions.Anchor.AddTicks(1_234_567);
        SessionRecord session = TestSessions.Finished(
            SessionStatus.Completed,
            startedAt: start,
            actualDuration: TimeSpan.FromMinutes(30));

        await store.Repository.AddAsync(session);
        SessionRecord? stored = await store.Repository.GetAsync(session.Id);

        Assert.Equal(start.UtcTicks, stored!.StartedAt.UtcTicks);
        Assert.Equal(session.EndedAt!.Value.UtcTicks, stored.EndedAt!.Value.UtcTicks);
    }

    [Fact]
    public async Task Add_NormalizesNonUtcInputToUtc()
    {
        using var store = new SessionStore();
        var cairoStart = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.FromHours(3));
        SessionRecord session = TestSessions.Running(startedAt: cairoStart);

        await store.Repository.AddAsync(session);
        SessionRecord? stored = await store.Repository.GetAsync(session.Id);

        Assert.Equal(TimeSpan.Zero, stored!.StartedAt.Offset);
        Assert.Equal(cairoStart.UtcTicks, stored.StartedAt.UtcTicks);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(600)]
    [InlineData(1800)]
    [InlineData(86_400)]
    public async Task Add_RoundTripsPlannedDurationExactly(int seconds)
    {
        using var store = new SessionStore();
        SessionRecord session = TestSessions.Running(plannedDuration: TimeSpan.FromSeconds(seconds));

        await store.Repository.AddAsync(session);

        SessionRecord? stored = await store.Repository.GetAsync(session.Id);
        Assert.Equal(TimeSpan.FromSeconds(seconds), stored!.PlannedDuration);
    }

    [Fact]
    public async Task Get_ReturnsNullForAnUnknownId()
    {
        using var store = new SessionStore();

        Assert.Null(await store.Repository.GetAsync(SessionId.New()));
    }

    [Fact]
    public async Task Get_RejectsAnEmptyId()
    {
        using var store = new SessionStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.Repository.GetAsync(default));
    }

    [Fact]
    public async Task Add_RejectsADuplicateId()
    {
        using var store = new SessionStore();
        SessionRecord session = TestSessions.Finished(SessionStatus.Completed);
        await store.Repository.AddAsync(session);

        SessionRecord sameId = TestSessions.Finished(SessionStatus.Stopped, id: session.Id);

        DuplicateSessionIdException error =
            await Assert.ThrowsAsync<DuplicateSessionIdException>(() => store.Repository.AddAsync(sameId));

        Assert.Equal(session.Id, error.Id);

        // The original row is untouched.
        SessionRecord? stored = await store.Repository.GetAsync(session.Id);
        Assert.Equal(SessionStatus.Completed, stored!.Status);
    }

    [Fact]
    public async Task Add_RejectsAnInvalidRecordBeforeTouchingStorage()
    {
        using var store = new SessionStore();
        SessionRecord invalid = TestSessions.Running() with { PlannedDuration = TimeSpan.Zero };

        await Assert.ThrowsAsync<ArgumentException>(() => store.Repository.AddAsync(invalid));

        Assert.Equal(0, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public async Task Add_RejectsASecondRunningSession()
    {
        using var store = new SessionStore();
        SessionRecord first = TestSessions.Running();
        await store.Repository.AddAsync(first);

        SessionRecord second = TestSessions.Running(startedAt: TestSessions.Anchor.AddMinutes(5));

        ActiveSessionAlreadyExistsException error =
            await Assert.ThrowsAsync<ActiveSessionAlreadyExistsException>(
                () => store.Repository.AddAsync(second));

        Assert.Equal(first.Id, error.ExistingId);
        Assert.Equal(1, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public async Task Add_AllowsAnotherRunningSessionOnceTheFirstOneEnded()
    {
        using var store = new SessionStore();
        SessionRecord first = TestSessions.Running();
        await store.Repository.AddAsync(first);

        await store.Repository.UpdateAsync(first with
        {
            Status = SessionStatus.Completed,
            EndedAt = first.PlannedEndAt,
        });

        SessionRecord second = TestSessions.Running(startedAt: TestSessions.Anchor.AddMinutes(45));
        await store.Repository.AddAsync(second);

        SessionRecord? running = await store.Repository.GetRunningAsync();
        Assert.Equal(second.Id, running!.Id);
    }

    [Fact]
    public async Task Add_AllowsManyFinishedSessions()
    {
        using var store = new SessionStore();

        for (int index = 0; index < 5; index++)
        {
            await store.Repository.AddAsync(TestSessions.Finished(
                SessionStatus.Completed,
                startedAt: TestSessions.Anchor.AddMinutes(index * 45)));
        }

        Assert.Equal(5, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public async Task Update_PersistsTheOutcomeOfASession()
    {
        using var store = new SessionStore();
        SessionRecord session = TestSessions.Running();
        await store.Repository.AddAsync(session);

        SessionRecord finished = session with
        {
            Status = SessionStatus.Stopped,
            EndedAt = session.StartedAt.AddMinutes(18),
        };

        await store.Repository.UpdateAsync(finished);

        SessionRecord? stored = await store.Repository.GetAsync(session.Id);
        Assert.Equal(SessionStatus.Stopped, stored!.Status);
        Assert.Equal(TimeSpan.FromMinutes(18), stored.ActualDuration);
        Assert.Equal(1, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public async Task Update_ThrowsForAnUnknownId()
    {
        using var store = new SessionStore();
        SessionRecord unknown = TestSessions.Finished(SessionStatus.Completed);

        SessionNotFoundException error =
            await Assert.ThrowsAsync<SessionNotFoundException>(() => store.Repository.UpdateAsync(unknown));

        Assert.Equal(unknown.Id, error.Id);
    }

    [Fact]
    public async Task Update_RejectsAnInvalidRecord()
    {
        using var store = new SessionStore();
        SessionRecord session = TestSessions.Running();
        await store.Repository.AddAsync(session);

        SessionRecord invalid = session with { Status = SessionStatus.Completed, EndedAt = null };

        await Assert.ThrowsAsync<ArgumentException>(() => store.Repository.UpdateAsync(invalid));
        Assert.Equal(SessionStatus.Running, (await store.Repository.GetAsync(session.Id))!.Status);
    }

    [Fact]
    public async Task Update_RejectsMakingASecondSessionRunning()
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        SessionRecord finished = TestSessions.Finished(
            SessionStatus.Stopped,
            startedAt: TestSessions.Anchor.AddHours(-1));

        await store.Repository.AddAsync(running);
        await store.Repository.AddAsync(finished);

        SessionRecord reopened = finished with { Status = SessionStatus.Running, EndedAt = null };

        ActiveSessionAlreadyExistsException error =
            await Assert.ThrowsAsync<ActiveSessionAlreadyExistsException>(
                () => store.Repository.UpdateAsync(reopened));

        Assert.Equal(running.Id, error.ExistingId);
    }

    [Fact]
    public async Task Update_AllowsRewritingTheRunningSessionItself()
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        await store.Repository.AddAsync(running);

        SessionRecord extended = running with { PlannedDuration = TimeSpan.FromMinutes(45) };
        await store.Repository.UpdateAsync(extended);

        SessionRecord? stored = await store.Repository.GetRunningAsync();
        Assert.Equal(TimeSpan.FromMinutes(45), stored!.PlannedDuration);
    }

    [Fact]
    public async Task Records_SurviveRepositoryAndConnectionRecreation()
    {
        using var store = new SessionStore();
        SessionRecord session = TestSessions.Finished(SessionStatus.Completed);
        await store.Repository.AddAsync(session);

        SessionRecord? stored = await store.ReopenRepository().GetAsync(session.Id);

        Assert.Equal(session, stored);
    }

    [Fact]
    public async Task GetRunning_ReturnsNullWhenNothingIsRunning()
    {
        using var store = new SessionStore();
        await store.Repository.AddAsync(TestSessions.Finished(SessionStatus.Completed));

        Assert.Null(await store.Repository.GetRunningAsync());
    }

    [Fact]
    public async Task GetRunning_ReturnsTheRunningSessionAmongFinishedOnes()
    {
        using var store = new SessionStore();
        await store.Repository.AddAsync(TestSessions.Finished(
            SessionStatus.Completed, startedAt: TestSessions.Anchor.AddHours(-2)));
        await store.Repository.AddAsync(TestSessions.Finished(
            SessionStatus.Interrupted, startedAt: TestSessions.Anchor.AddHours(-1)));

        SessionRecord running = TestSessions.Running();
        await store.Repository.AddAsync(running);

        SessionRecord? stored = await store.Repository.GetRunningAsync();

        Assert.Equal(running.Id, stored!.Id);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task GetStartedBetween_IncludesTheStartBoundaryAndExcludesTheEnd()
    {
        using var store = new SessionStore();
        DateTimeOffset from = TestSessions.Anchor;
        DateTimeOffset to = from.AddHours(1);

        SessionRecord justBefore = await Add(store, from.AddTicks(-1));
        SessionRecord exactlyAtFrom = await Add(store, from);
        SessionRecord inside = await Add(store, from.AddMinutes(30));
        SessionRecord justBeforeTo = await Add(store, to.AddTicks(-1));
        SessionRecord exactlyAtTo = await Add(store, to);

        IReadOnlyList<SessionRecord> range = await store.Repository.GetStartedBetweenAsync(from, to);

        Assert.Equal(
            [exactlyAtFrom.Id, inside.Id, justBeforeTo.Id],
            range.Select(session => session.Id));
        Assert.DoesNotContain(justBefore.Id, range.Select(session => session.Id));
        Assert.DoesNotContain(exactlyAtTo.Id, range.Select(session => session.Id));
    }

    [Fact]
    public async Task GetStartedBetween_OrdersByStartThenId()
    {
        using var store = new SessionStore();
        DateTimeOffset shared = TestSessions.Anchor.AddMinutes(10);

        SessionRecord later = await Add(store, TestSessions.Anchor.AddMinutes(20));
        SessionRecord tieA = await Add(store, shared);
        SessionRecord tieB = await Add(store, shared);
        SessionRecord earliest = await Add(store, TestSessions.Anchor);

        IReadOnlyList<SessionRecord> range = await store.Repository.GetStartedBetweenAsync(
            TestSessions.Anchor,
            TestSessions.Anchor.AddHours(1));

        SessionId[] expectedTies = tieA.Id.ToText().CompareTo(tieB.Id.ToText()) < 0
            ? [tieA.Id, tieB.Id]
            : [tieB.Id, tieA.Id];

        Assert.Equal(
            [earliest.Id, expectedTies[0], expectedTies[1], later.Id],
            range.Select(session => session.Id));
    }

    [Fact]
    public async Task GetStartedBetween_ReturnsEmptyForAnEmptyRange()
    {
        using var store = new SessionStore();
        await Add(store, TestSessions.Anchor);

        IReadOnlyList<SessionRecord> range = await store.Repository.GetStartedBetweenAsync(
            TestSessions.Anchor,
            TestSessions.Anchor);

        Assert.Empty(range);
    }

    [Fact]
    public async Task GetStartedBetween_ComparesInstantsNotLocalClockText()
    {
        using var store = new SessionStore();
        // 00:30 UTC on 3 September, expressed as 03:30 +03:00.
        var cairoStart = new DateTimeOffset(2026, 9, 3, 3, 30, 0, TimeSpan.FromHours(3));
        SessionRecord session = TestSessions.Running(startedAt: cairoStart);
        await store.Repository.AddAsync(session);

        IReadOnlyList<SessionRecord> utcDay = await store.Repository.GetStartedBetweenAsync(
            new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero));

        Assert.Single(utcDay);

        IReadOnlyList<SessionRecord> cairoDay = await store.Repository.GetStartedBetweenAsync(
            new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.FromHours(3)),
            new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.FromHours(3)));

        Assert.Single(cairoDay);
    }

    [Fact]
    public async Task GetStartedBetween_RejectsAnInvertedRange()
    {
        using var store = new SessionStore();

        await Assert.ThrowsAsync<ArgumentException>(() => store.Repository.GetStartedBetweenAsync(
            TestSessions.Anchor,
            TestSessions.Anchor.AddTicks(-1)));
    }

    [Fact]
    public async Task Reads_FailPredictablyOnStoredTextThatIsNotACanonicalTimestamp()
    {
        using var store = new SessionStore();
        SessionId id = SessionId.New();

        // Length passes the schema check; the content is not a timestamp.
        store.ExecuteRaw(
            """
            INSERT INTO sessions (
                id, type, status, started_at_utc, planned_duration_seconds, ended_at_utc, created_at_utc,
                resumed_at_utc, accumulated_active_seconds, paused_at_utc)
            VALUES ($id, 'work', 'running', 'XXXXXXXXXXXXXXXXXXXXXXXXXXXX', 1800, NULL, $createdAt,
                'XXXXXXXXXXXXXXXXXXXXXXXXXXXX', 0, NULL);
            """,
            ("$id", id.ToText()),
            ("$createdAt", Data.UtcTimestamp.Format(TestSessions.Anchor)));

        await Assert.ThrowsAsync<FormatException>(() => store.Repository.GetAsync(id));
    }

    [Fact]
    public async Task Add_HonoursCancellation()
    {
        using var store = new SessionStore();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.Repository.AddAsync(TestSessions.Running(), cancelled.Token));
    }

    [Fact]
    public async Task Add_RejectsANullRecord()
    {
        using var store = new SessionStore();

        await Assert.ThrowsAsync<ArgumentNullException>(() => store.Repository.AddAsync(null!));
    }

    [Fact]
    public void Constructor_RejectsAMissingConnectionFactory()
    {
        Assert.Throws<ArgumentNullException>(() => new SqliteSessionRepository(null!));
    }

    private static async Task<SessionRecord> Add(SessionStore store, DateTimeOffset startedAt)
    {
        SessionRecord session = TestSessions.Finished(
            SessionStatus.Completed,
            startedAt: startedAt,
            actualDuration: TimeSpan.FromMinutes(1));

        await store.Repository.AddAsync(session);
        return session;
    }
}
