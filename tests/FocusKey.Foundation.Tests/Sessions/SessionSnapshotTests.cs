using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class SessionSnapshotTests
{
    [Fact]
    public void For_ComputesElapsedAndRemainingAtAnInstant()
    {
        SessionRecord record = TestSessions.Running(plannedDuration: TimeSpan.FromMinutes(30));
        SessionSnapshot snapshot = SessionSnapshot.For(record, record.StartedAt.AddMinutes(12));

        Assert.Equal(TimeSpan.FromMinutes(12), snapshot.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(18), snapshot.Remaining);
        Assert.False(snapshot.HasReachedPlannedEnd);
    }

    [Fact]
    public void For_ClampsBeforeStartAndAfterEnd()
    {
        SessionRecord record = TestSessions.Running(plannedDuration: TimeSpan.FromMinutes(10));

        SessionSnapshot before = SessionSnapshot.For(record, record.StartedAt.AddMinutes(-2));
        Assert.Equal(TimeSpan.Zero, before.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(12), before.Remaining);

        SessionSnapshot after = SessionSnapshot.For(record, record.PlannedEndAt.AddMinutes(3));
        Assert.Equal(record.PlannedDuration, after.Elapsed);
        Assert.Equal(TimeSpan.Zero, after.Remaining);
        Assert.True(after.HasReachedPlannedEnd);
    }

    [Fact]
    public void For_DoesNotWriteOrChangeTheRecord()
    {
        SessionRecord record = TestSessions.Running();
        _ = SessionSnapshot.For(record, record.PlannedEndAt.AddHours(1));
        Assert.Equal(SessionStatus.Running, record.Status);
        Assert.Null(record.EndedAt);
    }

    [Theory]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Stopped)]
    public void For_RequiresARunningRecord(SessionStatus status)
    {
        SessionRecord record = TestSessions.Finished(status);
        Assert.Throws<ArgumentException>(() => SessionSnapshot.For(record, TestSessions.Anchor));
    }
}
