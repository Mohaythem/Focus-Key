namespace FocusKey.Foundation.Overlay;

/// <summary>Formats validated session durations for the compact Quick Overlay cards.</summary>
public static class QuickOverlayDurationFormatter
{
    public static string Format(TimeSpan? duration)
    {
        if (duration is null) return "--:--";
        long totalSeconds = checked((long)duration.Value.TotalSeconds);
        long hours = totalSeconds / 3600;
        long minutes = totalSeconds % 3600 / 60;
        long seconds = totalSeconds % 60;
        return hours == 0 ? $"{minutes}:{seconds:00}" : $"{hours}:{minutes:00}:{seconds:00}";
    }
}
