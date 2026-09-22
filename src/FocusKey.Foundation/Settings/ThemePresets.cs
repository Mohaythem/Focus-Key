namespace FocusKey.Foundation.Settings;

public sealed record ThemePreset
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsDark { get; init; }
    public required ThemePalette Palette { get; init; }
}

public static class ThemePresets
{
    public static IReadOnlyList<ThemePreset> LightPresets { get; } =
    [
        new ThemePreset
        {
            Id = "default",
            DisplayName = "Default Light (Slate)",
            IsDark = false,
            Palette = new ThemePalette
            {
                Background = HexColor.Parse("#F2F5F5"),
                Foreground = HexColor.Parse("#0F1414"),
                Accent = HexColor.Parse("#183739"),
                Sidebar = HexColor.Parse("#E6ECEC"),
                Surface = HexColor.Parse("#FFFFFF"),
                Surface2 = HexColor.Parse("#F7FAFA"),
                Border = HexColor.Parse("#D4DCDC"),
                Secondary = HexColor.Parse("#5A6A6A"),
                Dim = HexColor.Parse("#5C6E6E")
            }
        },
        new ThemePreset
        {
            Id = "studio",
            DisplayName = "Studio (Minimalist)",
            IsDark = false,
            Palette = new ThemePalette
            {
                Background = HexColor.Parse("#F9F9F9"),
                Foreground = HexColor.Parse("#111111"),
                Accent = HexColor.Parse("#0078D4"),
                Sidebar = HexColor.Parse("#F0F0F0"),
                Surface = HexColor.Parse("#FFFFFF"),
                Surface2 = HexColor.Parse("#F5F5F5"),
                Border = HexColor.Parse("#E0E0E0"),
                Secondary = HexColor.Parse("#5C5C5C"),
                Dim = HexColor.Parse("#8C8C8C")
            }
        },
        new ThemePreset
        {
            Id = "nordic",
            DisplayName = "Nordic Frost",
            IsDark = false,
            Palette = new ThemePalette
            {
                Background = HexColor.Parse("#ECEFF4"),
                Foreground = HexColor.Parse("#2E3440"),
                Accent = HexColor.Parse("#5E81AC"),
                Sidebar = HexColor.Parse("#E5E9F0"),
                Surface = HexColor.Parse("#FFFFFF"),
                Surface2 = HexColor.Parse("#F0F3F7"),
                Border = HexColor.Parse("#D8DEE9"),
                Secondary = HexColor.Parse("#4C566A"),
                Dim = HexColor.Parse("#7B88A1")
            }
        },
        new ThemePreset
        {
            Id = "sand",
            DisplayName = "Warm Sand",
            IsDark = false,
            Palette = new ThemePalette
            {
                Background = HexColor.Parse("#F7F5F0"),
                Foreground = HexColor.Parse("#24211E"),
                Accent = HexColor.Parse("#A85A2A"),
                Sidebar = HexColor.Parse("#EDE9E1"),
                Surface = HexColor.Parse("#FFFFFF"),
                Surface2 = HexColor.Parse("#FAF8F5"),
                Border = HexColor.Parse("#DDD8CE"),
                Secondary = HexColor.Parse("#6B645C"),
                Dim = HexColor.Parse("#9C948A")
            }
        },
        new ThemePreset
        {
            Id = "sage",
            DisplayName = "Sage Botanical",
            IsDark = false,
            Palette = new ThemePalette
            {
                Background = HexColor.Parse("#F0F4F1"),
                Foreground = HexColor.Parse("#142018"),
                Accent = HexColor.Parse("#2D6A4F"),
                Sidebar = HexColor.Parse("#E2ECE5"),
                Surface = HexColor.Parse("#FFFFFF"),
                Surface2 = HexColor.Parse("#F5F9F6"),
                Border = HexColor.Parse("#CDDDD2"),
                Secondary = HexColor.Parse("#4E6B56"),
                Dim = HexColor.Parse("#7D9B86")
            }
        },
    ];

    public static IReadOnlyList<ThemePreset> DarkPresets { get; } =
    [
        new ThemePreset
        {
            Id = "carbon",
            DisplayName = "Carbon Studio",
            IsDark = true,
            Palette = new ThemePalette
            {
                Background = HexColor.Parse("#121212"),
                Foreground = HexColor.Parse("#E0E0E0"),
                Accent = HexColor.Parse("#4CC2FF"),
                Sidebar = HexColor.Parse("#181818"),
                Surface = HexColor.Parse("#1E1E1E"),
                Surface2 = HexColor.Parse("#252525"),
                Border = HexColor.Parse("#363636"),
                Secondary = HexColor.Parse("#A0A0A0"),
                Dim = HexColor.Parse("#6E6E6E")
            }
        },
        new ThemePreset
        {
            Id = "obsidian",
            DisplayName = "Obsidian Dark",
            IsDark = true,
            Palette = new ThemePalette
            {
                Background = HexColor.Parse("#0A0D0D"),
                Foreground = HexColor.Parse("#F0F4F4"),
                Accent = HexColor.Parse("#2D6669"),
                Sidebar = HexColor.Parse("#0E1212"),
                Surface = HexColor.Parse("#0E1212"),
                Surface2 = HexColor.Parse("#131818"),
                Border = HexColor.Parse("#263030"),
                Secondary = HexColor.Parse("#909B9B"),
                Dim = HexColor.Parse("#5A6666")
            }
        },
        new ThemePreset
        {
            Id = "nordic",
            DisplayName = "Nordic Night",
            IsDark = true,
            Palette = new ThemePalette
            {
                Background = HexColor.Parse("#242933"),
                Foreground = HexColor.Parse("#ECEFF4"),
                Accent = HexColor.Parse("#88C0D0"),
                Sidebar = HexColor.Parse("#2E3440"),
                Surface = HexColor.Parse("#2E3440"),
                Surface2 = HexColor.Parse("#3B4252"),
                Border = HexColor.Parse("#4C566A"),
                Secondary = HexColor.Parse("#D8DEE9"),
                Dim = HexColor.Parse("#7B88A1")
            }
        },
        new ThemePreset
        {
            Id = "espresso",
            DisplayName = "Warm Espresso",
            IsDark = true,
            Palette = new ThemePalette
            {
                Background = HexColor.Parse("#141210"),
                Foreground = HexColor.Parse("#EDE5DE"),
                Accent = HexColor.Parse("#D97736"),
                Sidebar = HexColor.Parse("#1C1916"),
                Surface = HexColor.Parse("#1C1916"),
                Surface2 = HexColor.Parse("#24201C"),
                Border = HexColor.Parse("#3D352E"),
                Secondary = HexColor.Parse("#A89F95"),
                Dim = HexColor.Parse("#736B63")
            }
        },
        new ThemePreset
        {
            Id = "emerald",
            DisplayName = "Midnight Emerald",
            IsDark = true,
            Palette = new ThemePalette
            {
                Background = HexColor.Parse("#0B120E"),
                Foreground = HexColor.Parse("#E2EDE6"),
                Accent = HexColor.Parse("#40916C"),
                Sidebar = HexColor.Parse("#101A14"),
                Surface = HexColor.Parse("#101A14"),
                Surface2 = HexColor.Parse("#16231B"),
                Border = HexColor.Parse("#253D2F"),
                Secondary = HexColor.Parse("#8EAFA0"),
                Dim = HexColor.Parse("#587567")
            }
        },
    ];

    public static ThemePreset FindPreset(string id, bool isDark)
    {
        var list = isDark ? DarkPresets : LightPresets;
        if (isDark && string.Equals(id, "default", StringComparison.OrdinalIgnoreCase))
            return list[0]; // Carbon Studio is the default
        return list.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? list[0];
    }

    public static string DetectPresetId(HexColor bg, HexColor fg, HexColor accent, bool isDark)
    {
        var list = isDark ? DarkPresets : LightPresets;
        foreach (var p in list)
        {
            if (p.Palette.Background == bg && p.Palette.Foreground == fg && p.Palette.Accent == accent)
                return p.Id;
        }
        return "custom";
    }

    public static ThemePalette ResolvePalette(string presetId, HexColor bg, HexColor fg, HexColor accent, bool isDark, Contrast contrast = Contrast.Standard)
    {
        ThemePalette basePalette;
        if (!string.Equals(presetId, "custom", StringComparison.OrdinalIgnoreCase))
        {
            var match = FindPreset(presetId, isDark);
            if (match.Palette.Background == bg && match.Palette.Foreground == fg && match.Palette.Accent == accent)
                basePalette = match.Palette;
            else
                basePalette = ThemePalette.Derive(bg, fg, accent, isDark);
        }
        else
        {
            basePalette = ThemePalette.Derive(bg, fg, accent, isDark);
        }
        return ThemePalette.ApplyContrast(basePalette, contrast, isDark);
    }
}
