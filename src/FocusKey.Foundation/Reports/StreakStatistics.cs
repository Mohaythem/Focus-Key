using System.Globalization;
using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Reports;

/// <summary>
/// User-level streak statistics derived from completed work session history.
/// </summary>
public sealed record StreakStatistics(int CurrentStreak, int LongestStreak)
{
    public static readonly StreakStatistics Zero = new(0, 0);

    public static string Format(int days) =>
        days == 1
            ? "1 day"
            : string.Create(CultureInfo.InvariantCulture, $"{days} days");
}

/// <summary>
/// Pure calculation of streak statistics based on Focus Key calendar day rules.
/// </summary>
public static class StreakCalculator
{
    /// <summary>
    /// Calculates current streak and longest streak from historical sessions.
    /// A qualifying streak day is a calendar day containing at least one completed Work session.
    /// Break sessions and stopped/interrupted sessions never extend a streak.
    /// </summary>
    public static StreakStatistics Calculate(IEnumerable<SessionRecord> sessions, DateOnly today, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(zone);

        var qualifyingDays = new HashSet<DateOnly>();
        foreach (var session in sessions)
        {
            if (session.Type == SessionType.Work && session.Status == SessionStatus.Completed)
            {
                var local = TimeZoneInfo.ConvertTime(session.StartedAt, zone);
                var localDay = DateOnly.FromDateTime(local.DateTime);
                if (localDay <= today)
                {
                    qualifyingDays.Add(localDay);
                }
            }
        }

        if (qualifyingDays.Count == 0)
        {
            return StreakStatistics.Zero;
        }

        // 1. Longest Streak: greatest number of consecutive calendar days in history
        int longest = 0;
        int currentRun = 0;
        DateOnly? prev = null;
        foreach (var day in qualifyingDays.OrderBy(d => d))
        {
            if (prev is not null && day == prev.Value.AddDays(1))
            {
                currentRun++;
            }
            else
            {
                currentRun = 1;
            }

            if (currentRun > longest) longest = currentRun;
            prev = day;
        }

        // 2. Current Streak:
        // - If today has qualified, count backwards from today.
        // - If today has not yet qualified but yesterday belongs to an active streak,
        //   count backwards from yesterday while today is still in progress.
        // - Once a full calendar day is genuinely missed, current streak is 0.
        int currentStreak = 0;
        DateOnly checkDay;
        if (qualifyingDays.Contains(today))
        {
            checkDay = today;
        }
        else if (qualifyingDays.Contains(today.AddDays(-1)))
        {
            checkDay = today.AddDays(-1);
        }
        else
        {
            return new StreakStatistics(0, longest);
        }

        while (qualifyingDays.Contains(checkDay))
        {
            currentStreak++;
            checkDay = checkDay.AddDays(-1);
        }

        return new StreakStatistics(currentStreak, longest);
    }
}
