using System.Globalization;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FocusKey;

/// <summary>Presentation primitives shared by native pages; no session or persistence state.</summary>
internal static class Presentation
{
    internal static Brush ThemeBrush(string key, FrameworkElement? context = null)
    {
        ElementTheme theme = context?.ActualTheme ?? ElementTheme.Default;
        if (theme == ElementTheme.Default)
        {
            theme = Application.Current?.RequestedTheme == ApplicationTheme.Light 
                ? ElementTheme.Light 
                : ElementTheme.Dark;
        }

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
    internal static string Duration(TimeSpan value)
    {
        if (value.TotalHours >= 1) return string.Create(CultureInfo.InvariantCulture, $"{(long)value.TotalHours}h {value.Minutes:00}m");
        if (value.Seconds != 0) return string.Create(CultureInfo.InvariantCulture, $"{(long)value.TotalMinutes}m {value.Seconds:00}s");
        return string.Create(CultureInfo.InvariantCulture, $"{(long)value.TotalMinutes}m");
    }
}
