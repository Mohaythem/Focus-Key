using FocusKey.Foundation.Settings;
using Microsoft.UI.Xaml;

namespace FocusKey;

/// <summary>
/// Dedicated theme-aware palette engineered specifically for Reports data visualization,
/// fully independent of user-configured Work/Break session colors.
/// </summary>
internal sealed record ReportsPalette(HexColor Work, HexColor Break)
{
    public static ReportsPalette Resolve(ElementTheme theme, Contrast contrast = Contrast.Standard)
    {
        bool isLight = theme == ElementTheme.Light;
        bool higher = contrast == Contrast.HigherContrast;

        if (higher)
        {
            return isLight
                ? new ReportsPalette(HexColor.Parse("#005A5C"), HexColor.Parse("#313660"))
                : new ReportsPalette(HexColor.Parse("#4EE8E0"), HexColor.Parse("#9DA4E8"));
        }

        return isLight
            ? new ReportsPalette(HexColor.Parse("#1A686B"), HexColor.Parse("#484C72"))
            : new ReportsPalette(HexColor.Parse("#2BB0A6"), HexColor.Parse("#6366F1"));
    }
}
