using FocusKey.Foundation.Data;
using FocusKey.Foundation.Settings;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class SessionSoundsSettingTests
{
    [Fact]
    public void ApplicationSettings_Default_HasSessionSoundsEnabledTrue()
    {
        var settings = ApplicationSettings.Default;
        Assert.True(settings.SessionSoundsEnabled);
    }

    [Fact]
    public async Task SqliteSettingsRepository_SavesAndLoads_SessionSoundsSetting()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);

        // Verify initial loaded setting is true
        var initial = await repo.LoadAsync();
        Assert.True(initial.SessionSoundsEnabled);

        // Turn sounds off
        var disabled = initial with { SessionSoundsEnabled = false };
        await repo.SaveAsync(disabled);

        var reloadedDisabled = await repo.LoadAsync();
        Assert.False(reloadedDisabled.SessionSoundsEnabled);

        // Turn sounds back on
        var reenabled = reloadedDisabled with { SessionSoundsEnabled = true };
        await repo.SaveAsync(reenabled);

        var reloadedEnabled = await repo.LoadAsync();
        Assert.True(reloadedEnabled.SessionSoundsEnabled);
    }

    [Fact]
    public async Task SettingsPageController_SessionSounds_DispatchesUpdate()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var service = new SettingsService(new SqliteSettingsRepository(connections));
        var controller = new SettingsPageController(service, () => Task.CompletedTask, _ => Assert.Fail("Unexpected error"));

        await controller.LoadAsync();
        Assert.NotNull(controller.Saved);
        Assert.True(controller.Saved.SessionSoundsEnabled);

        bool soundsFired = false;
        ApplicationSettings? settledSettings = null;
        controller.Settled += (field, s) =>
        {
            if (field == SettingsField.SessionSounds)
            {
                soundsFired = true;
                settledSettings = s;
            }
        };

        await controller.UpdateSessionSoundsAsync(false);

        Assert.True(soundsFired);
        Assert.NotNull(settledSettings);
        Assert.False(settledSettings.SessionSoundsEnabled);
        Assert.False(controller.Saved.SessionSoundsEnabled);

        var loaded = await service.LoadAsync();
        Assert.False(loaded.SessionSoundsEnabled);

        // Update back to true
        await controller.UpdateSessionSoundsAsync(true);
        Assert.True(controller.Saved.SessionSoundsEnabled);

        var reloaded = await service.LoadAsync();
        Assert.True(reloaded.SessionSoundsEnabled);
    }
}
