namespace FocusKey.Foundation.Settings;

/// <summary>Complete coherent palette for a theme mode (Light or Dark).</summary>
public sealed record ThemePalette
{
    public required HexColor Background { get; init; }
    public required HexColor Foreground { get; init; }
    public required HexColor Accent { get; init; }
    public required HexColor Sidebar { get; init; }
    public required HexColor Surface { get; init; }
    public required HexColor Surface2 { get; init; }
    public required HexColor Border { get; init; }
    public required HexColor Secondary { get; init; }
    public required HexColor Dim { get; init; }

    public static ThemePalette DefaultLight => ThemePresets.LightPresets[0].Palette;
    public static ThemePalette DefaultDark => ThemePresets.DarkPresets[0].Palette;

    public static ThemePalette Derive(HexColor bg, HexColor fg, HexColor accent, bool isDark)
    {
        var bgC = ColorFromHex(bg);
        var fgC = ColorFromHex(fg);

        (byte r, byte g, byte b) Blend(double fgWeight) =>
            (
                (byte)Math.Clamp((int)Math.Round(bgC.r * (1 - fgWeight) + fgC.r * fgWeight), 0, 255),
                (byte)Math.Clamp((int)Math.Round(bgC.g * (1 - fgWeight) + fgC.g * fgWeight), 0, 255),
                (byte)Math.Clamp((int)Math.Round(bgC.b * (1 - fgWeight) + fgC.b * fgWeight), 0, 255)
            );

        HexColor HexFrom((byte r, byte g, byte b) c) =>
            HexColor.Parse($"#{c.r:X2}{c.g:X2}{c.b:X2}");

        if (isDark)
        {
            return new ThemePalette
            {
                Background = bg,
                Foreground = fg,
                Accent = accent,
                Sidebar = HexFrom(Blend(0.03)),
                Surface = HexFrom(Blend(0.04)),
                Surface2 = HexFrom(Blend(0.08)),
                Border = HexFrom(Blend(0.14)),
                Secondary = HexFrom(Blend(0.60)),
                Dim = HexFrom(Blend(0.38)),
            };
        }
        else
        {
            return new ThemePalette
            {
                Background = bg,
                Foreground = fg,
                Accent = accent,
                Sidebar = HexFrom(Blend(0.05)),
                Surface = HexColor.Parse("#FFFFFF"),
                Surface2 = HexFrom(Blend(0.02)),
                Border = HexFrom(Blend(0.12)),
                Secondary = HexFrom(Blend(0.60)),
                Dim = HexFrom(Blend(0.40)),
            };
        }
    }

    private static (byte r, byte g, byte b) ColorFromHex(HexColor hex)
    {
        string s = hex.Value.TrimStart('#');
        return (
            Convert.ToByte(s[..2], 16),
            Convert.ToByte(s[2..4], 16),
            Convert.ToByte(s[4..6], 16)
        );
    }
}
