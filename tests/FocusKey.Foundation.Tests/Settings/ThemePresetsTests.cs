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
        Assert.Contains(ThemePresets.DarkPresets, p => p.Id == "carbon");
        Assert.Equal("carbon", ThemePresets.DarkPresets[0].Id);
        Assert.Equal("Carbon Studio", ThemePresets.DarkPresets[0].DisplayName);
    }

    [Theory]
    [InlineData("default", false)]
    [InlineData("studio", false)]
    [InlineData("nordic", false)]
    [InlineData("sand", false)]
    [InlineData("sage", false)]
    [InlineData("carbon", true)]
    [InlineData("obsidian", true)]
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
    public void DefaultLightPreset_DimColor_MeetsWcagAaContrastRequirements()
    {
        var defaultLight = ThemePresets.LightPresets[0].Palette;
        Assert.Equal("#5C6E6E", defaultLight.Dim.Value);

        double Luminance(HexColor hex)
        {
            double Channel(int offset)
            {
                double value = Convert.ToByte(hex.Value.Substring(offset, 2), 16) / 255.0;
                return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        }

        double Contrast(HexColor c1, HexColor c2)
        {
            double l1 = Luminance(c1);
            double l2 = Luminance(c2);
            return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
        }

        // Contrast against App Background (#F2F5F5) must exceed 4.5:1 (WCAG AA)
        double crBackground = Contrast(defaultLight.Background, defaultLight.Dim);
        Assert.True(crBackground >= 4.5, $"Dim on Background contrast {crBackground:F2} must be >= 4.5");

        // Contrast against Card Surface (#FFFFFF) must exceed 4.5:1 (WCAG AA)
        double crSurface = Contrast(defaultLight.Surface, defaultLight.Dim);
        Assert.True(crSurface >= 4.5, $"Dim on Surface contrast {crSurface:F2} must be >= 4.5");

        // Contrast against Secondary Surface (#F7FAFA) must exceed 4.5:1 (WCAG AA)
        double crSurface2 = Contrast(defaultLight.Surface2, defaultLight.Dim);
        Assert.True(crSurface2 >= 4.5, $"Dim on Surface2 contrast {crSurface2:F2} must be >= 4.5");
    }

    [Fact]
    public void FindPreset_DefaultDarkReturnsCarbonStudio()
    {
        var preset = ThemePresets.FindPreset("default", true);
        Assert.Equal("carbon", preset.Id);
        Assert.Equal("Carbon Studio", preset.DisplayName);
    }

    [Fact]
    public void DetectPresetId_IdentifiesExactMatchAndCustom()
    {
        var defLight = ThemePresets.FindPreset("default", false);
        Assert.Equal("default", ThemePresets.DetectPresetId(
            defLight.Palette.Background, defLight.Palette.Foreground, defLight.Palette.Accent, false));

        var carbonDark = ThemePresets.FindPreset("carbon", true);
        Assert.Equal("carbon", ThemePresets.DetectPresetId(
            carbonDark.Palette.Background, carbonDark.Palette.Foreground, carbonDark.Palette.Accent, true));

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
    public void ThemePaletteApplyContrast_EnhancesBordersAndTextClarity()
    {
        var carbon = ThemePresets.DarkPresets[0].Palette;
        var highContrastDark = ThemePalette.ApplyContrast(carbon, Contrast.HigherContrast, true);

        Assert.Equal("#FFFFFF", highContrastDark.Foreground.Value);
        Assert.NotEqual(carbon.Border, highContrastDark.Border);
        Assert.NotEqual(carbon.Secondary, highContrastDark.Secondary);

        var light = ThemePresets.LightPresets[0].Palette;
        var highContrastLight = ThemePalette.ApplyContrast(light, Contrast.HigherContrast, false);

        Assert.Equal("#000000", highContrastLight.Foreground.Value);
        Assert.NotEqual(light.Border, highContrastLight.Border);
        Assert.NotEqual(light.Secondary, highContrastLight.Secondary);
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
        Assert.Equal("carbon", initial.DarkTheme.Preset);
        Assert.Equal("#121212", initial.DarkTheme.Background.Value);
        Assert.Equal(Contrast.Standard, initial.Contrast);

        var customized = initial with
        {
            Contrast = Contrast.HigherContrast,
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
        Assert.Equal(Contrast.HigherContrast, reloaded.Contrast);
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
    public async Task AppearanceCoordinator_NotifiesPalettesChangedOnContrast()
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
        ThemePalette? observedDark = null;
        coordinator.PalettesChanged += (_, d) =>
        {
            fired = true;
            observedDark = d;
        };

        await service.UpdateContrastAsync(Contrast.HigherContrast);
        await coordinator.RefreshAsync();

        Assert.True(fired);
        Assert.NotNull(observedDark);
        Assert.Equal("#FFFFFF", observedDark.Foreground.Value);
    }

    [Fact]
    public async Task SqliteSettingsRepository_PreservesExplicitDarkCustomizationWhileMigratingUncustomized()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        // 1. Manually simulate an uncustomized legacy database at version 3
        await using (var conn = await connections.OpenConnectionAsync())
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version INTEGER NOT NULL PRIMARY KEY,
                    name TEXT NOT NULL,
                    applied_at_utc TEXT NOT NULL
                );
                INSERT INTO schema_migrations VALUES (1, 'schema_metadata', '2026-09-01T00:00:00.0000000Z');
                INSERT INTO schema_migrations VALUES (2, 'sessions', '2026-09-01T00:00:00.0000000Z');
                INSERT INTO schema_migrations VALUES (3, 'application_settings', '2026-09-01T00:00:00.0000000Z');
                PRAGMA user_version = 3;

                CREATE TABLE IF NOT EXISTS application_settings (
                    singleton INTEGER NOT NULL PRIMARY KEY,
                    work_duration_seconds INTEGER NOT NULL,
                    break_duration_seconds INTEGER NOT NULL,
                    appearance TEXT NOT NULL,
                    work_color TEXT NOT NULL,
                    break_color TEXT NOT NULL
                );
                INSERT INTO application_settings VALUES (1, 1800, 600, 'system', '#183739', '#434763');

                CREATE TABLE IF NOT EXISTS theme_settings (
                    singleton INTEGER NOT NULL PRIMARY KEY CHECK (singleton = 1),
                    light_preset TEXT NOT NULL, light_background TEXT NOT NULL, light_foreground TEXT NOT NULL, light_accent TEXT NOT NULL,
                    dark_preset TEXT NOT NULL, dark_background TEXT NOT NULL, dark_foreground TEXT NOT NULL, dark_accent TEXT NOT NULL,
                    contrast TEXT NOT NULL DEFAULT 'standard'
                );
                INSERT OR REPLACE INTO theme_settings (
                    singleton, light_preset, light_background, light_foreground, light_accent,
                    dark_preset, dark_background, dark_foreground, dark_accent, contrast)
                VALUES (1, 'default', '#F2F5F5', '#0F1414', '#183739', 'default', '#0A0D0D', '#F0F4F4', '#2D6669', 'standard');
                """;
            await cmd.ExecuteNonQueryAsync();
        }

        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var loaded = await repo.LoadAsync();
        // Uncustomized legacy default dark must be migrated to carbon
        Assert.Equal("carbon", loaded.DarkTheme.Preset);
        Assert.Equal("#121212", loaded.DarkTheme.Background.Value);
        Assert.Equal("#E0E0E0", loaded.DarkTheme.Foreground.Value);
        Assert.Equal("#4CC2FF", loaded.DarkTheme.Accent.Value);

        // 2. Now explicitly customize dark theme to espresso
        var customized = loaded with
        {
            DarkTheme = new ThemeConfiguration
            {
                Preset = "espresso",
                Background = HexColor.Parse("#141210"),
                Foreground = HexColor.Parse("#EDE5DE"),
                Accent = HexColor.Parse("#D97736")
            }
        };
        await repo.SaveAsync(customized);

        // Reload to verify explicit choice is strictly preserved
        var reloaded = await new SqliteSettingsRepository(connections).LoadAsync();
        Assert.Equal("espresso", reloaded.DarkTheme.Preset);
        Assert.Equal("#141210", reloaded.DarkTheme.Background.Value);
    }

    [Theory]
    [InlineData("nordic", "#242933", "#ECEFF4", "#88C0D0")]
    [InlineData("espresso", "#141210", "#EDE5DE", "#D97736")]
    [InlineData("emerald", "#0B120E", "#E2EDE6", "#40916C")]
    [InlineData("obsidian", "#0A0D0D", "#F0F4F4", "#2D6669")]
    public void ResolvePalette_WhenSwitchingDarkPreset_AdoptsNewPresetColorsCompletely(
        string presetId, string expectedBg, string expectedFg, string expectedAccent)
    {
        var preset = ThemePresets.FindPreset(presetId, true);
        var config = new ThemeConfiguration
        {
            Preset = preset.Id,
            Background = preset.Palette.Background,
            Foreground = preset.Palette.Foreground,
            Accent = preset.Palette.Accent
        };

        var resolved = config.ResolvePalette(true);
        Assert.Equal(expectedBg, resolved.Background.Value);
        Assert.Equal(expectedFg, resolved.Foreground.Value);
        Assert.Equal(expectedAccent, resolved.Accent.Value);
        Assert.NotEqual("#121212", resolved.Background.Value);
        Assert.Equal(preset.Palette.Sidebar, resolved.Sidebar);
        Assert.Equal(preset.Palette.Surface, resolved.Surface);
        Assert.Equal(preset.Palette.Border, resolved.Border);
    }

    [Fact]
    public void ResolvePalette_CustomDarkBackground_DerivesAllSurfacesFromCustomColor()
    {
        var customBg = HexColor.Parse("#331122");
        var customFg = HexColor.Parse("#F0E0E8");
        var customAccent = HexColor.Parse("#FF4488");
        var config = new ThemeConfiguration
        {
            Preset = "custom",
            Background = customBg,
            Foreground = customFg,
            Accent = customAccent
        };

        var resolved = config.ResolvePalette(true);
        Assert.Equal("#331122", resolved.Background.Value);
        Assert.Equal("#F0E0E8", resolved.Foreground.Value);
        Assert.Equal("#FF4488", resolved.Accent.Value);

        // Surfaces must not be Carbon Studio (#121212, #181818, etc.)
        Assert.NotEqual("#121212", resolved.Background.Value);
        Assert.NotEqual("#181818", resolved.Sidebar.Value);
        Assert.NotEqual("#1E1E1E", resolved.Surface.Value);
        Assert.NotEqual("#2E2E2E", resolved.Border.Value);

        // Higher contrast on custom palette derives from custom background
        var highContrast = config.ResolvePalette(true, Contrast.HigherContrast);
        Assert.Equal("#331122", highContrast.Background.Value);
        Assert.Equal("#FFFFFF", highContrast.Foreground.Value);
        Assert.NotEqual(resolved.Border, highContrast.Border);
    }
}
