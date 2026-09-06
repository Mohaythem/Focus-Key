using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;
using FocusKey.Foundation.Today;

namespace FocusKey.Foundation.Tests.Today;

public sealed class TodayControllerTests
{
    [Fact]
    public async Task TodayIsDefaultAndEveryOpenReturnsToToday()
    {
        int reads = 0;
        using var controller = New(_ => { reads++; return Task.FromResult(Empty()); });
        Assert.Equal(MainPage.Today, controller.Page);
        await controller.OpenAsync(); Assert.Equal(1, reads);
        await controller.NavigateAsync(MainPage.Reports);
        controller.Hide(); await controller.OpenAsync();
        Assert.Equal(MainPage.Today, controller.Page); Assert.Equal(2, reads);
    }

    [Theory]
    [InlineData(MainPage.Reports)]
    [InlineData(MainPage.Settings)]
    public async Task PlaceholdersDoNotReadOrStopSessions(MainPage page)
    {
        int reads = 0, stops = 0;
        using var controller = New(_ => { reads++; return Task.FromResult(Empty(TestSessions.Running())); },
            (_, _) => { stops++; throw new InvalidOperationException(); });
        await controller.OpenAsync(); await controller.NavigateAsync(page);
        await controller.RefreshAsync(); await controller.StopAsync();
        Assert.Equal(page, controller.Page); Assert.Equal(1, reads); Assert.Equal(0, stops);
        await controller.NavigateAsync(MainPage.Today); Assert.Equal(2, reads);
    }

    [Fact]
    public async Task HiddenWindowDoesNoReadsAndReopeningRefreshes()
    {
        int reads = 0;
        using var controller = New(_ => { reads++; return Task.FromResult(Empty()); });
        await controller.RefreshAsync(); Assert.Equal(0, reads);
        await controller.OpenAsync(); controller.Hide(); await controller.RefreshAsync();
        Assert.Equal(1, reads);
        await controller.OpenAsync(); Assert.Equal(2, reads);
    }

    [Fact]
    public async Task NewerRefreshWinsWhenQueriesFinishOutOfOrder()
    {
        var first = new TaskCompletionSource<TodaySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource<TodaySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        using var controller = New(_ => ++reads == 1 ? first.Task : second.Task);
        Task open = controller.OpenAsync(); Task refresh = controller.RefreshAsync();
        var latest = Empty(TestSessions.Running());
        second.SetResult(latest); await refresh;
        first.SetResult(Empty()); await open;
        Assert.Same(latest, controller.Snapshot); Assert.False(controller.IsRefreshing);
    }

    [Fact]
    public async Task HideNavigationAndDisposeInvalidatePendingResults()
    {
        var pending = new TaskCompletionSource<TodaySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = New(_ => pending.Task);
        Task open = controller.OpenAsync(); controller.Hide();
        await controller.NavigateAsync(MainPage.Settings); controller.Dispose();
        pending.SetResult(Empty()); await open;
        Assert.Null(controller.Snapshot); Assert.Equal(MainPage.Settings, controller.Page);
    }

    [Fact]
    public async Task FailureKeepsLastDataButBlocksStopUntilSuccessfulRefresh()
    {
        int reads = 0, stops = 0; var errors = new List<Exception>();
        var snapshot = Empty(TestSessions.Running());
        using var controller = new TodayController(_ => ++reads == 2
            ? Task.FromException<TodaySnapshot>(new InvalidOperationException("read failed")) : Task.FromResult(snapshot),
            (_, _) => { stops++; throw new InvalidOperationException(); }, errors.Add);
        await controller.OpenAsync(); await controller.RefreshAsync();
        Assert.Same(snapshot, controller.Snapshot); Assert.NotNull(controller.Error);
        await controller.StopAsync(); Assert.Equal(0, stops); Assert.Single(errors);
        await controller.RefreshAsync(); Assert.Null(controller.Error);
    }

    [Fact]
    public async Task StopUsesDisplayedIdentityAndIgnoresDuplicateClicks()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var engine = new SessionEngine(store.Repository, clock);
        var running = await engine.StartAsync(SessionType.Work);
        var result = await engine.StopAsync(running.Id);
        var release = new TaskCompletionSource<SessionOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
        SessionId? stopped = null; int calls = 0, reads = 0;
        using var controller = New(_ => Task.FromResult(++reads == 1 ? Empty(running) : Empty()), (id, _) =>
        { stopped = id; calls++; return release.Task; });
        await controller.OpenAsync();
        Task stop = controller.StopAsync(); await controller.StopAsync();
        Assert.True(controller.IsStopping); Assert.Equal(1, calls); Assert.Equal(running.Id, stopped);
        release.SetResult(result); await stop;
        Assert.Null(controller.Snapshot!.Running); Assert.False(controller.IsStopping); Assert.Equal(2, reads);
    }

    [Fact]
    public async Task FailedStopLeavesStateAndShowsActionableError()
    {
        using var controller = New(_ => Task.FromResult(Empty(TestSessions.Running())),
            (_, _) => Task.FromException<SessionOutcome>(new InvalidOperationException("write failed")));
        await controller.OpenAsync(); await controller.StopAsync();
        Assert.NotNull(controller.Snapshot!.Running); Assert.NotNull(controller.Error); Assert.False(controller.IsStopping);
    }

    private static TodayController New(Func<CancellationToken, Task<TodaySnapshot>> read,
        Func<SessionId, CancellationToken, Task<SessionOutcome>>? stop = null) =>
        new(read, stop ?? ((_, _) => throw new InvalidOperationException("Unexpected Stop")), _ => { });

    private static TodaySnapshot Empty(SessionRecord? running = null) => new(new DateOnly(2026, 9, 2), TimeZoneInfo.Utc,
        TestSessions.Anchor, new DateTimeOffset(2026, 9, 3, 0, 0, 0, TimeSpan.Zero), [], running);
}
