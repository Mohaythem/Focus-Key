using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class SessionDurationsTests
{
    [Fact]
    public void Default_UsesProductDurations()
    {
        Assert.Equal(TimeSpan.FromMinutes(30), SessionDurations.Default.Work);
        Assert.Equal(TimeSpan.FromMinutes(10), SessionDurations.Default.Break);
    }

    [Fact]
    public void CustomDurations_AreReturnedByType()
    {
        var durations = new SessionDurations(TimeSpan.FromSeconds(7), TimeSpan.FromMinutes(2));

        Assert.Equal(TimeSpan.FromSeconds(7), durations.For(SessionType.Work));
        Assert.Equal(TimeSpan.FromMinutes(2), durations.For(SessionType.Break));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveDurations_AreRejected(int seconds) =>
        Assert.Throws<ArgumentException>(() => new SessionDurations(
            TimeSpan.FromSeconds(seconds), TimeSpan.FromMinutes(1)));

    [Fact]
    public void SubSecondDurations_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => new SessionDurations(
            TimeSpan.FromMilliseconds(1500), TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void UndefinedType_HasNoDuration() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionDurations.Default.For((SessionType)99));

    [Fact]
    public void MaximumDuration_IsAcceptedWhenRepresentable()
    {
        long wholeSecondsTicks = DateTimeOffset.MaxValue.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond;
        var durations = new SessionDurations(
            TimeSpan.FromTicks(wholeSecondsTicks),
            TimeSpan.FromSeconds(1));

        Assert.Equal(TimeSpan.FromTicks(wholeSecondsTicks), durations.Work);
    }

    [Fact]
    public void DurationBeyondTimestampRange_IsRejected()
    {
        long wholeSecondsTicks = DateTimeOffset.MaxValue.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond;
        Assert.Throws<ArgumentException>(() => new SessionDurations(
            TimeSpan.FromTicks(wholeSecondsTicks + TimeSpan.TicksPerSecond), TimeSpan.FromSeconds(1)));
        Assert.Throws<ArgumentException>(() => new SessionDurations(
            TimeSpan.FromSeconds(TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond), TimeSpan.FromSeconds(1)));
    }
}
