namespace FocusKey.Foundation.Sessions;

/// <summary>
/// Orders startup recovery, normal engine requests, and graceful shutdown for one application
/// lifetime. Stores lifecycle results only; all active-session facts continue to come from SQLite.
/// </summary>
public sealed class SessionCoordinator
{
    private readonly SessionEngine _engine;
    private readonly SessionRecovery _recovery;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private SessionRecoveryResult? _startupResult;
    private SessionRecoveryResult? _shutdownResult;

    public SessionCoordinator(
        ISessionRepository sessions,
        TimeProvider? timeProvider = null,
        SessionDurations? durations = null,
        ISessionDurationProvider? durationProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _engine = new SessionEngine(sessions, timeProvider, durations, durationProvider);
        _recovery = new SessionRecovery(sessions, timeProvider);
    }

    /// <summary>
    /// Performs startup recovery once successfully. Repeated calls return the original result,
    /// not a new observation. Failure/cancellation is not cached and permits an explicit retry.
    /// </summary>
    public async Task<SessionRecoveryResult> InitializeAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdownResult is not null)
            {
                throw new InvalidOperationException("The session subsystem has shut down.");
            }

            return _startupResult ??= await _recovery.RecoverStartupAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public Task<SessionRecord> StartAsync(SessionType type, CancellationToken cancellationToken = default) =>
        UseEngineAsync(token => _engine.StartAsync(type, token), cancellationToken);

    public Task<SessionSnapshot?> GetActiveAsync(CancellationToken cancellationToken = default) =>
        UseEngineAsync(_engine.GetActiveAsync, cancellationToken);

    public Task<SessionDurations> GetDurationsAsync(CancellationToken cancellationToken = default) =>
        UseEngineAsync(_engine.GetDurationsAsync, cancellationToken);

    public Task<SessionOutcome> StopAsync(CancellationToken cancellationToken = default) =>
        UseEngineAsync(_engine.StopAsync, cancellationToken);

    public Task<SessionOutcome> StopAsync(SessionId expectedId, CancellationToken cancellationToken = default) =>
        UseEngineAsync(token => _engine.StopAsync(expectedId, token), cancellationToken);

    public Task<SessionOutcome> CompleteIfDueAsync(CancellationToken cancellationToken = default) =>
        UseEngineAsync(_engine.CompleteIfDueAsync, cancellationToken);

    /// <summary>
    /// Finishes the current session and closes this coordinator to further normal operations.
    /// Successful repeated calls return the original result. A failed or cancelled shutdown leaves
    /// the coordinator usable so the application can remain open and retry instead of losing data.
    /// </summary>
    public async Task<SessionRecoveryResult> ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_shutdownResult is not null)
            {
                return _shutdownResult;
            }

            EnsureReady();
            return _shutdownResult ??= await _recovery.FinishShutdownAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private async Task<T> UseEngineAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EnsureReady();
            return await operation(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    private void EnsureReady()
    {
        if (_startupResult is null)
        {
            throw new InvalidOperationException("Initialize the session subsystem before using it.");
        }

        if (_shutdownResult is not null)
        {
            throw new InvalidOperationException("The session subsystem has shut down.");
        }
    }
}
