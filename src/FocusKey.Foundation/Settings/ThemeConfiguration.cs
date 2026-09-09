namespace FocusKey.Foundation.Settings;

public sealed record ThemeConfiguration
{
    public static ThemeConfiguration DefaultLight { get; } = new()
    {
        Preset = "default",
        Background = HexColor.Parse("#F2F5F5"),
        Foreground = HexColor.Parse("#0F1414"),
        Accent = HexColor.Parse("#183739"),
    };

    public static ThemeConfiguration DefaultDark { get; } = new()
    {
        Preset = "default",
        Background = HexColor.Parse("#0A0D0D"),
        Foreground = HexColor.Parse("#F0F4F4"),
        Accent = HexColor.Parse("#2D6669"),
    };

    public required string Preset { get; init; }
    public required HexColor Background { get; init; }
    public required HexColor Foreground { get; init; }
    public required HexColor Accent { get; init; }

    public ThemePalette ResolvePalette(bool isDark) =>
        ThemePresets.ResolvePalette(Preset, Background, Foreground, Accent, isDark);

    public void Validate(bool isDark)
    {
        if (string.IsNullOrWhiteSpace(Preset))
            throw new ArgumentException("Theme preset identifier cannot be empty.", nameof(Preset));
        ValidateColor(Background, nameof(Background));
        ValidateColor(Foreground, nameof(Foreground));
        ValidateColor(Accent, nameof(Accent));
    }

    private static void ValidateColor(HexColor color, string name)
    {
        if (!HexColor.TryParse(color.Value, out HexColor canonical) || canonical != color)
            throw new ArgumentException($"A {name} color must be canonical #RRGGBB.", name);
    }
}
