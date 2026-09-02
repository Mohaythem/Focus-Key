using FocusKey.Foundation.Logging;
using Microsoft.Data.Sqlite;

namespace FocusKey.Foundation.Data;

/// <summary>
/// Brings the local database up to the current schema version and verifies it is usable.
/// Forward-only, transactional, and safe to run on every start.
/// </summary>
public sealed class DatabaseBootstrapper
{
    private readonly SqliteConnectionFactory _connections;
    private readonly IAppLogger _logger;

    public DatabaseBootstrapper(SqliteConnectionFactory connections, IAppLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(connections);

        _connections = connections;
        _logger = logger ?? NullAppLogger.Instance;
    }

    /// <summary>
    /// Creates the database file if needed, applies any missing migrations, then verifies
    /// the result. Repeated calls on a current database change nothing.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The database reports a newer schema than this build understands, or verification failed.
    /// </exception>
    public DatabaseInitializationResult Initialize()
    {
        bool databaseFileCreated = !File.Exists(_connections.DatabaseFile);

        using SqliteConnection connection = _connections.OpenConnection();

        int versionBefore = ReadSchemaVersion(connection);
        if (versionBefore > SchemaMigrations.TargetVersion)
        {
            throw new InvalidOperationException(
                $"The database at '{_connections.DatabaseFile}' reports schema version {versionBefore}, " +
                $"which is newer than the supported version {SchemaMigrations.TargetVersion}.");
        }

        var appliedMigrations = new List<int>();
        foreach (SchemaMigration migration in SchemaMigrations.All.OrderBy(m => m.Version))
        {
            if (migration.Version <= versionBefore)
            {
                continue;
            }

            Apply(connection, migration);
            appliedMigrations.Add(migration.Version);
            _logger.Info($"Applied database migration {migration.Version} ({migration.Name}).");
        }

        int versionAfter = ReadSchemaVersion(connection);
        VerifyUsable(connection, versionAfter);

        if (databaseFileCreated)
        {
            _logger.Info($"Created database file '{_connections.DatabaseFile}'.");
        }

        _logger.Info($"Database ready at schema version {versionAfter}.");

        return new DatabaseInitializationResult
        {
            DatabaseFile = _connections.DatabaseFile,
            DatabaseFileCreated = databaseFileCreated,
            SchemaVersionBefore = versionBefore,
            SchemaVersionAfter = versionAfter,
            AppliedMigrations = appliedMigrations,
        };
    }

    private static void Apply(SqliteConnection connection, SchemaMigration migration)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();

        Execute(connection, transaction, migration.Sql);

        using (SqliteCommand record = connection.CreateCommand())
        {
            record.Transaction = transaction;
            record.CommandText =
                """
                INSERT INTO schema_migrations (version, name, applied_at_utc)
                VALUES ($version, $name, $appliedAtUtc);
                """;
            record.Parameters.AddWithValue("$version", migration.Version);
            record.Parameters.AddWithValue("$name", migration.Name);
            record.Parameters.AddWithValue("$appliedAtUtc", DateTimeOffset.UtcNow.ToString("O"));
            record.ExecuteNonQuery();
        }

        // Interpolated from an int constant in code, never from external input.
        Execute(connection, transaction, $"PRAGMA user_version = {migration.Version};");

        transaction.Commit();
    }

    private static void VerifyUsable(SqliteConnection connection, int versionAfter)
    {
        if (versionAfter != SchemaMigrations.TargetVersion)
        {
            throw new InvalidOperationException(
                $"Database initialization ended at schema version {versionAfter} " +
                $"instead of {SchemaMigrations.TargetVersion}.");
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT COUNT(*) FROM sqlite_master
            WHERE type = 'table' AND name = 'schema_migrations';
            """;

        if (Convert.ToInt64(command.ExecuteScalar()) != 1)
        {
            throw new InvalidOperationException(
                "Database initialization completed without a 'schema_migrations' table.");
        }
    }

    private static int ReadSchemaVersion(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction transaction, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
