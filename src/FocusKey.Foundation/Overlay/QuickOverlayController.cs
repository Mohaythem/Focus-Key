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
    private readonly Func<CancellationToken, Task<SessionDurations>> _getDurations;
    private readonly Func<SessionType, CancellationToken, Task<SessionRecord>> _start;
    private readonly Func<SessionId, CancellationToken, Task<SessionOutcome>> _stop;
    private IQuickOverlayView? _view;
    private bool _visible;
    private bool _starting;
    private bool _disposed;
    private int _observation;
    private QuickOverlayState _state = new(SessionType.Work, false, false, null);

    public QuickOverlayController(Func<IQuickOverlayView> createView,
        Func<CancellationToken, Task<SessionSnapshot?>> getActive,
        Func<CancellationToken, Task<SessionDurations>> getDurations,
        Func<SessionType, CancellationToken, Task<SessionRecord>> start,
        Func<SessionId, CancellationToken, Task<SessionOutcome>> stop)
    {
        ArgumentNullException.ThrowIfNull(createView);
        ArgumentNullException.ThrowIfNull(getActive);
        ArgumentNullException.ThrowIfNull(getDurations);
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(stop);
        _createView = createView;
        _getActive = getActive;
        _getDurations = getDurations;
        _start = start;
        _stop = stop;
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
            _view.StopRequested += OnStopRequested;
            _view.DismissRequested += Dismiss;
        }
        if (_visible || _starting)
        {
            _visible = true;
            _view.ShowAndFocus();
            if (!_starting && !_state.IsBusy) await RefreshIfVisibleAsync();
            return;
        }

        _visible = true;
        int observation = ++_observation;
        SetState(new(SessionType.Work, true, false, null));
        _view.ShowAndFocus();
        try
        {
            (SessionSnapshot? active, SessionDurations durations) = await ReadStateAsync();
            if (!IsCurrent(observation)) return;
            SetState(new(SessionType.Work, false, active is null, null)
                { Durations = durations, Active = active });
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

    /// <summary>Observe authoritative state without opening a hidden view.</summary>
    public async Task RefreshIfVisibleAsync()
    {
        if (_disposed || !_visible || _starting) return;
        int observation = ++_observation;
        try
        {
            (SessionSnapshot? active, SessionDurations durations) = await ReadStateAsync();
            if (IsCurrent(observation) && !_starting)
                SetState(new(_state.Selected, false, active is null, null)
                    { Durations = durations, Active = active });
        }
        catch (Exception exception)
        {
            if (IsCurrent(observation))
                SetState(new(_state.Selected, false, false, "Could not read session state. Close and reopen to retry."));
            ErrorOccurred?.Invoke(exception);
        }
    }

    public async Task StartAsync()
    {
        if (_disposed || !_visible || _starting || !_state.CanStart || _state.IsBusy) return;
        _starting = true;
        ++_observation;
        bool reconcile = true;
        SessionType selected = _state.Selected;
        SetState(_state with { IsBusy = true, CanStart = false, Feedback = null });
        try
        {
            // An accepted Start is not cancelled by Escape: a committed write must stay successful.
            await _start(selected, CancellationToken.None);
        }
        catch (ActiveSessionAlreadyExistsException)
        {
            // A different request may have won; display the actual persisted session.
        }
        catch (Exception exception)
        {
            reconcile = false;
            if (!_disposed) SetState(new(selected, false, true, "Could not start the session. Please try again."));
            ErrorOccurred?.Invoke(exception);
        }
        finally { _starting = false; }
        if (reconcile) await RefreshIfVisibleAsync();
    }

    public async Task StopAsync()
    {
        if (_disposed || !_visible || _starting || _state.IsBusy || _state.Active is not { } active) return;
        _starting = true;
        ++_observation;
        bool reconcile = true;
        SetState(_state with { IsBusy = true, Feedback = null });
        try { await _stop(active.Id, CancellationToken.None); }
        catch (Exception exception)
        {
            reconcile = false;
            if (!_disposed) SetState(_state with { IsBusy = false, Feedback = "Could not stop the session. Please try again." });
            ErrorOccurred?.Invoke(exception);
        }
        finally { _starting = false; }
        if (reconcile) await RefreshIfVisibleAsync();
    }

    public void Dismiss()
    {
        if (_disposed) return;
        ++_observation;
        _visible = false;
        _view?.Hide();
    }

    private async void OnStartRequested() => await StartAsync();
    private async void OnStopRequested() => await StopAsync();
    private async Task<(SessionSnapshot? Active, SessionDurations Durations)> ReadStateAsync()
    {
        Task<SessionSnapshot?> active = _getActive(CancellationToken.None);
        Task<SessionDurations> durations = _getDurations(CancellationToken.None);
        await Task.WhenAll(active, durations);
        return (await active, await durations);
    }

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
        _view.StopRequested -= OnStopRequested;
        _view.DismissRequested -= Dismiss;
        _view.Dispose();
    }
}
