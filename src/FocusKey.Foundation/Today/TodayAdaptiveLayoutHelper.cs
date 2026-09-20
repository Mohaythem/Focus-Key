namespace FocusKey.Foundation.Today;

/// <summary>
/// Navigation presentation mode corresponding to the Windows Clock-inspired adaptive shell.
/// </summary>
public enum AdaptiveNavMode
{
    /// <summary>Wide windows (>= 1060 DIP): Full expanded pane with icons and text labels.</summary>
    Expanded,

    /// <summary>Medium windows (740 to 1059 DIP): Compact icon-only rail.</summary>
    Compact,

    /// <summary>Narrow windows (< 740 DIP): Collapsed behind hamburger menu / overlay drawer.</summary>
    Collapsed,
}

/// <summary>
/// Today page composition mode.
/// </summary>
public enum TodayCompositionMode
{
    /// <summary>Two-column layout: Left column (Hero on top, Summary on bottom), Right column (Activity).</summary>
    TwoColumn,

    /// <summary>Single vertical stack: Hero on top -> Summary in middle -> Activity on bottom.</summary>
    VerticalStack,
}

/// <summary>
/// Pure helper for calculating responsive breakpoints, navigation presentation, and Today page layout geometry.
/// </summary>
public static class TodayAdaptiveLayoutHelper
{
    public const double BreakpointNavExpanded = 1060.0;
    public const double BreakpointNavCompact = 740.0;
    public const double BreakpointTodayTwoColumn = 620.0;
    public const double MaxContentWidth = 1260.0;

    public const double SidebarExpandedWidth = 220.0;
    public const double SidebarCompactWidth = 54.0;
    public const double SidebarCollapsedWidth = 0.0;

    public static AdaptiveNavMode ResolveNavMode(double windowWidth)
    {
        if (windowWidth >= BreakpointNavExpanded) return AdaptiveNavMode.Expanded;
        if (windowWidth >= BreakpointNavCompact) return AdaptiveNavMode.Compact;
        return AdaptiveNavMode.Collapsed;
    }

    public static double ResolveSidebarWidth(double windowWidth) => ResolveNavMode(windowWidth) switch
    {
        AdaptiveNavMode.Expanded => SidebarExpandedWidth,
        AdaptiveNavMode.Compact => SidebarCompactWidth,
        AdaptiveNavMode.Collapsed => SidebarCollapsedWidth,
        _ => SidebarExpandedWidth,
    };

    public static TodayCompositionMode ResolveTodayComposition(double availableContentWidth)
    {
        return availableContentWidth >= BreakpointTodayTwoColumn
            ? TodayCompositionMode.TwoColumn
            : TodayCompositionMode.VerticalStack;
    }

    public static double ClampContentWidth(double availableWidth)
    {
        if (availableWidth <= 0) return 0;
        return Math.Min(MaxContentWidth, availableWidth);
    }
}
