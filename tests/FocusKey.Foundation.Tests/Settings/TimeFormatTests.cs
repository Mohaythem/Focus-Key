using FocusKey.Foundation.Settings;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class TimeFormatTests
{
    [Fact]
    public void EnumValues_MatchExpectedUnderlyingInts()
    {
        Assert.Equal(0, (int)TimeFormat.TwentyFourHour);
        Assert.Equal(1, (int)TimeFormat.TwelveHour);
    }

    [Theory]
    [InlineData(TimeFormat.TwentyFourHour, "24h")]
    [InlineData(TimeFormat.TwelveHour, "12h")]
    public void TimeFormatText_Format_FormatsCorrectly(TimeFormat format, string expected)
    {
        Assert.Equal(expected, TimeFormatText.Format(format));
    }

    [Fact]
    public void TimeFormatText_Format_ThrowsForUndefinedEnum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TimeFormatText.Format((TimeFormat)99));
    }

    [Theory]
    [InlineData("24h", TimeFormat.TwentyFourHour)]
    [InlineData("24H", TimeFormat.TwentyFourHour)]
    [InlineData("  24h  ", TimeFormat.TwentyFourHour)]
    [InlineData("12h", TimeFormat.TwelveHour)]
    [InlineData("12H", TimeFormat.TwelveHour)]
    [InlineData("  12h  ", TimeFormat.TwelveHour)]
    public void TimeFormatText_Parse_ParsesValidStrings(string input, TimeFormat expected)
    {
        Assert.Equal(expected, TimeFormatText.Parse(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("8h")]
    [InlineData("24")]
    [InlineData("12")]
    [InlineData("ampm")]
    [InlineData("standard")]
    public void TimeFormatText_Parse_ThrowsForInvalidStrings(string? input)
    {
        Assert.Throws<FormatException>(() => TimeFormatText.Parse(input));
    }

    [Theory]
    [InlineData("24h", true, TimeFormat.TwentyFourHour)]
    [InlineData("12h", true, TimeFormat.TwelveHour)]
    [InlineData("invalid", false, default(TimeFormat))]
    [InlineData(null, false, default(TimeFormat))]
    public void TimeFormatText_TryParse_BehavesAsExpected(string? input, bool expectedSuccess, TimeFormat expectedFormat)
    {
        bool success = TimeFormatText.TryParse(input, out TimeFormat result);
        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(expectedFormat, result);
        }
    }
}
