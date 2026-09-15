using FocusKey.Foundation.Settings;
using Microsoft.UI.Xaml.Media;

namespace FocusKey;

internal static class SessionColorBrush
{
    internal static SolidColorBrush Create(HexColor color) => new(Windows.UI.Color.FromArgb(255,
        Convert.ToByte(color.Value.Substring(1, 2), 16),
        Convert.ToByte(color.Value.Substring(3, 2), 16),
        Convert.ToByte(color.Value.Substring(5, 2), 16)));

    internal static SolidColorBrush CreateShaded(HexColor color, int amount)
    {
        byte r = (byte)Math.Clamp(Convert.ToByte(color.Value.Substring(1, 2), 16) + amount, 0, 255);
        byte g = (byte)Math.Clamp(Convert.ToByte(color.Value.Substring(3, 2), 16) + amount, 0, 255);
        byte b = (byte)Math.Clamp(Convert.ToByte(color.Value.Substring(5, 2), 16) + amount, 0, 255);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
    }

    internal static SolidColorBrush CreateAlpha(HexColor color, double alpha)
    {
        byte a = (byte)Math.Clamp((int)Math.Round(alpha * 255), 0, 255);
        byte r = Convert.ToByte(color.Value.Substring(1, 2), 16);
        byte g = Convert.ToByte(color.Value.Substring(3, 2), 16);
        byte b = Convert.ToByte(color.Value.Substring(5, 2), 16);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
    }

    internal static SolidColorBrush CreateElevated(HexColor color, bool isDark, bool isHovered)
    {
        byte cr = Convert.ToByte(color.Value.Substring(1, 2), 16);
        byte cg = Convert.ToByte(color.Value.Substring(3, 2), 16);
        byte cb = Convert.ToByte(color.Value.Substring(5, 2), 16);

        if (isDark)
        {
            double alpha = isHovered ? 0.36 : 0.24;
            byte r = (byte)Math.Clamp((int)Math.Round(cr * alpha + 36 * (1 - alpha)), 0, 255);
            byte g = (byte)Math.Clamp((int)Math.Round(cg * alpha + 36 * (1 - alpha)), 0, 255);
            byte b = (byte)Math.Clamp((int)Math.Round(cb * alpha + 36 * (1 - alpha)), 0, 255);
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
        }
        else
        {
            double alpha = isHovered ? 0.24 : 0.16;
            byte r = (byte)Math.Clamp((int)Math.Round(cr * alpha + 255 * (1 - alpha)), 0, 255);
            byte g = (byte)Math.Clamp((int)Math.Round(cg * alpha + 255 * (1 - alpha)), 0, 255);
            byte b = (byte)Math.Clamp((int)Math.Round(cb * alpha + 255 * (1 - alpha)), 0, 255);
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
        }
    }

    /// <summary>Curated default session colors defined by the Visual System.</summary>
    internal static class Defaults
    {
        internal static readonly HexColor Work = HexColor.Parse("#2F8F83");
        internal static readonly HexColor Break = HexColor.Parse("#7667B8");
    }

    /// <summary>Creates a subtle surface tint (e.g. 5% opacity) over the base theme surface.</summary>
    internal static SolidColorBrush CreateTint(HexColor color, bool isDark, double opacity = 0.05)
    {
        byte cr = Convert.ToByte(color.Value.Substring(1, 2), 16);
        byte cg = Convert.ToByte(color.Value.Substring(3, 2), 16);
        byte cb = Convert.ToByte(color.Value.Substring(5, 2), 16);

        byte bg = isDark ? (byte)30 : (byte)255;
        byte r = (byte)Math.Clamp((int)Math.Round(cr * opacity + bg * (1 - opacity)), 0, 255);
        byte g = (byte)Math.Clamp((int)Math.Round(cg * opacity + bg * (1 - opacity)), 0, 255);
        byte b = (byte)Math.Clamp((int)Math.Round(cb * opacity + bg * (1 - opacity)), 0, 255);
        return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, b));
    }

    /// <summary>Creates a subtle semantic border brush (e.g. 30% alpha for idle choice card, 80% for hover).</summary>
    internal static SolidColorBrush CreateSemanticBorder(HexColor color, double alpha = 0.30) =>
        CreateAlpha(color, alpha);
}
