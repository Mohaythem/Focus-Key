using FocusKey.Foundation.Data;
using Microsoft.Data.Sqlite;

namespace FocusKey.Foundation.Tests;

public sealed class DatabaseBootstrapperTests
{
    [Fact]
    public void Initialize_CreatesDatabaseAtTargetSchemaVersion()
    {
        using var temp = new TempDirectory();
        string databaseFile = Path.Combine(temp.Path, "focus_key.db");

        DatabaseInitializationResult result = Initialize(databaseFile);

        Assert.True(result.DatabaseFileCreated);
        Assert.Equal(0, result.SchemaVersionBefore);
        Assert.Equal(SchemaMigrations.TargetVersion, result.SchemaVersionAfter);
        Assert.Equal(SchemaMigrations.All.Select(migration => migration.Version), result.AppliedMigrations);
        Assert.Equal(databaseFile, result.DatabaseFile);
        Assert.True(File.Exists(databaseFile));
    }

    [Fact]
    public void Initialize_RecordsAppliedMigrations()
    {
        using var temp = new TempDirectory();
        string databaseFile = Path.Combine(temp.Path, "focus_key.db");

        Initialize(databaseFile);

        using SqliteConnection connection = new SqliteConnectionFactory(databaseFile).OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT version, name, applied_at_utc FROM schema_migrations ORDER BY version;";
        using SqliteDataReader reader = command.ExecuteReader();

        foreach (SchemaMigration migration in SchemaMigrations.All.OrderBy(m => m.Version))
        {
            Assert.True(reader.Read());
            Assert.Equal(migration.Version, reader.GetInt32(0));
            Assert.Equal(migration.Name, reader.GetString(1));
            Assert.True(UtcTimestamp.TryParse(reader.GetString(2), out _));
        }

        Assert.False(reader.Read());
    }

    [Fact]
    public async Task Initialize_IsSafeWhenTwoWritersStartTogether()
    {
        using var temp = new TempDirectory();
        string databaseFile = Path.Combine(temp.Path, "focus_key.db");

        // Two independent bootstrappers on their own connections, as two processes would be.
        DatabaseInitializationResult[] results = await Task.WhenAll(
            Task.Run(() => Initialize(databaseFile)),
            Task.Run(() => Initialize(databaseFile)));

        Assert.All(results, result =>
            Assert.Equal(SchemaMigrations.TargetVersion, result.SchemaVersionAfter));

        // Every migration was applied exactly once across both writers, with no duplicate rows.
        Assert.Equal(SchemaMigrations.All.Count, results.Sum(result => result.AppliedMigrations.Count));
        Assert.Equal(SchemaMigrations.All.Count, CountMigrationRows(databaseFile));
    }

    [Fact]
    public void Initialize_IsIdempotent()
    {
        using var temp = new TempDirectory();
        string databaseFile = Path.Combine(temp.Path, "focus_key.db");

        Initialize(databaseFile);
        DatabaseInitializationResult second = Initialize(databaseFile);

        Assert.False(second.DatabaseFileCreated);
        Assert.Equal(SchemaMigrations.TargetVersion, second.SchemaVersionBefore);
        Assert.Equal(SchemaMigrations.TargetVersion, second.SchemaVersionAfter);
        Assert.Empty(second.AppliedMigrations);
        Assert.Equal(SchemaMigrations.All.Count, CountMigrationRows(databaseFile));
    }

    [Fact]
    public void Initialize_RejectsNewerSchemaVersion()
    {
        using var temp = new TempDirectory();
        string databaseFile = Path.Combine(temp.Path, "focus_key.db");

        Initialize(databaseFile);
        SetUserVersion(databaseFile, SchemaMigrations.TargetVersion + 1);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Initialize(databaseFile));

        Assert.Contains("newer than the supported version", error.Message);
    }

    [Fact]
    public void Initialize_CreatesOnlyTheDocumentedTables()
    {
        using var temp = new TempDirectory();
        string databaseFile = Path.Combine(temp.Path, "focus_key.db");

        Initialize(databaseFile);

        // A new table may only appear here together with the migration that introduces it.
        Assert.Equal(["application_settings", "historical_focus", "schema_migrations", "sessions", "theme_settings"], UserTables(databaseFile));
    }

    [Fact]
    public void OpenConnection_EnablesWriteAheadLogging()
    {
        using var temp = new TempDirectory();
        string databaseFile = Path.Combine(temp.Path, "focus_key.db");

        Initialize(databaseFile);

        using SqliteConnection connection = new SqliteConnectionFactory(databaseFile).OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode;";

        Assert.Equal("wal", Convert.ToString(command.ExecuteScalar())?.ToLowerInvariant());
    }

    [Fact]
    public void Constructor_RejectsMissingConnectionFactory()
    {
        Assert.Throws<ArgumentNullException>(() => new DatabaseBootstrapper(null!));
    }

    [Fact]
    public void ConnectionFactory_RejectsBlankDatabasePath()
    {
        Assert.Throws<ArgumentException>(() => new SqliteConnectionFactory("  "));
    }

    private static DatabaseInitializationResult Initialize(string databaseFile) =>
        new DatabaseBootstrapper(new SqliteConnectionFactory(databaseFile)).Initialize();

    private static long CountMigrationRows(string databaseFile)
    {
        using SqliteConnection connection = new SqliteConnectionFactory(databaseFile).OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations;";
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static List<string> UserTables(string databaseFile)
    {
        using SqliteConnection connection = new SqliteConnectionFactory(databaseFile).OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name FROM sqlite_master
            WHERE type = 'table' AND name NOT LIKE 'sqlite_%'
            ORDER BY name;
            """;

        var tables = new List<string>();
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static void SetUserVersion(string databaseFile, int version)
    {
        using SqliteConnection connection = new SqliteConnectionFactory(databaseFile).OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {version};";
        command.ExecuteNonQuery();
    }
}
