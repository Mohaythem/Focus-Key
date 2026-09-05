using FocusKey.Foundation.Data;
using Microsoft.Data.Sqlite;

namespace FocusKey.Foundation.Sessions;

/// <summary>
/// SQLite-backed session storage. Opens a short-lived pooled connection per operation, so there is
/// no shared connection state to reason about.
/// </summary>
/// <remarks>
/// Writes that must be all-or-nothing use an immediate transaction, which takes the write lock
/// before reading, so a check followed by a write cannot interleave with another writer.
/// Microsoft.Data.Sqlite's async methods are not truly asynchronous I/O — SQLite is synchronous —
/// but the async contract keeps callers off blocking assumptions and makes cancellation explicit.
/// </remarks>
public sealed class SqliteSessionRepository : ISessionRepository
{
    private const string SelectSessions =
        "SELECT id, type, status, started_at_utc, planned_duration_seconds, ended_at_utc, created_at_utc FROM sessions";

    private readonly SqliteConnectionFactory _connections;

    public SqliteSessionRepository(SqliteConnectionFactory connections)
    {
        ArgumentNullException.ThrowIfNull(connections);
        _connections = connections;
    }

    public async Task AddAsync(SessionRecord session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Validate();

        await using SqliteConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Created synchronously on purpose: BEGIN IMMEDIATE is a local operation and this overload
        // is the only one that guarantees the write lock up front.
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);

        if (await ExistsAsync(connection, transaction, session.Id, cancellationToken).ConfigureAwait(false))
        {
            throw new DuplicateSessionIdException(session.Id);
        }

        if (session.IsActive
            && await ReadRunningIdAsync(connection, transaction, cancellationToken).ConfigureAwait(false)
                is { } alreadyRunning)
        {
            throw new ActiveSessionAlreadyExistsException(alreadyRunning);
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO sessions (
                id, type, status, started_at_utc, planned_duration_seconds, ended_at_utc, created_at_utc)
            VALUES ($id, $type, $status, $startedAt, $plannedSeconds, $endedAt, $createdAt);
            """;
        AddSessionParameters(command, session);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    public async Task<SessionRecord?> GetAsync(SessionId id, CancellationToken cancellationToken = default)
    {
        if (id.IsEmpty)
        {
            throw new ArgumentException("A session id is required.", nameof(id));
        }

        await using SqliteConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{SelectSessions} WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToText());

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? Map(reader)
            : null;
    }

    public async Task UpdateAsync(SessionRecord session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        session.Validate();

        await using SqliteConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);

        if (!await ExistsAsync(connection, transaction, session.Id, cancellationToken).ConfigureAwait(false))
        {
            throw new SessionNotFoundException(session.Id);
        }

        if (session.IsActive
            && await ReadRunningIdAsync(connection, transaction, cancellationToken).ConfigureAwait(false)
                is { } alreadyRunning
            && alreadyRunning != session.Id)
        {
            throw new ActiveSessionAlreadyExistsException(alreadyRunning);
        }

        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE sessions
            SET type                     = $type,
                status                   = $status,
                started_at_utc           = $startedAt,
                planned_duration_seconds = $plannedSeconds,
                ended_at_utc             = $endedAt,
                created_at_utc           = $createdAt
            WHERE id = $id;
            """;
        AddSessionParameters(command, session);

        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        if (affected != 1)
        {
            // Unreachable while the existence check and the write share one immediate transaction.
            throw new SessionNotFoundException(session.Id);
        }

        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
    }

    public async Task<bool> TryUpdateAsync(
        SessionRecord expected,
        SessionRecord replacement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(replacement);
        expected.Validate();
        replacement.Validate();
        if (expected.Id != replacement.Id)
        {
            throw new ArgumentException("An atomic update cannot change session identity.", nameof(replacement));
        }

        cancellationToken.ThrowIfCancellationRequested();
        await using SqliteConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE sessions
            SET type = $type, status = $status, started_at_utc = $startedAt,
                planned_duration_seconds = $plannedSeconds, ended_at_utc = $endedAt,
                created_at_utc = $createdAt
            WHERE id = $id AND type = $expectedType AND status = $expectedStatus
                AND started_at_utc = $expectedStart AND planned_duration_seconds = $expectedSeconds
                AND ended_at_utc IS $expectedEnd AND created_at_utc = $expectedCreated;
            """;
        AddSessionParameters(command, replacement);
        command.Parameters.AddWithValue("$expectedType", SessionTypeText.Format(expected.Type));
        command.Parameters.AddWithValue("$expectedStatus", SessionStatusText.Format(expected.Status));
        command.Parameters.AddWithValue("$expectedStart", UtcTimestamp.Format(expected.StartedAt));
        command.Parameters.AddWithValue("$expectedSeconds", expected.PlannedDuration.Ticks / TimeSpan.TicksPerSecond);
        command.Parameters.AddWithValue("$expectedEnd",
            expected.EndedAt is { } end ? UtcTimestamp.Format(end) : (object)DBNull.Value);
        command.Parameters.AddWithValue("$expectedCreated", UtcTimestamp.Format(expected.CreatedAt));

        int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        // There is deliberately no cancellable work after commit: cancellation cannot turn a
        // durable transition into an apparent failure. Disposal rolls back any earlier failure.
        cancellationToken.ThrowIfCancellationRequested();
        transaction.Commit();
        return affected == 1;
    }

    public async Task<SessionRecord?> GetRunningAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"{SelectSessions} WHERE status = $status;";
        command.Parameters.AddWithValue("$status", SessionStatusText.Running);

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? Map(reader)
            : null;
    }

    public async Task<IReadOnlyList<SessionRecord>> GetStartedBetweenAsync(
        DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive,
        CancellationToken cancellationToken = default)
    {
        if (toExclusive < fromInclusive)
        {
            throw new ArgumentException(
                $"The range end {toExclusive:O} precedes the range start {fromInclusive:O}.",
                nameof(toExclusive));
        }

        await using SqliteConnection connection =
            await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            $"{SelectSessions} WHERE started_at_utc >= $from AND started_at_utc < $to " +
            "ORDER BY started_at_utc ASC, id ASC;";
        command.Parameters.AddWithValue("$from", UtcTimestamp.Format(fromInclusive));
        command.Parameters.AddWithValue("$to", UtcTimestamp.Format(toExclusive));

        var sessions = new List<SessionRecord>();

        await using SqliteDataReader reader =
            await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sessions.Add(Map(reader));
        }

        return sessions.AsReadOnly();
    }

    private static async Task<bool> ExistsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SessionId id,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT 1 FROM sessions WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToText());

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not null;
    }

    private static async Task<SessionId?> ReadRunningIdAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM sessions WHERE status = $status;";
        command.Parameters.AddWithValue("$status", SessionStatusText.Running);

        object? id = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return id is string text ? SessionId.Parse(text) : null;
    }

    private static void AddSessionParameters(SqliteCommand command, SessionRecord session)
    {
        object endedAt = session.EndedAt is { } value
            ? UtcTimestamp.Format(value)
            : DBNull.Value;

        command.Parameters.AddWithValue("$id", session.Id.ToText());
        command.Parameters.AddWithValue("$type", SessionTypeText.Format(session.Type));
        command.Parameters.AddWithValue("$status", SessionStatusText.Format(session.Status));
        command.Parameters.AddWithValue("$startedAt", UtcTimestamp.Format(session.StartedAt));
        command.Parameters.AddWithValue(
            "$plannedSeconds", session.PlannedDuration.Ticks / TimeSpan.TicksPerSecond);
        command.Parameters.AddWithValue("$endedAt", endedAt);
        command.Parameters.AddWithValue("$createdAt", UtcTimestamp.Format(session.CreatedAt));
    }

    /// <summary>
    /// Reads one row. Any stored value outside the canonical forms throws rather than being coerced
    /// into something valid.
    /// </summary>
    private static SessionRecord Map(SqliteDataReader reader)
    {
        var session = new SessionRecord
        {
            Id = SessionId.Parse(reader.GetString(0)),
            Type = SessionTypeText.Parse(reader.GetString(1)),
            Status = SessionStatusText.Parse(reader.GetString(2)),
            StartedAt = UtcTimestamp.Parse(reader.GetString(3)),
            PlannedDuration = TimeSpan.FromTicks(checked(reader.GetInt64(4) * TimeSpan.TicksPerSecond)),
            EndedAt = reader.IsDBNull(5) ? null : UtcTimestamp.Parse(reader.GetString(5)),
            CreatedAt = UtcTimestamp.Parse(reader.GetString(6)),
        };
        session.Validate();
        return session;
    }
}
