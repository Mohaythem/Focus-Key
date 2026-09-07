namespace FocusKey.Foundation.Settings;

public enum Appearance
{
    System,
    Light,
    Dark,
}

internal static class AppearanceText
{
    internal const string System = "system";
    internal const string Light = "light";
    internal const string Dark = "dark";

    internal static string Format(Appearance value) => value switch
    {
        Appearance.System => System,
        Appearance.Light => Light,
        Appearance.Dark => Dark,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unsupported appearance."),
    };

    internal static Appearance Parse(string value) => value switch
    {
        System => Appearance.System,
        Light => Appearance.Light,
        Dark => Appearance.Dark,
        _ => throw new InvalidDataException($"Persisted appearance '{value}' is unsupported."),
    };
}
