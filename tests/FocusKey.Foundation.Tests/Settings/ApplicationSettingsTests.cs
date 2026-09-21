using FocusKey.Foundation.Settings;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class ApplicationSettingsTests
{
    [Fact]
    public void DefaultsAreTheOfficialPhaseNineValues()
    {
        ApplicationSettings settings = ApplicationSettings.Default;
        settings.Validate();
        Assert.Equal(TimeSpan.FromMinutes(30), settings.WorkDuration);
        Assert.Equal(TimeSpan.FromMinutes(10), settings.BreakDuration);
        Assert.Equal(Appearance.System, settings.Appearance);
        Assert.Equal("#2F8F83", settings.WorkColor.Value);
        Assert.Equal("#7667B8", settings.BreakColor.Value);
        Assert.Equal(GlobalShortcut.Default, settings.GlobalShortcut);
        Assert.Equal(TimeFormat.TwentyFourHour, settings.TimeFormat);
        Assert.Null(settings.OverlayPositionX);
        Assert.Null(settings.OverlayPositionY);
        Assert.False(settings.AppearanceExpanded);
        Assert.False(settings.ShortcutsExpanded);
        Assert.False(settings.AdvancedExpanded);
        Assert.Equal(100, settings.UiScalePercent);
    }

    [Theory]
    [InlineData("#abcdef", "#ABCDEF")]
    [InlineData("#183739", "#183739")]
    [InlineData("#a0B1c2", "#A0B1C2")]
    public void ColorParsingProducesCanonicalOpaqueRgb(string value, string expected) =>
        Assert.Equal(expected, HexColor.Parse(value).Value);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("183739")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#GG0000")]
    [InlineData("#12345 ")]
    public void ColorParsingRejectsInvalidValues(string? value)
    {
        Assert.False(HexColor.TryParse(value, out _));
        Assert.Throws<ArgumentException>(() => HexColor.Parse(value!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidationRejectsNonPositiveDurations(int seconds)
    {
        Assert.Throws<ArgumentException>(() => (ApplicationSettings.Default with
            { WorkDuration = TimeSpan.FromSeconds(seconds) }).Validate());
        Assert.Throws<ArgumentException>(() => (ApplicationSettings.Default with
            { BreakDuration = TimeSpan.FromSeconds(seconds) }).Validate());
    }

    [Fact]
    public void ValidationRejectsFractionalSecondsUnsupportedAppearanceAndDefaultColor()
    {
        Assert.Throws<ArgumentException>(() => (ApplicationSettings.Default with
            { WorkDuration = TimeSpan.FromTicks(TimeSpan.TicksPerSecond + 1) }).Validate());
        Assert.Throws<ArgumentException>(() => (ApplicationSettings.Default with
            { Appearance = (Appearance)99 }).Validate());
        Assert.Throws<ArgumentException>(() => (ApplicationSettings.Default with
            { WorkColor = default }).Validate());
    }

    [Fact]
    public void ValidationAcceptsValidTimeFormats()
    {
        (ApplicationSettings.Default with { TimeFormat = TimeFormat.TwentyFourHour }).Validate();
        (ApplicationSettings.Default with { TimeFormat = TimeFormat.TwelveHour }).Validate();
    }

    [Fact]
    public void ValidationRejectsUnsupportedTimeFormat()
    {
        var invalid = ApplicationSettings.Default with { TimeFormat = (TimeFormat)99 };
        var ex = Assert.Throws<ArgumentException>(() => invalid.Validate());
        Assert.Equal("TimeFormat", ex.ParamName);
    }

    [Theory]
    [InlineData(100, 200)]
    [InlineData(-500, -200)]
    [InlineData(0, 0)]
    public void ValidationAcceptsBothOverlayCoordinatesSet(int x, int y)
    {
        var valid = ApplicationSettings.Default with { OverlayPositionX = x, OverlayPositionY = y };
        valid.Validate();
        Assert.Equal(x, valid.OverlayPositionX);
        Assert.Equal(y, valid.OverlayPositionY);
    }

    [Fact]
    public void ValidationAcceptsBothOverlayCoordinatesNull()
    {
        var valid = ApplicationSettings.Default with { OverlayPositionX = null, OverlayPositionY = null };
        valid.Validate();
        Assert.Null(valid.OverlayPositionX);
        Assert.Null(valid.OverlayPositionY);
    }

    [Theory]
    [InlineData(100, null)]
    [InlineData(null, 200)]
    public void ValidationRejectsPartialOverlayCoordinates(int? x, int? y)
    {
        var invalid = ApplicationSettings.Default with { OverlayPositionX = x, OverlayPositionY = y };
        var ex = Assert.Throws<ArgumentException>(() => invalid.Validate());
        Assert.Contains("must both be set or both be null", ex.Message);
    }

    [Fact]
    public void ValidationRejectsDurationBeyondUtcTimestampRange()
    {
        Assert.Throws<ArgumentException>(() => (ApplicationSettings.Default with
            { WorkDuration = TimeSpan.MaxValue }).Validate());
    }

    [Theory]
    [InlineData(80)]
    [InlineData(90)]
    [InlineData(100)]
    [InlineData(110)]
    [InlineData(125)]
    [InlineData(150)]
    public void ValidationAcceptsSupportedUiScalePercentages(int percent)
    {
        var settings = ApplicationSettings.Default with { UiScalePercent = percent };
        settings.Validate();
        Assert.Equal(percent, settings.UiScalePercent);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(85)]
    [InlineData(95)]
    [InlineData(105)]
    [InlineData(120)]
    [InlineData(130)]
    [InlineData(175)]
    [InlineData(200)]
    public void ValidationRejectsUnsupportedUiScalePercentages(int percent)
    {
        var settings = ApplicationSettings.Default with { UiScalePercent = percent };
        var ex = Assert.Throws<ArgumentException>(() => settings.Validate());
        Assert.Equal("UiScalePercent", ex.ParamName);
    }

    [Fact]
    public void UiScaleLevels_ConstantsAndCollectionsAreAccurate()
    {
        Assert.Equal(100, UiScaleLevels.DefaultPercent);
        Assert.Equal(80, UiScaleLevels.MinPercent);
        Assert.Equal(150, UiScaleLevels.MaxPercent);
        Assert.Equal([80, 90, 100, 110, 125, 150], UiScaleLevels.All);
        Assert.Equal(UiScaleLevels.All, UiScaleLevels.SupportedPercentages);
    }

    [Theory]
    [InlineData(80, true)]
    [InlineData(90, true)]
    [InlineData(100, true)]
    [InlineData(110, true)]
    [InlineData(125, true)]
    [InlineData(150, true)]
    [InlineData(70, false)]
    [InlineData(85, false)]
    [InlineData(105, false)]
    [InlineData(160, false)]
    [InlineData(0, false)]
    [InlineData(-80, false)]
    public void UiScaleLevels_IsValid_ValidatesDiscreteLevels(int percent, bool expected)
    {
        Assert.Equal(expected, UiScaleLevels.IsValid(percent));
    }

    [Theory]
    [InlineData(80, 0.80)]
    [InlineData(90, 0.90)]
    [InlineData(100, 1.00)]
    [InlineData(110, 1.10)]
    [InlineData(125, 1.25)]
    [InlineData(150, 1.50)]
    [InlineData(999, 1.00)]
    public void UiScaleLevels_ToFactor_ConvertsAccurately(int percent, double expectedFactor)
    {
        Assert.Equal(expectedFactor, UiScaleLevels.ToFactor(percent), precision: 2);
    }

    [Theory]
    [InlineData(80, 90)]
    [InlineData(90, 100)]
    [InlineData(100, 110)]
    [InlineData(110, 125)]
    [InlineData(125, 150)]
    [InlineData(150, 150)]
    [InlineData(70, 80)]
    [InlineData(105, 110)]
    [InlineData(160, 150)]
    public void UiScaleLevels_NextLevel_StepsUpAndClamps(int current, int expectedNext)
    {
        Assert.Equal(expectedNext, UiScaleLevels.NextLevel(current));
    }

    [Theory]
    [InlineData(150, 125)]
    [InlineData(125, 110)]
    [InlineData(110, 100)]
    [InlineData(100, 90)]
    [InlineData(90, 80)]
    [InlineData(80, 80)]
    [InlineData(200, 150)]
    [InlineData(105, 100)]
    [InlineData(50, 80)]
    public void UiScaleLevels_PreviousLevel_StepsDownAndClamps(int current, int expectedPrevious)
    {
        Assert.Equal(expectedPrevious, UiScaleLevels.PreviousLevel(current));
    }

    [Theory]
    [InlineData(1200, 1.50, 800)]
    [InlineData(1000, 1.00, 1000)]
    [InlineData(800, 0.80, 1000)]
    [InlineData(1200, 0.0, 1200)]
    [InlineData(1200, -1.0, 1200)]
    public void UiScaleLevels_CalculateEffectiveWidth_ComputesCorrectly(double width, double factor, double expected)
    {
        Assert.Equal(expected, UiScaleLevels.CalculateEffectiveWidth(width, factor), precision: 2);
    }

    [Theory]
    [InlineData(1200, 150, 800)]
    [InlineData(1000, 100, 1000)]
    [InlineData(800, 80, 1000)]
    public void UiScaleLevels_CalculateEffectiveWidth_WithPercent_ComputesCorrectly(double width, int percent, double expected)
    {
        Assert.Equal(expected, UiScaleLevels.CalculateEffectiveWidth(width, percent), precision: 2);
    }
}
