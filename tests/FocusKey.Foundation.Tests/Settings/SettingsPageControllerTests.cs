using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Settings;

public sealed class SettingsPageControllerTests
{
    private static ApplicationSettings Custom => ApplicationSettings.Default with
    {
        WorkDuration = TimeSpan.FromSeconds(2525), BreakDuration = TimeSpan.FromSeconds(305),
        Appearance = Appearance.Dark, WorkColor = HexColor.Parse("#FFFFFF"), BreakColor = HexColor.Parse("#000000")
    };

    [Theory]
    [InlineData(Appearance.System)]
    [InlineData(Appearance.Light)]
    [InlineData(Appearance.Dark)]
    public async Task CompleteSaveRefreshReopenRestartPath(Appearance appearance)
    {
        using var store = new SessionStore();
        var service = new SettingsService(new SqliteSettingsRepository(store.Connections));
        var runtime = new AppearanceCoordinator(service);
        await runtime.InitializeAsync();
        var page = new SettingsPageController(service, async () => { await runtime.RefreshAsync(); }, _ => Assert.Fail("Unexpected failure"));
        await page.LoadAsync();
        Assert.Equal(ApplicationSettings.Default, page.Saved);
        var expected = Custom with { Appearance = appearance };
        await page.SaveAsync(() => expected);
        Assert.Equal(expected, page.Saved);
        Assert.Equal(appearance, runtime.Current);
        Assert.Equal(SessionColors.From(expected), runtime.Colors);
        await page.LoadAsync();
        Assert.Equal(expected, page.Saved);
        var restarted = new SettingsPageController(new SettingsService(new SqliteSettingsRepository(
            new Foundation.Data.SqliteConnectionFactory(store.DatabaseFile))), () => Task.CompletedTask, _ => Assert.Fail("Unexpected failure"));
        await restarted.LoadAsync();
        Assert.Equal(expected, restarted.Saved);
    }

    [Theory]
    [InlineData("", "0")]
    [InlineData("-1", "0")]
    [InlineData("1.5", "0")]
    [InlineData("1", "60")]
    [InlineData("1", "-1")]
    [InlineData("abc", "0")]
    [InlineData("9223372036854775807", "0")]
    public async Task InvalidInputDoesNotSaveOrRefresh(string minutes, string seconds)
    {
        var repo = new FakeRepository();
        int refreshes = 0;
        var page = new SettingsPageController(new(repo), () => { refreshes++; return Task.CompletedTask; }, _ => { });
        await page.LoadAsync();
        await page.SaveAsync(() => Custom with { WorkDuration = SettingsPageController.Duration(minutes, seconds) });
        Assert.Equal(ApplicationSettings.Default, page.Saved);
        Assert.Equal(0, repo.Saves);
        Assert.Equal(0, refreshes);
        Assert.Contains("Check your input", page.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DomainValidationRemainsAuthoritative(int invalid)
    {
        var repo = new FakeRepository();
        var page = new SettingsPageController(new(repo), () => Task.CompletedTask, _ => { });
        await page.LoadAsync();
        await page.SaveAsync(() => invalid switch
        {
            0 => Custom with { WorkDuration = TimeSpan.Zero },
            1 => Custom with { Appearance = (Appearance)99 },
            _ => Custom with { WorkColor = default }
        });
        Assert.Equal(0, repo.Saves);
        Assert.Equal(ApplicationSettings.Default, page.Saved);
    }

    [Fact]
    public async Task WriteFailureRetainsSavedStateAndAllowsRetry()
    {
        var repo = new FakeRepository { FailSave = true };
        int refreshes = 0;
        var page = new SettingsPageController(new(repo), () => { refreshes++; return Task.CompletedTask; }, _ => { });
        await page.LoadAsync();
        await page.SaveAsync(() => Custom);
        Assert.Equal(ApplicationSettings.Default, page.Saved);
        Assert.Contains("Could not save", page.Message);
        Assert.Equal(0, refreshes);
        Assert.False(page.IsBusy);
        repo.FailSave = false;
        await page.SaveAsync(() => Custom);
        Assert.Equal(Custom, repo.Value);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task RuntimeFailureDoesNotMisreportSuccessfulCommit()
    {
        var repo = new FakeRepository();
        var page = new SettingsPageController(new(repo), () => throw new IOException(), _ => { });
        await page.LoadAsync();
        await page.SaveAsync(() => Custom);
        Assert.Equal(Custom, page.Saved);
        Assert.Equal(Custom, repo.Value);
        Assert.Contains("Settings saved, but", page.Message);
        Assert.False(page.IsBusy);
    }

    [Fact]
    public async Task LoadFailureDisablesSavingAndRetryReadsCurrentValues()
    {
        var repo = new FakeRepository();
        var page = new SettingsPageController(new(repo), () => Task.CompletedTask, _ => { });
        await page.LoadAsync();
        repo.FailLoad = true;
        await page.LoadAsync();
        Assert.Null(page.Saved);
        await page.SaveAsync(() => Custom);
        Assert.Equal(0, repo.Saves);
        repo.FailLoad = false;
        repo.Value = Custom;
        await page.LoadAsync();
        Assert.Equal(Custom, page.Saved);
    }

    [Fact]
    public async Task DuplicateSaveAndReloadCannotOvertakePendingWrite()
    {
        var repo = new FakeRepository { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var page = new SettingsPageController(new(repo), () => Task.CompletedTask, _ => { });
        await page.LoadAsync();
        Task pending = page.SaveAsync(() => Custom);
        Assert.True(page.IsBusy);
        await page.SaveAsync(() => ApplicationSettings.Default);
        await page.LoadAsync();
        repo.Pending.SetResult();
        await pending;
        Assert.Equal(1, repo.Saves);
        Assert.Equal(Custom, page.Saved);
    }

    [Fact]
    public async Task SavingAllSettingsLeavesRunningAndHistoricalSessionsIntact()
    {
        using var store = new SessionStore();
        var service = new SettingsService(new SqliteSettingsRepository(store.Connections));
        var engine = new SessionEngine(store.Repository, new ManualTimeProvider(TestSessions.Anchor),
            durationProvider: new SettingsSessionDurationProvider(service));
        var historical = await engine.StartAsync(SessionType.Break);
        await engine.StopAsync();
        historical = (await store.Repository.GetAsync(historical.Id))!;
        var running = await engine.StartAsync(SessionType.Work);
        var page = new SettingsPageController(service, () => Task.CompletedTask, _ => Assert.Fail("Unexpected failure"));
        await page.LoadAsync();
        await page.SaveAsync(() => Custom);
        Assert.Equal(running, await store.Repository.GetAsync(running.Id));
        Assert.Equal(historical, await store.Repository.GetAsync(historical.Id));
        await engine.StopAsync();
        Assert.Equal(Custom.WorkDuration, (await engine.StartAsync(SessionType.Work)).PlannedDuration);
        await engine.StopAsync();
        Assert.Equal(Custom.BreakDuration, (await engine.StartAsync(SessionType.Break)).PlannedDuration);
    }

    [Fact]
    public void DurationInputPreservesSeconds() =>
        Assert.Equal(TimeSpan.FromSeconds(2525), SettingsPageController.Duration("42", "5"));

    private sealed class FakeRepository : ISettingsRepository
    {
        public ApplicationSettings Value = ApplicationSettings.Default;
        public bool FailLoad, FailSave;
        public int Saves;
        public TaskCompletionSource? Pending;
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
            FailLoad ? Task.FromException<ApplicationSettings>(new IOException()) : Task.FromResult(Value);
        public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            Saves++;
            if (Pending is not null) await Pending.Task;
            if (FailSave) throw new IOException();
            Value = settings;
        }
    }
}
