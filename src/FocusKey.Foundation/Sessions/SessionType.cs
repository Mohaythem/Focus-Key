namespace FocusKey.Foundation.Sessions;

/// <summary>The two kinds of session Focus Key records. There is no third kind in V1.</summary>
public enum SessionType
{
    Work = 1,
    Break = 2,
}

/// <summary>
/// Maps <see cref="SessionType"/> to and from the exact text stored in SQLite.
/// Mapping is explicit in both directions: unknown text never becomes Work or Break, and
/// <see cref="Enum"/> parsing is deliberately not used so numeric or renamed values cannot slip in.
/// </summary>
public static class SessionTypeText
{
    public const string Work = "work";
    public const string Break = "break";

    public static string Format(SessionType type) => type switch
    {
        SessionType.Work => Work,
        SessionType.Break => Break,
        _ => throw new ArgumentOutOfRangeException(
            nameof(type), type, "Unknown session type cannot be persisted."),
    };

    /// <exception cref="FormatException">The stored text is not a known session type.</exception>
    public static SessionType Parse(string? text) =>
        TryParse(text, out SessionType type)
            ? type
            : throw new FormatException(
                $"'{text}' is not a valid stored session type. Expected '{Work}' or '{Break}'.");

    public static bool TryParse(string? text, out SessionType type)
    {
        switch (text)
        {
            case Work:
                type = SessionType.Work;
                return true;
            case Break:
                type = SessionType.Break;
                return true;
            default:
                type = default;
                return false;
        }
    }
}
