namespace FocusKey.Foundation.Settings;

public enum Contrast
{
    Standard = 0,
    HigherContrast = 1
}

public static class ContrastText
{
    public static string Format(Contrast contrast) => contrast switch
    {
        Contrast.Standard => "standard",
        Contrast.HigherContrast => "high",
        _ => throw new ArgumentOutOfRangeException(nameof(contrast), contrast, "Unsupported contrast.")
    };

    public static Contrast Parse(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "standard" or "" or null => Contrast.Standard,
        "high" or "higher" or "highercontrast" => Contrast.HigherContrast,
        _ => throw new FormatException($"Unsupported contrast value '{text}'.")
    };
}
