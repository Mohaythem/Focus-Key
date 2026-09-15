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
    internal static Brush ThemeBrush(string key, bool isDark)
    {
        string themeKey = isDark ? "Dark" : "Light";
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

    internal static Brush ThemeBrush(string key, FrameworkElement? context = null)
    {
        ElementTheme theme = context?.ActualTheme is ElementTheme.Light or ElementTheme.Dark
            ? context.ActualTheme
            : (context?.RequestedTheme is ElementTheme.Light or ElementTheme.Dark
                ? context.RequestedTheme
                : EffectiveSystemTheme());

        return ThemeBrush(key, theme == ElementTheme.Dark);
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
    internal static Border CardHero(UIElement child, Thickness? padding = null) => new()
    {
        Style = Application.Current?.Resources["FkCardHero"] as Style,
        Padding = padding ?? Paddings.HeroCard,
        Child = child
    };

    internal static TextBlock PageTitle(string value) => new()
    {
        Text = value,
        Style = Application.Current?.Resources["FkPageTitleText"] as Style,
        IsTextSelectionEnabled = true,
        Language = "en-US",
        FlowDirection = FlowDirection.LeftToRight,
        TextReadingOrder = TextReadingOrder.UseFlowDirection
    };

    internal static TextBlock SectionHeader(string value) => new()
    {
        Text = value,
        Style = Application.Current?.Resources["FkSectionHeaderText"] as Style,
        IsTextSelectionEnabled = true,
        Language = "en-US",
        FlowDirection = FlowDirection.LeftToRight,
        TextReadingOrder = TextReadingOrder.UseFlowDirection
    };

    internal static TextBlock BodyStrong(string value, double size = 13) => new()
    {
        Text = value,
        FontSize = size,
        Style = Application.Current?.Resources["FkBodyStrongText"] as Style,
        IsTextSelectionEnabled = true,
        Language = "en-US",
        FlowDirection = FlowDirection.LeftToRight,
        TextReadingOrder = TextReadingOrder.UseFlowDirection
    };

    internal static TextBlock Supporting(string value, double size = 12) => new()
    {
        Text = value,
        FontSize = size,
        Style = Application.Current?.Resources["FkSupportingText"] as Style,
        IsTextSelectionEnabled = true,
        Language = "en-US",
        FlowDirection = FlowDirection.LeftToRight,
        TextReadingOrder = TextReadingOrder.UseFlowDirection
    };

    internal static TextBlock CaptionHint(string value, double size = 11) => new()
    {
        Text = value,
        FontSize = size,
        Style = Application.Current?.Resources["FkCaptionHintText"] as Style,
        IsTextSelectionEnabled = true,
        Language = "en-US",
        FlowDirection = FlowDirection.LeftToRight,
        TextReadingOrder = TextReadingOrder.UseFlowDirection
    };

    internal static TextBlock MetricValue(string value, double size = 28) => new()
    {
        Text = value,
        FontSize = size,
        Style = Application.Current?.Resources["FkMetricValueText"] as Style,
        IsTextSelectionEnabled = true,
        Language = "en-US",
        FlowDirection = FlowDirection.LeftToRight,
        TextReadingOrder = TextReadingOrder.UseFlowDirection
    };

    internal static TextBlock DisplayTimer(string value, double size = 40) => new()
    {
        Text = value,
        FontSize = size,
        Style = Application.Current?.Resources["FkDisplayTimerText"] as Style,
        IsTextSelectionEnabled = true,
        Language = "en-US",
        FlowDirection = FlowDirection.LeftToRight,
        TextReadingOrder = TextReadingOrder.UseFlowDirection
    };

    internal static TextBlock DurationChoice(string value, double size = 26) => new()
    {
        Text = value,
        FontSize = size,
        Style = Application.Current?.Resources["FkDurationChoiceText"] as Style,
        IsTextSelectionEnabled = true,
        Language = "en-US",
        FlowDirection = FlowDirection.LeftToRight,
        TextReadingOrder = TextReadingOrder.UseFlowDirection
    };

    internal static SolidColorBrush Stroke(HexColor color) => SessionColorBrush.Create(SessionColors.Foreground(color));
    internal static string Duration(TimeSpan value) => FocusKey.Foundation.Reports.ReportsFormatting.FormatDuration(value);
    internal static string FormatLauncherDuration(TimeSpan duration) => FocusKey.Foundation.Today.TodayFormatting.FormatLauncherDuration(duration);

    internal static class Radii
    {
        internal static readonly CornerRadius Control = new(4);
        internal static readonly CornerRadius Card = new(6);
        internal static readonly CornerRadius Hero = new(8);
    }

    internal static class Spacing
    {
        internal const double S2 = 2;
        internal const double S4 = 4;
        internal const double S8 = 8;
        internal const double S12 = 12;
        internal const double S16 = 16;
        internal const double S20 = 20;
        internal const double S24 = 24;
        internal const double S28 = 28;
        internal const double S40 = 40;
    }

    internal static class Paddings
    {
        internal static readonly Thickness PageStandard = new(40, 28, 40, 36);
        internal static readonly Thickness PageCompact = new(24, 20, 24, 28);
        internal static readonly Thickness Card = new(20);
        internal static readonly Thickness HeroCard = new(28, 24, 28, 24);
    }

    internal static class LayoutConstraints
    {
        internal const double UtilityPageMaxWidth = 880;
        internal const double ReportsPageMaxWidth = 1040;
        internal const double OverlayWidth = 420;
        internal const double BreakpointCompact = 740;
        internal const double BreakpointMaximized = 1200;
    }

    internal static class Motion
    {
        internal static readonly TimeSpan QuickDuration = TimeSpan.FromMilliseconds(150);
        internal static readonly TimeSpan StandardDuration = TimeSpan.FromMilliseconds(200);
        internal static readonly TimeSpan SmoothDuration = TimeSpan.FromMilliseconds(250);

        internal static bool AreAnimationsEnabled
        {
            get
            {
                try { return new UISettings().AnimationsEnabled; }
                catch { return true; }
            }
        }
    }
}
