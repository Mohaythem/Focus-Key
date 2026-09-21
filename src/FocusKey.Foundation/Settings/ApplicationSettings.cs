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
        WorkColor = HexColor.Parse("#2F8F83"),
        BreakColor = HexColor.Parse("#7667B8"),
        LightTheme = ThemeConfiguration.DefaultLight,
        DarkTheme = ThemeConfiguration.DefaultDark,
        SessionSoundsEnabled = true,
        StartSoundEnabled = true,
        CompletionSoundEnabled = true,
        ActivityCollapsed = true,
        GlobalShortcut = GlobalShortcut.Default,
        MainWindowShortcut = GlobalShortcut.DefaultMainWindow,
        TimeFormat = TimeFormat.TwentyFourHour,
        OverlayPositionX = null,
        OverlayPositionY = null,
        AppearanceExpanded = false,
        ShortcutsExpanded = false,
        AdvancedExpanded = false,
        UiScalePercent = 100,
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
    public bool StartSoundEnabled { get; init; } = true;
    public bool CompletionSoundEnabled { get; init; } = true;
    public bool ActivityCollapsed { get; init; } = true;
    public GlobalShortcut GlobalShortcut { get; init; } = GlobalShortcut.Default;
    public GlobalShortcut MainWindowShortcut { get; init; } = GlobalShortcut.DefaultMainWindow;
    public TimeFormat TimeFormat { get; init; } = TimeFormat.TwentyFourHour;
    public int? OverlayPositionX { get; init; }
    public int? OverlayPositionY { get; init; }
    public bool AppearanceExpanded { get; init; } = false;
    public bool ShortcutsExpanded { get; init; } = false;
    public bool AdvancedExpanded { get; init; } = false;
    public int UiScalePercent { get; init; } = 100;

    public void Validate()
    {
        ValidateDuration(WorkDuration, nameof(WorkDuration));
        ValidateDuration(BreakDuration, nameof(BreakDuration));
        if (!Enum.IsDefined(Appearance))
            throw new ArgumentException($"Unsupported appearance '{Appearance}'.", nameof(Appearance));
        if (!Enum.IsDefined(Contrast))
            throw new ArgumentException($"Unsupported contrast '{Contrast}'.", nameof(Contrast));
        if (!Enum.IsDefined(TimeFormat))
            throw new ArgumentException($"Unsupported time format '{TimeFormat}'.", nameof(TimeFormat));
        if (!UiScaleLevels.IsValid(UiScalePercent))
            throw new ArgumentException($"Unsupported UI scale percentage '{UiScalePercent}'.", nameof(UiScalePercent));
        if ((OverlayPositionX.HasValue && !OverlayPositionY.HasValue) || (!OverlayPositionX.HasValue && OverlayPositionY.HasValue))
            throw new ArgumentException("Overlay position X and Y must both be set or both be null.");
        ValidateColor(WorkColor, nameof(WorkColor));
        ValidateColor(BreakColor, nameof(BreakColor));
        (LightTheme ?? ThemeConfiguration.DefaultLight).Validate(false);
        (DarkTheme ?? ThemeConfiguration.DefaultDark).Validate(true);
        var overlayShortcut = GlobalShortcut ?? GlobalShortcut.Default;
        var mainWindowShortcut = MainWindowShortcut ?? GlobalShortcut.DefaultMainWindow;
        overlayShortcut.Validate();
        mainWindowShortcut.Validate();
        if (overlayShortcut == mainWindowShortcut)
        {
            throw new ArgumentException("Quick Overlay shortcut and Open Focus Key shortcut cannot be identical.");
        }
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
