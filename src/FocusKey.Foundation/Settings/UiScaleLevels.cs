namespace FocusKey.Foundation.Settings;

/// <summary>
/// Authoritative definition and calculations for discrete application UI scale levels.
/// </summary>
public static class UiScaleLevels
{
    public const int DefaultPercent = 100;
    public const int MinPercent = 80;
    public const int MaxPercent = 150;

    public static readonly int[] All = [80, 90, 100, 110, 125, 150];

    public static IReadOnlyList<int> SupportedPercentages => All;

    public static bool IsValid(int percent) =>
        percent is 80 or 90 or 100 or 110 or 125 or 150;

    public static double ToFactor(int percent) =>
        (IsValid(percent) ? percent : DefaultPercent) / 100.0;

    public static int NextLevel(int currentPercent)
    {
        for (int i = 0; i < All.Length; i++)
        {
            if (All[i] > currentPercent)
                return All[i];
        }
        return MaxPercent;
    }

    public static int PreviousLevel(int currentPercent)
    {
        for (int i = All.Length - 1; i >= 0; i--)
        {
            if (All[i] < currentPercent)
                return All[i];
        }
        return MinPercent;
    }

    public static double CalculateEffectiveWidth(double actualWidth, double factor) =>
        factor > 0 && !double.IsNaN(factor) && !double.IsInfinity(factor) ? actualWidth / factor : actualWidth;

    public static double CalculateEffectiveWidth(double actualWidth, int scalePercent) =>
        CalculateEffectiveWidth(actualWidth, ToFactor(scalePercent));
}
