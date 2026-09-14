using System.Globalization;
using System.Text.RegularExpressions;
using FocusKey.Foundation.Reports;

namespace FocusKey.Foundation.Tests.Reports;

public sealed class ReportsFormattingTests
{
    private static readonly Regex ArabicIndicDigits = new(@"[\u0660-\u0669\u06F0-\u06F9]", RegexOptions.Compiled);
    private static readonly Regex WesternDigitsOnly = new(@"^[0-9]+$", RegexOptions.Compiled);

    [Theory]
    [InlineData("ar-EG")] // Arabic (Egypt)
    [InlineData("ar-SA")] // Arabic (Saudi Arabia)
    [InlineData("fa-IR")] // Persian (Iran)
    [InlineData("en-US")] // English (US)
    [InlineData("")]      // Invariant
    public void ReportsFormatting_UnderAnyCulture_AlwaysProducesWesternDigitsAndEnglishText(string cultureName)
    {
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        var originalUiCulture = Thread.CurrentThread.CurrentUICulture;

        try
        {
            var targetCulture = string.IsNullOrEmpty(cultureName) ? CultureInfo.InvariantCulture : new CultureInfo(cultureName);
            Thread.CurrentThread.CurrentCulture = targetCulture;
            Thread.CurrentThread.CurrentUICulture = targetCulture;

            // 1. Y-axis hour formatting
            for (int h = 0; h <= 24; h += 2)
            {
                string axisText = ReportsFormatting.FormatAxisHour(h);
                AssertNoArabicIndic(axisText);
                Assert.EndsWith("h", axisText);
                Assert.Matches(WesternDigitsOnly, axisText[..^1]);
            }

            // 2. Bar duration labels
            string bar11h24m = ReportsFormatting.FormatBarDuration(new TimeSpan(11, 24, 0));
            Assert.Equal("11h 24m", bar11h24m);
            AssertNoArabicIndic(bar11h24m);

            string bar5h = ReportsFormatting.FormatBarDuration(TimeSpan.FromHours(5));
            Assert.Equal("5h", bar5h);
            AssertNoArabicIndic(bar5h);

            string bar45m = ReportsFormatting.FormatBarDuration(TimeSpan.FromMinutes(45));
            Assert.Equal("45m", bar45m);
            AssertNoArabicIndic(bar45m);

            Assert.Equal(string.Empty, ReportsFormatting.FormatBarDuration(TimeSpan.Zero));

            // 3. Metric duration formatting
            string dur5h = ReportsFormatting.FormatDuration(TimeSpan.FromHours(5));
            Assert.Equal("5h 00m", dur5h);
            AssertNoArabicIndic(dur5h);

            string dur45m = ReportsFormatting.FormatDuration(TimeSpan.FromMinutes(45));
            Assert.Equal("45m", dur45m);
            AssertNoArabicIndic(dur45m);

            string dur90s = ReportsFormatting.FormatDuration(TimeSpan.FromSeconds(90));
            Assert.Equal("1m 30s", dur90s);
            AssertNoArabicIndic(dur90s);

            // 4. X-axis date and weekday formatting
            var date = new DateOnly(2026, 9, 6); // Sunday
            string dayDate = ReportsFormatting.FormatDayDate(date);
            Assert.Equal("Sep 6", dayDate);
            AssertNoArabicIndic(dayDate);

            string dayOfWeek = ReportsFormatting.FormatDayOfWeek(date);
            Assert.Equal("(Sun)", dayOfWeek);
            AssertNoArabicIndic(dayOfWeek);

            string dayOfWeekLong = ReportsFormatting.FormatDayOfWeekLong(date);
            Assert.Equal("Sunday", dayOfWeekLong);
            AssertNoArabicIndic(dayOfWeekLong);

            // 5. Tooltip date and content formatting
            string tooltipDate = ReportsFormatting.FormatTooltipDate(date);
            Assert.Equal("Sep 6 (Sun)", tooltipDate);
            AssertNoArabicIndic(tooltipDate);

            var bucket = new ReportBucket(
                "2026-09-06",
                new ReportTotals(1, 1, 0, 1, 0, 0, 0, 0, TimeSpan.FromMinutes(690), TimeSpan.Zero));
            string tooltipText = ReportsFormatting.FormatTooltip(bucket, ReportPeriod.Weekly);
            Assert.Equal("Sep 6 (Sun)\nFocus Time: 11h 30m\nBreak Time: 0m", tooltipText);
            AssertNoArabicIndic(tooltipText);

            // 6. Monthly tooltip formatting
            var monthlyBucket = new ReportBucket(
                "W1",
                new ReportTotals(3, 2, 1, 2, 1, 0, 0, 0, TimeSpan.FromHours(2), TimeSpan.FromMinutes(30)));
            string monthlyTooltip = ReportsFormatting.FormatTooltip(monthlyBucket, ReportPeriod.Monthly);
            Assert.Equal("W1\nFocus Time: 2h 00m\nBreak Time: 30m", monthlyTooltip);
            AssertNoArabicIndic(monthlyTooltip);

            // 7. Date range formatting
            string dateRange = ReportsFormatting.FormatDateRange(new DateOnly(2026, 9, 6), new DateOnly(2026, 9, 12));
            Assert.Equal("2026-09-06 – 2026-09-12", dateRange);
            AssertNoArabicIndic(dateRange);

            // 8. Rate formatting
            string rate = ReportsFormatting.FormatRate(85.5);
            Assert.Equal("85.5%", rate);
            AssertNoArabicIndic(rate);

            // 9. Monthly week index formatting
            string w1 = ReportsFormatting.FormatMonthWeek(1);
            Assert.Equal("W1", w1);
            AssertNoArabicIndic(w1);

            // 10. Streak formatting
            Assert.Equal("1 day", ReportsFormatting.FormatStreak(1));
            Assert.Equal("3 days", ReportsFormatting.FormatStreak(3));
            AssertNoArabicIndic(ReportsFormatting.FormatStreak(3));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = originalCulture;
            Thread.CurrentThread.CurrentUICulture = originalUiCulture;
        }
    }

    private static void AssertNoArabicIndic(string text)
    {
        Assert.False(ArabicIndicDigits.IsMatch(text), $"Expected no Arabic-Indic digits in '{text}', but found matching characters.");
    }
}
