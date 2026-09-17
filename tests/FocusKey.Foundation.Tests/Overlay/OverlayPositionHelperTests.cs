using FocusKey.Foundation.Overlay;

namespace FocusKey.Foundation.Tests.Overlay;

public sealed class OverlayPositionHelperTests
{
    [Fact]
    public void ScreenRect_CalculatesRightAndBottomCorrectly()
    {
        var rect = new ScreenRect(100, 200, 800, 600);
        Assert.Equal(100, rect.X);
        Assert.Equal(200, rect.Y);
        Assert.Equal(800, rect.Width);
        Assert.Equal(600, rect.Height);
        Assert.Equal(900, rect.Right);
        Assert.Equal(800, rect.Bottom);
    }

    [Theory]
    [InlineData(0, 0, 1920, 1080, 400, 300, 760, 390)]
    [InlineData(1920, 0, 1920, 1080, 400, 300, 2680, 390)]
    [InlineData(-1920, 100, 1920, 1080, 400, 300, -1160, 490)]
    public void CalculateInitialCenter_CentersWindowInTargetWorkArea(
        int areaX, int areaY, int areaW, int areaH,
        int windowW, int windowH,
        int expectedX, int expectedY)
    {
        var targetArea = new ScreenRect(areaX, areaY, areaW, areaH);
        ScreenPoint center = OverlayPositionHelper.CalculateInitialCenter(windowW, windowH, targetArea);
        Assert.Equal(expectedX, center.X);
        Assert.Equal(expectedY, center.Y);
    }

    [Fact]
    public void ClampToWorkAreas_ReturnsOriginalCoordinatesWhenNoWorkAreas()
    {
        var result1 = OverlayPositionHelper.ClampToWorkAreas(123, 456, 300, 200, null!);
        Assert.Equal(new ScreenPoint(123, 456), result1);

        var result2 = OverlayPositionHelper.ClampToWorkAreas(123, 456, 300, 200, Array.Empty<ScreenRect>());
        Assert.Equal(new ScreenPoint(123, 456), result2);
    }

    [Fact]
    public void ClampToWorkAreas_RetainsCoordinatesWhenFullyInsideWorkArea()
    {
        var workAreas = new[] { new ScreenRect(0, 0, 1920, 1080) };
        var result = OverlayPositionHelper.ClampToWorkAreas(500, 300, 400, 300, workAreas);
        Assert.Equal(new ScreenPoint(500, 300), result);
    }

    [Fact]
    public void ClampToWorkAreas_ClampsToLeftAndTopEdges()
    {
        var workAreas = new[] { new ScreenRect(100, 50, 1920, 1080) };
        var result = OverlayPositionHelper.ClampToWorkAreas(50, 20, 400, 300, workAreas);
        Assert.Equal(new ScreenPoint(100, 50), result);
    }

    [Fact]
    public void ClampToWorkAreas_ClampsToRightAndBottomEdges()
    {
        var workAreas = new[] { new ScreenRect(0, 0, 1920, 1080) };
        // Max X = 1920 - 400 = 1520, Max Y = 1080 - 300 = 780
        var result = OverlayPositionHelper.ClampToWorkAreas(1600, 900, 400, 300, workAreas);
        Assert.Equal(new ScreenPoint(1520, 780), result);
    }

    [Fact]
    public void ClampToWorkAreas_PinsTopLeftWhenWindowIsLargerThanWorkArea()
    {
        var workAreas = new[] { new ScreenRect(100, 100, 800, 600) };
        // Window 1000x800 is larger than 800x600 work area
        var result = OverlayPositionHelper.ClampToWorkAreas(200, 200, 1000, 800, workAreas);
        Assert.Equal(new ScreenPoint(100, 100), result);
    }

    [Fact]
    public void ClampToWorkAreas_SelectsMonitorContainingWindowCenter()
    {
        var monitor1 = new ScreenRect(0, 0, 1920, 1080);
        var monitor2 = new ScreenRect(1920, 0, 1920, 1080);
        var workAreas = new[] { monitor1, monitor2 };

        // Window at (2000, 100) with size 400x300 has center (2200, 250), which is inside monitor 2
        var result = OverlayPositionHelper.ClampToWorkAreas(2000, 100, 400, 300, workAreas);
        Assert.Equal(new ScreenPoint(2000, 100), result);
    }

    [Fact]
    public void ClampToWorkAreas_SelectsMonitorWithLargestOverlapWhenCenterNotInAny()
    {
        var monitor1 = new ScreenRect(0, 0, 1000, 1000);
        var monitor2 = new ScreenRect(1100, 0, 1000, 1000); // 100px gap
        var workAreas = new[] { monitor1, monitor2 };

        // Window width 300 at x = 900 -> overlap with monitor1: x in [900, 1000] -> 100px overlap
        // Overlap with monitor2: none (ends at 1200, monitor2 starts at 1100 -> overlap x in [1100, 1200] -> 100px)
        // Let's create asymmetric overlap: window at x = 850, w = 300:
        // monitor1 overlap: [850, 1000] = 150px
        // monitor2 overlap: [1100, 1150] = 50px
        var result = OverlayPositionHelper.ClampToWorkAreas(850, 100, 300, 200, workAreas);
        // Best monitor should be monitor1, clamped to [0, 700]
        Assert.Equal(new ScreenPoint(700, 100), result);
    }

    [Fact]
    public void ClampToWorkAreas_RecoversFromOffScreenToNearestMonitor()
    {
        var monitor1 = new ScreenRect(0, 0, 1920, 1080);
        var monitor2 = new ScreenRect(1920, 0, 1920, 1080);
        var workAreas = new[] { monitor1, monitor2 };

        // Completely off to the far right, closer to monitor 2
        var resultFarRight = OverlayPositionHelper.ClampToWorkAreas(5000, 500, 400, 300, workAreas);
        // Should clamp to monitor 2 right edge: 1920 + 1920 - 400 = 3440
        Assert.Equal(new ScreenPoint(3440, 500), resultFarRight);

        // Completely off to the far left, closer to monitor 1
        var resultFarLeft = OverlayPositionHelper.ClampToWorkAreas(-2000, 200, 400, 300, workAreas);
        // Should clamp to monitor 1 left edge: 0
        Assert.Equal(new ScreenPoint(0, 200), resultFarLeft);
    }

    [Fact]
    public void IsPositionValid_ReturnsTrueWhenWindowHeaderIsOnScreen()
    {
        var workAreas = new[] { new ScreenRect(0, 0, 1920, 1080) };
        Assert.True(OverlayPositionHelper.IsPositionValid(100, 100, 400, 300, workAreas));
    }

    [Fact]
    public void IsPositionValid_ReturnsFalseWhenCompletelyOffScreen()
    {
        var workAreas = new[] { new ScreenRect(0, 0, 1920, 1080) };
        Assert.False(OverlayPositionHelper.IsPositionValid(-500, -500, 400, 300, workAreas));
        Assert.False(OverlayPositionHelper.IsPositionValid(2500, 2500, 400, 300, workAreas));
    }

    [Fact]
    public void IsPositionValid_ReturnsFalseWhenHeaderIsMostlyOffScreen()
    {
        var workAreas = new[] { new ScreenRect(0, 0, 1920, 1080) };
        // Window x = 1910, w = 400 -> only 10px on screen (< 32px min width)
        Assert.False(OverlayPositionHelper.IsPositionValid(1910, 100, 400, 300, workAreas));

        // Window y = -25, h = 300 -> header 32px has only 7px on screen (< 16px min height)
        Assert.False(OverlayPositionHelper.IsPositionValid(100, -25, 400, 300, workAreas));
    }

    [Fact]
    public void IsPositionValid_ReturnsTrueWhenHeaderHasSufficientOverlap()
    {
        var workAreas = new[] { new ScreenRect(0, 0, 1920, 1080) };
        // Window x = 1880, w = 400 -> 40px on screen (>= 32px)
        // Window y = -10, h = 300 -> 22px of header on screen (>= 16px)
        Assert.True(OverlayPositionHelper.IsPositionValid(1880, -10, 400, 300, workAreas));
    }

    [Fact]
    public void IsPositionValid_ReturnsFalseForEmptyWorkAreasOrInvalidDimensions()
    {
        var workAreas = new[] { new ScreenRect(0, 0, 1920, 1080) };
        Assert.False(OverlayPositionHelper.IsPositionValid(100, 100, 400, 300, null!));
        Assert.False(OverlayPositionHelper.IsPositionValid(100, 100, 400, 300, Array.Empty<ScreenRect>()));
        Assert.False(OverlayPositionHelper.IsPositionValid(100, 100, 0, 300, workAreas));
        Assert.False(OverlayPositionHelper.IsPositionValid(100, 100, 400, -1, workAreas));
    }
}
