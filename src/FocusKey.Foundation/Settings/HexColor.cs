using System.Globalization;

namespace FocusKey.Foundation.Settings;

/// <summary>An opaque RGB color in canonical uppercase <c>#RRGGBB</c> form.</summary>
public readonly record struct HexColor
{
    private HexColor(string value) => Value = value;

    public string Value { get; }

    public static HexColor Parse(string value)
    {
        if (!TryParse(value, out HexColor color))
            throw new ArgumentException("A color must use exactly six hexadecimal RGB digits: #RRGGBB.", nameof(value));
        return color;
    }

    public static bool TryParse(string? value, out HexColor color)
    {
        color = default;
        if (value is null || value.Length != 7 || value[0] != '#') return false;
        for (int index = 1; index < value.Length; index++)
            if (!Uri.IsHexDigit(value[index])) return false;
        color = new HexColor(string.Create(CultureInfo.InvariantCulture, $"#{value[1..].ToUpperInvariant()}"));
        return true;
    }

    public override string ToString() => Value ?? string.Empty;
}
