namespace FocusKey.Foundation.Sessions;

/// <summary>
/// Applies application startup/shutdown policy to one observed Running row. Persistence must
/// already be initialized. Does not start sessions, schedule time, or identify process ownership.
/// </summary>
public sealed class SessionRecovery
{
    private readonly ISessionRepository _sessions;
    private readonly TimeProvider _time;

    public SessionRecovery(ISessionRepository sessions, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        _sessions = sessions;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Finishes a leftover Running session on startup. If not due, its unknown termination time is
    /// conservatively represented by StartedAt (zero credited duration), not by restart time.
    /// Call through a coordinator to run once before accepting normal session requests.
    /// </summary>
    public Task<SessionRecoveryResult> RecoverStartupAsync(CancellationToken cancellationToken = default) =>
        FinishAsync(isStartup: true, cancellationToken);

    /// <summary>
    /// On graceful exit, the end instant is known: interrupt at max(now, start), or complete at the
    /// planned end if due. This is an application exit, not the user's explicit Stop Session action.
    /// </summary>
    public Task<SessionRecoveryResult> FinishShutdownAsync(CancellationToken cancellationToken = default) =>
        FinishAsync(isStartup: false, cancellationToken);

    private async Task<SessionRecoveryResult> FinishAsync(bool isStartup, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SessionRecord? active = await _sessions.GetActiveAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (active is null)
        {
            return SessionRecoveryResult.NoActiveSession();
        }

        if (active.Status == SessionStatus.Paused)
        {
            // PRESERVE PAUSED SESSION ACROSS RESTART AND SHUTDOWN!
            return SessionRecoveryResult.StillPaused(active);
        }

        // Running session recovery:
        active.Validate();
        DateTimeOffset now = _time.GetUtcNow().ToUniversalTime();
        bool completed = now >= active.PlannedEndAt;
        DateTimeOffset endedAt = completed
            ? active.PlannedEndAt
            : isStartup || now < active.StartedAt ? active.StartedAt : now;
        SessionRecord finished = active with
        {
            Status = completed ? SessionStatus.Completed : SessionStatus.Interrupted,
            EndedAt = endedAt,
            PausedAt = null,
        };

        cancellationToken.ThrowIfCancellationRequested();
        if (!await _sessions.TryUpdateAsync(active, finished, cancellationToken).ConfigureAwait(false))
        {
            // Never retry against a newer row or session: this operation refers to exactly the
            // record it observed. A competing terminal result must not be reclassified.
            return SessionRecoveryResult.Conflict(active);
        }

        // No post-commit cancellation check. Preserve the repository's successful outcome.
        return completed ? SessionRecoveryResult.Completed(finished) : SessionRecoveryResult.Interrupted(finished);
    }
}
