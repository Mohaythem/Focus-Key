using FocusKey.Foundation.Reports;

namespace FocusKey.Foundation.Tests.Reports;

public sealed class ReportsControllerTests
{
    [Fact]
    public async Task HiddenControllerDoesNotQueryAndOpenRefreshes()
    {
        int reads = 0;
        using var controller = New((_, _, _) => { reads++; return Task.FromResult(Empty()); });
        await controller.RefreshAsync(); Assert.Equal(0, reads);
        await controller.OpenAsync(); controller.Hide(); await controller.RefreshAsync();
        Assert.Equal(1, reads);
        await controller.OpenAsync(); Assert.Equal(2, reads);
    }

    [Fact]
    public async Task NewerRefreshWinsWhenQueriesFinishOutOfOrder()
    {
        var first = new TaskCompletionSource<ReportsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<ReportsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        using var controller = New((_, _, _) => ++reads == 1 ? first.Task : second.Task);
        Task open = controller.OpenAsync(); Task refresh = controller.RefreshAsync();
        var latest = Empty(ReportPeriod.Daily); second.SetResult(latest); await refresh;
        first.SetResult(Empty()); await open;
        Assert.Same(latest, controller.Snapshot); Assert.False(controller.IsRefreshing);
    }

    [Fact]
    public async Task HideAndDisposeInvalidatePendingResultsAndReportErrors()
    {
        var pending = new TaskCompletionSource<ReportsSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var errors = new List<Exception>();
        using var controller = New((_, _, _) => pending.Task, errors.Add);
        Task open = controller.OpenAsync(); controller.Hide(); pending.SetResult(Empty()); await open;
        Assert.Null(controller.Snapshot);
        var failing = new ReportsController((_, _, _) => Task.FromException<ReportsSnapshot>(new InvalidOperationException()), () => new DateOnly(2026, 9, 2), errors.Add);
        await failing.OpenAsync(); Assert.NotNull(failing.Error); Assert.Single(errors); failing.Dispose();
        int changes = 0; controller.Changed += () => changes++;
        controller.Dispose(); await controller.OpenAsync(); await controller.RefreshAsync();
        Assert.Equal(0, changes);
    }

    [Fact]
    public async Task NavigationSelectMoveCurrentAndInvalidDirectionAreDeterministic()
    {
        var calls = new List<(ReportPeriod Period, DateOnly Date)>();
        using var controller = New((period, date, _) => { calls.Add((period, date)); return Task.FromResult(Empty(period)); });
        await controller.OpenAsync();
        await controller.SelectAsync(ReportPeriod.Daily, new DateOnly(2026, 9, 2));
        await controller.MoveAsync(1); Assert.Equal(new DateOnly(2026, 9, 3), controller.Date);
        await controller.SelectAsync(ReportPeriod.Monthly, new DateOnly(2026, 9, 15));
        await controller.MoveAsync(-1); Assert.Equal(new DateOnly(2026, 8, 1), controller.Date);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => controller.MoveAsync(0));
        await controller.CurrentAsync(); Assert.Equal(new DateOnly(2026, 9, 2), controller.Date);
        Assert.Equal(6, calls.Count);
    }

    [Fact]
    public async Task PeriodChangeClearsOldDataAndOldFailureCannotOverwriteNewResult()
    {
        var old = new TaskCompletionSource<ReportsSnapshot>();
        int reads = 0;
        var errors = new List<Exception>();
        using var controller = New((period, _, _) => ++reads == 2 ? old.Task : Task.FromResult(Empty(period)), errors.Add);
        await controller.OpenAsync();
        var pending = controller.RefreshAsync();
        Assert.Null(controller.Snapshot);
        await controller.SelectAsync(ReportPeriod.Monthly, new(2026, 9, 2));
        old.SetException(new IOException("Old read failed")); await pending;
        Assert.Null(controller.Error);
        Assert.Equal(ReportPeriod.Monthly, controller.Snapshot!.Period);
        Assert.Single(errors);
    }

    [Fact]
    public async Task FailureShowsNoStaleMetricsAndManualRefreshRecovers()
    {
        int reads = 0;
        using var controller = New((_, _, _) => ++reads == 2 ? Task.FromException<ReportsSnapshot>(new IOException()) : Task.FromResult(Empty()));
        await controller.OpenAsync(); await controller.RefreshAsync();
        Assert.NotNull(controller.Error); Assert.Null(controller.Snapshot); Assert.False(controller.IsRefreshing);
        await controller.RefreshAsync();
        Assert.Null(controller.Error); Assert.NotNull(controller.Snapshot);
    }

    [Fact]
    public async Task CurrentDateTracksClockUntilUserPinsHistoricalSelection()
    {
        var current = new DateOnly(2026, 9, 2);
        using var controller = new ReportsController((_, _, _) => Task.FromResult(Empty()), () => current, _ => { });
        await controller.OpenAsync();
        current = current.AddDays(1); await controller.RefreshAsync(); Assert.Equal(current, controller.Date);
        var historical = new DateOnly(2026, 8, 10);
        await controller.SelectAsync(ReportPeriod.Weekly, historical);
        controller.Hide(); current = current.AddDays(1); await controller.OpenAsync();
        Assert.Equal(historical, controller.Date);
        await controller.CurrentAsync(); Assert.Equal(current, controller.Date);
    }

    [Fact]
    public async Task DisposeDuringReadInvalidatesResultAndAllLaterRequests()
    {
        var pending = new TaskCompletionSource<ReportsSnapshot>();
        int reads = 0, changed = 0;
        using var controller = New((_, _, _) => { reads++; return pending.Task; });
        var open = controller.OpenAsync();
        controller.Changed += () => changed++;
        controller.Dispose(); pending.SetResult(Empty()); await open;
        await controller.OpenAsync(); await controller.RefreshAsync();
        Assert.Null(controller.Snapshot); Assert.Equal(1, reads); Assert.Equal(0, changed);
    }

    [Theory]
    [InlineData(ReportPeriod.Daily)]
    [InlineData(ReportPeriod.Weekly)]
    [InlineData(ReportPeriod.Monthly)]
    public async Task NavigationAtSupportedLimitsIsSafe(ReportPeriod period)
    {
        using var controller = New((_, _, _) => Task.FromResult(Empty()));
        await controller.SelectAsync(period, ReportRange.MinimumDate);
        await controller.MoveAsync(-1); Assert.Equal(ReportRange.MinimumDate, controller.Date);
        await controller.SelectAsync(period, ReportRange.MaximumDate);
        await controller.MoveAsync(1); Assert.Equal(ReportRange.MaximumDate, controller.Date);
    }

    private static ReportsController New(Func<ReportPeriod, DateOnly, CancellationToken, Task<ReportsSnapshot>> read, Action<Exception>? report = null) =>
        new(read, () => new DateOnly(2026, 9, 2), report ?? (_ => { }));

    private static ReportsSnapshot Empty(ReportPeriod period = ReportPeriod.Weekly) =>
        new(period, ReportRange.For(period, new DateOnly(2026, 9, 2)), TimeZoneInfo.Utc,
            new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero),
            new ReportTotals(0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero, TimeSpan.Zero), [], [],
            ReportRange.For(ReportPeriod.Weekly, new DateOnly(2026, 9, 2)), TimeSpan.Zero, TimeSpan.Zero);
}
