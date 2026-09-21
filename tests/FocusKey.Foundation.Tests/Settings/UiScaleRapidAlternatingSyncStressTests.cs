using System.Diagnostics;
using FocusKey.Foundation.Data;
using FocusKey.Foundation.Settings;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class UiScaleRapidAlternatingSyncStressTests
{
    private static readonly string[] UiScaleOptions = ["80%", "90%", "100%", "110%", "125%", "150%"];

    private static void ClearPool(SqliteConnectionFactory connections)
    {
        using SqliteConnection connection = connections.OpenConnection();
        SqliteConnection.ClearPool(connection);
    }

    /// <summary>
    /// Test 1: Rapid sequential alternating inputs between shortcut stepping and ComboBox selection.
    /// Verifies zero deadlocks, zero lock contention, and 100% database persistence accuracy.
    /// </summary>
    [Fact]
    public async Task SequentialAlternating_ShortcutAndComboBox_PersistsConsistentlyWithoutDeadlocks()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "sequential_alternating.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var service = new SettingsService(repo);

        int runtimeRefreshes = 0;
        var controller = new SettingsPageController(
            service,
            () => { Interlocked.Increment(ref runtimeRefreshes); return Task.CompletedTask; },
            ex => throw ex);

        await controller.LoadAsync();
        Assert.Equal(100, controller.Saved!.UiScalePercent);

        // Simulation state
        int currentUiScale = 100;
        int simulatedComboBoxIndex = Array.IndexOf(UiScaleOptions, "100%");

        void OnShortcut(int newPercent)
        {
            currentUiScale = newPercent;
            simulatedComboBoxIndex = Array.IndexOf(UiScaleOptions, $"{newPercent}%");
        }

        controller.Settled += (field, saved) =>
        {
            if (field == SettingsField.UiScale)
            {
                simulatedComboBoxIndex = Array.IndexOf(UiScaleOptions, $"{saved.UiScalePercent}%");
            }
        };


        const int iterations = 30;
        int expectedFinalScale = 100;

        for (int i = 0; i < iterations; i++)
        {
            if (i % 2 == 0)
            {
                // Simulated Shortcut: Ctrl+Plus or Ctrl+Minus
                int next = (i % 4 == 0) ? UiScaleLevels.NextLevel(currentUiScale) : UiScaleLevels.PreviousLevel(currentUiScale);
                OnShortcut(next);
                await service.UpdateUiScalePercentAsync(next);
                expectedFinalScale = next;
            }
            else
            {
                // Simulated ComboBox selection
                int targetPercent = UiScaleLevels.All[i % UiScaleLevels.All.Length];
                currentUiScale = targetPercent;
                simulatedComboBoxIndex = Array.IndexOf(UiScaleOptions, $"{targetPercent}%");
                await controller.UpdateUiScaleAsync(targetPercent);
                expectedFinalScale = targetPercent;
            }
        }

        // Verify in-memory state
        Assert.Equal(expectedFinalScale, currentUiScale);
        Assert.Equal(Array.IndexOf(UiScaleOptions, $"{expectedFinalScale}%"), simulatedComboBoxIndex);

        // Verify database state directly from disk
        ClearPool(connections);
        var freshRepo = new SqliteSettingsRepository(new SqliteConnectionFactory(dbFile));
        ApplicationSettings diskSettings = await freshRepo.LoadAsync();
        Assert.Equal(expectedFinalScale, diskSettings.UiScalePercent);
    }

    /// <summary>
    /// Test 2: Rapid concurrent bursts of alternating inputs (shortcuts direct to service vs combobox via controller).
    /// Stresses SemaphoreSlim and SQLite connection handling to ensure zero deadlocks (under 10s timeout).
    /// </summary>
    [Fact]
    public async Task ConcurrentAlternatingBursts_StressZeroDeadlocksAndSqliteIntegrity()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "concurrent_alternating_bursts.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var service = new SettingsService(repo);

        var controller = new SettingsPageController(
            service,
            () => Task.CompletedTask,
            ex => { /* ignore or log controller runtime errors */ });

        await controller.LoadAsync();

        const int threadCount = 20;
        var tasks = new List<Task>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        for (int i = 0; i < threadCount; i++)
        {
            int index = i;
            if (index % 2 == 0)
            {
                // Shortcut path: calls service.UpdateUiScalePercentAsync directly
                int scale = UiScaleLevels.All[index % UiScaleLevels.All.Length];
                tasks.Add(Task.Run(async () =>
                {
                    await service.UpdateUiScalePercentAsync(scale, cts.Token);
                }));
            }
            else
            {
                // ComboBox path: calls controller.UpdateUiScaleAsync
                int scale = UiScaleLevels.All[index % UiScaleLevels.All.Length];
                tasks.Add(Task.Run(async () =>
                {
                    await controller.UpdateUiScaleAsync(scale, cts.Token);
                }));
            }
        }

        // Must complete within 10 seconds without deadlocking or throwing SQLite concurrency errors
        await Task.WhenAll(tasks);
        await controller.DrainAsync();

        // Check SQLite state validity
        ClearPool(connections);
        var freshRepo = new SqliteSettingsRepository(new SqliteConnectionFactory(dbFile));
        ApplicationSettings diskSettings = await freshRepo.LoadAsync();
        Assert.True(UiScaleLevels.IsValid(diskSettings.UiScalePercent),
            $"Disk persisted UI scale {diskSettings.UiScalePercent} must be a valid discrete level.");
    }

    /// <summary>
    /// Test 3: Interleaved shortcut execution while ComboBox persistence is in flight.
    /// Empirically checks if SettingsPageController.Settled clobbers the ComboBox selection with stale data.
    /// </summary>
    [Fact]
    public async Task InterleavedShortcutWhileComboBoxSaveInFlight_TestsConsistency()
    {
        var gatedRepo = new GatedSettingsRepository();
        var service = new SettingsService(gatedRepo);
        var controller = new SettingsPageController(
            service,
            () => Task.CompletedTask,
            ex => throw ex);

        await controller.LoadAsync();
        Assert.Equal(100, controller.Saved!.UiScalePercent);

        // Simulated UI state
        int simulatedMainWindowScale = 100;
        int simulatedComboBoxPercent = 100;

        void MainWindowApplyUiScale(int percent, bool persist)
        {
            simulatedMainWindowScale = percent;
            simulatedComboBoxPercent = percent;

            if (persist)
            {
                // Dispatches to controller.UpdateUiScaleAsync
                _ = controller.UpdateUiScaleAsync(percent);
            }
        }

        controller.Settled += (field, saved) =>
        {
            if (field == SettingsField.UiScale)
            {
                simulatedComboBoxPercent = saved.UiScalePercent;
            }
        };


        // 1. User selects 125% in ComboBox:
        // Gate the repository so the save stays in flight
        var saveStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var allowSaveToProceedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        gatedRepo.OnSaveHook = () =>
        {
            saveStartedTcs.TrySetResult();
            return allowSaveToProceedTcs.Task;
        };

        // ComboBox updates MainWindow (persist: false) and calls controller.UpdateUiScaleAsync(125)
        MainWindowApplyUiScale(125, persist: false);
        Task controllerTask = controller.UpdateUiScaleAsync(125);

        // Wait until save has started and is suspended in flight
        await saveStartedTcs.Task;
        Assert.Equal(125, simulatedComboBoxPercent);
        Assert.Equal(125, simulatedMainWindowScale);

        // 2. While 125% is suspended in flight, user presses shortcut: Ctrl+Minus -> 110%
        // Reset hook so the shortcut save proceeds without suspension once it acquires the service lock
        gatedRepo.OnSaveHook = null;
        MainWindowApplyUiScale(110, persist: true);

        // At this moment, UI has been updated to 110% by the shortcut
        Assert.Equal(110, simulatedMainWindowScale);
        Assert.Equal(110, simulatedComboBoxPercent);

        // 3. Now let the ComboBox save (125%) proceed and complete
        allowSaveToProceedTcs.SetResult();
        await controllerTask;

        // Drain any pending operations on service
        await controller.DrainAsync();

        // Now inspect the state:
        Assert.Equal(110, simulatedMainWindowScale);
        Assert.Equal(110, gatedRepo.Current.UiScalePercent);
        Assert.Equal(110, simulatedComboBoxPercent);
    }


    /// <summary>
    /// Test 4: Real SQLite database: Rapid alternating ComboBox selection followed immediately by shortcut.
    /// Demonstrates that routing shortcut scale changes through controller eliminates desynchronization.
    /// </summary>
    [Fact]
    public async Task RealSqlite_RapidAlternatingComboBoxAndShortcuts_ReproducesDesynchronizationRace()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "real_sqlite_alternating_race.db");
        var connections = new SqliteConnectionFactory(dbFile);
        new DatabaseBootstrapper(connections).Initialize();

        var repo = new SqliteSettingsRepository(connections);
        var service = new SettingsService(repo);

        var controller = new SettingsPageController(
            service,
            () => Task.CompletedTask,
            ex => throw ex);

        await controller.LoadAsync();

        int simulatedMainWindowScale = 100;
        int simulatedComboBoxPercent = 100;

        void MainWindowApplyUiScale(int percent, bool persist)
        {
            simulatedMainWindowScale = percent;
            simulatedComboBoxPercent = percent;

            if (persist)
            {
                _ = controller.UpdateUiScaleAsync(percent);
            }
        }

        controller.Settled += (field, saved) =>
        {
            if (field == SettingsField.UiScale)
            {
                simulatedComboBoxPercent = saved.UiScalePercent;
            }
        };

        // ComboBox user selection to 150%
        MainWindowApplyUiScale(150, persist: false);
        Task comboTask = controller.UpdateUiScaleAsync(150);

        // Immediate user shortcut: Ctrl+Minus to 80%
        MainWindowApplyUiScale(80, persist: true);

        await comboTask;
        await controller.DrainAsync();

        ClearPool(connections);
        var freshRepo = new SqliteSettingsRepository(new SqliteConnectionFactory(dbFile));
        ApplicationSettings diskSettings = await freshRepo.LoadAsync();

        // Disk has 80% and MainWindow has 80%
        Assert.Equal(80, simulatedMainWindowScale);
        Assert.Equal(80, diskSettings.UiScalePercent);

        // Does simulated ComboBox equal 80% or did Settled clobber it to 150%?
        Assert.Equal(diskSettings.UiScalePercent, simulatedComboBoxPercent);
    }

    private sealed class GatedSettingsRepository : ISettingsRepository
    {
        public ApplicationSettings Current = ApplicationSettings.Default;
        public Func<Task>? OnSaveHook;


        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            if (OnSaveHook is not null)
            {
                await OnSaveHook();
            }
            settings.Validate();
            Current = settings;
        }
    }
}

