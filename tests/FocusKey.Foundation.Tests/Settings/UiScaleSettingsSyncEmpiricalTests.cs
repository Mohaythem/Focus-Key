using System.Data.Common;
using FocusKey.Foundation.Data;
using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Tests;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FocusKey.Foundation.Tests.Settings;

/// <summary>
/// Empirical challenge test suite for UI scaling settings sync (Settings UI ComboBox & Live Bidirectional Sync).
/// Tests discrete string/int mapping, boundary values, re-entrancy prevention, and concurrency invariants.
/// </summary>
public sealed class UiScaleSettingsSyncEmpiricalTests
{
    private static readonly string[] UiScaleOptions = ["80%", "90%", "100%", "110%", "125%", "150%"];

    #region 1. Exhaustive Discrete String/Int Mapping and Boundary Oracle

    [Theory]
    [InlineData(80, "80%", 0, 0.80)]
    [InlineData(90, "90%", 1, 0.90)]
    [InlineData(100, "100%", 2, 1.00)]
    [InlineData(110, "110%", 3, 1.10)]
    [InlineData(125, "125%", 4, 1.25)]
    [InlineData(150, "150%", 5, 1.50)]
    public void DiscreteLevelBijectiveMapping_ExactContracts(int percent, string text, int expectedIndex, double expectedFactor)
    {
        // 1. Value in UiScaleLevels.All
        Assert.Equal(percent, UiScaleLevels.All[expectedIndex]);
        Assert.True(UiScaleLevels.IsValid(percent));

        // 2. Formatted string equals option
        Assert.Equal(text, UiScaleOptions[expectedIndex]);

        // 3. String to Int parsing
        Assert.True(text.EndsWith('%'));
        Assert.True(int.TryParse(text.TrimEnd('%'), out int parsed));
        Assert.Equal(percent, parsed);

        // 4. Index lookup
        Assert.Equal(expectedIndex, Array.IndexOf(UiScaleOptions, text));

        // 5. Factor calculation
        Assert.Equal(expectedFactor, UiScaleLevels.ToFactor(percent), precision: 4);
    }

    [Fact]
    public void ExhaustiveRange_AllIntegersBetweenNegative1000And1000_ClampSafelyToValidLevel()
    {
        // For every integer from -1000 to +1000, test the clamping logic used in SettingsView and MainWindow
        for (int p = -1000; p <= 1000; p++)
        {
            int clamped = UiScaleLevels.IsValid(p)
                ? p
                : (p < UiScaleLevels.MinPercent
                    ? UiScaleLevels.MinPercent
                    : (p > UiScaleLevels.MaxPercent ? UiScaleLevels.MaxPercent : UiScaleLevels.DefaultPercent));

            Assert.True(UiScaleLevels.IsValid(clamped), $"Clamped value {clamped} for input {p} must be valid.");

            string target = $"{clamped}%";
            int idx = Array.IndexOf(UiScaleOptions, target);
            Assert.True(idx >= 0 && idx < UiScaleOptions.Length, $"Target string '{target}' must exist in UiScaleOptions.");
            Assert.Equal(clamped, int.Parse(UiScaleOptions[idx].TrimEnd('%')));
        }
    }

    [Theory]
    [InlineData(int.MinValue, 80)]
    [InlineData(int.MinValue + 1, 80)]
    [InlineData(-100000, 80)]
    [InlineData(-1, 80)]
    [InlineData(0, 80)]
    [InlineData(79, 80)]
    [InlineData(80, 80)]
    [InlineData(81, 100)]
    [InlineData(89, 100)]
    [InlineData(90, 90)]
    [InlineData(91, 100)]
    [InlineData(99, 100)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(109, 100)]
    [InlineData(110, 110)]
    [InlineData(111, 100)]
    [InlineData(124, 100)]
    [InlineData(125, 125)]
    [InlineData(126, 100)]
    [InlineData(149, 100)]
    [InlineData(150, 150)]
    [InlineData(151, 150)]
    [InlineData(100000, 150)]
    [InlineData(int.MaxValue - 1, 150)]
    [InlineData(int.MaxValue, 150)]
    public void BoundaryAndExtremeIntegerValues_ClampAccurately(int input, int expectedClamped)
    {
        int clamped = UiScaleLevels.IsValid(input)
            ? input
            : (input < UiScaleLevels.MinPercent
                ? UiScaleLevels.MinPercent
                : (input > UiScaleLevels.MaxPercent ? UiScaleLevels.MaxPercent : UiScaleLevels.DefaultPercent));

        Assert.Equal(expectedClamped, clamped);
        Assert.True(UiScaleLevels.IsValid(clamped));
    }

    [Theory]
    [InlineData(null, -1, 100)]
    [InlineData("", -1, 100)]
    [InlineData("invalid", -1, 100)]
    [InlineData("100", -1, 100)] // missing %
    [InlineData("999%", -1, 100)] // out of range percentage
    [InlineData("80%", 0, 80)]
    [InlineData("150%", 5, 150)]
    public void StringParsing_RobustFallbackLogic(object? selectedItem, int selectedIndex, int expectedResult)
    {
        // Replicates SettingsView.SelectionChanged parsing logic
        int percent = UiScaleLevels.DefaultPercent;
        if (selectedItem is string text && text.EndsWith('%') && int.TryParse(text.TrimEnd('%'), out int parsed))
        {
            percent = parsed;
        }
        else if (selectedIndex >= 0 && selectedIndex < UiScaleOptions.Length &&
                 int.TryParse(UiScaleOptions[selectedIndex].TrimEnd('%'), out int optParsed))
        {
            percent = optParsed;
        }

        if (!UiScaleLevels.IsValid(percent))
            percent = UiScaleLevels.DefaultPercent;

        Assert.Equal(expectedResult, percent);
        Assert.True(UiScaleLevels.IsValid(percent));
    }

    #endregion

    #region 2. Live Bidirectional Synchronization & Re-entrancy Prevention Simulation

    private sealed class SimulatedMainWindowAndSettingsHarness
    {
        public int MainWindowScale { get; set; } = 100;
        public int SettingsViewIndex { get; set; } = 2; // "100%"
        public string SettingsViewItem { get; set; } = "100%";
        public bool ApplyingFlag { get; set; } = false;

        public int MainWindowPersistCalls { get; set; } = 0;
        public int ControllerPersistCalls { get; set; } = 0;
        public int HudDisplayCalls { get; set; } = 0;
        public int MaxCallStackDepth { get; private set; } = 0;

        private int _currentStackDepth = 0;

        private void EnterMethod()
        {
            _currentStackDepth++;
            if (_currentStackDepth > MaxCallStackDepth)
                MaxCallStackDepth = _currentStackDepth;
            if (_currentStackDepth > 10)
                throw new InvalidOperationException($"Re-entrant loop detected! Call depth: {_currentStackDepth}");
        }

        private void ExitMethod() => _currentStackDepth--;

        public void MainWindow_ApplyUiScale(int percent, bool persist = true)
        {
            EnterMethod();
            try
            {
                int clamped = UiScaleLevels.IsValid(percent)
                    ? percent
                    : (percent < UiScaleLevels.MinPercent ? UiScaleLevels.MinPercent : (percent > UiScaleLevels.MaxPercent ? UiScaleLevels.MaxPercent : UiScaleLevels.DefaultPercent));

                MainWindowScale = clamped;
                HudDisplayCalls++;

                // Propagate to SettingsView
                SettingsView_ApplyUiScale(clamped);

                if (persist)
                {
                    MainWindowPersistCalls++;
                }
            }
            finally
            {
                ExitMethod();
            }
        }

        public void SettingsView_ApplyUiScale(int percent)
        {
            EnterMethod();
            ApplyingFlag = true;
            try
            {
                UpdateUiScaleSelection(percent);
            }
            finally
            {
                ApplyingFlag = false;
                ExitMethod();
            }
        }

        public void SettingsView_UserChangesSelection(int newIndex)
        {
            EnterMethod();
            try
            {
                SettingsViewIndex = newIndex;
                SettingsViewItem = UiScaleOptions[newIndex];
                SettingsView_OnSelectionChanged();
            }
            finally
            {
                ExitMethod();
            }
        }

        private void SettingsView_OnSelectionChanged()
        {
            EnterMethod();
            try
            {
                if (ApplyingFlag) return;
                if (SettingsViewIndex < 0) return;

                int percent = UiScaleLevels.DefaultPercent;
                if (SettingsViewItem.EndsWith('%') && int.TryParse(SettingsViewItem.TrimEnd('%'), out int parsed))
                {
                    percent = parsed;
                }

                if (!UiScaleLevels.IsValid(percent))
                    percent = UiScaleLevels.DefaultPercent;

                // 1. Notify MainWindow with persist: false
                MainWindow_ApplyUiScale(percent, persist: false);

                // 2. Controller updates persistence
                ControllerPersistCalls++;
            }
            finally
            {
                ExitMethod();
            }
        }

        private void UpdateUiScaleSelection(int percent)
        {
            EnterMethod();
            try
            {
                int clamped = UiScaleLevels.IsValid(percent)
                    ? percent
                    : (percent < UiScaleLevels.MinPercent ? UiScaleLevels.MinPercent : (percent > UiScaleLevels.MaxPercent ? UiScaleLevels.MaxPercent : UiScaleLevels.DefaultPercent));

                string target = $"{clamped}%";
                int idx = Array.IndexOf(UiScaleOptions, target);
                if (idx >= 0)
                {
                    if (SettingsViewIndex != idx)
                    {
                        SettingsViewIndex = idx;
                        SettingsView_OnSelectionChanged();
                    }
                    SettingsViewItem = UiScaleOptions[idx];
                }
            }
            finally
            {
                ExitMethod();
            }
        }
    }

    [Fact]
    public void BidirectionalSync_ShortcutTriggered_PersistsOnceAndUpdatesComboBoxWithoutRecursion()
    {
        var harness = new SimulatedMainWindowAndSettingsHarness();
        Assert.Equal(100, harness.MainWindowScale);
        Assert.Equal(2, harness.SettingsViewIndex);

        // User triggers Ctrl+Plus (100 -> 110)
        harness.MainWindow_ApplyUiScale(110, persist: true);

        // Verifications
        Assert.Equal(110, harness.MainWindowScale);
        Assert.Equal(3, harness.SettingsViewIndex);
        Assert.Equal("110%", harness.SettingsViewItem);
        Assert.Equal(1, harness.MainWindowPersistCalls);
        Assert.Equal(0, harness.ControllerPersistCalls); // No duplicate save from controller
        Assert.Equal(1, harness.HudDisplayCalls);
        Assert.True(harness.MaxCallStackDepth <= 4, $"Max stack depth was {harness.MaxCallStackDepth}, expected <= 4");
    }

    [Fact]
    public void BidirectionalSync_SettingsViewSelectionTriggered_PersistsOnceAndUpdatesMainWindowLive()
    {
        var harness = new SimulatedMainWindowAndSettingsHarness();
        Assert.Equal(100, harness.MainWindowScale);
        Assert.Equal(2, harness.SettingsViewIndex);

        // User clicks ComboBox item at index 4 ("125%")
        harness.SettingsView_UserChangesSelection(4);

        // Verifications
        Assert.Equal(125, harness.MainWindowScale);
        Assert.Equal(4, harness.SettingsViewIndex);
        Assert.Equal("125%", harness.SettingsViewItem);
        Assert.Equal(0, harness.MainWindowPersistCalls); // MainWindow persist is suppressed (persist: false)
        Assert.Equal(1, harness.ControllerPersistCalls); // Controller saves to DB
        Assert.Equal(1, harness.HudDisplayCalls);
        Assert.True(harness.MaxCallStackDepth <= 5, $"Max stack depth was {harness.MaxCallStackDepth}, expected <= 5");
    }

    [Fact]
    public void BidirectionalSync_RapidInterleavedActions_MaintainsConsistencyAndNeverRecurses()
    {
        var harness = new SimulatedMainWindowAndSettingsHarness();
        var rng = new Random(42);

        for (int i = 0; i < 200; i++)
        {
            if (rng.Next(2) == 0)
            {
                // Keyboard shortcut step
                int current = harness.MainWindowScale;
                int next = rng.Next(2) == 0 ? UiScaleLevels.NextLevel(current) : UiScaleLevels.PreviousLevel(current);
                harness.MainWindow_ApplyUiScale(next, persist: true);
            }
            else
            {
                // UI ComboBox selection
                int targetIndex = rng.Next(UiScaleOptions.Length);
                harness.SettingsView_UserChangesSelection(targetIndex);
            }

            // Invariant check at every step
            Assert.Equal($"{harness.MainWindowScale}%", harness.SettingsViewItem);
            Assert.Equal(Array.IndexOf(UiScaleOptions, harness.SettingsViewItem), harness.SettingsViewIndex);
            Assert.False(harness.ApplyingFlag);
        }

        Assert.True(harness.MaxCallStackDepth <= 5);
    }

    #endregion

    #region 3. SettingsPageController Concurrency, Versioning, and Persistence Invariants

    [Fact]
    public async Task Controller_RapidConcurrentUpdates_CoalescesVersionsAndSettlesLatest()
    {
        var repo = new MemorySettingsRepo();
        int runtimeRefreshes = 0;
        var controller = new SettingsPageController(
            new SettingsService(repo),
            () => { Interlocked.Increment(ref runtimeRefreshes); return Task.CompletedTask; },
            ex => Assert.Fail($"Unexpected exception in controller: {ex}"));

        await controller.LoadAsync();
        Assert.Equal(100, controller.Saved!.UiScalePercent);

        var settledScales = new List<int>();
        controller.Settled += (f, s) =>
        {
            if (f == SettingsField.UiScale)
                settledScales.Add(s.UiScalePercent);
        };

        // Fire 20 rapid updates concurrently
        int[] targetScales = [80, 90, 100, 110, 125, 150, 125, 110, 100, 90, 80, 150];
        var tasks = targetScales.Select(s => controller.UpdateUiScaleAsync(s)).ToArray();

        await Task.WhenAll(tasks);
        await controller.DrainAsync();

        Assert.False(controller.IsBusy);
        Assert.Equal(150, controller.Saved.UiScalePercent);
        Assert.Equal(150, repo.Current.UiScalePercent);
        Assert.True(settledScales.Count >= 1, "At least the final version must settle.");
        Assert.Equal(150, settledScales.Last());
    }

    [Fact]
    public async Task Controller_RealSqliteDatabase_PersistsUiScaleAndSurvivesReload()
    {
        using var temp = new TempDirectory();
        string dbFile = Path.Combine(temp.Path, "challenger_m3_test.db");
        var connectionFactory = new SqliteConnectionFactory(dbFile);

        // Run migrations up to 14
        var bootstrapper = new DatabaseBootstrapper(connectionFactory);
        bootstrapper.Initialize();

        var repo = new SqliteSettingsRepository(connectionFactory);
        var service = new SettingsService(repo);

        int refreshes = 0;
        var controller = new SettingsPageController(
            service,
            () => { refreshes++; return Task.CompletedTask; },
            ex => Assert.Fail($"Controller error: {ex}"));

        await controller.LoadAsync();
        Assert.Equal(UiScaleLevels.DefaultPercent, controller.Saved!.UiScalePercent);

        // Update to 125%
        await controller.UpdateUiScaleAsync(125);
        Assert.Equal(125, controller.Saved.UiScalePercent);

        // Re-read directly from SQLite database to ensure persistence on disk
        var reloadedSettings = await repo.LoadAsync();
        Assert.Equal(125, reloadedSettings.UiScalePercent);

        // Update to 80%
        await controller.UpdateUiScaleAsync(80);
        Assert.Equal(80, controller.Saved.UiScalePercent);

        var reloadedSettings2 = await repo.LoadAsync();
        Assert.Equal(80, reloadedSettings2.UiScalePercent);

        // Update to 150%
        await controller.UpdateUiScaleAsync(150);
        Assert.Equal(150, controller.Saved.UiScalePercent);

        var reloadedSettings3 = await repo.LoadAsync();
        Assert.Equal(150, reloadedSettings3.UiScalePercent);
    }

    [Fact]
    public async Task Controller_PreCancelledToken_ThrowsOperationCanceledExceptionWithoutCorruptingState()
    {
        var repo = new MemorySettingsRepo();
        var controller = new SettingsPageController(
            new SettingsService(repo),
            () => Task.CompletedTask,
            _ => { });

        await controller.LoadAsync();
        Assert.Equal(100, controller.Saved!.UiScalePercent);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            controller.UpdateUiScaleAsync(125, cts.Token));

        Assert.Equal(100, controller.Saved.UiScalePercent);
        Assert.False(controller.IsBusy);

        // Subsequent uncancelled update succeeds
        await controller.UpdateUiScaleAsync(150);
        Assert.Equal(150, controller.Saved.UiScalePercent);
        Assert.Equal(150, repo.Current.UiScalePercent);
    }

    [Fact]
    public async Task Controller_RepositoryThrows_ReportsErrorAndRecovers()
    {
        var faultyRepo = new FaultySettingsRepo();
        Exception? reported = null;
        var controller = new SettingsPageController(
            new SettingsService(faultyRepo),
            () => Task.CompletedTask,
            ex => reported = ex);

        await controller.LoadAsync();
        Assert.Equal(100, controller.Saved!.UiScalePercent);

        // Arm failure on next write
        faultyRepo.FailOnSave = true;

        await controller.UpdateUiScaleAsync(125);
        Assert.NotNull(reported);
        Assert.Contains("UiScale: could not save", controller.Message);

        // Disarm failure and retry
        faultyRepo.FailOnSave = false;
        reported = null;

        await controller.UpdateUiScaleAsync(125);
        Assert.Null(reported);
        Assert.Equal(125, controller.Saved.UiScalePercent);
        Assert.Equal(125, faultyRepo.Current.UiScalePercent);
    }

    #endregion

    #region Helper Test Doubles

    private sealed class MemorySettingsRepo : ISettingsRepository
    {
        public ApplicationSettings Current = ApplicationSettings.Default;

        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            settings.Validate();
            Current = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FaultySettingsRepo : ISettingsRepository
    {
        public ApplicationSettings Current = ApplicationSettings.Default;
        public bool FailOnSave { get; set; } = false;

        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current);

        public Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            if (FailOnSave)
                throw new InvalidOperationException("Simulated disk write failure.");

            settings.Validate();
            Current = settings;
            return Task.CompletedTask;
        }
    }

    #endregion
}
