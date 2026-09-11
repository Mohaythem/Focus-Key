using FocusKey.Foundation.Data;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Tests.Sessions;
using Microsoft.Data.Sqlite;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class SettingsPersistenceTests
{
    [Fact]
    public async Task FreshDatabaseCreatesExactlyOneDefaultSettingsRecord()
    {
        using var fixture = new Fixture();
        Assert.Equal(SchemaMigrations.TargetVersion, fixture.Initialization.SchemaVersionAfter);
        Assert.Equal(Enumerable.Range(1, SchemaMigrations.TargetVersion), fixture.Initialization.AppliedMigrations);
        Assert.Equal(1, fixture.Scalar<long>("SELECT COUNT(*) FROM application_settings;"));
        Assert.Equal(ApplicationSettings.Default, await fixture.Repository.LoadAsync());
    }

    [Fact]
    public async Task SchemaTwoDatabaseMigratesWithoutChangingExistingSessionData()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        CreateSchemaTwo(connections);
        var sessions = new SqliteSessionRepository(connections);
        SessionRecord expected = TestSessions.Finished(SessionStatus.Completed);
        await sessions.AddAsync(expected);

        DatabaseInitializationResult result = new DatabaseBootstrapper(connections).Initialize();

        Assert.Equal(2, result.SchemaVersionBefore);
        Assert.Equal(SchemaMigrations.TargetVersion, result.SchemaVersionAfter);
        Assert.Equal(Enumerable.Range(3, SchemaMigrations.TargetVersion - 2), result.AppliedMigrations);
        Assert.Equal(expected, await sessions.GetAsync(expected.Id));
        Assert.Equal(ApplicationSettings.Default, await new SqliteSettingsRepository(connections).LoadAsync());
        Assert.Equal(1L, Scalar<long>(connections, "SELECT COUNT(*) FROM sessions;"));
        ClearPool(connections);
    }

    [Fact]
    public async Task SaveAndRestartPersistTheWholeConfiguration()
    {
        using var fixture = new Fixture();
        var changed = ApplicationSettings.Default with
        {
            WorkDuration = TimeSpan.FromMinutes(52),
            BreakDuration = TimeSpan.FromSeconds(725),
            Appearance = Appearance.Dark,
            WorkColor = HexColor.Parse("#A1B2C3"),
            BreakColor = HexColor.Parse("#01020F"),
        };
        await fixture.Repository.SaveAsync(changed);
        var reopened = new SqliteSettingsRepository(new SqliteConnectionFactory(fixture.File));
        Assert.Equal(changed, await reopened.LoadAsync());
        Assert.Equal(1, fixture.Scalar<long>("SELECT COUNT(*) FROM application_settings;"));
    }

    [Fact]
    public async Task IndividualUpdatesChangeOnlyTheRequestedSettingAndSurviveRestart()
    {
        using var fixture = new Fixture();
        var service = new SettingsService(fixture.Repository);
        await service.UpdateWorkDurationAsync(TimeSpan.FromMinutes(45));
        await service.UpdateBreakDurationAsync(TimeSpan.FromMinutes(12));
        await service.UpdateAppearanceAsync(Appearance.Light);
        await service.UpdateWorkColorAsync(HexColor.Parse("#abcdef"));
        ApplicationSettings final = await service.UpdateBreakColorAsync(HexColor.Parse("#FEDCBA"));
        Assert.Equal(TimeSpan.FromMinutes(45), final.WorkDuration);
        Assert.Equal(TimeSpan.FromMinutes(12), final.BreakDuration);
        Assert.Equal(Appearance.Light, final.Appearance);
        Assert.Equal("#ABCDEF", final.WorkColor.Value);
        Assert.Equal("#FEDCBA", final.BreakColor.Value);
        Assert.Equal(final, await new SqliteSettingsRepository(new SqliteConnectionFactory(fixture.File)).LoadAsync());
    }

    [Fact]
    public async Task ConcurrentIndividualUpdatesOnOneServiceDoNotLoseEachOther()
    {
        using var fixture = new Fixture();
        var service = new SettingsService(fixture.Repository);
        await Task.WhenAll(
            service.UpdateWorkDurationAsync(TimeSpan.FromMinutes(35)),
            service.UpdateBreakDurationAsync(TimeSpan.FromMinutes(15)),
            service.UpdateAppearanceAsync(Appearance.Dark));
        var settings = await service.LoadAsync();
        Assert.Equal(TimeSpan.FromMinutes(35), settings.WorkDuration);
        Assert.Equal(TimeSpan.FromMinutes(15), settings.BreakDuration);
        Assert.Equal(Appearance.Dark, settings.Appearance);
    }

    [Fact]
    public async Task MigrationAndInitializationAreIdempotentAndKeepSavedValues()
    {
        using var fixture = new Fixture();
        var saved = ApplicationSettings.Default with { Appearance = Appearance.Dark };
        await fixture.Repository.SaveAsync(saved);
        var second = new DatabaseBootstrapper(fixture.Connections).Initialize();
        var third = new DatabaseBootstrapper(fixture.Connections).Initialize();
        Assert.Empty(second.AppliedMigrations);
        Assert.Empty(third.AppliedMigrations);
        Assert.Equal(SchemaMigrations.TargetVersion, second.SchemaVersionBefore);
        Assert.Equal(SchemaMigrations.TargetVersion, third.SchemaVersionAfter);
        Assert.Equal(saved, await fixture.Repository.LoadAsync());
        Assert.Equal(1, fixture.Scalar<long>("SELECT COUNT(*) FROM application_settings;"));
    }

    [Theory]
    [InlineData("work_duration_seconds", "0")]
    [InlineData("break_duration_seconds", "-1")]
    [InlineData("appearance", "'sepia'")]
    [InlineData("work_color", "'#GG0000'")]
    [InlineData("break_color", "'#abcdef'")]
    public async Task InvalidPersistedValuesFailExplicitlyWithoutBeingReplaced(string column, string literal)
    {
        using var fixture = new Fixture();
        fixture.Corrupt($"UPDATE application_settings SET {column} = {literal} WHERE singleton = 1;");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Repository.LoadAsync());
        Assert.Equal(literal.Trim('\''), Convert.ToString(fixture.Scalar<object>($"SELECT {column} FROM application_settings WHERE singleton = 1;")));
    }

    [Fact]
    public async Task InvalidSaveIsRejectedBeforePersistenceAndExistingSettingsRemain()
    {
        using var fixture = new Fixture();
        var invalid = ApplicationSettings.Default with { Appearance = (Appearance)42 };
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Repository.SaveAsync(invalid));
        Assert.Equal(ApplicationSettings.Default, await fixture.Repository.LoadAsync());
    }

    [Fact]
    public async Task MissingAuthoritativeRecordFailsAndSaveDoesNotRecreateItSilently()
    {
        using var fixture = new Fixture();
        fixture.Execute("DELETE FROM application_settings;");
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Repository.LoadAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Repository.SaveAsync(ApplicationSettings.Default));
        Assert.Equal(0, fixture.Scalar<long>("SELECT COUNT(*) FROM application_settings;"));
    }

    [Fact]
    public void StorageRejectsSecondSingletonAndInvalidValues()
    {
        using var fixture = new Fixture();
        Assert.Throws<SqliteException>(() => fixture.Execute(
            "INSERT INTO application_settings VALUES (2, 1, 1, 'system', '#000000', '#FFFFFF');"));
        Assert.Throws<SqliteException>(() => fixture.Execute("UPDATE application_settings SET work_color = '#GG0000';"));
        Assert.Throws<SqliteException>(() => fixture.Execute("UPDATE application_settings SET appearance = 'sepia';"));
        Assert.Equal(1, fixture.Scalar<long>("SELECT COUNT(*) FROM application_settings;"));
    }

    [Fact]
    public async Task LoadAndSaveHonorPreCanceledTokens()
    {
        using var fixture = new Fixture();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Repository.LoadAsync(canceled.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Repository.SaveAsync(ApplicationSettings.Default, canceled.Token));
    }

    private static void CreateSchemaTwo(SqliteConnectionFactory connections)
    {
        using SqliteConnection connection = connections.OpenConnection();
        foreach (SchemaMigration migration in SchemaMigrations.All.Where(m => m.Version <= 2))
        {
            Execute(connection, migration.Sql);
            Execute(connection,
                $"INSERT INTO schema_migrations VALUES ({migration.Version}, '{migration.Name}', '{UtcTimestamp.Format(TestSessions.Anchor)}');");
        }
        Execute(connection, "PRAGMA user_version = 2;");
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static T Scalar<T>(SqliteConnectionFactory connections, string sql)
    {
        using SqliteConnection connection = connections.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)Convert.ChangeType(command.ExecuteScalar()!, typeof(T));
    }

    private static void ClearPool(SqliteConnectionFactory connections)
    {
        using SqliteConnection connection = connections.OpenConnection();
        SqliteConnection.ClearPool(connection);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly TempDirectory _temp = new();
        internal Fixture()
        {
            File = Path.Combine(_temp.Path, "focus_key.db");
            Connections = new(File);
            Initialization = new DatabaseBootstrapper(Connections).Initialize();
            Repository = new(Connections);
        }
        internal string File { get; }
        internal SqliteConnectionFactory Connections { get; }
        internal DatabaseInitializationResult Initialization { get; }
        internal SqliteSettingsRepository Repository { get; }
        internal void Execute(string sql) { using var connection = Connections.OpenConnection(); SettingsPersistenceTests.Execute(connection, sql); }
        internal void Corrupt(string sql)
        {
            using var connection = Connections.OpenConnection();
            SettingsPersistenceTests.Execute(connection, "PRAGMA ignore_check_constraints = ON;");
            SettingsPersistenceTests.Execute(connection, sql);
            SettingsPersistenceTests.Execute(connection, "PRAGMA ignore_check_constraints = OFF;");
        }
        internal T Scalar<T>(string sql) => SettingsPersistenceTests.Scalar<T>(Connections, sql);
        public void Dispose() { ClearPool(Connections); _temp.Dispose(); }
    }
}
