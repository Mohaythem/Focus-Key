using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Reports;

/// <summary>
/// Adversarial coverage for the Reports date lifecycle: the report period must always resolve against
/// the live clock at the moment it is read, including when the application stays open across midnight
/// or a month boundary. These use deterministic fake time, never the wall clock.
/// </summary>
public sealed class ReportsDateLifecycleTests
{
    [Fact]
    public async Task CurrentWeekRecalculatesWhenTheClockCrossesMidnight()
    {
        using var store = new SessionStore();
        // A completed work session immediately after midnight on Oct 1.
        await store.Repository.AddAsync(Finished(new DateTimeOffset(2026, 10, 1, 0, 15, 0, TimeSpan.Zero), 30));

        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 9, 30, 23, 30, 0, TimeSpan.Zero));
        var service = new ReportsService(store.Repository, clock, () => TimeZoneInfo.Utc);

        // Before midnight the current day is Sep 30; the week ends (exclusive) at Oct 1, excluding the session.
        Assert.Equal(new DateOnly(2026, 9, 30), service.CurrentDate());
        var before = await service.ReadAsync(ReportPeriod.Weekly, service.CurrentDate());
        Assert.Equal(new DateOnly(2026, 9, 24), before.Range.Start);
        Assert.Equal(new DateOnly(2026, 10, 1), before.Range.End);
        Assert.Equal(0, before.Totals.Started);

        // After midnight the current day is Oct 1; the week shifts forward and now includes the session.
        clock.Set(new DateTimeOffset(2026, 10, 1, 0, 45, 0, TimeSpan.Zero));
        Assert.Equal(new DateOnly(2026, 10, 1), service.CurrentDate());
        var after = await service.ReadAsync(ReportPeriod.Weekly, service.CurrentDate());
        Assert.Equal(new DateOnly(2026, 9, 25), after.Range.Start);
        Assert.Equal(new DateOnly(2026, 10, 2), after.Range.End);
        Assert.Equal(1, after.Totals.Started);
    }

    [Fact]
    public async Task CurrentMonthAndYearResolveAgainstTheClockAtABoundary()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 12, 31, 23, 0, 0, TimeSpan.Zero));
        var service = new ReportsService(store.Repository, clock, () => TimeZoneInfo.Utc);

        Assert.Equal(new DateOnly(2026, 12, 31), service.CurrentDate());
        Assert.Equal(new DateOnly(2026, 12, 1), (await service.ReadAsync(ReportPeriod.Monthly, service.CurrentDate())).Range.Start);
        Assert.Equal(new DateOnly(2026, 1, 1), (await service.ReadAsync(ReportPeriod.Yearly, service.CurrentDate())).Range.Start);

        // Cross into the new year while the app stays open.
        clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(new DateOnly(2027, 1, 1), service.CurrentDate());
        Assert.Equal(new DateOnly(2027, 1, 1), (await service.ReadAsync(ReportPeriod.Monthly, service.CurrentDate())).Range.Start);
        Assert.Equal(new DateOnly(2027, 1, 1), (await service.ReadAsync(ReportPeriod.Yearly, service.CurrentDate())).Range.Start);
        Assert.Equal(new DateOnly(2028, 1, 1), (await service.ReadAsync(ReportPeriod.Yearly, service.CurrentDate())).Range.End);
    }

    private static SessionRecord Finished(DateTimeOffset start, int minutes) =>
        TestSessions.Finished(SessionStatus.Completed, startedAt: start, type: SessionType.Work, plannedDuration: TimeSpan.FromMinutes(minutes));
}
