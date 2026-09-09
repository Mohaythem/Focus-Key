using FocusKey.Foundation.Data;
using FocusKey.Foundation.Settings;
using Microsoft.Data.Sqlite;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class ThemePresetsTests
{
    [Fact]
    public void PresetsCatalog_ContainsFiveCuratedPresetsForBothThemes()
    {
        Assert.Equal(5, ThemePresets.LightPresets.Count);
        Assert.Equal(5, ThemePresets.DarkPresets.Count);
        Assert.Contains(ThemePresets.LightPresets, p => p.Id == "default");
        Assert.Contains(ThemePresets.DarkPresets, p => p.Id == "default");
    }

    [Theory]
    [InlineData("default", false)]
    [InlineData("studio", false)]
    [InlineData("nordic", false)]
    [InlineData("sand", false)]
    [InlineData("sage", false)]
    [InlineData("default", true)]
    [InlineData("carbon", true)]
    [InlineData("nordic", true)]
    [InlineData("espresso", true)]
    [InlineData("emerald", true)]
    public void Presets_HaveUniqueValidColorsAndCanBeFound(string id, bool isDark)
    {
        var preset = ThemePresets.FindPreset(id, isDark);
        Assert.Equal(id, preset.Id);
        Assert.Equal(isDark, preset.IsDark);
        Assert.True(HexColor.TryParse(preset.Palette.Background.Value, out _));
        Assert.True(HexColor.TryParse(preset.Palette.Foreground.Value, out _));
        Assert.True(HexColor.TryParse(preset.Palette.Accent.Value, out _));
    }

    [Fact]
    public void DetectPresetId_IdentifiesExactMatchAndCustom()
    {
        var defLight = ThemePresets.FindPreset("default", false);
        Assert.Equal("default", ThemePresets.DetectPresetId(
            defLight.Palette.Background, defLight.Palette.Foreground, defLight.Palette.Accent, false));

        Assert.Equal("custom", ThemePresets.DetectPresetId(
            HexColor.Parse("#123456"), HexColor.Parse("#654321"), HexColor.Parse("#AABBCC"), false));
    }

    [Fact]
    public void ThemePaletteDerive_ComputesCoherentContrastSurfaces()
    {
        var dark = ThemePalette.Derive(HexColor.Parse("#101010"), HexColor.Parse("#CCCCCC"), HexColor.Parse("#007ACC"), true);
        Assert.Equal("#101010", dark.Background.Value);
        Assert.Equal("#CCCCCC", dark.Foreground.Value);
        Assert.Equal("#007ACC", dark.Accent.Value);
        Assert.NotEqual(dark.Background, dark.Surface);
        Assert.NotEqual(dark.Background, dark.Border);

        var light = ThemePalette.Derive(HexColor.Parse("#F9F9F9"), HexColor.Parse("#101010"), HexColor.Parse("#007ACC"), false);
        Assert.Equal("#F9F9F9", light.Background.Value);
        Assert.Equal("#101010", light.Foreground.Value);
        Assert.Equal("#007ACC", light.Accent.Value);
        Assert.NotEqual(light.Background, light.Border);
    }

    [Fact]
    public async Task ThemeCustomization_PersistsAndSurvivesRestart()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var initial = await repo.LoadAsync();
        Assert.Equal("default", initial.LightTheme.Preset);
        Assert.Equal("default", initial.DarkTheme.Preset);

        var customized = initial with
        {
            LightTheme = new ThemeConfiguration
            {
                Preset = "custom",
                Background = HexColor.Parse("#F9F9F9"),
                Foreground = HexColor.Parse("#101010"),
                Accent = HexColor.Parse("#007ACC")
            },
            DarkTheme = new ThemeConfiguration
            {
                Preset = "nordic",
                Background = HexColor.Parse("#242933"),
                Foreground = HexColor.Parse("#ECEFF4"),
                Accent = HexColor.Parse("#88C0D0")
            }
        };
        await repo.SaveAsync(customized);

        var reloaded = await new SqliteSettingsRepository(connections).LoadAsync();
        Assert.Equal("custom", reloaded.LightTheme.Preset);
        Assert.Equal("#F9F9F9", reloaded.LightTheme.Background.Value);
        Assert.Equal("#101010", reloaded.LightTheme.Foreground.Value);
        Assert.Equal("#007ACC", reloaded.LightTheme.Accent.Value);

        Assert.Equal("nordic", reloaded.DarkTheme.Preset);
        Assert.Equal("#242933", reloaded.DarkTheme.Background.Value);
        Assert.Equal("#ECEFF4", reloaded.DarkTheme.Foreground.Value);
        Assert.Equal("#88C0D0", reloaded.DarkTheme.Accent.Value);

        // Work and Break colors must remain untouched
        Assert.Equal(initial.WorkColor, reloaded.WorkColor);
        Assert.Equal(initial.BreakColor, reloaded.BreakColor);
        Assert.Equal(initial.WorkDuration, reloaded.WorkDuration);
        Assert.Equal(initial.BreakDuration, reloaded.BreakDuration);
    }

    [Fact]
    public async Task AppearanceCoordinator_NotifiesPalettesChanged()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var service = new SettingsService(repo);
        var coordinator = new AppearanceCoordinator(service);
        await coordinator.InitializeAsync();

        bool fired = false;
        ThemePalette? observedLight = null;
        ThemePalette? observedDark = null;
        coordinator.PalettesChanged += (l, d) =>
        {
            fired = true;
            observedLight = l;
            observedDark = d;
        };

        var newLight = new ThemeConfiguration
        {
            Preset = "studio",
            Background = HexColor.Parse("#F9F9F9"),
            Foreground = HexColor.Parse("#111111"),
            Accent = HexColor.Parse("#0078D4")
        };
        await service.UpdateLightThemeAsync(newLight);
        await coordinator.RefreshAsync();

        Assert.True(fired);
        Assert.NotNull(observedLight);
        Assert.Equal("#F9F9F9", observedLight.Background.Value);
        Assert.Equal("#111111", observedLight.Foreground.Value);
        Assert.Equal("#0078D4", observedLight.Accent.Value);
    }
}
