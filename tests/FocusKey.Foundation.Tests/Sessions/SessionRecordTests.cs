using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class SessionRecordTests
{
    [Fact]
    public void PlannedEndAt_IsStartPlusPlannedDuration()
    {
        SessionRecord session = TestSessions.Running(
            startedAt: TestSessions.Anchor,
            plannedDuration: TimeSpan.FromMinutes(30));

        Assert.Equal(TestSessions.Anchor.AddMinutes(30), session.PlannedEndAt);
    }

    [Fact]
    public void ActualDuration_IsNullWhileRunning()
    {
        SessionRecord session = TestSessions.Running();

        Assert.Null(session.ActualDuration);
        Assert.Null(session.EndedAt);
        Assert.True(session.IsActive);
    }

    [Fact]
    public void ActualDuration_IsEndMinusStartOnceFinished()
    {
        SessionRecord session = TestSessions.Finished(
            SessionStatus.Stopped,
            actualDuration: TimeSpan.FromMinutes(18));

        Assert.Equal(TimeSpan.FromMinutes(18), session.ActualDuration);
        Assert.False(session.IsActive);
    }

    [Fact]
    public void EffectiveDuration_RunningOrNullEnded_ReturnsZero()
    {
        SessionRecord session = TestSessions.Running(plannedDuration: TimeSpan.FromMinutes(30));
        Assert.Equal(TimeSpan.Zero, session.EffectiveDuration);
    }

    [Fact]
    public void EffectiveDuration_StoppedSession_ReturnsElapsedClampedToPlanned()
    {
        SessionRecord normal = TestSessions.Finished(
            SessionStatus.Stopped,
            plannedDuration: TimeSpan.FromMinutes(40),
            actualDuration: TimeSpan.FromMinutes(20));
        Assert.Equal(TimeSpan.FromMinutes(20), normal.EffectiveDuration);

        SessionRecord exceeded = TestSessions.Finished(
            SessionStatus.Stopped,
            plannedDuration: TimeSpan.FromMinutes(40),
            actualDuration: TimeSpan.FromMinutes(50));
        Assert.Equal(TimeSpan.FromMinutes(40), exceeded.EffectiveDuration);
    }

    [Fact]
    public void EffectiveDuration_CrashRecovery_ReturnsZero()
    {
        SessionRecord crash = TestSessions.Finished(
            SessionStatus.Interrupted,
            plannedDuration: TimeSpan.FromMinutes(30),
            actualDuration: TimeSpan.Zero);
        Assert.Equal(TimeSpan.Zero, crash.EffectiveDuration);
    }

    [Fact]
    public void Timestamps_AreNormalizedToUtc()
    {
        var localStart = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.FromHours(3));

        SessionRecord session = new()
        {
            Id = SessionId.New(),
            Type = SessionType.Work,
            Status = SessionStatus.Completed,
            StartedAt = localStart,
            PlannedDuration = TimeSpan.FromMinutes(30),
            EndedAt = localStart.AddMinutes(30),
            CreatedAt = localStart,
        };

        Assert.Equal(TimeSpan.Zero, session.StartedAt.Offset);
        Assert.Equal(TimeSpan.Zero, session.EndedAt!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, session.CreatedAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero), session.StartedAt);
    }

    [Fact]
    public void Validate_AcceptsARunningRecord()
    {
        TestSessions.Running().Validate();
    }

    [Theory]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Stopped)]
    [InlineData(SessionStatus.Interrupted)]
    public void Validate_AcceptsEachFinishedStatus(SessionStatus status)
    {
        TestSessions.Finished(status).Validate();
    }

    [Fact]
    public void Validate_RejectsAnEmptyId()
    {
        SessionRecord session = TestSessions.Running() with { Id = default };

        Assert.Throws<ArgumentException>(session.Validate);
    }

    [Fact]
    public void Validate_RejectsUndefinedTypeOrStatus()
    {
        Assert.Throws<ArgumentException>((TestSessions.Running() with { Type = (SessionType)7 }).Validate);
        Assert.Throws<ArgumentException>((TestSessions.Running() with { Status = (SessionStatus)9 }).Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void Validate_RejectsNonPositivePlannedDuration(int seconds)
    {
        SessionRecord session = TestSessions.Running() with { PlannedDuration = TimeSpan.FromSeconds(seconds) };

        Assert.Throws<ArgumentException>(session.Validate);
    }

    [Fact]
    public void Validate_RejectsSubSecondPlannedDuration()
    {
        SessionRecord session = TestSessions.Running()
            with { PlannedDuration = TimeSpan.FromMilliseconds(1500) };

        ArgumentException error = Assert.Throws<ArgumentException>(session.Validate);

        Assert.Contains("whole number of seconds", error.Message);
    }

    [Fact]
    public void Validate_RejectsARunningRecordThatAlreadyEnded()
    {
        SessionRecord session = TestSessions.Running() with { EndedAt = TestSessions.Anchor.AddMinutes(5) };

        Assert.Throws<ArgumentException>(session.Validate);
    }

    [Theory]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Stopped)]
    [InlineData(SessionStatus.Interrupted)]
    public void Validate_RejectsAFinishedRecordWithoutAnEnd(SessionStatus status)
    {
        SessionRecord session = TestSessions.Finished(status) with { EndedAt = null };

        Assert.Throws<ArgumentException>(session.Validate);
    }

    [Fact]
    public void Validate_RejectsAnEndBeforeTheStart()
    {
        SessionRecord session = TestSessions.Finished(SessionStatus.Stopped)
            with { EndedAt = TestSessions.Anchor.AddSeconds(-1) };

        Assert.Throws<ArgumentException>(session.Validate);
    }

    [Fact]
    public void Validate_AllowsAZeroLengthStoppedSession()
    {
        // Stopping immediately is legitimate: ended exactly at started.
        SessionRecord session = TestSessions.Finished(SessionStatus.Stopped, actualDuration: TimeSpan.Zero);

        session.Validate();

        Assert.Equal(TimeSpan.Zero, session.ActualDuration);
    }
}
