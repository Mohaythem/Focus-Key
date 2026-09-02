using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class SessionTypeTextTests
{
    [Theory]
    [InlineData(SessionType.Work, "work")]
    [InlineData(SessionType.Break, "break")]
    public void Format_UsesTheStableCode(SessionType type, string expected)
    {
        Assert.Equal(expected, SessionTypeText.Format(type));
    }

    [Theory]
    [InlineData(SessionType.Work)]
    [InlineData(SessionType.Break)]
    public void RoundTrip_PreservesTheType(SessionType type)
    {
        Assert.Equal(type, SessionTypeText.Parse(SessionTypeText.Format(type)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Work")]
    [InlineData("WORK")]
    [InlineData(" work")]
    [InlineData("1")]
    [InlineData("focus")]
    [InlineData("idle")]
    public void TryParse_RefusesUnknownText(string? text)
    {
        Assert.False(SessionTypeText.TryParse(text, out SessionType type));

        // The out value must not land on a real type when parsing failed.
        Assert.Equal(default, type);
        Assert.False(Enum.IsDefined(type));
    }

    [Fact]
    public void Parse_ThrowsOnUnknownText()
    {
        FormatException error = Assert.Throws<FormatException>(() => SessionTypeText.Parse("Work"));

        Assert.Contains("not a valid stored session type", error.Message);
    }

    [Fact]
    public void Format_RejectsUndefinedEnumValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionTypeText.Format((SessionType)99));
    }
}

public sealed class SessionStatusTextTests
{
    [Theory]
    [InlineData(SessionStatus.Running, "running")]
    [InlineData(SessionStatus.Completed, "completed")]
    [InlineData(SessionStatus.Stopped, "stopped")]
    [InlineData(SessionStatus.Interrupted, "interrupted")]
    public void Format_UsesTheStableCode(SessionStatus status, string expected)
    {
        Assert.Equal(expected, SessionStatusText.Format(status));
    }

    [Theory]
    [InlineData(SessionStatus.Running)]
    [InlineData(SessionStatus.Completed)]
    [InlineData(SessionStatus.Stopped)]
    [InlineData(SessionStatus.Interrupted)]
    public void RoundTrip_PreservesTheStatus(SessionStatus status)
    {
        Assert.Equal(status, SessionStatusText.Parse(SessionStatusText.Format(status)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Running")]
    [InlineData("RUNNING")]
    [InlineData("2")]
    [InlineData("paused")]
    [InlineData("cancelled")]
    [InlineData("finished")]
    public void TryParse_RefusesUnknownText(string? text)
    {
        Assert.False(SessionStatusText.TryParse(text, out SessionStatus status));
        Assert.Equal(default, status);
        Assert.False(Enum.IsDefined(status));
    }

    [Fact]
    public void Parse_ThrowsOnUnknownText()
    {
        Assert.Throws<FormatException>(() => SessionStatusText.Parse("done"));
    }

    [Fact]
    public void Format_RejectsUndefinedEnumValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SessionStatusText.Format((SessionStatus)42));
    }
}
