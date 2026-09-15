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
    public void ValidationRejectsDurationBeyondUtcTimestampRange()
    {
        Assert.Throws<ArgumentException>(() => (ApplicationSettings.Default with
            { WorkDuration = TimeSpan.MaxValue }).Validate());
    }
}
