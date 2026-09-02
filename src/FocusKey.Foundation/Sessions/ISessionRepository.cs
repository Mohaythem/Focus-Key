namespace FocusKey.Foundation.Sessions;

/// <summary>
/// Durable storage for focus sessions. Every operation is a raw persistence concern: nothing here
/// starts, stops, completes, or times a session.
/// </summary>
public interface ISessionRepository
{
    /// <summary>
    /// Stores a new session. The specification requires a session to be durable from the moment it
    /// begins, so this is the write that happens first, not at the end.
    /// </summary>
    /// <exception cref="ArgumentException">The record violates a stored-record invariant.</exception>
    /// <exception cref="DuplicateSessionIdException">A session with this id already exists.</exception>
    /// <exception cref="ActiveSessionAlreadyExistsException">
    /// The record is Running and another running session is already stored.
    /// </exception>
    Task AddAsync(SessionRecord session, CancellationToken cancellationToken = default);

    /// <summary>Reads one session by identity. Returns null when it does not exist.</summary>
    Task<SessionRecord?> GetAsync(SessionId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Overwrites the stored session that has the same id. Used when a session's outcome becomes
    /// known, which is why a whole-record write is the honest shape here.
    /// </summary>
    /// <exception cref="ArgumentException">The record violates a stored-record invariant.</exception>
    /// <exception cref="SessionNotFoundException">No session with this id is stored.</exception>
    /// <exception cref="ActiveSessionAlreadyExistsException">
    /// The record is Running and a different running session is already stored.
    /// </exception>
    Task UpdateAsync(SessionRecord session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the single Running session, or null when none is stored. Storage guarantees there can
    /// never be more than one.
    /// </summary>
    Task<SessionRecord?> GetRunningAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the raw sessions that started within a half-open range:
    /// <c>fromInclusive &lt;= StartedAt &lt; toExclusive</c>.
    /// Ordered by start time, then by id so equal timestamps still produce a stable order.
    /// Returns records only — no totals, rates, or groupings.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="toExclusive"/> precedes <paramref name="fromInclusive"/>.</exception>
    Task<IReadOnlyList<SessionRecord>> GetStartedBetweenAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken = default);
}
