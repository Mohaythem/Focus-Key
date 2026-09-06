namespace FocusKey.Foundation.Sessions;

/// <summary>
/// Application-lifetime deadline scheduling. The session coordinator remains the only authority
/// for transitions. No timer runs while idle, and callbacks never invent an end timestamp.
/// </summary>
public sealed class CompletionCoordinator : IDisposable
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaximumWait = TimeSpan.FromDays(1);
    private readonly SessionCoordinator _sessions;
    private readonly TimeProvider _time;
    private readonly Func<SessionRecord, Task> _notify;
    private readonly Action<Exception> _report;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ITimer _timer;
    private volatile bool _disposed;
    private bool _shutdown;

    public CompletionCoordinator(SessionCoordinator sessions, Func<SessionRecord, Task> notify,
        Action<Exception> report, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(notify);
        ArgumentNullException.ThrowIfNull(report);
        _sessions = sessions;
        _notify = notify;
        _report = report;
        _time = timeProvider ?? TimeProvider.System;
        _timer = _time.CreateTimer(_ => RequestEvaluation(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public async Task<SessionRecord> StartAsync(SessionType type, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_shutdown) throw new InvalidOperationException("Completion coordination has shut down.");
            SessionRecord session = await _sessions.StartAsync(type, cancellationToken).ConfigureAwait(false);
            // Accepted persistence is never turned into a failed Start by caller cancellation.
            Arm(session.PlannedEndAt - _time.GetUtcNow());
            return session;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Safe entry point for native clock/resume signals and the one-shot timer.</summary>
    public void RequestEvaluation() => _ = EvaluateAsync();

    public async Task EvaluateAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed || _shutdown) return;
            Disarm();
            try
            {
                SessionOutcome result = await _sessions.CompleteIfDueAsync().ConfigureAwait(false);
                if (result.Kind == SessionOutcomeKind.Completed)
                {
                    // Only the winning durable transition emits UX. Never replay history or retry
                    // an uncertain OS submission: doing so could duplicate a notification/sound.
                    await NotifyAsync(result.Session!).ConfigureAwait(false);
                }
                SessionSnapshot? active = await _sessions.GetActiveAsync().ConfigureAwait(false);
                if (active is not null)
                {
                    TimeSpan remaining = active.PlannedEndAt - _time.GetUtcNow();
                    Arm(result.Kind == SessionOutcomeKind.Conflict && remaining <= TimeSpan.Zero ? RetryDelay : remaining);
                }
            }
            catch (Exception exception)
            {
                Report(exception);
                Arm(RetryDelay);
            }
        }
        finally { _gate.Release(); }
    }

    public async Task<SessionRecoveryResult> ShutdownAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            Disarm();
            try
            {
                SessionRecoveryResult result = await _sessions.ShutdownAsync().ConfigureAwait(false);
                bool firstShutdown = !_shutdown;
                _shutdown = true;
                // Recovery may win the due transition when Exit races the timer. Its cached
                // repeated result must not emit UX again. Interrupted shutdown remains silent.
                if (firstShutdown && result.Kind == SessionRecoveryKind.Completed)
                    await NotifyAsync(result.Session!).ConfigureAwait(false);
                return result;
            }
            catch
            {
                // Phase 3 deliberately leaves a failed shutdown usable. Keep its timer usable too.
                if (!_shutdown) Arm(RetryDelay);
                throw;
            }
        }
        finally { _gate.Release(); }
    }

    private void Arm(TimeSpan delay)
    {
        if (_disposed) return;
        delay = delay <= TimeSpan.Zero ? TimeSpan.Zero : delay > MaximumWait ? MaximumWait : delay;
        try { _timer.Change(delay, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) when (_disposed) { }
    }

    private async Task NotifyAsync(SessionRecord session)
    {
        try { if (!_disposed) await _notify(session).ConfigureAwait(false); }
        catch (Exception exception) { Report(exception); }
    }

    private void Disarm()
    {
        if (_disposed) return;
        try { _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) when (_disposed) { }
    }

    private void Report(Exception exception)
    {
        // Diagnostics must not escape a ThreadPool timer callback.
        try { _report(exception); } catch { }
    }

    /// <summary>Call ShutdownAsync first to drain accepted work before releasing native resources.</summary>
    public void Dispose()
    {
        _disposed = true;
        _timer.Dispose();
    }
}
