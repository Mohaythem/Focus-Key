using FocusKey.Foundation.MiniTimer;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.MiniTimer;

public sealed class MiniTimerControllerTests
{
    [Theory]
    [InlineData(SessionType.Work, 30)]
    [InlineData(SessionType.Break, 10)]
    public async Task TimestampDisplayDoesNotPollOrComplete(SessionType type, int minutes)
    {
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var record = TestSessions.Running(type: type, plannedDuration: TimeSpan.FromMinutes(minutes));
        int reads = 0;
        using var view = new MiniTimerController(_ => { reads++; return Task.FromResult<SessionSnapshot?>(SessionSnapshot.For(record, clock.GetUtcNow())); }, _ => Assert.Fail(), clock);
        Assert.False(view.IsVisible);
        await view.OpenAsync();
        Assert.Equal($"{type} · {minutes:00}:00", view.Text);
        clock.Advance(TimeSpan.FromSeconds(65.2));
        Assert.Equal(TimeSpan.FromSeconds(minutes * 60 - 65.2), view.Remaining);
        Assert.Equal($"{type} · {minutes - 2:00}:55", view.Text);
        clock.Advance(TimeSpan.FromHours(4));
        Assert.Equal($"{type} · 00:00", view.Text);
        Assert.NotNull(view.Session); // Completion belongs to the coordinator, not this projection.
        Assert.Equal(1, reads);
        Assert.Equal(SessionStatus.Running, record.Status);
    }

    [Fact]
    public async Task RealLifecycleCustomDurationsStopCompleteAndRecovery()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        await settings.UpdateWorkDurationAsync(TimeSpan.FromSeconds(75));
        await settings.UpdateBreakDurationAsync(TimeSpan.FromSeconds(12));
        var sessions = new SessionCoordinator(store.Repository, clock, durationProvider: new SettingsSessionDurationProvider(settings));
        await sessions.InitializeAsync();
        using var view = new MiniTimerController(sessions.GetActiveAsync, _ => Assert.Fail(), clock);
        await view.OpenAsync();
        Assert.Equal("No active session", view.Text);
        var work = await sessions.StartAsync(SessionType.Work);
        await view.RefreshAsync();
        Assert.Equal("Work · 01:15", view.Text);
        await settings.UpdateWorkDurationAsync(TimeSpan.FromMinutes(9));
        Assert.Equal("Work · 01:15", view.Text);
        clock.Advance(TimeSpan.FromSeconds(3));
        await sessions.StopAsync(work.Id);
        await view.RefreshAsync();
        Assert.Null(view.Session);
        var rest = await sessions.StartAsync(SessionType.Break);
        await view.RefreshAsync();
        Assert.Equal("Break · 00:12", view.Text);
        clock.Advance(TimeSpan.FromSeconds(20));
        Assert.Equal("Break · 00:00", view.Text);
        await sessions.CompleteIfDueAsync();
        await view.RefreshAsync();
        Assert.Equal("No active session", view.Text);
        Assert.Equal(rest.PlannedEndAt, (await store.Repository.GetAsync(rest.Id))!.EndedAt);
        await sessions.StartAsync(SessionType.Work);
        var restarted = new SessionCoordinator(store.ReopenRepository(), clock);
        await restarted.InitializeAsync();
        using var reopened = new MiniTimerController(restarted.GetActiveAsync, _ => Assert.Fail(), clock);
        await reopened.OpenAsync();
        Assert.Null(reopened.Session);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HideAndDisposeDiscardPendingRead(bool dispose)
    {
        var pending = new TaskCompletionSource<SessionSnapshot?>();
        using var view = new MiniTimerController(_ => pending.Task, _ => Assert.Fail());
        Task opening = view.OpenAsync();
        if (dispose) view.Dispose(); else view.Hide();
        pending.SetResult(SessionSnapshot.For(TestSessions.Running(), TestSessions.Anchor));
        await opening;
        Assert.Null(view.Session);
        Assert.False(view.IsVisible);
        Assert.False(view.IsLoading);
    }

    [Fact]
    public async Task ReopenLoadsFreshAndHiddenRefreshDoesNotRead()
    {
        int reads = 0;
        using var view = new MiniTimerController(_ => { reads++; return Task.FromResult<SessionSnapshot?>(null); }, _ => Assert.Fail());
        await view.RefreshAsync();
        Assert.Equal(0, reads);
        await view.OpenAsync(); view.Hide(); await view.RefreshAsync(); await view.OpenAsync();
        Assert.Equal(2, reads);
        view.Dispose(); await view.OpenAsync();
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task OlderRefreshCannotOverwriteNewSession()
    {
        var pending = new TaskCompletionSource<SessionSnapshot?>();
        int reads = 0;
        var newer = SessionSnapshot.For(TestSessions.Running(type: SessionType.Break), TestSessions.Anchor);
        using var view = new MiniTimerController(_ => ++reads == 1 ? pending.Task : Task.FromResult<SessionSnapshot?>(newer), _ => Assert.Fail());
        Task first = view.OpenAsync();
        await view.RefreshAsync();
        pending.SetResult(null);
        await first;
        Assert.Equal(newer, view.Session);
    }

    [Fact]
    public async Task ReadFailureClearsStaleStateAndReopenRetries()
    {
        bool fail = false;
        int errors = 0;
        using var view = new MiniTimerController(_ => fail ? Task.FromException<SessionSnapshot?>(new IOException()) :
            Task.FromResult<SessionSnapshot?>(SessionSnapshot.For(TestSessions.Running(), TestSessions.Anchor)), _ => errors++);
        await view.OpenAsync(); fail = true; await view.RefreshAsync();
        Assert.Null(view.Session); Assert.Contains("retry", view.Text); Assert.Equal(1, errors);
        fail = false; await view.OpenAsync(); Assert.Null(view.Error); Assert.NotNull(view.Session);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(0.1, "00:01")]
    [InlineData(3601, "60:01")]
    [InlineData(-1, "00:00")]
    public void CountdownFormatting(double seconds, string expected) =>
        Assert.Equal(expected, MiniTimerController.Format(TimeSpan.FromSeconds(seconds)));
}
