namespace FocusKey.Foundation.Settings;

/// <summary>Persisted semantic colors and opaque text with the greater black/white contrast.</summary>
public sealed record SessionColors(HexColor Work, HexColor Break)
{
    public static SessionColors From(ApplicationSettings settings) => new(settings.WorkColor, settings.BreakColor);

    public static HexColor Foreground(HexColor background)
    {
        HexColor.Parse(background.Value); // Reject an uninitialized HexColor too.
        double Channel(int offset)
        {
            double value = Convert.ToByte(background.Value.Substring(offset, 2), 16) / 255.0;
            return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
        }
        double luminance = 0.2126 * Channel(1) + 0.7152 * Channel(3) + 0.0722 * Channel(5);
        return HexColor.Parse((luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05) ? "#000000" : "#FFFFFF");
    }
}
