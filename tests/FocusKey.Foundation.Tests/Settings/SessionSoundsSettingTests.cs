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
        Assert.True(settings.StartSoundEnabled);
        Assert.True(settings.CompletionSoundEnabled);
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
        Assert.True(initial.StartSoundEnabled);
        Assert.True(initial.CompletionSoundEnabled);

        // Turn master sounds off
        var disabled = initial with { SessionSoundsEnabled = false };
        await repo.SaveAsync(disabled);

        var reloadedDisabled = await repo.LoadAsync();
        Assert.False(reloadedDisabled.SessionSoundsEnabled);
        Assert.True(reloadedDisabled.StartSoundEnabled);
        Assert.True(reloadedDisabled.CompletionSoundEnabled);

        // Turn sounds back on
        var reenabled = reloadedDisabled with { SessionSoundsEnabled = true };
        await repo.SaveAsync(reenabled);

        var reloadedEnabled = await repo.LoadAsync();
        Assert.True(reloadedEnabled.SessionSoundsEnabled);
    }

    [Fact]
    public async Task SqliteSettingsRepository_SavesAndLoads_GranularSoundsSettings()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);

        var initial = await repo.LoadAsync();
        Assert.True(initial.StartSoundEnabled);
        Assert.True(initial.CompletionSoundEnabled);

        // Disable start sound only
        await repo.SaveAsync(initial with { StartSoundEnabled = false });
        var afterStartDisabled = await repo.LoadAsync();
        Assert.True(afterStartDisabled.SessionSoundsEnabled);
        Assert.False(afterStartDisabled.StartSoundEnabled);
        Assert.True(afterStartDisabled.CompletionSoundEnabled);

        // Disable completion sound as well
        await repo.SaveAsync(afterStartDisabled with { CompletionSoundEnabled = false });
        var afterBothDisabled = await repo.LoadAsync();
        Assert.True(afterBothDisabled.SessionSoundsEnabled);
        Assert.False(afterBothDisabled.StartSoundEnabled);
        Assert.False(afterBothDisabled.CompletionSoundEnabled);

        // Re-enable start sound only
        await repo.SaveAsync(afterBothDisabled with { StartSoundEnabled = true });
        var afterStartReenabled = await repo.LoadAsync();
        Assert.True(afterStartReenabled.SessionSoundsEnabled);
        Assert.True(afterStartReenabled.StartSoundEnabled);
        Assert.False(afterStartReenabled.CompletionSoundEnabled);
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

    [Fact]
    public async Task SettingsPageController_StartSound_DispatchesUpdate()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var service = new SettingsService(new SqliteSettingsRepository(connections));
        var controller = new SettingsPageController(service, () => Task.CompletedTask, _ => Assert.Fail("Unexpected error"));

        await controller.LoadAsync();
        Assert.True(controller.Saved!.StartSoundEnabled);

        bool startSoundFired = false;
        ApplicationSettings? settledSettings = null;
        controller.Settled += (field, s) =>
        {
            if (field == SettingsField.StartSound)
            {
                startSoundFired = true;
                settledSettings = s;
            }
        };

        await controller.UpdateStartSoundAsync(false);

        Assert.True(startSoundFired);
        Assert.NotNull(settledSettings);
        Assert.False(settledSettings.StartSoundEnabled);
        Assert.False(controller.Saved.StartSoundEnabled);

        var loaded = await service.LoadAsync();
        Assert.False(loaded.StartSoundEnabled);

        await controller.UpdateStartSoundAsync(true);
        Assert.True(controller.Saved.StartSoundEnabled);

        var reloaded = await service.LoadAsync();
        Assert.True(reloaded.StartSoundEnabled);
    }

    [Fact]
    public async Task SettingsPageController_CompletionSound_DispatchesUpdate()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var service = new SettingsService(new SqliteSettingsRepository(connections));
        var controller = new SettingsPageController(service, () => Task.CompletedTask, _ => Assert.Fail("Unexpected error"));

        await controller.LoadAsync();
        Assert.True(controller.Saved!.CompletionSoundEnabled);

        bool completionSoundFired = false;
        ApplicationSettings? settledSettings = null;
        controller.Settled += (field, s) =>
        {
            if (field == SettingsField.CompletionSound)
            {
                completionSoundFired = true;
                settledSettings = s;
            }
        };

        await controller.UpdateCompletionSoundAsync(false);

        Assert.True(completionSoundFired);
        Assert.NotNull(settledSettings);
        Assert.False(settledSettings.CompletionSoundEnabled);
        Assert.False(controller.Saved.CompletionSoundEnabled);

        var loaded = await service.LoadAsync();
        Assert.False(loaded.CompletionSoundEnabled);

        await controller.UpdateCompletionSoundAsync(true);
        Assert.True(controller.Saved.CompletionSoundEnabled);

        var reloaded = await service.LoadAsync();
        Assert.True(reloaded.CompletionSoundEnabled);
    }

    [Fact]
    public async Task SettingsService_UpdatesGranularSoundsAndSurvivesRestart()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var service = new SettingsService(new SqliteSettingsRepository(connections));
        await service.UpdateStartSoundEnabledAsync(false);
        await service.UpdateCompletionSoundEnabledAsync(false);

        var reopenedRepo = new SqliteSettingsRepository(new SqliteConnectionFactory(file));
        var loaded = await reopenedRepo.LoadAsync();
        Assert.False(loaded.StartSoundEnabled);
        Assert.False(loaded.CompletionSoundEnabled);
        Assert.True(loaded.SessionSoundsEnabled);
    }
}
