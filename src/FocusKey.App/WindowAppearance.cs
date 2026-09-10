using FocusKey.Foundation.Settings;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.UI;

namespace FocusKey;

/// <summary>Maps persisted appearance semantics to WinUI element and native title-bar themes.</summary>
internal static class WindowAppearance
{
    internal static ElementTheme ToElementTheme(Appearance appearance) => appearance switch
    {
        Appearance.System => ElementTheme.Default,
        Appearance.Light => ElementTheme.Light,
        Appearance.Dark => ElementTheme.Dark,
        _ => throw new ArgumentOutOfRangeException(nameof(appearance), appearance, "Unsupported appearance."),
    };

    internal static void Apply(FrameworkElement root, AppWindow appWindow, Appearance appearance, ThemePalette? palette = null)
    {
        root.RequestedTheme = ToElementTheme(appearance);
        if (palette is not null)
        {
            ApplyTitleBar(appWindow, palette);
        }
    }

    internal static void ApplyTitleBar(AppWindow appWindow, ThemePalette? palette)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        AppWindowTitleBar titleBar = appWindow.TitleBar;

        if (palette is null)
        {
            titleBar.ResetToDefault();
            return;
        }

        Color background = ToWinColor(palette.Sidebar);
        Color foreground = ToWinColor(palette.Foreground);
        Color hover = ToWinColor(palette.Surface2);
        Color pressed = ToWinColor(palette.Border);
        Color inactive = ToWinColor(palette.Dim);
        titleBar.BackgroundColor = titleBar.ButtonBackgroundColor = background;
        titleBar.ForegroundColor = titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = inactive;
    }

    private static Color ToWinColor(HexColor hex) =>
        Color.FromArgb(255,
            Convert.ToByte(hex.Value.Substring(1, 2), 16),
            Convert.ToByte(hex.Value.Substring(3, 2), 16),
            Convert.ToByte(hex.Value.Substring(5, 2), 16));
}
