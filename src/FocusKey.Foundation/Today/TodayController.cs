using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Today;

public enum MainPage { Today, Reports, Settings }

/// <summary>UI-thread presentation and refresh ordering; no timer or session authority.</summary>
public sealed class TodayController : IDisposable
{
    private readonly Func<CancellationToken, Task<TodaySnapshot>> _read;
    private readonly Func<SessionType, CancellationToken, Task<SessionRecord>>? _start;
    private readonly Func<SessionId, CancellationToken, Task<SessionOutcome>> _stop;
    private readonly Func<SessionId, CancellationToken, Task<SessionOutcome>>? _pause;
    private readonly Func<SessionId, CancellationToken, Task<SessionOutcome>>? _continue;
    private readonly Action<Exception> _report;
    private int _generation;
    private bool _visible;
    private bool _disposed;

    public MainPage Page { get; private set; } = MainPage.Today;
    public TodaySnapshot? Snapshot { get; private set; }
    public bool IsRefreshing { get; private set; }
    public bool IsStopping { get; private set; }
    public bool IsStarting { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;

    public TodayController(
        Func<CancellationToken, Task<TodaySnapshot>> read,
        Func<SessionType, CancellationToken, Task<SessionRecord>>? start,
        Func<SessionId, CancellationToken, Task<SessionOutcome>> stop,
        Action<Exception> report,
        Func<SessionId, CancellationToken, Task<SessionOutcome>>? pause = null,
        Func<SessionId, CancellationToken, Task<SessionOutcome>>? @continue = null)
    {
        _read = read ?? throw new ArgumentNullException(nameof(read));
        _start = start;
        _stop = stop ?? throw new ArgumentNullException(nameof(stop));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _pause = pause;
        _continue = @continue;
    }

    public TodayController(
        Func<CancellationToken, Task<TodaySnapshot>> read,
        Func<SessionId, CancellationToken, Task<SessionOutcome>> stop,
        Action<Exception> report)
        : this(read, null, stop, report)
    {
    }

    public async Task OpenAsync()
    {
        if (_disposed) return;
        _visible = true;
        await NavigateAsync(MainPage.Today);
    }

    public async Task NavigateAsync(MainPage page)
    {
        if (_disposed) return;
        if (!Enum.IsDefined(page)) throw new ArgumentOutOfRangeException(nameof(page));
        Page = page;
        ++_generation;
        IsRefreshing = false;
        Changed?.Invoke();
        if (page == MainPage.Today) await RefreshAsync();
    }

    public void Hide()
    {
        _visible = false;
        ++_generation;
        IsRefreshing = false;
    }

    public async Task RefreshAsync()
    {
        if (_disposed || !_visible || Page != MainPage.Today) return;
        int generation = ++_generation;
        IsRefreshing = true;
        Changed?.Invoke();
        try
        {
            TodaySnapshot snapshot = await _read(CancellationToken.None);
            if (generation != _generation || _disposed) return;
            Snapshot = snapshot;
            Error = null;
        }
        catch (Exception exception)
        {
            if (generation == _generation && !_disposed) Error = "Could not load Today. Try Refresh again.";
            _report(exception);
        }
        finally
        {
            if (generation == _generation && !_disposed)
            {
                IsRefreshing = false;
                Changed?.Invoke();
            }
        }
    }

    public async Task PauseAsync()
    {
        if (_disposed || !_visible || Page != MainPage.Today || IsStopping || IsStarting || IsRefreshing || Error is not null || Snapshot?.Running is not { } running || _pause is null) return;
        IsStopping = true;
        Changed?.Invoke();
        try
        {
            await _pause(running.Id, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            if (!_disposed) Error = "Could not pause the session. Refresh and try again.";
            _report(ex);
        }
        finally
        {
            IsStopping = false;
            if (!_disposed) Changed?.Invoke();
        }
    }

    public async Task ContinueAsync()
    {
        if (_disposed || !_visible || Page != MainPage.Today || IsStopping || IsStarting || IsRefreshing || Error is not null || Snapshot?.Paused is not { } paused || _continue is null) return;
        IsStarting = true;
        Changed?.Invoke();
        try
        {
            await _continue(paused.Id, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            if (!_disposed) Error = "Could not continue the session. Refresh and try again.";
            _report(ex);
        }
        finally
        {
            IsStarting = false;
            if (!_disposed) Changed?.Invoke();
        }
    }

    public async Task StartNewAsync()
    {
        if (_disposed || !_visible || Page != MainPage.Today || IsStopping || IsStarting || IsRefreshing || Error is not null || Snapshot?.Paused is not { } paused || _stop is null) return;
        IsStopping = true;
        Changed?.Invoke();
        try
        {
            await _stop(paused.Id, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            if (!_disposed) Error = "Could not finalize the paused session. Refresh and try again.";
            _report(ex);
        }
        finally
        {
            IsStopping = false;
            if (!_disposed) Changed?.Invoke();
        }
    }

    public async Task StartAsync(SessionType type)
    {
        if (_disposed || !_visible || Page != MainPage.Today || IsStarting || IsStopping || IsRefreshing || Error is not null || Snapshot?.Running is not null || _start is null) return;

        if (Snapshot?.Paused is { } paused)
        {
            if (paused.Type == type)
            {
                await ContinueAsync();
                return;
            }

            // Alternate type: stop paused session before starting new
            await _stop(paused.Id, CancellationToken.None);
        }

        IsStarting = true;
        Changed?.Invoke();
        try
        {
            await _start(type, CancellationToken.None);
            await RefreshAsync();
        }
        catch (ActiveSessionAlreadyExistsException)
        {
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            if (!_disposed) Error = "Could not start the session. Refresh and try again.";
            _report(exception);
        }
        finally
        {
            IsStarting = false;
            if (!_disposed) Changed?.Invoke();
        }
    }

    public async Task StopAsync()
    {
        SessionRecord? active = Snapshot?.Running ?? Snapshot?.Paused;
        if (_disposed || !_visible || Page != MainPage.Today || IsStopping || IsStarting || IsRefreshing || Error is not null || active is null) return;
        IsStopping = true;
        Changed?.Invoke();
        try
        {
            await _stop(active.Id, CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            if (!_disposed) Error = "Could not stop the session. Refresh and try again.";
            _report(exception);
        }
        finally
        {
            IsStopping = false;
            if (!_disposed) Changed?.Invoke();
        }
    }

    public void Dispose() { Hide(); _disposed = true; Changed = null; }
}
