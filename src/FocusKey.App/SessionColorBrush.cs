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
}
