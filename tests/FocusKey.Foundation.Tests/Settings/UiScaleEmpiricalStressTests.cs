using System.Data.Common;
using FocusKey.Foundation.Data;
using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests;
using FocusKey.Foundation.Tests.Sessions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class UiScaleEmpiricalStressTests
{
    private static readonly HexColor CustomWorkColor = HexColor.Parse("#1A2B3C");
    private static readonly HexColor CustomBreakColor = HexColor.Parse("#4D5E6F");

    #region Helper Methods

    private static void ExecuteSql(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static void ClearPool(SqliteConnectionFactory connections)
    {
        using SqliteConnection connection = connections.OpenConnection();
        SqliteConnection.ClearPool(connection);
    }

    private static void CreateSchemaUpTo(SqliteConnectionFactory connections, int maxVersion)
    {
        using SqliteConnection connection = connections.OpenConnection();
        foreach (SchemaMigration migration in SchemaMigrations.All.Where(m => m.Version <= maxVersion))
        {
            ExecuteSql(connection, migration.Sql);
            ExecuteSql(connection,
                $"INSERT INTO schema_migrations VALUES ({migration.Version}, '{migration.Name}', '{UtcTimestamp.Format(TestSessions.Anchor)}');");
        }
        ExecuteSql(connection, $"PRAGMA user_version = {maxVersion};");
    }

    #endregion

    #region 1. SQLite Migration 14 Upgrade Path from v13 Database

    [Fact]
    public async Task Migration14_FromV13WithCustomUserData_PreservesAllSettingsAndInitializesScaleTo100()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_m14_stress.db");
        var connections = new SqliteConnectionFactory(dbFile);

        // 1. Build schema up to version 13
        CreateSchemaUpTo(connections, 13);

        // 2. Populate comprehensive non-default settings in version 13 database
        using (SqliteConnection conn = connections.OpenConnection())
        {
            ExecuteSql(conn,
                """
                UPDATE application_settings SET
                    work_duration_seconds = 2700,
                    break_duration_seconds = 900,
                    appearance = 'dark',
                    work_color = '#1A2B3C',
                    break_color = '#4D5E6F',
                    session_sounds_enabled = 0,
                    start_sound_enabled = 0,
                    completion_sound_enabled = 0,
                    activity_collapsed = 0,
                    global_shortcut = 'Ctrl+Shift+O',
                    main_window_shortcut = 'Ctrl+Shift+M',
                    overlay_position_x = 123,
                    overlay_position_y = 456,
                    time_format = '12h',
                    appearance_expanded = 1,
                    shortcuts_expanded = 1,
                    advanced_expanded = 1
                WHERE singleton = 1;
                """);

            ExecuteSql(conn,
                """
                UPDATE theme_settings SET
                    light_preset = 'solarized',
                    light_background = '#FDF6E3',
                    light_foreground = '#657B83',
                    light_accent = '#268BD2',
                    dark_preset = 'nord',
                    dark_background = '#2E3440',
                    dark_foreground = '#D8DEE9',
                    dark_accent = '#88C0D0',
                    contrast = 'high'
                WHERE singleton = 1;
                """);
        }

        // 3. Run bootstrapper migration to target version (14)
        DatabaseBootstrapper bootstrapper = new(connections);
        DatabaseInitializationResult result = bootstrapper.Initialize();

        // 4. Assert migration metadata
        Assert.Equal(13, result.SchemaVersionBefore);
        Assert.Equal(14, result.SchemaVersionAfter);
        Assert.Equal([14], result.AppliedMigrations);

        // 5. Verify PRAGMA table_info confirms column schema definition
        using (SqliteConnection conn = connections.OpenConnection())
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA table_info(application_settings);";
            using SqliteDataReader reader = cmd.ExecuteReader();
            bool columnFound = false;
            while (reader.Read())
            {
                string colName = reader.GetString(1);
                if (colName == "ui_scale_percent")
                {
                    columnFound = true;
                    string colType = reader.GetString(2);
                    int notNull = reader.GetInt32(3);
                    string defaultVal = reader.IsDBNull(4) ? "" : reader.GetString(4);

                    Assert.Equal("INTEGER", colType.ToUpperInvariant());
                    Assert.Equal(1, notNull);
                    Assert.Equal("100", defaultVal);
                }
            }
            Assert.True(columnFound, "Column 'ui_scale_percent' must exist in table 'application_settings'.");
        }

        // 6. Verify SqliteSettingsRepository loads exact preserved values alongside default UiScalePercent 100
        var repo = new SqliteSettingsRepository(connections);
        ApplicationSettings loaded = await repo.LoadAsync();

        Assert.Equal(TimeSpan.FromMinutes(45), loaded.WorkDuration);
        Assert.Equal(TimeSpan.FromMinutes(15), loaded.BreakDuration);
        Assert.Equal(Appearance.Dark, loaded.Appearance);
        Assert.Equal(CustomWorkColor, loaded.WorkColor);
        Assert.Equal(CustomBreakColor, loaded.BreakColor);
        Assert.False(loaded.SessionSoundsEnabled);
        Assert.False(loaded.StartSoundEnabled);
        Assert.False(loaded.CompletionSoundEnabled);
        Assert.False(loaded.ActivityCollapsed);
        Assert.Equal("Ctrl + Shift + O", loaded.GlobalShortcut.ToString());
        Assert.Equal("Ctrl + Shift + M", loaded.MainWindowShortcut.ToString());
        Assert.Equal(123, loaded.OverlayPositionX);
        Assert.Equal(456, loaded.OverlayPositionY);
        Assert.Equal(TimeFormat.TwelveHour, loaded.TimeFormat);
        Assert.True(loaded.AppearanceExpanded);
        Assert.True(loaded.ShortcutsExpanded);
        Assert.True(loaded.AdvancedExpanded);
        Assert.Equal(Contrast.HigherContrast, loaded.Contrast);
        Assert.Equal("solarized", loaded.LightTheme.Preset);
        Assert.Equal("nord", loaded.DarkTheme.Preset);
        Assert.Equal(100, loaded.UiScalePercent);

        // 7. Verify updating scale on this migrated database works seamlessly
        var service = new SettingsService(repo);
        ApplicationSettings updated = await service.UpdateUiScalePercentAsync(125);
        Assert.Equal(125, updated.UiScalePercent);

        ApplicationSettings reloaded = await repo.LoadAsync();
        Assert.Equal(125, reloaded.UiScalePercent);
        Assert.Equal(TimeSpan.FromMinutes(45), reloaded.WorkDuration); // other fields preserved

        ClearPool(connections);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    public async Task Migration14_FromVariousHistoricalBaselines_UpgradesCleanlyToTargetVersion(int fromVersion)
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, $"focus_key_hist_{fromVersion}.db");
        var connections = new SqliteConnectionFactory(dbFile);

        if (fromVersion > 0)
        {
            CreateSchemaUpTo(connections, fromVersion);
        }

        DatabaseInitializationResult result = new DatabaseBootstrapper(connections).Initialize();

        Assert.Equal(fromVersion, result.SchemaVersionBefore);
        Assert.Equal(14, result.SchemaVersionAfter);

        var repo = new SqliteSettingsRepository(connections);
        ApplicationSettings loaded = await repo.LoadAsync();
        Assert.Equal(100, loaded.UiScalePercent);

        // Idempotent re-initialization should be a no-op
        DatabaseInitializationResult reResult = new DatabaseBootstrapper(connections).Initialize();
        Assert.Equal(14, reResult.SchemaVersionBefore);
        Assert.Equal(14, reResult.SchemaVersionAfter);
        Assert.Empty(reResult.AppliedMigrations);

        ClearPool(connections);
    }

    #endregion

    #region 2. Roundtrip Persistence in SqliteSettingsRepository

    [Theory]
    [InlineData(80)]
    [InlineData(90)]
    [InlineData(100)]
    [InlineData(110)]
    [InlineData(125)]
    [InlineData(150)]
    public async Task SqliteSettingsRepository_ExhaustiveRoundtrip_AllValidDiscreteScaleLevels(int scalePercent)
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, $"focus_key_scale_{scalePercent}.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);

        ApplicationSettings initial = await repo.LoadAsync();
        ApplicationSettings modified = initial with { UiScalePercent = scalePercent };
        await repo.SaveAsync(modified);

        // Reopen with fresh repository instance
        var freshRepo = new SqliteSettingsRepository(connections);
        ApplicationSettings loaded = await freshRepo.LoadAsync();

        Assert.Equal(scalePercent, loaded.UiScalePercent);
        Assert.Equal(initial.WorkDuration, loaded.WorkDuration);
        Assert.Equal(initial.Appearance, loaded.Appearance);

        ClearPool(connections);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(50)]
    [InlineData(75)]
    [InlineData(85)]
    [InlineData(95)]
    [InlineData(105)]
    [InlineData(120)]
    [InlineData(130)]
    [InlineData(175)]
    [InlineData(200)]
    public async Task SqliteSettingsRepository_SaveAsync_RejectsInvalidScaleLevels_ThrowsArgumentExceptionAndLeavesDbUnchanged(int invalidPercent)
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_invalid_save.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        ApplicationSettings before = await repo.LoadAsync();
        Assert.Equal(100, before.UiScalePercent);

        // Attempting to save invalid scale level
        var invalid = before with { UiScalePercent = invalidPercent };
        await Assert.ThrowsAsync<ArgumentException>(() => repo.SaveAsync(invalid));

        // Verify DB value remained 100
        ApplicationSettings after = await repo.LoadAsync();
        Assert.Equal(100, after.UiScalePercent);

        ClearPool(connections);
    }

    [Fact]
    public async Task SqliteSettingsRepository_DirectDbCorruption_InvalidScaleThrowsInvalidDataException()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_corrupted.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        // Corrupt ui_scale_percent directly in SQLite
        using (SqliteConnection conn = connections.OpenConnection())
        {
            ExecuteSql(conn, "UPDATE application_settings SET ui_scale_percent = 999 WHERE singleton = 1;");
        }

        var repo = new SqliteSettingsRepository(connections);
        InvalidDataException ex = await Assert.ThrowsAsync<InvalidDataException>(() => repo.LoadAsync());
        Assert.Contains("Persisted application settings are invalid", ex.Message);

        ClearPool(connections);
    }

    [Fact]
    public async Task SqliteSettingsRepository_NotNullConstraintAndNullFallback_EmpiricalVerification()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_null_scale.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        // 1. Verify that the SQLite schema NOT NULL constraint prevents NULL writes
        using (SqliteConnection conn = connections.OpenConnection())
        {
            var ex = Assert.Throws<SqliteException>(() =>
                ExecuteSql(conn, "UPDATE application_settings SET ui_scale_percent = NULL WHERE singleton = 1;"));
            Assert.Equal(19, ex.SqliteErrorCode); // SQLITE_CONSTRAINT
        }

        // 2. Now simulate a schema where NOT NULL was omitted by migrating data through a table without NOT NULL constraint
        using (SqliteConnection conn = connections.OpenConnection())
        {
            ExecuteSql(conn,
                """
                CREATE TABLE temp_settings (
                    singleton INTEGER PRIMARY KEY CHECK (singleton = 1),
                    work_duration_seconds INTEGER NOT NULL,
                    break_duration_seconds INTEGER NOT NULL,
                    appearance TEXT NOT NULL,
                    work_color TEXT NOT NULL,
                    break_color TEXT NOT NULL,
                    session_sounds_enabled INTEGER NOT NULL DEFAULT 1,
                    activity_collapsed INTEGER NOT NULL DEFAULT 1,
                    global_shortcut TEXT NOT NULL DEFAULT 'Win+Alt+F',
                    main_window_shortcut TEXT NOT NULL DEFAULT 'Ctrl+Alt+F',
                    overlay_position_x INTEGER,
                    overlay_position_y INTEGER,
                    time_format TEXT NOT NULL DEFAULT '24h',
                    appearance_expanded INTEGER NOT NULL DEFAULT 0,
                    shortcuts_expanded INTEGER NOT NULL DEFAULT 0,
                    advanced_expanded INTEGER NOT NULL DEFAULT 0,
                    start_sound_enabled INTEGER NOT NULL DEFAULT 1,
                    completion_sound_enabled INTEGER NOT NULL DEFAULT 1,
                    ui_scale_percent INTEGER
                );
                INSERT INTO temp_settings SELECT
                    singleton, work_duration_seconds, break_duration_seconds, appearance, work_color, break_color,
                    session_sounds_enabled, activity_collapsed, global_shortcut, main_window_shortcut,
                    overlay_position_x, overlay_position_y, time_format, appearance_expanded, shortcuts_expanded,
                    advanced_expanded, start_sound_enabled, completion_sound_enabled, NULL
                FROM application_settings;
                DROP TABLE application_settings;
                ALTER TABLE temp_settings RENAME TO application_settings;
                """);
        }

        var repo = new SqliteSettingsRepository(connections);
        ApplicationSettings loaded = await repo.LoadAsync();
        Assert.Equal(100, loaded.UiScalePercent);

        ClearPool(connections);
    }

    #endregion

    #region 3. Restart Survival and Cold Connection Cycling

    [Fact]
    public async Task RestartSurvival_MultiCycleColdRestart_PersistsAcrossPoolClearingAndDisposals()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_cold_restart.db");

        // Cycle through all supported levels across simulated cold restarts
        int[] cycle = [110, 80, 150, 90, 125, 100];

        // Bootstrap initially
        {
            var initFactory = new SqliteConnectionFactory(dbFile);
            new DatabaseBootstrapper(initFactory).Initialize();
            ClearPool(initFactory);
        }

        for (int i = 0; i < cycle.Length; i++)
        {
            int targetScale = cycle[i];

            // Cold boot 1: open, mutate, save, clear pool, teardown
            {
                var factory = new SqliteConnectionFactory(dbFile);
                var repo = new SqliteSettingsRepository(factory);
                var service = new SettingsService(repo);

                await service.UpdateUiScalePercentAsync(targetScale);

                ClearPool(factory);
            }

            // Force GC to simulate process termination / releasing references
            GC.Collect();
            GC.WaitForPendingFinalizers();

            // Cold boot 2: brand-new factory, brand-new repository, assert persistence
            {
                var factory = new SqliteConnectionFactory(dbFile);
                var repo = new SqliteSettingsRepository(factory);

                ApplicationSettings loaded = await repo.LoadAsync();
                Assert.Equal(targetScale, loaded.UiScalePercent);

                ClearPool(factory);
            }
        }
    }

    #endregion

    #region 4. Concurrency in SettingsService

    [Fact]
    public async Task SettingsService_HighConcurrencyStress_100ParallelTasks_NoDeadlocksOrErrors()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_concurrency_stress.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var service = new SettingsService(repo);

        int[] validScales = UiScaleLevels.All;
        const int taskCount = 100;
        var tasks = new Task[taskCount];

        for (int i = 0; i < taskCount; i++)
        {
            int index = i;
            tasks[i] = Task.Run(async () =>
            {
                if (index % 4 == 0)
                {
                    // Update UI scale
                    int scale = validScales[index % validScales.Length];
                    ApplicationSettings result = await service.UpdateUiScalePercentAsync(scale);
                    Assert.True(UiScaleLevels.IsValid(result.UiScalePercent));
                }
                else if (index % 4 == 1)
                {
                    // Update Work Duration
                    TimeSpan duration = TimeSpan.FromMinutes(20 + (index % 30));
                    ApplicationSettings result = await service.UpdateWorkDurationAsync(duration);
                    Assert.Equal(duration, result.WorkDuration);
                }
                else if (index % 4 == 2)
                {
                    // Update Appearance
                    Appearance appearance = (Appearance)(index % 3);
                    ApplicationSettings result = await service.UpdateAppearanceAsync(appearance);
                    Assert.Equal(appearance, result.Appearance);
                }
                else
                {
                    // Load concurrently
                    ApplicationSettings result = await service.LoadAsync();
                    Assert.True(UiScaleLevels.IsValid(result.UiScalePercent));
                }
            });
        }

        // Must complete without throwing or deadlocking
        await Task.WhenAll(tasks);

        // Cold verification from disk
        ClearPool(connections);
        var verifyRepo = new SqliteSettingsRepository(new SqliteConnectionFactory(dbFile));
        ApplicationSettings finalSettings = await verifyRepo.LoadAsync();
        Assert.True(UiScaleLevels.IsValid(finalSettings.UiScalePercent));

        ClearPool(connections);
    }

    [Fact]
    public async Task SettingsService_InterleavedMultiFieldMutations_NoLostUpdatesAcrossFields()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_interleaved.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var service = new SettingsService(repo);

        // Perform simultaneous distinct field updates
        var t1 = Task.Run(() => service.UpdateUiScalePercentAsync(125));
        var t2 = Task.Run(() => service.UpdateWorkDurationAsync(TimeSpan.FromMinutes(45)));
        var t3 = Task.Run(() => service.UpdateBreakDurationAsync(TimeSpan.FromMinutes(12)));
        var t4 = Task.Run(() => service.UpdateAppearanceAsync(Appearance.Dark));

        await Task.WhenAll(t1, t2, t3, t4);

        // Reload and verify all distinct fields reflect their updates
        ApplicationSettings settled = await service.LoadAsync();
        Assert.Equal(125, settled.UiScalePercent);
        Assert.Equal(TimeSpan.FromMinutes(45), settled.WorkDuration);
        Assert.Equal(TimeSpan.FromMinutes(12), settled.BreakDuration);
        Assert.Equal(Appearance.Dark, settled.Appearance);

        ClearPool(connections);
    }

    [Fact]
    public async Task SettingsService_ConcurrencyWithCancellations_SemaphoreNeverLeaks()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_cancel_stress.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var service = new SettingsService(repo);

        // Fire 30 tasks with already-cancelled tokens
        for (int i = 0; i < 30; i++)
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.UpdateUiScalePercentAsync(110, cts.Token));
        }

        // Fire 30 tasks with rapidly-cancelling tokens concurrently
        var tasks = new List<Task>();
        for (int i = 0; i < 30; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(i % 3));
                try
                {
                    await service.UpdateUiScalePercentAsync(125, cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }));
        }
        await Task.WhenAll(tasks);

        // Crucial test: verify the semaphore was NOT leaked and service still accepts normal updates promptly
        ApplicationSettings healthy = await service.UpdateUiScalePercentAsync(150);
        Assert.Equal(150, healthy.UiScalePercent);

        ApplicationSettings verified = await service.LoadAsync();
        Assert.Equal(150, verified.UiScalePercent);

        ClearPool(connections);
    }

    #endregion

    #region 5. SettingsPageController Rapid Burst & Debounce

    [Fact]
    public async Task SettingsPageController_RapidBurstScaling_LatestScaleWinsAndDrains()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_page_controller.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var service = new SettingsService(repo);

        int refreshCount = 0;
        Exception? reportedError = null;
        var controller = new SettingsPageController(
            service,
            () => { Interlocked.Increment(ref refreshCount); return Task.CompletedTask; },
            ex => { reportedError = ex; });

        await controller.LoadAsync();
        Assert.Equal(100, controller.Saved!.UiScalePercent);

        // Rapidly fire scale changes without awaiting each individually (simulating user sliding/keying)
        int[] sequence = [80, 90, 110, 125, 150];
        foreach (int target in sequence)
        {
            _ = controller.UpdateUiScaleAsync(target);
        }

        // Await the queue draining
        await controller.DrainAsync();

        Assert.Null(reportedError);
        Assert.Equal(150, controller.Saved.UiScalePercent);

        // Verify database persistence
        ApplicationSettings onDisk = await repo.LoadAsync();
        Assert.Equal(150, onDisk.UiScalePercent);

        ClearPool(connections);
    }

    #endregion

    #region 6. UiScaleLevels Pure Domain Calculation Edge Cases

    [Theory]
    [InlineData(80, 0.80)]
    [InlineData(90, 0.90)]
    [InlineData(100, 1.00)]
    [InlineData(110, 1.10)]
    [InlineData(125, 1.25)]
    [InlineData(150, 1.50)]
    public void UiScaleLevels_ToFactor_ExactFactors(int percent, double expectedFactor)
    {
        Assert.Equal(expectedFactor, UiScaleLevels.ToFactor(percent), precision: 4);
    }

    [Theory]
    [InlineData(0, 1.00)]
    [InlineData(-50, 1.00)]
    [InlineData(105, 1.00)]
    [InlineData(200, 1.00)]
    public void UiScaleLevels_ToFactor_InvalidPercentFallsBackTo1(int invalidPercent, double fallbackFactor)
    {
        Assert.Equal(fallbackFactor, UiScaleLevels.ToFactor(invalidPercent));
    }

    [Theory]
    [InlineData(80, 90)]
    [InlineData(90, 100)]
    [InlineData(100, 110)]
    [InlineData(110, 125)]
    [InlineData(125, 150)]
    [InlineData(150, 150)] // Clamped at upper bound
    [InlineData(50, 80)]   // Below minimum clamps to lowest
    [InlineData(95, 100)]  // Between levels steps to next
    [InlineData(200, 150)] // Above maximum clamps to highest
    public void UiScaleLevels_NextLevel_BoundaryAndIntermediateValues(int current, int expectedNext)
    {
        Assert.Equal(expectedNext, UiScaleLevels.NextLevel(current));
    }

    [Theory]
    [InlineData(150, 125)]
    [InlineData(125, 110)]
    [InlineData(110, 100)]
    [InlineData(100, 90)]
    [InlineData(90, 80)]
    [InlineData(80, 80)]   // Clamped at lower bound
    [InlineData(200, 150)] // Above maximum clamps to highest
    [InlineData(95, 90)]   // Between levels steps to previous
    [InlineData(50, 80)]   // Below minimum clamps to lowest
    public void UiScaleLevels_PreviousLevel_BoundaryAndIntermediateValues(int current, int expectedPrev)
    {
        Assert.Equal(expectedPrev, UiScaleLevels.PreviousLevel(current));
    }

    [Theory]
    [InlineData(1000, 1.25, 800)]
    [InlineData(1000, 0.80, 1250)]
    [InlineData(1000, 1.00, 1000)]
    [InlineData(1000, 0.0, 1000)] // Zero factor fallback to actualWidth
    [InlineData(1000, -1.0, 1000)] // Negative factor fallback to actualWidth
    [InlineData(1000, double.NaN, 1000)] // NaN fallback
    [InlineData(1000, double.PositiveInfinity, 1000)] // Infinity fallback
    public void UiScaleLevels_CalculateEffectiveWidth_CalculatesCorrectlyOrFallsBack(double width, double factor, double expected)
    {
        Assert.Equal(expected, UiScaleLevels.CalculateEffectiveWidth(width, factor));
    }

    #endregion

    #region 7. Advanced Stress: Multi-Instance, Atomic Rollback, and Heavy Concurrency

    [Fact]
    public async Task MultiInstanceConcurrency_TwoIndependentServices_SameDatabase_CoordinatedCleanly()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_multi_instance.db");
        var connFactory1 = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connFactory1).Initialize();

        var connFactory2 = new SqliteConnectionFactory(dbFile);

        var service1 = new SettingsService(new SqliteSettingsRepository(connFactory1));
        var service2 = new SettingsService(new SqliteSettingsRepository(connFactory2));

        var tasks = new List<Task>();
        for (int i = 0; i < 40; i++)
        {
            int scale = UiScaleLevels.All[i % UiScaleLevels.All.Length];
            var svc = (i % 2 == 0) ? service1 : service2;
            tasks.Add(Task.Run(async () =>
            {
                await svc.UpdateUiScalePercentAsync(scale);
            }));
        }

        await Task.WhenAll(tasks);

        ClearPool(connFactory1);
        ClearPool(connFactory2);

        var verifyRepo = new SqliteSettingsRepository(new SqliteConnectionFactory(dbFile));
        ApplicationSettings finalSettings = await verifyRepo.LoadAsync();
        Assert.True(UiScaleLevels.IsValid(finalSettings.UiScalePercent));
    }

    [Fact]
    public async Task TransactionRollback_SecondaryTableFailure_AtomicallyRollsBackUiScale()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_rollback.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        ApplicationSettings before = await repo.LoadAsync();
        Assert.Equal(100, before.UiScalePercent);

        // Add a trigger on theme_settings to deliberately abort updates to that table
        using (SqliteConnection conn = connections.OpenConnection())
        {
            ExecuteSql(conn,
                """
                CREATE TRIGGER fail_theme_update
                BEFORE UPDATE ON theme_settings
                BEGIN
                    SELECT RAISE(ABORT, 'Deliberate test abort in theme_settings update');
                END;
                """);
        }

        // Attempt to save scale change (150)
        var modified = before with { UiScalePercent = 150 };
        await Assert.ThrowsAnyAsync<SqliteException>(() => repo.SaveAsync(modified));

        // Disable trigger
        using (SqliteConnection conn = connections.OpenConnection())
        {
            ExecuteSql(conn, "DROP TRIGGER fail_theme_update;");
        }

        // Verify that application_settings.ui_scale_percent was NOT committed (rolled back)
        ApplicationSettings after = await repo.LoadAsync();
        Assert.Equal(100, after.UiScalePercent);

        ClearPool(connections);
    }

    [Fact]
    public async Task Migration14_WithPreExistingSessions_DoesNotAffectSessionRecordsOrIntegrity()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_sessions_mig.db");
        var connections = new SqliteConnectionFactory(dbFile);

        // Setup schema at version 13
        CreateSchemaUpTo(connections, 13);

        // Insert a completed session record using official repository
        var sessionRepo = new SqliteSessionRepository(connections);
        var finished = TestSessions.Finished(SessionStatus.Completed);
        await sessionRepo.AddAsync(finished);

        // Execute migration 14
        var result = new DatabaseBootstrapper(connections).Initialize();
        Assert.Equal([14], result.AppliedMigrations);

        // Verify completed session is intact
        SessionRecord? reloadedSession = await sessionRepo.GetAsync(finished.Id);
        Assert.NotNull(reloadedSession);
        Assert.Equal(finished.Id, reloadedSession.Id);
        Assert.Equal(finished.Status, reloadedSession.Status);

        // Verify settings loaded with default scale 100
        var repo = new SqliteSettingsRepository(connections);
        ApplicationSettings loaded = await repo.LoadAsync();
        Assert.Equal(100, loaded.UiScalePercent);

        ClearPool(connections);
    }

    [Fact]
    public async Task SettingsService_HeavyConcurrentMixedLoad_500Operations_SucceedsWithoutError()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "focus_key_heavy_stress.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var service = new SettingsService(repo);

        const int operations = 500;
        var tasks = new Task[operations];
        int[] validScales = UiScaleLevels.All;

        for (int i = 0; i < operations; i++)
        {
            int index = i;
            tasks[i] = Task.Run(async () =>
            {
                switch (index % 5)
                {
                    case 0:
                        await service.UpdateUiScalePercentAsync(validScales[index % validScales.Length]);
                        break;
                    case 1:
                        await service.UpdateWorkDurationAsync(TimeSpan.FromMinutes(25 + (index % 10)));
                        break;
                    case 2:
                        await service.UpdateBreakDurationAsync(TimeSpan.FromMinutes(5 + (index % 5)));
                        break;
                    case 3:
                        await service.UpdateAppearanceAsync((Appearance)(index % 3));
                        break;
                    default:
                        await service.LoadAsync();
                        break;
                }
            });
        }

        await Task.WhenAll(tasks);

        ApplicationSettings finalState = await service.LoadAsync();
        Assert.True(UiScaleLevels.IsValid(finalState.UiScalePercent));

        ClearPool(connections);
    }

    #endregion
}

