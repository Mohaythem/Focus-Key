using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Shell;

namespace FocusKey.Foundation.Overlay;

/// <summary>
/// UI-thread presentation state only. Reads and starts delegate to the existing session
/// coordinator; no session rules, timing, completion or mutable session authority live here.
/// </summary>
public sealed class QuickOverlayController : IDisposable
{
    private readonly Func<IQuickOverlayView> _createView;
    private readonly Func<CancellationToken, Task<SessionSnapshot?>> _getActive;
    private readonly Func<SessionType, CancellationToken, Task<SessionRecord>> _start;
    private IQuickOverlayView? _view;
    private bool _visible;
    private bool _starting;
    private bool _disposed;
    private int _observation;
    private QuickOverlayState _state = new(SessionType.Work, false, false, null);

    public QuickOverlayController(Func<IQuickOverlayView> createView,
        Func<CancellationToken, Task<SessionSnapshot?>> getActive,
        Func<SessionType, CancellationToken, Task<SessionRecord>> start)
    {
        ArgumentNullException.ThrowIfNull(createView);
        ArgumentNullException.ThrowIfNull(getActive);
        ArgumentNullException.ThrowIfNull(start);
        _createView = createView;
        _getActive = getActive;
        _start = start;
    }

    public event Action<Exception>? ErrorOccurred;

    public async Task HandleActivationAsync(ShellActivationKind kind)
    {
        if (_disposed || kind != ShellActivationKind.Hotkey) return;
        if (_view is null)
        {
            _view = _createView();
            _view.SelectionRequested += Select;
            _view.StartRequested += OnStartRequested;
            _view.DismissRequested += Dismiss;
        }
        if (_visible || _starting)
        {
            _visible = true;
            _view.ShowAndFocus();
            return;
        }

        _visible = true;
        int observation = ++_observation;
        SetState(new(SessionType.Work, true, false, null));
        _view.ShowAndFocus();
        try
        {
            SessionSnapshot? active = await _getActive(CancellationToken.None);
            if (!IsCurrent(observation)) return;
            SetState(new(SessionType.Work, false, active is null,
                active is null ? null : $"A {active.Type.ToString().ToLowerInvariant()} session is already running."));
        }
        catch (Exception exception)
        {
            if (IsCurrent(observation))
                SetState(new(SessionType.Work, false, false, "Could not read session state. Close and reopen to retry."));
            ErrorOccurred?.Invoke(exception);
        }
    }

    public void Select(SessionType type)
    {
        if (_disposed || !_visible || _state.IsBusy || !_state.CanStart) return;
        if (type is not (SessionType.Work or SessionType.Break)) throw new ArgumentOutOfRangeException(nameof(type));
        SetState(_state with { Selected = type });
    }

    public async Task StartAsync()
    {
        if (_disposed || !_visible || _starting || !_state.CanStart || _state.IsBusy) return;
        _starting = true;
        SessionType selected = _state.Selected;
        SetState(_state with { IsBusy = true, CanStart = false, Feedback = null });
        try
        {
            // An accepted Start is not cancelled by Escape: a committed write must stay successful.
            await _start(selected, CancellationToken.None);
            Dismiss();
        }
        catch (ActiveSessionAlreadyExistsException)
        {
            if (!_disposed) SetState(new(selected, false, false, "A session is already running."));
        }
        catch (Exception exception)
        {
            if (!_disposed) SetState(new(selected, false, true, "Could not start the session. Please try again."));
            ErrorOccurred?.Invoke(exception);
        }
        finally { _starting = false; }
    }

    public void Dismiss()
    {
        if (_disposed) return;
        ++_observation;
        _visible = false;
        _view?.Hide();
    }

    private async void OnStartRequested() => await StartAsync();
    private bool IsCurrent(int observation) => !_disposed && _visible && observation == _observation;
    private void SetState(QuickOverlayState state)
    {
        _state = state;
        _view?.Render(state);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Dismiss();
        _disposed = true;
        if (_view is null) return;
        _view.SelectionRequested -= Select;
        _view.StartRequested -= OnStartRequested;
        _view.DismissRequested -= Dismiss;
        _view.Dispose();
    }
}
