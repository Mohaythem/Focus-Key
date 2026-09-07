using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class RuntimeDurationSettingsTests
{
    [Fact]
    public async Task FreshPersistedDefaultsDriveNewWorkAndBreakSessions()
    {
        using var store = new SessionStore();
        SessionCoordinator sessions = CreateCoordinator(store, new ManualTimeProvider(TestSessions.Anchor));
        await sessions.InitializeAsync();

        SessionRecord work = await sessions.StartAsync(SessionType.Work);
        Assert.Equal(TimeSpan.FromMinutes(30), work.PlannedDuration);
        await sessions.StopAsync();
        SessionRecord rest = await sessions.StartAsync(SessionType.Break);
        Assert.Equal(TimeSpan.FromMinutes(10), rest.PlannedDuration);
    }

    [Fact]
    public async Task PersistedCustomDurationsDriveBothSessionTypesAndSurviveRestart()
    {
        using var store = new SessionStore();
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        await settings.UpdateWorkDurationAsync(TimeSpan.FromMinutes(47));
        await settings.UpdateBreakDurationAsync(TimeSpan.FromSeconds(725));

        var reopenedSettings = new SettingsService(new SqliteSettingsRepository(
            new Foundation.Data.SqliteConnectionFactory(store.DatabaseFile)));
        var sessions = new SessionCoordinator(store.ReopenRepository(),
            new ManualTimeProvider(TestSessions.Anchor),
            durationProvider: new SettingsSessionDurationProvider(reopenedSettings));
        await sessions.InitializeAsync();

        SessionRecord work = await sessions.StartAsync(SessionType.Work);
        Assert.Equal(TimeSpan.FromMinutes(47), work.PlannedDuration);
        await sessions.StopAsync();
        SessionRecord rest = await sessions.StartAsync(SessionType.Break);
        Assert.Equal(TimeSpan.FromSeconds(725), rest.PlannedDuration);
    }

    [Fact]
    public async Task RuntimeChangeAffectsOnlyFutureSessionsAndPreservesHistory()
    {
        using var store = new SessionStore();
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        await settings.UpdateWorkDurationAsync(TimeSpan.FromMinutes(40));
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock,
            durationProvider: new SettingsSessionDurationProvider(settings));
        await sessions.InitializeAsync();

        SessionRecord first = await sessions.StartAsync(SessionType.Work);
        await settings.UpdateWorkDurationAsync(TimeSpan.FromMinutes(55));

        SessionSnapshot active = (await sessions.GetActiveAsync())!;
        Assert.Equal(first.Id, active.Id);
        Assert.Equal(TimeSpan.FromMinutes(40), active.PlannedDuration);
        Assert.Equal(TimeSpan.FromMinutes(40), (await store.Repository.GetAsync(first.Id))!.PlannedDuration);

        await sessions.StopAsync();
        SessionRecord second = await sessions.StartAsync(SessionType.Work);
        Assert.Equal(TimeSpan.FromMinutes(55), second.PlannedDuration);
        Assert.Equal(TimeSpan.FromMinutes(40), (await store.Repository.GetAsync(first.Id))!.PlannedDuration);
    }

    [Fact]
    public async Task RuntimeBreakUpdateIsVisibleWithoutRestart()
    {
        using var store = new SessionStore();
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        var sessions = new SessionCoordinator(store.Repository, new ManualTimeProvider(TestSessions.Anchor),
            durationProvider: new SettingsSessionDurationProvider(settings));
        await sessions.InitializeAsync();

        Assert.Equal(SessionDurations.Default, await sessions.GetDurationsAsync());
        await settings.UpdateBreakDurationAsync(TimeSpan.FromMinutes(6));

        Assert.Equal(TimeSpan.FromMinutes(6), (await sessions.GetDurationsAsync()).Break);
        Assert.Equal(TimeSpan.FromMinutes(6), (await sessions.StartAsync(SessionType.Break)).PlannedDuration);
    }

    [Fact]
    public async Task CustomDurationControlsExactAndDelayedCompletionTime()
    {
        using var store = new SessionStore();
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        await settings.UpdateWorkDurationAsync(TimeSpan.FromSeconds(17));
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock,
            durationProvider: new SettingsSessionDurationProvider(settings));
        await sessions.InitializeAsync();

        SessionRecord started = await sessions.StartAsync(SessionType.Work);
        clock.Advance(TimeSpan.FromSeconds(16));
        Assert.Equal(SessionOutcomeKind.StillRunning, (await sessions.CompleteIfDueAsync()).Kind);
        clock.Advance(TimeSpan.FromMinutes(3));
        SessionOutcome completed = await sessions.CompleteIfDueAsync();

        Assert.Equal(SessionOutcomeKind.Completed, completed.Kind);
        Assert.Equal(started.PlannedEndAt, completed.Session!.EndedAt);
        Assert.Equal(TestSessions.Anchor.AddSeconds(17), completed.Session.EndedAt);
    }

    [Fact]
    public async Task CanceledOrFailedDurationReadDoesNotCreateSession()
    {
        using var store = new SessionStore();
        var failed = new SessionCoordinator(store.Repository, new ManualTimeProvider(TestSessions.Anchor),
            durationProvider: new FailingDurationProvider());
        await failed.InitializeAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => failed.StartAsync(SessionType.Work));
        Assert.Null(await store.Repository.GetRunningAsync());

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => failed.StartAsync(SessionType.Work, canceled.Token));
        Assert.Null(await store.Repository.GetRunningAsync());
    }

    private static SessionCoordinator CreateCoordinator(SessionStore store, TimeProvider clock)
    {
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        return new SessionCoordinator(store.Repository, clock,
            durationProvider: new SettingsSessionDurationProvider(settings));
    }

    private sealed class FailingDurationProvider : ISessionDurationProvider
    {
        public Task<SessionDurations> GetDurationsAsync(CancellationToken cancellationToken = default) =>
            Task.FromException<SessionDurations>(new InvalidOperationException("duration read failed"));
    }
}
