using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Reports;

public sealed class StreakCalculationTests
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.Utc;
    private static readonly DateOnly Today = new(2026, 9, 10); // Thursday

    private static SessionRecord WorkSession(DateOnly date, int hour = 10, SessionStatus status = SessionStatus.Completed)
    {
        var start = new DateTimeOffset(date.Year, date.Month, date.Day, hour, 0, 0, TimeSpan.Zero);
        return new SessionRecord
        {
            Id = SessionId.New(),
            Type = SessionType.Work,
            Status = status,
            StartedAt = start,
            PlannedDuration = TimeSpan.FromMinutes(25),
            EndedAt = status == SessionStatus.Running ? null : start.AddMinutes(25),
            CreatedAt = start
        };
    }

    private static SessionRecord BreakSession(DateOnly date, int hour = 11, SessionStatus status = SessionStatus.Completed)
    {
        var start = new DateTimeOffset(date.Year, date.Month, date.Day, hour, 0, 0, TimeSpan.Zero);
        return new SessionRecord
        {
            Id = SessionId.New(),
            Type = SessionType.Break,
            Status = status,
            StartedAt = start,
            PlannedDuration = TimeSpan.FromMinutes(5),
            EndedAt = status == SessionStatus.Running ? null : start.AddMinutes(5),
            CreatedAt = start
        };
    }

    [Fact]
    public void EmptyHistory_YieldsZeroStreaks()
    {
        var result = StreakCalculator.Calculate([], Today, Zone);
        Assert.Equal(0, result.CurrentStreak);
        Assert.Equal(0, result.LongestStreak);
    }

    [Fact]
    public void BreakSessionsOnly_YieldsZeroStreaks()
    {
        var sessions = new[]
        {
            BreakSession(Today),
            BreakSession(Today.AddDays(-1)),
            BreakSession(Today.AddDays(-2))
        };
        var result = StreakCalculator.Calculate(sessions, Today, Zone);
        Assert.Equal(0, result.CurrentStreak);
        Assert.Equal(0, result.LongestStreak);
    }

    [Fact]
    public void StoppedOrInterruptedWork_YieldsZeroStreaks()
    {
        var sessions = new[]
        {
            WorkSession(Today, status: SessionStatus.Stopped),
            WorkSession(Today.AddDays(-1), status: SessionStatus.Interrupted),
            WorkSession(Today.AddDays(-2), status: SessionStatus.Running)
        };
        var result = StreakCalculator.Calculate(sessions, Today, Zone);
        Assert.Equal(0, result.CurrentStreak);
        Assert.Equal(0, result.LongestStreak);
    }

    [Fact]
    public void SingleCompletedDay_Today_YieldsOneDayStreak()
    {
        var sessions = new[] { WorkSession(Today) };
        var result = StreakCalculator.Calculate(sessions, Today, Zone);
        Assert.Equal(1, result.CurrentStreak);
        Assert.Equal(1, result.LongestStreak);
    }

    [Fact]
    public void MultipleSessionsOnSameDay_CountAsSingleStreakDay()
    {
        var sessions = new[]
        {
            WorkSession(Today, hour: 9),
            WorkSession(Today, hour: 10),
            WorkSession(Today, hour: 14),
            BreakSession(Today, hour: 11)
        };
        var result = StreakCalculator.Calculate(sessions, Today, Zone);
        Assert.Equal(1, result.CurrentStreak);
        Assert.Equal(1, result.LongestStreak);
    }

    [Fact]
    public void ConsecutiveDaysThroughToday_CountsThroughToday()
    {
        // Monday, Tuesday, Wednesday, Thursday(Today)
        var sessions = new[]
        {
            WorkSession(Today.AddDays(-3)),
            WorkSession(Today.AddDays(-2)),
            WorkSession(Today.AddDays(-1)),
            WorkSession(Today)
        };
        var result = StreakCalculator.Calculate(sessions, Today, Zone);
        Assert.Equal(4, result.CurrentStreak);
        Assert.Equal(4, result.LongestStreak);
    }

    [Fact]
    public void TodayNotYetQualified_PreservesActiveStreakFromYesterday()
    {
        // Mon, Tue, Wed completed. Today is Thursday, no work completed yet.
        var sessions = new[]
        {
            WorkSession(Today.AddDays(-3)), // Mon
            WorkSession(Today.AddDays(-2)), // Tue
            WorkSession(Today.AddDays(-1))  // Wed
        };
        var result = StreakCalculator.Calculate(sessions, Today, Zone);
        // Streak remains 3 while today is still in progress
        Assert.Equal(3, result.CurrentStreak);
        Assert.Equal(3, result.LongestStreak);
    }

    [Fact]
    public void MissedFullCalendarDay_BreaksCurrentStreak()
    {
        // Mon, Tue, Wed completed. Today is Friday, Thursday had no completed work.
        var friday = Today.AddDays(1);
        var sessions = new[]
        {
            WorkSession(friday.AddDays(-4)), // Mon
            WorkSession(friday.AddDays(-3)), // Tue
            WorkSession(friday.AddDays(-2))  // Wed
            // Thursday missed
        };
        var result = StreakCalculator.Calculate(sessions, friday, Zone);
        // Thursday was genuinely missed, so current streak is 0
        Assert.Equal(0, result.CurrentStreak);
        Assert.Equal(3, result.LongestStreak);
    }

    [Fact]
    public void LongestHistoricalStreak_PreservedEvenWhenCurrentStreakIsShorter()
    {
        // Historical 5-day streak: Sept 1 to Sept 5
        // Gap on Sept 6 and Sept 7
        // Current 2-day streak: Sept 8 and Sept 9 (with today Sept 10 in progress)
        var sept1 = new DateOnly(2026, 9, 1);
        var sessions = new[]
        {
            WorkSession(sept1),
            WorkSession(sept1.AddDays(1)),
            WorkSession(sept1.AddDays(2)),
            WorkSession(sept1.AddDays(3)),
            WorkSession(sept1.AddDays(4)),
            // Gaps: Sept 6, 7
            WorkSession(sept1.AddDays(7)), // Sept 8
            WorkSession(sept1.AddDays(8))  // Sept 9
        };
        var result = StreakCalculator.Calculate(sessions, Today, Zone);
        Assert.Equal(2, result.CurrentStreak);
        Assert.Equal(5, result.LongestStreak);
    }

    [Fact]
    public void CrossMonthBoundary_CalculatesCorrectly()
    {
        // Jan 30, Jan 31, Feb 1
        var jan30 = new DateOnly(2026, 1, 30);
        var feb1 = new DateOnly(2026, 2, 1);
        var sessions = new[]
        {
            WorkSession(jan30),
            WorkSession(jan30.AddDays(1)), // Jan 31
            WorkSession(feb1)              // Feb 1
        };
        var result = StreakCalculator.Calculate(sessions, feb1, Zone);
        Assert.Equal(3, result.CurrentStreak);
        Assert.Equal(3, result.LongestStreak);
    }

    [Fact]
    public void FormatStreak_UsesWesternDigitsAndSingularPluralCorrectly()
    {
        Assert.Equal("0 days", StreakStatistics.Format(0));
        Assert.Equal("1 day", StreakStatistics.Format(1));
        Assert.Equal("2 days", StreakStatistics.Format(2));
        Assert.Equal("6 days", StreakStatistics.Format(6));
        Assert.Equal("14 days", StreakStatistics.Format(14));
        Assert.Equal("100 days", StreakStatistics.Format(100));
    }
}
