using FocusKey.Foundation.Settings;
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
    [InlineData(880.0, TodayCompositionMode.TwoColumn)]
    [InlineData(768.0, TodayCompositionMode.TwoColumn)]
    [InlineData(720.0, TodayCompositionMode.TwoColumn)]
    [InlineData(719.0, TodayCompositionMode.VerticalStack)]
    [InlineData(680.0, TodayCompositionMode.VerticalStack)]
    [InlineData(640.0, TodayCompositionMode.VerticalStack)]
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

    [Theory]
    // At 100% scale (factor = 1.0)
    // 1200 -> Expanded (220 nav, 64 pad -> 916 available >= 720) -> TwoColumn
    [InlineData(1200.0, 100, AdaptiveNavMode.Expanded, TodayCompositionMode.TwoColumn)]
    // 880 -> Compact (54 nav, 64 pad -> 762 available >= 720) -> TwoColumn
    [InlineData(880.0, 100, AdaptiveNavMode.Compact, TodayCompositionMode.TwoColumn)]
    // 768 -> Compact (54 nav, 64 pad -> 650 available < 720) -> VerticalStack
    [InlineData(768.0, 100, AdaptiveNavMode.Compact, TodayCompositionMode.VerticalStack)]
    // 680 -> Collapsed (0 nav, 64 pad -> 616 available < 720) -> VerticalStack
    [InlineData(680.0, 100, AdaptiveNavMode.Collapsed, TodayCompositionMode.VerticalStack)]
    // 640 -> Collapsed (0 nav, 64 pad -> 576 available < 720) -> VerticalStack
    [InlineData(640.0, 100, AdaptiveNavMode.Collapsed, TodayCompositionMode.VerticalStack)]
    // At 150% scale (factor = 1.5, effective width = actual / 1.5)
    // 1200 / 1.5 = 800 -> Compact, available = 800 - 54 - 64 = 682 -> VerticalStack (< 720)
    [InlineData(1200.0, 150, AdaptiveNavMode.Compact, TodayCompositionMode.VerticalStack)]
    // 1100 / 1.5 = 733.3 -> Collapsed (< 740), available = 733.3 - 0 - 64 = 669.3 -> VerticalStack (< 720)
    [InlineData(1100.0, 150, AdaptiveNavMode.Collapsed, TodayCompositionMode.VerticalStack)]
    // 1000 / 1.5 = 666.7 -> Collapsed (< 740), available = 666.7 - 0 - 64 = 602.7 -> VerticalStack (< 720)
    [InlineData(1000.0, 150, AdaptiveNavMode.Collapsed, TodayCompositionMode.VerticalStack)]
    // 880 / 1.5 = 586.7 -> Collapsed (< 740), available 586.7 - 0 - 64 = 522.7 -> VerticalStack (< 720)
    [InlineData(880.0, 150, AdaptiveNavMode.Collapsed, TodayCompositionMode.VerticalStack)]
    // At 80% scale (factor = 0.8, effective width = actual / 0.8)
    // 880 / 0.8 = 1100 -> Expanded, available = 1100 - 220 - 64 = 816 -> TwoColumn (>= 720)
    [InlineData(880.0, 80, AdaptiveNavMode.Expanded, TodayCompositionMode.TwoColumn)]
    // 600 / 0.8 = 750 -> Compact, available = 750 - 54 - 64 = 632 -> VerticalStack (< 720)
    [InlineData(600.0, 80, AdaptiveNavMode.Compact, TodayCompositionMode.VerticalStack)]
    public void ScaleAwareEffectiveWidth_ResolvesExpectedLayoutTransitions(
        double physicalWidth, int scalePercent, AdaptiveNavMode expectedNav, TodayCompositionMode expectedComp)
    {
        double effectiveWidth = UiScaleLevels.CalculateEffectiveWidth(physicalWidth, scalePercent);
        var navMode = TodayAdaptiveLayoutHelper.ResolveNavMode(effectiveWidth);
        Assert.Equal(expectedNav, navMode);

        double effectiveAvailable = effectiveWidth - (navMode == AdaptiveNavMode.Expanded ? 220 : (navMode == AdaptiveNavMode.Compact ? 54 : 0)) - 64;
        var compMode = TodayAdaptiveLayoutHelper.ResolveTodayComposition(effectiveAvailable);
        Assert.Equal(expectedComp, compMode);
    }
}
