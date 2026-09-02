using System.Globalization;

namespace FocusKey.Foundation.Data;

/// <summary>
/// The one text representation Focus Key uses for every timestamp it stores.
/// Fixed width, UTC, sortable as plain text, and exact to a .NET tick so values round-trip
/// without loss.
/// </summary>
public static class UtcTimestamp
{
    /// <summary>Canonical pattern, for example <c>2026-09-02T05:19:43.7134567Z</c>.</summary>
    public const string Pattern = "yyyy-MM-ddTHH:mm:ss.fffffff'Z'";

    /// <summary>Converts to UTC first, so a local or offset value never leaks into storage.</summary>
    public static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(Pattern, CultureInfo.InvariantCulture);

    /// <exception cref="FormatException">The text is not in the canonical form.</exception>
    public static DateTimeOffset Parse(string? text) =>
        TryParse(text, out DateTimeOffset value)
            ? value
            : throw new FormatException(
                $"'{text}' is not a canonical UTC timestamp. Expected the form 2026-09-02T05:19:43.7134567Z.");

    public static bool TryParse(string? text, out DateTimeOffset value)
    {
        if (string.IsNullOrEmpty(text))
        {
            value = default;
            return false;
        }

        return DateTimeOffset.TryParseExact(
            text,
            Pattern,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out value);
    }
}
