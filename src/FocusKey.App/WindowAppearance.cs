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

    internal static void Apply(FrameworkElement root, AppWindow appWindow, Appearance appearance)
    {
        root.RequestedTheme = ToElementTheme(appearance);
        ApplyTitleBar(appWindow, appearance);
    }

    private static void ApplyTitleBar(AppWindow appWindow, Appearance appearance)
    {
        if (!AppWindowTitleBar.IsCustomizationSupported()) return;
        AppWindowTitleBar titleBar = appWindow.TitleBar;
        if (appearance == Appearance.System)
        {
            titleBar.BackgroundColor = null;
            titleBar.ForegroundColor = null;
            titleBar.ButtonBackgroundColor = null;
            titleBar.ButtonForegroundColor = null;
            titleBar.ButtonHoverBackgroundColor = null;
            titleBar.ButtonHoverForegroundColor = null;
            titleBar.ButtonPressedBackgroundColor = null;
            titleBar.ButtonPressedForegroundColor = null;
            titleBar.ButtonInactiveBackgroundColor = null;
            titleBar.ButtonInactiveForegroundColor = null;
            return;
        }

        bool dark = appearance == Appearance.Dark;
        Color background = dark ? Color.FromArgb(255, 32, 32, 32) : Color.FromArgb(255, 243, 243, 243);
        Color foreground = dark ? Color.FromArgb(255, 255, 255, 255) : Color.FromArgb(255, 26, 26, 26);
        Color hover = dark ? Color.FromArgb(255, 51, 51, 51) : Color.FromArgb(255, 229, 229, 229);
        Color pressed = dark ? Color.FromArgb(255, 68, 68, 68) : Color.FromArgb(255, 216, 216, 216);
        titleBar.BackgroundColor = titleBar.ButtonBackgroundColor = background;
        titleBar.ForegroundColor = titleBar.ButtonForegroundColor = foreground;
        titleBar.ButtonHoverBackgroundColor = hover;
        titleBar.ButtonHoverForegroundColor = foreground;
        titleBar.ButtonPressedBackgroundColor = pressed;
        titleBar.ButtonPressedForegroundColor = foreground;
        titleBar.ButtonInactiveBackgroundColor = background;
        titleBar.ButtonInactiveForegroundColor = foreground;
    }
}
