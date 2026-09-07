using FocusKey.Foundation.Settings;
using Microsoft.UI.Xaml.Media;

namespace FocusKey;

internal static class SessionColorBrush
{
    internal static SolidColorBrush Create(HexColor color) => new(Windows.UI.Color.FromArgb(255,
        Convert.ToByte(color.Value.Substring(1, 2), 16),
        Convert.ToByte(color.Value.Substring(3, 2), 16),
        Convert.ToByte(color.Value.Substring(5, 2), 16)));
}
