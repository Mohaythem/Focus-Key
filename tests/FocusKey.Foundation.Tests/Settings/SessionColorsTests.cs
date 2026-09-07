using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class SessionColorsTests
{
    [Theory]
    [InlineData("#183739", "#FFFFFF")]
    [InlineData("#434763", "#FFFFFF")]
    [InlineData("#000000", "#FFFFFF")]
    [InlineData("#FFFFFF", "#000000")]
    [InlineData("#FFFF00", "#000000")]
    [InlineData("#0000FF", "#FFFFFF")]
    [InlineData("#777777", "#000000")]
    public void ForegroundChoosesReadableOpaqueText(string background, string foreground) =>
        Assert.Equal(foreground, SessionColors.Foreground(HexColor.Parse(background)).Value);

    [Fact]
    public void UninitializedColorIsRejected() =>
        Assert.Throws<ArgumentException>(() => SessionColors.Foreground(default));

    [Fact]
    public async Task DefaultsAndUnchangedRefresh()
    {
        using var store = new SessionStore();
        var coordinator = new AppearanceCoordinator(new SettingsService(new SqliteSettingsRepository(store.Connections)));
        Assert.Throws<InvalidOperationException>(() => coordinator.Colors);
        var events = new List<SessionColors>();
        coordinator.ColorsChanged += events.Add;
        await coordinator.InitializeAsync();
        await coordinator.RefreshAsync();
        Assert.Equal(new SessionColors(HexColor.Parse("#183739"), HexColor.Parse("#434763")), coordinator.Colors);
        Assert.Single(events);
    }

    [Theory]
    [InlineData(Appearance.System)]
    [InlineData(Appearance.Light)]
    [InlineData(Appearance.Dark)]
    public async Task ColorsRefreshIndependentlyAndSurviveRestart(Appearance appearance)
    {
        using var store = new SessionStore();
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        var coordinator = new AppearanceCoordinator(settings);
        await coordinator.InitializeAsync();
        await coordinator.UpdateAsync(appearance);
        int appearanceEvents = 0;
        var colors = new List<SessionColors>();
        coordinator.Changed += _ => appearanceEvents++;
        coordinator.ColorsChanged += colors.Add;
        await settings.UpdateWorkColorAsync(HexColor.Parse("#abcdef"));
        await coordinator.RefreshAsync();
        await settings.UpdateBreakColorAsync(HexColor.Parse("#010203"));
        await coordinator.RefreshAsync();
        Assert.Equal(2, colors.Count);
        Assert.Equal(0, appearanceEvents);
        Assert.Equal(appearance, coordinator.Current);
        Assert.Equal("#ABCDEF", coordinator.Colors.Work.Value);
        Assert.Equal("#010203", coordinator.Colors.Break.Value);
        var restarted = new AppearanceCoordinator(new SettingsService(new SqliteSettingsRepository(
            new Foundation.Data.SqliteConnectionFactory(store.DatabaseFile))));
        await restarted.InitializeAsync();
        Assert.Equal(coordinator.Colors, restarted.Colors);
        Assert.Equal(appearance, restarted.Current);
        Assert.Equal(ApplicationSettings.Default.WorkDuration, (await settings.LoadAsync()).WorkDuration);
        await coordinator.UpdateAsync(Appearance.System);
        Assert.Equal(2, colors.Count);
    }

    [Fact]
    public async Task CanceledRefreshDoesNotPublishUnobservedColors()
    {
        using var store = new SessionStore();
        var settings = new SettingsService(new SqliteSettingsRepository(store.Connections));
        var coordinator = new AppearanceCoordinator(settings);
        await coordinator.InitializeAsync();
        var original = coordinator.Colors;
        await settings.UpdateWorkColorAsync(HexColor.Parse("#FFFFFF"));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.RefreshAsync(canceled.Token));
        Assert.Equal(original, coordinator.Colors);
        await coordinator.RefreshAsync();
        Assert.Equal("#FFFFFF", coordinator.Colors.Work.Value);
    }
}
