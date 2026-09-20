using FocusKey.Foundation.Today;

namespace FocusKey.Foundation.Tests.Today;

public sealed class TodayAdaptiveLayoutTests
{
    [Theory]
    [InlineData(1920.0, AdaptiveNavMode.Expanded, 220.0)]
    [InlineData(1360.0, AdaptiveNavMode.Expanded, 220.0)]
    [InlineData(1060.0, AdaptiveNavMode.Expanded, 220.0)]
    [InlineData(1059.0, AdaptiveNavMode.Compact, 54.0)]
    [InlineData(880.0, AdaptiveNavMode.Compact, 54.0)]
    [InlineData(740.0, AdaptiveNavMode.Compact, 54.0)]
    [InlineData(739.0, AdaptiveNavMode.Collapsed, 0.0)]
    [InlineData(600.0, AdaptiveNavMode.Collapsed, 0.0)]
    [InlineData(480.0, AdaptiveNavMode.Collapsed, 0.0)]
    public void ResolveNavModeAndSidebarWidth(double windowWidth, AdaptiveNavMode expectedMode, double expectedSidebarWidth)
    {
        Assert.Equal(expectedMode, TodayAdaptiveLayoutHelper.ResolveNavMode(windowWidth));
        Assert.Equal(expectedSidebarWidth, TodayAdaptiveLayoutHelper.ResolveSidebarWidth(windowWidth));
    }

    [Theory]
    [InlineData(1400.0, TodayCompositionMode.TwoColumn)]
    [InlineData(1000.0, TodayCompositionMode.TwoColumn)]
    [InlineData(800.0, TodayCompositionMode.TwoColumn)]
    [InlineData(780.0, TodayCompositionMode.TwoColumn)]
    [InlineData(779.0, TodayCompositionMode.VerticalStack)]
    [InlineData(650.0, TodayCompositionMode.VerticalStack)]
    [InlineData(500.0, TodayCompositionMode.VerticalStack)]
    [InlineData(350.0, TodayCompositionMode.VerticalStack)]
    public void ResolveTodayComposition(double availableContentWidth, TodayCompositionMode expectedMode)
    {
        Assert.Equal(expectedMode, TodayAdaptiveLayoutHelper.ResolveTodayComposition(availableContentWidth));
    }

    [Theory]
    [InlineData(1500.0, 1260.0)]
    [InlineData(1260.0, 1260.0)]
    [InlineData(1100.0, 1100.0)]
    [InlineData(700.0, 700.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(-50.0, 0.0)]
    public void ClampContentWidth(double available, double expected)
    {
        Assert.Equal(expected, TodayAdaptiveLayoutHelper.ClampContentWidth(available));
    }
}
