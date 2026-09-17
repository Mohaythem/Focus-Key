using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Overlay;

public sealed record QuickOverlayState(SessionType Selected, bool IsBusy, bool CanStart, string? Feedback)
{
    /// <summary>The current persisted values to display, or null while they are being loaded.</summary>
    public SessionDurations? Durations { get; init; }
    public SessionSnapshot? Active { get; init; }
}

/// <summary>One reusable native utility window. Calls and events belong to the UI thread.</summary>
public interface IQuickOverlayView : IDisposable
{
    event Action<SessionType>? SelectionRequested;
    event Action? StartRequested;
    event Action? PauseRequested;
    event Action? StartNewRequested;
    event Action? StopRequested;
    event Action? DismissRequested;
    void Render(QuickOverlayState state);
    void ShowAndFocus();
    void Hide();
}
