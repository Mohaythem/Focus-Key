namespace FocusKey.Foundation.Settings;

public enum TimeFormat
{
    TwentyFourHour = 0,
    TwelveHour = 1,
}

public static class TimeFormatText
{
    public const string TwentyFourHour = "24h";
    public const string TwelveHour = "12h";

    public static string Format(TimeFormat format) => format switch
    {
        TimeFormat.TwentyFourHour => TwentyFourHour,
        TimeFormat.TwelveHour => TwelveHour,
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, $"Unsupported time format '{format}'."),
    };

    public static TimeFormat Parse(string? text) =>
        TryParse(text, out var format)
            ? format
            : throw new FormatException($"Unsupported time format value '{text}'.");

    public static bool TryParse(string? text, out TimeFormat format)
    {
        string? normalized = text?.Trim();
        if (string.Equals(normalized, TwentyFourHour, StringComparison.OrdinalIgnoreCase))
        {
            format = TimeFormat.TwentyFourHour;
            return true;
        }

        if (string.Equals(normalized, TwelveHour, StringComparison.OrdinalIgnoreCase))
        {
            format = TimeFormat.TwelveHour;
            return true;
        }

        format = default;
        return false;
    }
}
