using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Overlay;

public sealed record QuickOverlayState(SessionType Selected, bool IsBusy, bool CanStart, string? Feedback);

/// <summary>One reusable native utility window. Calls and events belong to the UI thread.</summary>
public interface IQuickOverlayView : IDisposable
{
    event Action<SessionType>? SelectionRequested;
    event Action? StartRequested;
    event Action? DismissRequested;
    void Render(QuickOverlayState state);
    void ShowAndFocus();
    void Hide();
}
