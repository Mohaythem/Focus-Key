namespace FocusKey.Foundation.Sessions;

/// <summary>
/// The Focus Key session lifecycle: start one session, observe it, stop it, or finish it when its
/// planned end arrives.
/// </summary>
/// <remarks>
/// <para>
/// Time is never counted down. A running session carries a start instant and a planned duration, so
/// remaining and elapsed time are always recomputed from the clock. Nothing derived is stored.
/// </para>
/// <para>
/// The database is the only source of truth. The engine holds no mutable copy of the active
/// session: every operation re-reads it, so a failed write cannot leave the engine believing
/// something storage disagrees with.
/// </para>
/// <para>
/// Lifecycle writes are serialized by one in-process gate, so concurrent callers cannot interleave a
/// read and a write. Across instances, atomic repository comparisons protect transitions and the
/// unique index protects starts. A conflict never retries against a newer active session.
/// </para>
/// </remarks>
public sealed class SessionEngine
{
    private readonly ISessionRepository _sessions;
    private readonly TimeProvider _time;
    private readonly ISessionDurationProvider _durationProvider;

    /// <summary>One gate for the whole lifecycle: start, stop, and completion cannot overlap.</summary>
    private readonly SemaphoreSlim _lifecycle = new(initialCount: 1, maxCount: 1);

    public SessionEngine(
        ISessionRepository sessions,
        TimeProvider? timeProvider = null,
        SessionDurations? durations = null,
        ISessionDurationProvider? durationProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        if (durations is not null && durationProvider is not null)
            throw new ArgumentException("Specify fixed durations or a duration provider, not both.");

        _sessions = sessions;
        _time = timeProvider ?? TimeProvider.System;
        _durationProvider = durationProvider ??
            new FixedSessionDurationProvider(durations ?? SessionDurations.Default);
    }

    /// <summary>Reads the duration snapshot that would be used by a subsequent start.</summary>
    public Task<SessionDurations> GetDurationsAsync(CancellationToken cancellationToken = default) =>
        _durationProvider.GetDurationsAsync(cancellationToken);

    /// <summary>
    /// Starts a Work or Break session and persists it immediately, so the session is durable from
    /// the moment it begins.
    /// </summary>
    /// <exception cref="ActiveSessionAlreadyExistsException">
    /// A session is already running. The existing session is left exactly as it was — it is never
    /// replaced or stopped on the caller's behalf.
    /// </exception>
    public async Task<SessionRecord> StartAsync(
        SessionType type,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (type is not (SessionType.Work or SessionType.Break))
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown session type has no duration.");
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            SessionRecord? running = await _sessions.GetRunningAsync(cancellationToken)
                .ConfigureAwait(false);

            if (running is not null)
            {
                throw new ActiveSessionAlreadyExistsException(running.Id);
            }

            SessionDurations durations = await _durationProvider.GetDurationsAsync(cancellationToken)
                .ConfigureAwait(false);
            TimeSpan duration = durations.For(type);
            DateTimeOffset now = _time.GetUtcNow();

            var session = new SessionRecord
            {
                Id = SessionId.New(),
                Type = type,
                Status = SessionStatus.Running,
                StartedAt = now,
                PlannedDuration = duration,
                EndedAt = null,
                CreatedAt = now,
            };

            session.Validate();
            cancellationToken.ThrowIfCancellationRequested();
            await _sessions.AddAsync(session, cancellationToken).ConfigureAwait(false);

            return session;
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Describes the running session as it stands now, or null when nothing is running.
    /// Observation never writes: a session whose planned end has passed is reported as such and left
    /// running until <see cref="CompleteIfDueAsync"/> is called.
    /// </summary>
    public async Task<SessionSnapshot?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        SessionRecord? running = await _sessions.GetRunningAsync(cancellationToken)
            .ConfigureAwait(false);

        return running is null ? null : SessionSnapshot.For(running, _time.GetUtcNow());
    }

    /// <summary>
    /// Ends the running session. Before its planned end this stops it; at or after its planned end
    /// the session has in fact finished, so it completes instead — with the planned end as its end
    /// timestamp, not the moment Stop was called.
    /// </summary>
    /// <returns>
    /// <see cref="SessionOutcomeKind.NoActiveSession"/>, <see cref="SessionOutcomeKind.Stopped"/>,
    /// <see cref="SessionOutcomeKind.Completed"/>, or <see cref="SessionOutcomeKind.Conflict"/>
    /// when another writer changed the observed session.
    /// </returns>
    public Task<SessionOutcome> StopAsync(CancellationToken cancellationToken = default) =>
        StopCoreAsync(null, cancellationToken);

    /// <summary>Stops only the session the user saw; a stale UI cannot stop its replacement.</summary>
    public Task<SessionOutcome> StopAsync(SessionId expectedId, CancellationToken cancellationToken = default) =>
        StopCoreAsync(expectedId, cancellationToken);

    private async Task<SessionOutcome> StopCoreAsync(SessionId? expectedId, CancellationToken cancellationToken)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            SessionRecord? running = await _sessions.GetRunningAsync(cancellationToken)
                .ConfigureAwait(false);

            if (running is null)
            {
                return SessionOutcome.NoActiveSession();
            }

            if (expectedId is { } expected && expected != running.Id)
                return SessionOutcome.Conflict(running);

            DateTimeOffset now = _time.GetUtcNow();

            if (now >= running.PlannedEndAt)
            {
                return await CompleteAsync(running, cancellationToken).ConfigureAwait(false);
            }

            // A clock that moved backwards must not produce a record that ends before it started.
            DateTimeOffset endedAt = now < running.StartedAt ? running.StartedAt : now;

            SessionRecord stopped = running with
            {
                Status = SessionStatus.Stopped,
                EndedAt = endedAt,
            };

            cancellationToken.ThrowIfCancellationRequested();
            return await _sessions.TryUpdateAsync(running, stopped, cancellationToken).ConfigureAwait(false)
                ? SessionOutcome.Stopped(stopped)
                : SessionOutcome.Conflict(running);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Completes the running session if its planned end has arrived, and does nothing otherwise.
    /// Safe to call as often as a caller likes: once a session is completed it is no longer running,
    /// so a second call finds nothing to do and no session can complete twice.
    /// </summary>
    /// <returns>
    /// <see cref="SessionOutcomeKind.NoActiveSession"/>,
    /// <see cref="SessionOutcomeKind.StillRunning"/>, <see cref="SessionOutcomeKind.Completed"/>,
    /// or <see cref="SessionOutcomeKind.Conflict"/> when another writer changed the observed session.
    /// </returns>
    public async Task<SessionOutcome> CompleteIfDueAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            SessionRecord? running = await _sessions.GetRunningAsync(cancellationToken)
                .ConfigureAwait(false);

            if (running is null)
            {
                return SessionOutcome.NoActiveSession();
            }

            if (_time.GetUtcNow() < running.PlannedEndAt)
            {
                return SessionOutcome.StillRunning(running);
            }

            return await CompleteAsync(running, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// Writes the completed session. The end timestamp is the planned end, so a completion noticed
    /// late records when the session actually finished rather than when anyone looked.
    /// </summary>
    private async Task<SessionOutcome> CompleteAsync(
        SessionRecord running,
        CancellationToken cancellationToken)
    {
        SessionRecord completed = running with
        {
            Status = SessionStatus.Completed,
            EndedAt = running.PlannedEndAt,
        };

        cancellationToken.ThrowIfCancellationRequested();
        return await _sessions.TryUpdateAsync(running, completed, cancellationToken).ConfigureAwait(false)
            ? SessionOutcome.Completed(completed)
            : SessionOutcome.Conflict(running);
    }
}
