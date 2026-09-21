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
        SessionRecord expected = TestSessions.Finished(SessionStatus.Completed);
        using (var connection = connections.OpenConnection())
        {
            Execute(connection, $"""
                INSERT INTO sessions (id, type, status, started_at_utc, planned_duration_seconds, ended_at_utc, created_at_utc)
                VALUES ('{expected.Id.ToText()}', 'work', 'completed', '{UtcTimestamp.Format(expected.StartedAt)}', {expected.PlannedDuration.Ticks / TimeSpan.TicksPerSecond}, '{UtcTimestamp.Format(expected.EndedAt!.Value)}', '{UtcTimestamp.Format(expected.CreatedAt)}');
                """);
        }
        var sessions = new SqliteSessionRepository(connections);

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
    public async Task SqliteSettingsRepository_SavesAndLoads_MainWindowShortcut()
    {
        using var fixture = new Fixture();
        var custom = GlobalShortcut.Parse("Ctrl + Shift + O");
        var changed = ApplicationSettings.Default with { MainWindowShortcut = custom };
        await fixture.Repository.SaveAsync(changed);

        var reopened = new SqliteSettingsRepository(new SqliteConnectionFactory(fixture.File));
        ApplicationSettings loaded = await reopened.LoadAsync();
        Assert.Equal(custom, loaded.MainWindowShortcut);
        Assert.Equal("Ctrl + Shift + O", loaded.MainWindowShortcut.ToString());
    }

    [Fact]
    public async Task SqliteSettingsRepository_SavesAndLoads_OverlayPositionAndTimeFormat()
    {
        using var fixture = new Fixture();
        var changed = ApplicationSettings.Default with
        {
            TimeFormat = TimeFormat.TwelveHour,
            OverlayPositionX = 350,
            OverlayPositionY = 150,
        };
        await fixture.Repository.SaveAsync(changed);

        var reopened = new SqliteSettingsRepository(new SqliteConnectionFactory(fixture.File));
        ApplicationSettings loaded = await reopened.LoadAsync();
        Assert.Equal(TimeFormat.TwelveHour, loaded.TimeFormat);
        Assert.Equal(350, loaded.OverlayPositionX);
        Assert.Equal(150, loaded.OverlayPositionY);
    }

    [Fact]
    public async Task SqliteSettingsRepository_SavesAndLoads_NegativeOverlayPositions()
    {
        using var fixture = new Fixture();
        var changed = ApplicationSettings.Default with
        {
            OverlayPositionX = -1200,
            OverlayPositionY = -200,
        };
        await fixture.Repository.SaveAsync(changed);

        var reopened = new SqliteSettingsRepository(new SqliteConnectionFactory(fixture.File));
        ApplicationSettings loaded = await reopened.LoadAsync();
        Assert.Equal(-1200, loaded.OverlayPositionX);
        Assert.Equal(-200, loaded.OverlayPositionY);
    }

    [Fact]
    public async Task SettingsService_UpdatesTimeFormatAndOverlayPositionAndSurvivesRestart()
    {
        using var fixture = new Fixture();
        var service = new SettingsService(fixture.Repository);
        await service.UpdateTimeFormatAsync(TimeFormat.TwelveHour);
        await service.UpdateOverlayPositionAsync(500, 300);

        var reopened = new SqliteSettingsRepository(new SqliteConnectionFactory(fixture.File));
        ApplicationSettings loaded = await reopened.LoadAsync();
        Assert.Equal(TimeFormat.TwelveHour, loaded.TimeFormat);
        Assert.Equal(500, loaded.OverlayPositionX);
        Assert.Equal(300, loaded.OverlayPositionY);

        await service.ResetOverlayPositionAsync();
        ApplicationSettings afterReset = await reopened.LoadAsync();
        Assert.Null(afterReset.OverlayPositionX);
        Assert.Null(afterReset.OverlayPositionY);
        Assert.Equal(TimeFormat.TwelveHour, afterReset.TimeFormat);
    }

    [Fact]
    public async Task SchemaTenDatabaseMigratesToElevenWithOverlayPositionAndTimeFormat()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        CreateSchemaTen(connections);

        DatabaseInitializationResult result = new DatabaseBootstrapper(connections).Initialize();

        Assert.Equal(10, result.SchemaVersionBefore);
        Assert.Equal(SchemaMigrations.TargetVersion, result.SchemaVersionAfter);
        Assert.Equal([11, 12, 13, 14], result.AppliedMigrations);

        var repo = new SqliteSettingsRepository(connections);
        ApplicationSettings settings = await repo.LoadAsync();
        Assert.Equal(TimeFormat.TwentyFourHour, settings.TimeFormat);
        Assert.Null(settings.OverlayPositionX);
        Assert.Null(settings.OverlayPositionY);
        Assert.False(settings.AppearanceExpanded);
        Assert.False(settings.ShortcutsExpanded);
        Assert.False(settings.AdvancedExpanded);
        Assert.True(settings.StartSoundEnabled);
        Assert.True(settings.CompletionSoundEnabled);
        Assert.Equal(100, settings.UiScalePercent);
        ClearPool(connections);
    }

    [Fact]
    public async Task SchemaElevenDatabaseMigratesToTwelveWithSectionExpansionFlags()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        CreateSchemaEleven(connections);

        DatabaseInitializationResult result = new DatabaseBootstrapper(connections).Initialize();

        Assert.Equal(11, result.SchemaVersionBefore);
        Assert.Equal(SchemaMigrations.TargetVersion, result.SchemaVersionAfter);
        Assert.Equal([12, 13, 14], result.AppliedMigrations);

        var repo = new SqliteSettingsRepository(connections);
        ApplicationSettings settings = await repo.LoadAsync();
        Assert.False(settings.AppearanceExpanded);
        Assert.False(settings.ShortcutsExpanded);
        Assert.False(settings.AdvancedExpanded);
        Assert.True(settings.StartSoundEnabled);
        Assert.True(settings.CompletionSoundEnabled);
        Assert.Equal(100, settings.UiScalePercent);
        ClearPool(connections);
    }

    [Fact]
    public async Task SchemaTwelveDatabaseMigratesToThirteenWithSessionSoundFlags()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        CreateSchemaTwelve(connections);

        DatabaseInitializationResult result = new DatabaseBootstrapper(connections).Initialize();

        Assert.Equal(12, result.SchemaVersionBefore);
        Assert.Equal(SchemaMigrations.TargetVersion, result.SchemaVersionAfter);
        Assert.Equal([13, 14], result.AppliedMigrations);

        var repo = new SqliteSettingsRepository(connections);
        ApplicationSettings settings = await repo.LoadAsync();
        Assert.True(settings.StartSoundEnabled);
        Assert.True(settings.CompletionSoundEnabled);
        Assert.Equal(100, settings.UiScalePercent);
        ClearPool(connections);
    }

    [Fact]
    public async Task SchemaThirteenDatabaseMigratesToFourteenWithUiScalePreference()
    {
        using var temp = new TempDirectory();
        string file = Path.Combine(temp.Path, "focus_key.db");
        var connections = new SqliteConnectionFactory(file);
        CreateSchemaThirteen(connections);

        DatabaseInitializationResult result = new DatabaseBootstrapper(connections).Initialize();

        Assert.Equal(13, result.SchemaVersionBefore);
        Assert.Equal(SchemaMigrations.TargetVersion, result.SchemaVersionAfter);
        Assert.Equal([14], result.AppliedMigrations);

        var repo = new SqliteSettingsRepository(connections);
        ApplicationSettings settings = await repo.LoadAsync();
        Assert.Equal(100, settings.UiScalePercent);
        ClearPool(connections);
    }

    [Fact]
    public async Task SqliteSettingsRepository_SavesAndLoads_UiScalePercent()
    {
        using var fixture = new Fixture();
        var changed = ApplicationSettings.Default with { UiScalePercent = 125 };
        await fixture.Repository.SaveAsync(changed);

        var reopened = new SqliteSettingsRepository(new SqliteConnectionFactory(fixture.File));
        ApplicationSettings loaded = await reopened.LoadAsync();
        Assert.Equal(125, loaded.UiScalePercent);
    }

    [Fact]
    public async Task SettingsService_UpdatesUiScaleAndSurvivesRestart()
    {
        using var fixture = new Fixture();
        var service = new SettingsService(fixture.Repository);
        await service.UpdateUiScalePercentAsync(150);

        var reopened = new SqliteSettingsRepository(new SqliteConnectionFactory(fixture.File));
        ApplicationSettings loaded = await reopened.LoadAsync();
        Assert.Equal(150, loaded.UiScalePercent);
    }

    [Fact]
    public async Task SqliteSettingsRepository_SavesAndLoads_SectionExpansionFlags()
    {
        using var fixture = new Fixture();
        var changed = ApplicationSettings.Default with
        {
            AppearanceExpanded = true,
            ShortcutsExpanded = true,
            AdvancedExpanded = true,
        };
        await fixture.Repository.SaveAsync(changed);

        var reopened = new SqliteSettingsRepository(new SqliteConnectionFactory(fixture.File));
        ApplicationSettings loaded = await reopened.LoadAsync();
        Assert.True(loaded.AppearanceExpanded);
        Assert.True(loaded.ShortcutsExpanded);
        Assert.True(loaded.AdvancedExpanded);
    }

    [Fact]
    public async Task SettingsService_UpdatesSectionExpansionAndSurvivesRestart()
    {
        using var fixture = new Fixture();
        var service = new SettingsService(fixture.Repository);
        await service.UpdateAppearanceExpandedAsync(true);
        await service.UpdateShortcutsExpandedAsync(false);
        await service.UpdateAdvancedExpandedAsync(true);

        var reopened = new SqliteSettingsRepository(new SqliteConnectionFactory(fixture.File));
        ApplicationSettings loaded = await reopened.LoadAsync();
        Assert.True(loaded.AppearanceExpanded);
        Assert.False(loaded.ShortcutsExpanded);
        Assert.True(loaded.AdvancedExpanded);
    }

    [Fact]
    public void ApplicationSettings_Validation_RejectsIdenticalGlobalAndMainWindowShortcuts()
    {
        var shortcut = GlobalShortcut.Parse("Shift + F3");
        var invalid = ApplicationSettings.Default with
        {
            GlobalShortcut = shortcut,
            MainWindowShortcut = shortcut,
        };
        var ex = Assert.Throws<ArgumentException>(() => invalid.Validate());
        Assert.Contains("identical", ex.Message, StringComparison.OrdinalIgnoreCase);
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

    private static void CreateSchemaTen(SqliteConnectionFactory connections)
    {
        using SqliteConnection connection = connections.OpenConnection();
        foreach (SchemaMigration migration in SchemaMigrations.All.Where(m => m.Version <= 10))
        {
            Execute(connection, migration.Sql);
            Execute(connection,
                $"INSERT INTO schema_migrations VALUES ({migration.Version}, '{migration.Name}', '{UtcTimestamp.Format(TestSessions.Anchor)}');");
        }
        Execute(connection, "PRAGMA user_version = 10;");
    }

    private static void CreateSchemaEleven(SqliteConnectionFactory connections)
    {
        using SqliteConnection connection = connections.OpenConnection();
        foreach (SchemaMigration migration in SchemaMigrations.All.Where(m => m.Version <= 11))
        {
            Execute(connection, migration.Sql);
            Execute(connection,
                $"INSERT INTO schema_migrations VALUES ({migration.Version}, '{migration.Name}', '{UtcTimestamp.Format(TestSessions.Anchor)}');");
        }
        Execute(connection, "PRAGMA user_version = 11;");
    }

    private static void CreateSchemaTwelve(SqliteConnectionFactory connections)
    {
        using SqliteConnection connection = connections.OpenConnection();
        foreach (SchemaMigration migration in SchemaMigrations.All.Where(m => m.Version <= 12))
        {
            Execute(connection, migration.Sql);
            Execute(connection,
                $"INSERT INTO schema_migrations VALUES ({migration.Version}, '{migration.Name}', '{UtcTimestamp.Format(TestSessions.Anchor)}');");
        }
        Execute(connection, "PRAGMA user_version = 12;");
    }

    private static void CreateSchemaThirteen(SqliteConnectionFactory connections)
    {
        using SqliteConnection connection = connections.OpenConnection();
        foreach (SchemaMigration migration in SchemaMigrations.All.Where(m => m.Version <= 13))
        {
            Execute(connection, migration.Sql);
            Execute(connection,
                $"INSERT INTO schema_migrations VALUES ({migration.Version}, '{migration.Name}', '{UtcTimestamp.Format(TestSessions.Anchor)}');");
        }
        Execute(connection, "PRAGMA user_version = 13;");
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
