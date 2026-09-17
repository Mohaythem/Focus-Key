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

        var service = new ReportsService(store.Repository, new ManualTimeProvider(thursday.AddHours(18)), () => zone);

        // 1. Weekly Verification (Rolling 7-day window ending on Thursday Sept 10: Sept 4 to Sept 10)
        var weekly = await service.ReadAsync(ReportPeriod.Weekly, DateOnly.FromDateTime(thursday.DateTime));
        Assert.Equal(7, weekly.Trend.Count); // 7 rolling days
        Assert.Equal(11, weekly.Totals.Started); // 2 on Mon + 2 on Tue + 7 on Thu
        Assert.Equal(TimeSpan.FromMinutes(45 + 50 + 90), weekly.Totals.FocusTime); // 185m (including 10m stopped work session)
        Assert.Equal(TimeSpan.FromMinutes(15 + 10 + 20), weekly.Totals.BreakTime); // 45m
        Assert.Equal(TimeSpan.FromMinutes(185), weekly.WeekFocus);
        Assert.Equal(TimeSpan.FromMinutes(150), weekly.PreviousWeekFocus);
        Assert.Equal(TimeSpan.FromMinutes(35), weekly.WeekDifference);

        // Rightmost day (index 6, Thursday Sept 10) has 90m work (80m completed + 10m stopped), 20m break
        Assert.Equal("2026-09-10", weekly.Trend[6].Label);
        Assert.Equal(TimeSpan.FromMinutes(90), weekly.Trend[6].Totals.FocusTime);
        Assert.Equal(TimeSpan.FromMinutes(20), weekly.Trend[6].Totals.BreakTime);

        // Monday Sept 7 (index 3) and Tuesday Sept 8 (index 4)
        Assert.Equal("2026-09-07", weekly.Trend[3].Label);
        Assert.Equal(TimeSpan.FromMinutes(45), weekly.Trend[3].Totals.FocusTime);
        Assert.Equal("2026-09-08", weekly.Trend[4].Label);
        Assert.Equal(TimeSpan.FromMinutes(50), weekly.Trend[4].Totals.FocusTime);

        // 2. Monthly Verification
        var monthly = await service.ReadAsync(ReportPeriod.Monthly, DateOnly.FromDateTime(thursday.DateTime));
        Assert.True(monthly.Trend.Count >= 4);
        Assert.Equal(TimeSpan.FromMinutes(245), monthly.Totals.FocusTime); // 185m this week + 60m on Sept 3

        // 3. Streak Statistics Verification (User-level, invariant across periods)
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

    [Fact]
    public async Task ConsistencyMetrics_WeeklyMonthlyAndYearlyCalculateCorrectActiveBuckets()
    {
        using var store = new SessionStore();
        var zone = TimeZoneInfo.Utc;
        var date = new DateTimeOffset(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

        // Add sessions across 3 days this week
        await store.Repository.AddAsync(Finished(new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.Zero), SessionType.Work, SessionStatus.Completed, 60));
        await store.Repository.AddAsync(Finished(new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero), SessionType.Work, SessionStatus.Completed, 60));
        await store.Repository.AddAsync(Finished(new DateTimeOffset(2026, 9, 10, 10, 0, 0, TimeSpan.Zero), SessionType.Work, SessionStatus.Completed, 60));

        var service = new ReportsService(store.Repository, new ManualTimeProvider(date), () => zone);

        // Weekly consistency: 3 active days out of 7
        var weekly = await service.ReadAsync(ReportPeriod.Weekly, new DateOnly(2026, 9, 15));
        int activeWeeklyDays = weekly.Trend.Count(b => b.Totals.FocusTime > TimeSpan.Zero);
        Assert.Equal(3, activeWeeklyDays);
        Assert.Equal(TimeSpan.FromMinutes(180), weekly.Totals.FocusTime);

        // Monthly consistency: active weeks in month
        var monthly = await service.ReadAsync(ReportPeriod.Monthly, new DateOnly(2026, 9, 15));
        int activeMonthlyWeeks = monthly.Trend.Count(b => b.Totals.FocusTime > TimeSpan.Zero);
        Assert.Equal(2, activeMonthlyWeeks); // Week 2 (Sept 10) and Week 3 (Sept 14 & 15)

        // Yearly consistency: active months in year
        var yearly = await service.ReadAsync(ReportPeriod.Yearly, new DateOnly(2026, 9, 15));
        int activeYearlyMonths = yearly.Trend.Count(b => b.Totals.FocusTime > TimeSpan.Zero);
        Assert.Equal(1, activeYearlyMonths); // September (month 9)
    }

    private static SessionRecord Finished(DateTimeOffset start, SessionType type, SessionStatus status, int minutes) =>
        TestSessions.Finished(status, startedAt: start, type: type, plannedDuration: TimeSpan.FromMinutes(minutes));
}
