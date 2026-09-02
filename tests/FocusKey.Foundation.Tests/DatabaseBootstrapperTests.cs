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
        Assert.Equal([1], result.AppliedMigrations);
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

        Assert.True(reader.Read());
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("schema_metadata", reader.GetString(1));
        Assert.True(UtcTimestamp.TryParse(reader.GetString(2), out _));
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

        // Exactly one writer applied the migration, and the audit table has no duplicate row.
        Assert.Equal(1, results.Sum(result => result.AppliedMigrations.Count));
        Assert.Equal(1, CountMigrationRows(databaseFile));
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
        Assert.Equal(1, CountMigrationRows(databaseFile));
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
    public void Initialize_CreatesNoProductTables()
    {
        using var temp = new TempDirectory();
        string databaseFile = Path.Combine(temp.Path, "focus_key.db");

        Initialize(databaseFile);

        Assert.Equal(["schema_migrations"], UserTables(databaseFile));
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
