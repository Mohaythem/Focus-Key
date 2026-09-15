namespace FocusKey.Foundation.Settings;

/// <summary>The complete user-editable application configuration.</summary>
public sealed record ApplicationSettings
{
    public static ApplicationSettings Default { get; } = new()
    {
        WorkDuration = TimeSpan.FromMinutes(30),
        BreakDuration = TimeSpan.FromMinutes(10),
        Appearance = Appearance.System,
        Contrast = Contrast.Standard,
        WorkColor = HexColor.Parse("#183739"),
        BreakColor = HexColor.Parse("#434763"),
        LightTheme = ThemeConfiguration.DefaultLight,
        DarkTheme = ThemeConfiguration.DefaultDark,
        SessionSoundsEnabled = true,
        ActivityCollapsed = true,
    };

    public required TimeSpan WorkDuration { get; init; }
    public required TimeSpan BreakDuration { get; init; }
    public required Appearance Appearance { get; init; }
    public Contrast Contrast { get; init; } = Contrast.Standard;
    public required HexColor WorkColor { get; init; }
    public required HexColor BreakColor { get; init; }
    public ThemeConfiguration LightTheme { get; init; } = ThemeConfiguration.DefaultLight;
    public ThemeConfiguration DarkTheme { get; init; } = ThemeConfiguration.DefaultDark;
    public bool SessionSoundsEnabled { get; init; } = true;
    public bool ActivityCollapsed { get; init; } = true;

    public void Validate()
    {
        ValidateDuration(WorkDuration, nameof(WorkDuration));
        ValidateDuration(BreakDuration, nameof(BreakDuration));
        if (!Enum.IsDefined(Appearance))
            throw new ArgumentException($"Unsupported appearance '{Appearance}'.", nameof(Appearance));
        if (!Enum.IsDefined(Contrast))
            throw new ArgumentException($"Unsupported contrast '{Contrast}'.", nameof(Contrast));
        ValidateColor(WorkColor, nameof(WorkColor));
        ValidateColor(BreakColor, nameof(BreakColor));
        (LightTheme ?? ThemeConfiguration.DefaultLight).Validate(false);
        (DarkTheme ?? ThemeConfiguration.DefaultDark).Validate(true);
    }

    private static void ValidateDuration(TimeSpan duration, string name)
    {
        if (duration <= TimeSpan.Zero)
            throw new ArgumentException("A session duration must be positive.", name);
        if (duration.Ticks % TimeSpan.TicksPerSecond != 0)
            throw new ArgumentException("A session duration must be a whole number of seconds.", name);
        if (duration.Ticks > DateTimeOffset.MaxValue.UtcTicks)
            throw new ArgumentException("A session duration cannot exceed the UTC timestamp range.", name);
    }

    private static void ValidateColor(HexColor color, string name)
    {
        if (!HexColor.TryParse(color.Value, out HexColor canonical) || canonical != color)
            throw new ArgumentException("A color must be canonical #RRGGBB.", name);
    }
}
