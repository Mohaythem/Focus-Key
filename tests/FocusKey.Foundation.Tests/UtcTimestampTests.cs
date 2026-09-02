using FocusKey.Foundation.Data;

namespace FocusKey.Foundation.Tests;

public sealed class UtcTimestampTests
{
    [Fact]
    public void Format_ProducesTheCanonicalShape()
    {
        var value = new DateTimeOffset(2026, 9, 2, 5, 19, 43, TimeSpan.Zero).AddTicks(7_134_567);

        Assert.Equal("2026-09-02T05:19:43.7134567Z", UtcTimestamp.Format(value));
    }

    [Fact]
    public void Format_NormalizesOffsetsToUtc()
    {
        var utc = new DateTimeOffset(2026, 9, 2, 5, 0, 0, TimeSpan.Zero);
        var sameInstantInCairo = utc.ToOffset(TimeSpan.FromHours(3));

        Assert.Equal(UtcTimestamp.Format(utc), UtcTimestamp.Format(sameInstantInCairo));
        Assert.Equal("2026-09-02T05:00:00.0000000Z", UtcTimestamp.Format(sameInstantInCairo));
    }

    [Fact]
    public void RoundTrip_IsExactToTheTick()
    {
        DateTimeOffset original = DateTimeOffset.UtcNow;

        DateTimeOffset parsed = UtcTimestamp.Parse(UtcTimestamp.Format(original));

        Assert.Equal(original.UtcTicks, parsed.UtcTicks);
        Assert.Equal(TimeSpan.Zero, parsed.Offset);
    }

    [Fact]
    public void Format_IsLexicographicallySortable()
    {
        var earlier = new DateTimeOffset(2026, 9, 2, 23, 59, 59, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero);

        Assert.True(string.CompareOrdinal(UtcTimestamp.Format(earlier), UtcTimestamp.Format(later)) < 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-09-02")]
    [InlineData("2026-09-02T05:19:43Z")]
    [InlineData("2026-09-02T05:19:43.7134567+03:00")]
    [InlineData("not a timestamp")]
    public void TryParse_RejectsAnythingOutsideTheCanonicalForm(string? text)
    {
        Assert.False(UtcTimestamp.TryParse(text, out _));
    }

    [Fact]
    public void Parse_ThrowsOnInvalidText()
    {
        Assert.Throws<FormatException>(() => UtcTimestamp.Parse("2026-09-02T05:19:43Z"));
    }
}
