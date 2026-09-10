using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Reports;

public sealed class ReportsPolishVerificationTests
{
    [Fact]
    public async Task DailyWeeklyAndMonthlyCalculationsMatchExpectedRepresentativeTotals()
    {
        using var store = new SessionStore();
        var zone = TimeZoneInfo.Utc;
        var thursday = new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero);

        // Seed Thursday (today)
        await store.Repository.AddAsync(Finished(thursday.AddHours(9), SessionType.Work, SessionStatus.Completed, 25));
        await store.Repository.AddAsync(Finished(thursday.AddHours(9).AddMinutes(25), SessionType.Break, SessionStatus.Completed, 5));
        await store.Repository.AddAsync(Finished(thursday.AddHours(10), SessionType.Work, SessionStatus.Completed, 25));
        await store.Repository.AddAsync(Finished(thursday.AddHours(10).AddMinutes(25), SessionType.Break, SessionStatus.Completed, 5));
        await store.Repository.AddAsync(Finished(thursday.AddHours(14), SessionType.Work, SessionStatus.Completed, 30));
        await store.Repository.AddAsync(Finished(thursday.AddHours(14).AddMinutes(35), SessionType.Break, SessionStatus.Completed, 10));
        await store.Repository.AddAsync(Finished(thursday.AddHours(16), SessionType.Work, SessionStatus.Stopped, 10));

        // Seed earlier this week (Monday and Tuesday)
        var monday = thursday.AddDays(-3);
        await store.Repository.AddAsync(Finished(monday.AddHours(10), SessionType.Work, SessionStatus.Completed, 45));
        await store.Repository.AddAsync(Finished(monday.AddHours(11), SessionType.Break, SessionStatus.Completed, 15));
        var tuesday = thursday.AddDays(-2);
        await store.Repository.AddAsync(Finished(tuesday.AddHours(14), SessionType.Work, SessionStatus.Completed, 50));
        await store.Repository.AddAsync(Finished(tuesday.AddHours(15), SessionType.Break, SessionStatus.Completed, 10));

        // Seed prior week: Thursday Sept 3 (60m) and Monday Aug 31 (90m)
        await store.Repository.AddAsync(Finished(thursday.AddDays(-7).AddHours(9), SessionType.Work, SessionStatus.Completed, 60));
        await store.Repository.AddAsync(Finished(thursday.AddDays(-7).AddHours(10).AddMinutes(10), SessionType.Break, SessionStatus.Completed, 15));
        await store.Repository.AddAsync(Finished(new DateTimeOffset(2026, 8, 31, 11, 0, 0, TimeSpan.Zero), SessionType.Work, SessionStatus.Completed, 90));

        var service = new ReportsService(store.Repository, TimeProvider.System, () => zone);

        // 1. Daily Verification
        var daily = await service.ReadAsync(ReportPeriod.Daily, DateOnly.FromDateTime(thursday.DateTime));
        Assert.Equal(7, daily.Totals.Started);
        Assert.Equal(3, daily.Totals.CompletedWork);
        Assert.Equal(3, daily.Totals.CompletedBreak);
        Assert.Equal(1, daily.Totals.Stopped);
        Assert.Equal(TimeSpan.FromMinutes(80), daily.Totals.FocusTime);
        Assert.Equal(TimeSpan.FromMinutes(20), daily.Totals.BreakTime);
        Assert.Equal(24, daily.Trend.Count);
        // Hour 9 has 25m work, 5m break
        Assert.Equal(TimeSpan.FromMinutes(25), daily.Trend[9].Totals.FocusTime);
        Assert.Equal(TimeSpan.FromMinutes(5), daily.Trend[9].Totals.BreakTime);
        // Hour 10 has 25m work, 5m break
        Assert.Equal(TimeSpan.FromMinutes(25), daily.Trend[10].Totals.FocusTime);
        Assert.Equal(TimeSpan.FromMinutes(5), daily.Trend[10].Totals.BreakTime);
        // Hour 14 has 30m work, 10m break
        Assert.Equal(TimeSpan.FromMinutes(30), daily.Trend[14].Totals.FocusTime);
        Assert.Equal(TimeSpan.FromMinutes(10), daily.Trend[14].Totals.BreakTime);

        // 2. Weekly Verification
        var weekly = await service.ReadAsync(ReportPeriod.Weekly, DateOnly.FromDateTime(thursday.DateTime));
        Assert.Equal(7, weekly.Trend.Count); // 7 days (Mon-Sun)
        Assert.Equal(11, weekly.Totals.Started); // 2 on Mon + 2 on Tue + 7 on Thu
        Assert.Equal(TimeSpan.FromMinutes(45 + 50 + 80), weekly.Totals.FocusTime); // 175m
        Assert.Equal(TimeSpan.FromMinutes(15 + 10 + 20), weekly.Totals.BreakTime); // 45m
        Assert.Equal(TimeSpan.FromMinutes(175), weekly.WeekFocus);
        Assert.Equal(TimeSpan.FromMinutes(150), weekly.PreviousWeekFocus);
        Assert.Equal(TimeSpan.FromMinutes(25), weekly.WeekDifference);

        // 3. Monthly Verification
        var monthly = await service.ReadAsync(ReportPeriod.Monthly, DateOnly.FromDateTime(thursday.DateTime));
        Assert.True(monthly.Trend.Count >= 4);
        Assert.Equal(TimeSpan.FromMinutes(235), monthly.Totals.FocusTime); // 175m this week + 60m on Sept 3

        // 4. Streak Statistics Verification (User-level, invariant across periods)
        Assert.Equal(1, daily.Streaks.CurrentStreak);
        Assert.Equal(2, daily.Streaks.LongestStreak);
        Assert.Equal(1, weekly.Streaks.CurrentStreak);
        Assert.Equal(2, weekly.Streaks.LongestStreak);
        Assert.Equal(1, monthly.Streaks.CurrentStreak);
        Assert.Equal(2, monthly.Streaks.LongestStreak);

        // 5. Empty Period Verification (Future)
        var future = await service.ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 12, 1));
        Assert.Equal(0, future.Totals.Started);
        Assert.Equal(0, future.Totals.Completed);
        Assert.Equal(TimeSpan.Zero, future.Totals.FocusTime);
        Assert.Equal(TimeSpan.Zero, future.Totals.BreakTime);
        Assert.Null(future.Totals.CompletionRate);
        Assert.Equal(1, future.Streaks.CurrentStreak);
        Assert.Equal(2, future.Streaks.LongestStreak);
    }

    private static SessionRecord Finished(DateTimeOffset start, SessionType type, SessionStatus status, int minutes) =>
        TestSessions.Finished(status, startedAt: start, type: type, plannedDuration: TimeSpan.FromMinutes(minutes));
}
