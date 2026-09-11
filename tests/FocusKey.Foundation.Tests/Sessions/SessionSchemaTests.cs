using FocusKey.Foundation.Data;
using FocusKey.Foundation.Sessions;
using Microsoft.Data.Sqlite;

namespace FocusKey.Foundation.Tests.Sessions;

/// <summary>
/// Schema, migration, and storage-enforced invariants for the session table.
/// These tests go through the real migration mechanism; none of them recreate the database to
/// avoid a migration.
/// </summary>
public sealed class SessionSchemaTests
{
    private static readonly DateTimeOffset Phase0AppliedAt =
        new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void FreshDatabase_InitializesDirectlyToTheSessionSchema()
    {
        using var store = new SessionStore();

        Assert.True(store.Initialization.DatabaseFileCreated);
        Assert.Equal(0, store.Initialization.SchemaVersionBefore);
        Assert.Equal(SchemaMigrations.TargetVersion, store.Initialization.SchemaVersionAfter);
        Assert.Equal(6, SchemaMigrations.TargetVersion);
        Assert.Equal(Enumerable.Range(1, SchemaMigrations.TargetVersion), store.Initialization.AppliedMigrations);
        Assert.Equal(1, store.ScalarRaw<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'sessions';"));
    }

    [Fact]
    public void FreshDatabase_CreatesTheExpectedIndexes()
    {
        using var store = new SessionStore();

        Assert.Equal(1, store.ScalarRaw<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ux_sessions_single_running';"));
        Assert.Equal(1, store.ScalarRaw<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'index' AND name = 'ix_sessions_started_at_utc';"));
    }

    [Fact]
    public void Phase0Database_MigratesForwardWithoutLosingItsHistory()
    {
        using var temp = new TempDirectory();
        string databaseFile = Path.Combine(temp.Path, "focus_key.db");

        CreatePhase0Database(databaseFile);

        var connections = new SqliteConnectionFactory(databaseFile);
        Assert.Equal(1, ReadUserVersion(connections));
        Assert.Equal(0, CountTable(connections, "sessions"));

        DatabaseInitializationResult result = new DatabaseBootstrapper(connections).Initialize();

        Assert.False(result.DatabaseFileCreated);
        Assert.Equal(1, result.SchemaVersionBefore);
        Assert.Equal(SchemaMigrations.TargetVersion, result.SchemaVersionAfter);
        Assert.Equal(Enumerable.Range(2, SchemaMigrations.TargetVersion - 1), result.AppliedMigrations);
        Assert.Equal(1, CountTable(connections, "sessions"));

        // The original migration row is still exactly as Phase 0 wrote it.
        Assert.Equal(
            UtcTimestamp.Format(Phase0AppliedAt),
            Scalar<string>(connections, "SELECT applied_at_utc FROM schema_migrations WHERE version = 1;"));
        Assert.Equal(
            "sessions",
            Scalar<string>(connections, "SELECT name FROM schema_migrations WHERE version = 2;"));

        using (SqliteConnection connection = connections.OpenConnection()) SqliteConnection.ClearPool(connection);
    }

    [Fact]
    public async Task MigratedDatabase_AcceptsSessionsAndSurvivesReinitialization()
    {
        using var temp = new TempDirectory();
        string databaseFile = Path.Combine(temp.Path, "focus_key.db");

        CreatePhase0Database(databaseFile);

        var connections = new SqliteConnectionFactory(databaseFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repository = new SqliteSessionRepository(connections);
        SessionRecord finished = TestSessions.Finished(SessionStatus.Completed);
        SessionRecord running = TestSessions.Running(startedAt: TestSessions.Anchor.AddHours(1));
        await repository.AddAsync(finished);
        await repository.AddAsync(running);

        DatabaseInitializationResult second = new DatabaseBootstrapper(connections).Initialize();

        Assert.Empty(second.AppliedMigrations);
        Assert.Equal(SchemaMigrations.TargetVersion, second.SchemaVersionAfter);
        Assert.Equal(finished, await repository.GetAsync(finished.Id));
        Assert.Equal(running.Id, (await repository.GetRunningAsync())!.Id);
        Assert.Equal(SchemaMigrations.TargetVersion, Scalar<long>(connections, "SELECT COUNT(*) FROM schema_migrations;"));

        using (SqliteConnection connection = connections.OpenConnection()) SqliteConnection.ClearPool(connection);
    }

    [Fact]
    public async Task Reinitialization_IsIdempotentAndPreservesData()
    {
        using var store = new SessionStore();
        SessionRecord session = TestSessions.Finished(SessionStatus.Stopped);
        await store.Repository.AddAsync(session);

        DatabaseInitializationResult again = store.Reinitialize();
        DatabaseInitializationResult andAgain = store.Reinitialize();

        Assert.Empty(again.AppliedMigrations);
        Assert.Empty(andAgain.AppliedMigrations);
        Assert.Equal(SchemaMigrations.TargetVersion, andAgain.SchemaVersionBefore);
        Assert.Equal(SchemaMigrations.TargetVersion, andAgain.SchemaVersionAfter);
        Assert.Equal(session, await store.Repository.GetAsync(session.Id));
        Assert.Equal(1, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public async Task Storage_RefusesASecondRunningRowEvenWhenInsertedDirectly()
    {
        using var store = new SessionStore();
        await store.Repository.AddAsync(TestSessions.Running());

        SqliteException error = Assert.Throws<SqliteException>(() => InsertRaw(
            store,
            status: SessionStatusText.Running,
            endedAt: null));

        Assert.Contains("UNIQUE", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions;"));
    }

    [Fact]
    public void Storage_RefusesAnUnknownSessionType()
    {
        using var store = new SessionStore();

        Assert.Throws<SqliteException>(() => InsertRaw(store, type: "nap"));
    }

    [Fact]
    public void Storage_RefusesAnUnknownStatus()
    {
        using var store = new SessionStore();

        Assert.Throws<SqliteException>(() => InsertRaw(
            store,
            status: "paused",
            endedAt: UtcTimestamp.Format(TestSessions.Anchor.AddMinutes(5))));
    }

    [Fact]
    public void Storage_RefusesARunningRowWithAnEndTimestamp()
    {
        using var store = new SessionStore();

        Assert.Throws<SqliteException>(() => InsertRaw(
            store,
            status: SessionStatusText.Running,
            endedAt: UtcTimestamp.Format(TestSessions.Anchor.AddMinutes(5))));
    }

    [Fact]
    public void Storage_RefusesAFinishedRowWithoutAnEndTimestamp()
    {
        using var store = new SessionStore();

        Assert.Throws<SqliteException>(() => InsertRaw(
            store,
            status: SessionStatusText.Completed,
            endedAt: null));
    }

    [Fact]
    public void Storage_RefusesAnEndBeforeTheStart()
    {
        using var store = new SessionStore();

        Assert.Throws<SqliteException>(() => InsertRaw(
            store,
            status: SessionStatusText.Stopped,
            endedAt: UtcTimestamp.Format(TestSessions.Anchor.AddSeconds(-1))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Storage_RefusesANonPositivePlannedDuration(long seconds)
    {
        using var store = new SessionStore();

        Assert.Throws<SqliteException>(() => InsertRaw(store, plannedSeconds: seconds));
    }

    [Fact]
    public void Storage_RefusesAnIdOfTheWrongLength()
    {
        using var store = new SessionStore();

        Assert.Throws<SqliteException>(() => InsertRaw(store, id: "too-short"));
    }

    [Fact]
    public void Storage_RefusesATimestampOfTheWrongLength()
    {
        using var store = new SessionStore();

        Assert.Throws<SqliteException>(() => InsertRaw(store, startedAt: "2026-09-02T09:00:00Z"));
    }

    [Fact]
    public void Storage_RefusesTextInTheDurationColumn()
    {
        using var store = new SessionStore();

        // STRICT tables reject values that are not losslessly an integer, instead of coercing them.
        SqliteException error = Assert.Throws<SqliteException>(
            () => InsertRaw(store, plannedSeconds: "half an hour"));

        Assert.Contains("INTEGER", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void InsertRaw(
        SessionStore store,
        string? id = null,
        string type = SessionTypeText.Work,
        string status = SessionStatusText.Running,
        string? startedAt = null,
        object? plannedSeconds = null,
        string? endedAt = null,
        string? createdAt = null) =>
        store.ExecuteRaw(
            """
            INSERT INTO sessions (
                id, type, status, started_at_utc, planned_duration_seconds, ended_at_utc, created_at_utc)
            VALUES ($id, $type, $status, $startedAt, $plannedSeconds, $endedAt, $createdAt);
            """,
            ("$id", id ?? SessionId.New().ToText()),
            ("$type", type),
            ("$status", status),
            ("$startedAt", startedAt ?? UtcTimestamp.Format(TestSessions.Anchor)),
            ("$plannedSeconds", plannedSeconds ?? 1800L),
            ("$endedAt", endedAt ?? (object)DBNull.Value),
            ("$createdAt", createdAt ?? UtcTimestamp.Format(TestSessions.Anchor)));

    private static void CreatePhase0Database(string databaseFile)
    {
        var connections = new SqliteConnectionFactory(databaseFile);
        using SqliteConnection connection = connections.OpenConnection();

        SchemaMigration first = SchemaMigrations.All.Single(migration => migration.Version == 1);

        Execute(connection, first.Sql);
        Execute(
            connection,
            $"""
            INSERT INTO schema_migrations (version, name, applied_at_utc)
            VALUES (1, '{first.Name}', '{UtcTimestamp.Format(Phase0AppliedAt)}');
            """);
        Execute(connection, "PRAGMA user_version = 1;");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static int ReadUserVersion(SqliteConnectionFactory connections) =>
        (int)Scalar<long>(connections, "PRAGMA user_version;");

    private static long CountTable(SqliteConnectionFactory connections, string table) =>
        Scalar<long>(
            connections,
            $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';");

    private static T Scalar<T>(SqliteConnectionFactory connections, string sql)
    {
        using SqliteConnection connection = connections.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T));
    }
}
