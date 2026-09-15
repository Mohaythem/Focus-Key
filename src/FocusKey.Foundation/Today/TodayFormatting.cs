using System.Globalization;

namespace FocusKey.Foundation.Today;

/// <summary>
/// Deterministic formatting for Today launcher durations and labels.
/// Enforces Western Latin digits (0-9) and English abbreviations regardless of OS culture.
/// </summary>
public static class TodayFormatting
{
    public static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    /// <summary>
    /// Formats a session duration for launcher cards (e.g. "30 min", "10 min", "45 min", "1h 30m").
    /// </summary>
    public static string FormatLauncherDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return "0 min";
        long totalSeconds = (long)Math.Round(duration.TotalSeconds);
        if (totalSeconds < 60)
        {
            return string.Create(Culture, $"{totalSeconds} sec");
        }
        long totalMinutes = totalSeconds / 60;
        long remainingSec = totalSeconds % 60;
        if (remainingSec > 0)
        {
            return string.Create(Culture, $"{totalMinutes}m {remainingSec}s");
        }
        if (totalMinutes < 60)
        {
            return string.Create(Culture, $"{totalMinutes} min");
        }
        long hours = totalMinutes / 60;
        long mins = totalMinutes % 60;
        return mins > 0
            ? string.Create(Culture, $"{hours}h {mins}m")
            : string.Create(Culture, $"{hours} hr");
    }

    /// <summary>
    /// Formats a session duration into distinct number and unit parts (e.g. ("30", "min"), ("10", "min"), ("1", "hr")).
    /// </summary>
    public static (string Number, string Unit) FormatLauncherDurationParts(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero) return ("0", "min");
        long totalSeconds = (long)Math.Round(duration.TotalSeconds);
        if (totalSeconds < 60)
        {
            return (string.Create(Culture, $"{totalSeconds}"), "sec");
        }
        long totalMinutes = totalSeconds / 60;
        long remainingSec = totalSeconds % 60;
        if (remainingSec > 0)
        {
            return (string.Create(Culture, $"{totalMinutes}:{remainingSec:D2}"), "min");
        }
        if (totalMinutes < 60)
        {
            return (string.Create(Culture, $"{totalMinutes}"), "min");
        }
        long hours = totalMinutes / 60;
        long mins = totalMinutes % 60;
        if (mins > 0)
        {
            return (string.Create(Culture, $"{hours}h {mins}"), "m");
        }
        return (string.Create(Culture, $"{hours}"), hours == 1 ? "hr" : "hrs");
    }
}
