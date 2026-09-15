using System.Globalization;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace FocusKey;

/// <summary>Presentation primitives shared by native pages; no session or persistence state.</summary>
internal static class Presentation
{
    internal static Brush ThemeBrush(string key, FrameworkElement? context = null)
    {
        ElementTheme theme = context?.ActualTheme is ElementTheme.Light or ElementTheme.Dark
            ? context.ActualTheme
            : EffectiveSystemTheme();

        string themeKey = theme switch
        {
            ElementTheme.Light => "Light",
            ElementTheme.Dark => "Dark",
            _ => "Dark",
        };

        if (Application.Current?.Resources.ThemeDictionaries is { } dicts &&
            dicts.TryGetValue(themeKey, out object? dictObj) &&
            dictObj is ResourceDictionary dict &&
            dict.TryGetValue(key, out object? brushObj) &&
            brushObj is Brush brush)
        {
            return brush;
        }

        return Application.Current?.Resources[key] as Brush ?? new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    private static ElementTheme EffectiveSystemTheme()
    {
        if (Application.Current?.RequestedTheme is ApplicationTheme.Light)
            return ElementTheme.Light;
        if (Application.Current?.RequestedTheme is ApplicationTheme.Dark)
            return ElementTheme.Dark;

        // RequestedTheme == Default means "follow Windows"; it is not evidence of Dark.
        // UISettings exposes the resolved system background without inventing a second theme
        // lookup path for the application's own ThemeDictionaries.
        Color background = new UISettings().GetColorValue(UIColorType.Background);
        double luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255.0;
        return luminance < 0.5 ? ElementTheme.Dark : ElementTheme.Light;
    }

    internal static TextBlock Text(string value, double size = 13, bool muted = false) => new()
    {
        Text = value,
        Style = Application.Current?.Resources[muted ? "FkMutedText" : "FkText"] as Style,
        FontSize = size,
        IsTextSelectionEnabled = true,
        Language = "en-US",
        FlowDirection = FlowDirection.LeftToRight,
        TextReadingOrder = TextReadingOrder.UseFlowDirection
    };

    internal static TextBlock DimText(string value, double size = 11) => new()
    {
        Text = value,
        Style = Application.Current?.Resources["FkDimText"] as Style,
        FontSize = size,
        IsTextSelectionEnabled = true,
        Language = "en-US",
        FlowDirection = FlowDirection.LeftToRight,
        TextReadingOrder = TextReadingOrder.UseFlowDirection
    };

    /// <summary>Renders a session status with dynamic theme-aware typography and colors.</summary>
    internal static TextBlock StatusText(SessionStatus status, double size = 11)
    {
        string styleKey = status switch
        {
            SessionStatus.Running => "FkStatusRunningText",
            SessionStatus.Completed => "FkStatusCompletedText",
            SessionStatus.Stopped => "FkStatusStoppedText",
            SessionStatus.Interrupted => "FkStatusInterruptedText",
            _ => "FkStatusCompletedText",
        };
        return new TextBlock
        {
            Text = status.ToString(),
            FontSize = size,
            Style = Application.Current?.Resources[styleKey] as Style,
            IsTextSelectionEnabled = true,
            Language = "en-US",
            FlowDirection = FlowDirection.LeftToRight,
            TextReadingOrder = TextReadingOrder.UseFlowDirection
        };
    }

    internal static Border Card(UIElement child, double padding = 20) => new()
    {
        Style = Application.Current?.Resources["FkCard"] as Style,
        Padding = new Thickness(padding),
        Child = child
    };

    internal static Border CardSubtle(UIElement child, double padding = 20) => new()
    {
        Style = Application.Current?.Resources["FkCardSubtle"] as Style,
        Padding = new Thickness(padding),
        Child = child
    };
    internal static SolidColorBrush Stroke(HexColor color) => SessionColorBrush.Create(SessionColors.Foreground(color));
    internal static string Duration(TimeSpan value) => FocusKey.Foundation.Reports.ReportsFormatting.FormatDuration(value);
    internal static string FormatLauncherDuration(TimeSpan duration) => FocusKey.Foundation.Today.TodayFormatting.FormatLauncherDuration(duration);
}
