using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Today;

namespace FocusKey.Foundation.Tests.Today;

public sealed class TodayClockTimeFormattingTests
{
    private static readonly TimeZoneInfo EstZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

    [Theory]
    [InlineData("2026-09-17T00:00:00Z", "00:00")]
    [InlineData("2026-09-17T09:05:00Z", "09:05")]
    [InlineData("2026-09-17T12:00:00Z", "12:00")]
    [InlineData("2026-09-17T14:30:00Z", "14:30")]
    [InlineData("2026-09-17T23:59:00Z", "23:59")]
    public void FormatClockTime_TwentyFourHour_FormatsUtcProperly(string timestamp, string expected)
    {
        var time = DateTimeOffset.Parse(timestamp);
        string result = TodayFormatting.FormatClockTime(time, TimeZoneInfo.Utc, TimeFormat.TwentyFourHour);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("2026-09-17T00:00:00Z", "12:00 AM")]
    [InlineData("2026-09-17T00:30:00Z", "12:30 AM")]
    [InlineData("2026-09-17T09:05:00Z", "9:05 AM")]
    [InlineData("2026-09-17T12:00:00Z", "12:00 PM")]
    [InlineData("2026-09-17T14:30:00Z", "2:30 PM")]
    [InlineData("2026-09-17T23:59:00Z", "11:59 PM")]
    public void FormatClockTime_TwelveHour_FormatsUtcProperly(string timestamp, string expected)
    {
        var time = DateTimeOffset.Parse(timestamp);
        string result = TodayFormatting.FormatClockTime(time, TimeZoneInfo.Utc, TimeFormat.TwelveHour);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FormatClockTime_ConvertsToSpecifiedTimeZone()
    {
        // 14:00 UTC with Eastern Daylight Time (-4 hours) = 10:00 AM
        var time = DateTimeOffset.Parse("2026-07-15T14:00:00Z");
        string result24 = TodayFormatting.FormatClockTime(time, EstZone, TimeFormat.TwentyFourHour);
        string result12 = TodayFormatting.FormatClockTime(time, EstZone, TimeFormat.TwelveHour);

        Assert.Equal("10:00", result24);
        Assert.Equal("10:00 AM", result12);
    }

    [Fact]
    public void FormatClockTime_ThrowsOnNullZone()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TodayFormatting.FormatClockTime(DateTimeOffset.UtcNow, null!, TimeFormat.TwentyFourHour));
    }
}
