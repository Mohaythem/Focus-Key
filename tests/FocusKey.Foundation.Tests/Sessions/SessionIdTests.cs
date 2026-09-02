using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class SessionIdTests
{
    [Fact]
    public void New_ProducesDistinctNonEmptyIds()
    {
        SessionId first = SessionId.New();
        SessionId second = SessionId.New();

        Assert.NotEqual(first, second);
        Assert.False(first.IsEmpty);
    }

    [Fact]
    public void Constructor_RejectsTheEmptyGuid()
    {
        Assert.Throws<ArgumentException>(() => new SessionId(Guid.Empty));
    }

    [Fact]
    public void Default_IsRecognizedAsEmpty()
    {
        Assert.True(default(SessionId).IsEmpty);
    }

    [Fact]
    public void ToText_IsThirtySixCharacterLowercase()
    {
        string text = SessionId.New().ToText();

        Assert.Equal(SessionId.TextLength, text.Length);
        Assert.Equal(text.ToLowerInvariant(), text);
    }

    [Fact]
    public void RoundTrip_PreservesTheValue()
    {
        SessionId original = SessionId.New();

        SessionId parsed = SessionId.Parse(original.ToText());

        Assert.Equal(original, parsed);
        Assert.Equal(original.Value, parsed.Value);
    }

    [Fact]
    public void Equality_IsByValue()
    {
        var guid = Guid.NewGuid();

        Assert.Equal(new SessionId(guid), new SessionId(guid));
        Assert.True(new SessionId(guid) == new SessionId(guid));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("{c1a0f9d6-4b1e-4f5a-9c3d-1b2a3c4d5e6f}")]
    [InlineData("c1a0f9d64b1e4f5a9c3d1b2a3c4d5e6f")]
    public void TryParse_RejectsAnythingOutsideTheCanonicalForm(string? text)
    {
        Assert.False(SessionId.TryParse(text, out SessionId id));
        Assert.True(id.IsEmpty);
    }

    [Fact]
    public void Parse_ThrowsOnInvalidText()
    {
        Assert.Throws<FormatException>(() => SessionId.Parse("00000000-0000-0000-0000-000000000000"));
    }
}
