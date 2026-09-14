using System.Globalization;

namespace FocusKey.Foundation.Reports;

/// <summary>
/// Centralized, deterministic formatting for all Reports user-facing dates, times, durations,
/// and numerical metrics. Enforces Western Latin digits (0-9) and English abbreviations
/// regardless of the user's active Windows regional locale or culture settings.
/// </summary>
public static class ReportsFormatting
{
    public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>Formats Y-axis tick values (e.g. "0h", "2h", "12h").</summary>
    public static string FormatAxisHour(int hour) =>
        string.Create(Culture, $"{hour}h");

    /// <summary>Formats duration labels placed above chart bars (e.g. "11h 24m", "5h", "45m").</summary>
    public static string FormatBarDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return string.Empty;
        int hours = (int)duration.TotalHours;
        int minutes = duration.Minutes;
        if (hours > 0 && minutes > 0) return string.Create(Culture, $"{hours}h {minutes}m");
        if (hours > 0) return string.Create(Culture, $"{hours}h");
        return string.Create(Culture, $"{Math.Max(1, minutes)}m");
    }

    /// <summary>Formats duration values used in metric cards and tooltips (e.g. "5h 00m", "45m").</summary>
    public static string FormatDuration(TimeSpan value)
    {
        if (value.TotalHours >= 1) return string.Create(Culture, $"{(long)value.TotalHours}h {value.Minutes:00}m");
        if (value.Seconds != 0) return string.Create(Culture, $"{(long)value.TotalMinutes}m {value.Seconds:00}s");
        return string.Create(Culture, $"{(long)value.TotalMinutes}m");
    }

    /// <summary>Formats the short date line for X-axis weekly columns (e.g. "Sep 6").</summary>
    public static string FormatDayDate(DateOnly date) =>
        date.ToString("MMM d", Culture);

    /// <summary>Formats the weekday line for X-axis weekly columns (e.g. "(Sun)").</summary>
    public static string FormatDayOfWeek(DateOnly date) =>
        $"({date.ToString("ddd", Culture)})";

    /// <summary>Formats full weekday name for insight sentences (e.g. "Sunday").</summary>
    public static string FormatDayOfWeekLong(DateOnly date) =>
        date.ToString("dddd", Culture);

    /// <summary>Formats tooltip header date line (e.g. "Sep 6 (Sun)").</summary>
    public static string FormatTooltipDate(DateOnly date) =>
        date.ToString("MMM d (ddd)", Culture);

    /// <summary>Formats tooltip content for a bucket.</summary>
    public static string FormatTooltip(ReportBucket bucket, ReportPeriod period)
    {
        string focusStr = FormatDuration(bucket.Totals.FocusTime);
        string breakStr = FormatDuration(bucket.Totals.BreakTime);

        if (period == ReportPeriod.Weekly &&
            DateOnly.TryParseExact(bucket.Label, "yyyy-MM-dd", Culture, DateTimeStyles.None, out var day))
        {
            string datePart = FormatTooltipDate(day);
            return $"{datePart}\nFocus Time: {focusStr}\nBreak Time: {breakStr}";
        }

        return $"{bucket.Label}\nFocus Time: {focusStr}\nBreak Time: {breakStr}";
    }

    /// <summary>Formats date range descriptions (e.g. "2026-09-06 – 2026-09-12").</summary>
    public static string FormatDateRange(DateOnly start, DateOnly endInclusive) =>
        string.Create(Culture, $"{start:yyyy-MM-dd} – {endInclusive:yyyy-MM-dd}");

    /// <summary>Formats completion rate percentages (e.g. "85.5%").</summary>
    public static string FormatRate(double rate) =>
        string.Create(Culture, $"{rate:0.#}%");

    /// <summary>Formats monthly week column labels (e.g. "W1", "W2").</summary>
    public static string FormatMonthWeek(int weekIndex) =>
        string.Create(Culture, $"W{weekIndex}");

    /// <summary>Formats streak count values (e.g. "1 day", "3 days").</summary>
    public static string FormatStreak(int days) =>
        StreakStatistics.Format(days);
}
