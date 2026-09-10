using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class AppearanceCoordinatorTests
{
    [Fact]
    public void CurrentRequiresInitialization()
    {
        using var store = new SessionStore();
        var coordinator = New(store);
        Assert.Throws<InvalidOperationException>(() => coordinator.Current);
    }

    [Fact]
    public async Task FreshDatabaseInitializesToSystemAndDoesNotRepublishUnchangedRefresh()
    {
        using var store = new SessionStore();
        var coordinator = New(store);
        var observed = new List<Appearance>();
        coordinator.Changed += observed.Add;

        Assert.Equal(Appearance.System, await coordinator.InitializeAsync());
        Assert.Equal(Appearance.System, await coordinator.RefreshAsync());

        Assert.Equal(Appearance.System, coordinator.Current);
        Assert.Equal([Appearance.System], observed);
    }

    [Theory]
    [InlineData(Appearance.System)]
    [InlineData(Appearance.Light)]
    [InlineData(Appearance.Dark)]
    public async Task PersistedValueControlsStartupAndSurvivesRestart(Appearance appearance)
    {
        using var store = new SessionStore();
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        await settings.UpdateAppearanceAsync(appearance);

        var restarted = new AppearanceCoordinator(new SettingsService(
            new SqliteSettingsRepository(new Foundation.Data.SqliteConnectionFactory(store.DatabaseFile))));

        Assert.Equal(appearance, await restarted.InitializeAsync());
        Assert.Equal(appearance, restarted.Current);
    }

    [Fact]
    public async Task RuntimeUpdatesPublishInOrderAndPersistAllSupportedValues()
    {
        using var store = new SessionStore();
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        var coordinator = new AppearanceCoordinator(settings);
        await coordinator.InitializeAsync();
        var observed = new List<Appearance>();
        coordinator.Changed += observed.Add;

        await coordinator.UpdateAsync(Appearance.Light);
        await coordinator.UpdateAsync(Appearance.Dark);
        await coordinator.UpdateAsync(Appearance.System);

        Assert.Equal([Appearance.Light, Appearance.Dark, Appearance.System], observed);
        Assert.Equal(Appearance.System, coordinator.Current);
        Assert.Equal(Appearance.System, (await settings.LoadAsync()).Appearance);
    }

    [Fact]
    public async Task RefreshObservesExternalPersistedChangeAtRuntime()
    {
        using var store = new SessionStore();
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        var coordinator = new AppearanceCoordinator(settings);
        await coordinator.InitializeAsync();
        Appearance? observed = null;
        coordinator.Changed += value => observed = value;

        await new SettingsService(new SqliteSettingsRepository(store.Connections))
            .UpdateAppearanceAsync(Appearance.Dark);
        await coordinator.RefreshAsync();

        Assert.Equal(Appearance.Dark, observed);
        Assert.Equal(Appearance.Dark, coordinator.Current);
    }

    [Fact]
    public async Task UnrelatedSettingsChangeDoesNotPublishAppearance()
    {
        using var store = new SessionStore();
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        var coordinator = new AppearanceCoordinator(settings);
        await coordinator.InitializeAsync();
        int notifications = 0;
        coordinator.Changed += _ => notifications++;

        await settings.UpdateWorkDurationAsync(TimeSpan.FromMinutes(48));
        await coordinator.RefreshAsync();

        Assert.Equal(0, notifications);
        Assert.Equal(Appearance.System, coordinator.Current);
    }

    [Fact]
    public async Task AppearanceUpdatePreservesDurationsAndColors()
    {
        using var store = new SessionStore();
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        var expected = ApplicationSettings.Default with
        {
            WorkDuration = TimeSpan.FromMinutes(41),
            BreakDuration = TimeSpan.FromMinutes(7),
            WorkColor = HexColor.Parse("#102030"),
            BreakColor = HexColor.Parse("#A0B0C0"),
        };
        await settings.SaveAsync(expected);
        var coordinator = new AppearanceCoordinator(settings);
        await coordinator.InitializeAsync();

        await coordinator.UpdateAsync(Appearance.Dark);

        Assert.Equal(expected with { Appearance = Appearance.Dark }, await settings.LoadAsync());
    }

    [Fact]
    public async Task InvalidAndCanceledUpdatesPreserveRuntimeAndPersistence()
    {
        using var store = new SessionStore();
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        var coordinator = new AppearanceCoordinator(settings);
        await coordinator.InitializeAsync();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => coordinator.UpdateAsync((Appearance)99));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.UpdateAsync(Appearance.Dark, canceled.Token));

        Assert.Equal(Appearance.System, coordinator.Current);
        Assert.Equal(Appearance.System, (await settings.LoadAsync()).Appearance);
    }

    [Fact]
    public async Task CoordinatorReflectsAndTracksContrast()
    {
        using var store = new SessionStore();
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        var coordinator = new AppearanceCoordinator(settings);
        await coordinator.InitializeAsync();

        Assert.Equal(Contrast.Standard, coordinator.Contrast);

        await settings.UpdateContrastAsync(Contrast.HigherContrast);
        await coordinator.RefreshAsync();

        Assert.Equal(Contrast.HigherContrast, coordinator.Contrast);
    }

    private static AppearanceCoordinator New(SessionStore store) =>
        new(new SettingsService(new SqliteSettingsRepository(store.Connections)));
}
