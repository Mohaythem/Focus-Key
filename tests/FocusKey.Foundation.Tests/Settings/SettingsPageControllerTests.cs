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
        await SetAll(page, expected);
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
        refreshes = 0;
        await page.UpdateDurationAsync(true, minutes, seconds);
        Assert.Equal(ApplicationSettings.Default, page.Saved);
        Assert.Equal(0, repo.Saves);
        Assert.Equal(0, refreshes);
        Assert.Contains("invalid value", page.Message);
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
        await (invalid switch
        {
            0 => page.UpdateDurationAsync(true, "0", "0"),
            1 => page.UpdateAppearanceAsync((Appearance)99),
            _ => page.UpdateWorkColorAsync(default)
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
        refreshes = 0;
        await page.UpdateWorkColorAsync(Custom.WorkColor);
        Assert.Equal(ApplicationSettings.Default, page.Saved);
        Assert.Contains("could not save", page.Message);
        Assert.Equal(0, refreshes);
        Assert.False(page.IsBusy);
        repo.FailSave = false;
        await page.UpdateWorkColorAsync(Custom.WorkColor);
        Assert.Equal(ApplicationSettings.Default with { WorkColor = Custom.WorkColor }, repo.Value);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task RuntimeFailureDoesNotMisreportSuccessfulCommit()
    {
        var repo = new FakeRepository();
        var page = new SettingsPageController(new(repo), () => throw new IOException(), _ => { });
        await page.LoadAsync();
        await SetAll(page, Custom);
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
        await page.UpdateWorkColorAsync(Custom.WorkColor);
        Assert.Equal(0, repo.Saves);
        repo.FailLoad = false;
        repo.Value = Custom;
        await page.LoadAsync();
        Assert.Equal(Custom, page.Saved);
    }

    [Fact]
    public async Task ReloadWaitsForPendingUpdatesAndLatestValueWins()
    {
        var repo = new FakeRepository { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var page = new SettingsPageController(new(repo), () => Task.CompletedTask, _ => { });
        await page.LoadAsync();
        Task pending = page.UpdateWorkColorAsync(Custom.WorkColor);
        Assert.True(page.IsBusy);
        Task latest = page.UpdateWorkColorAsync(HexColor.Parse("#ABCDEF"));
        Task reload = page.LoadAsync();
        Assert.False(reload.IsCompleted);
        repo.Pending.SetResult();
        await Task.WhenAll(pending, latest, reload);
        Assert.Equal(2, repo.Saves);
        Assert.Equal("#ABCDEF", page.Saved!.WorkColor.Value);
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
        await SetAll(page, Custom);
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

    [Theory]
    [InlineData("30", "30")]
    [InlineData("٣٠", "30")]
    [InlineData("۳۰", "30")]
    [InlineData("１２3٤", "1234")]
    [InlineData("٠١٢٣٤٥٦٧٨٩", "0123456789")]
    [InlineData("٣.٥", "3.5")]
    public void DecimalDigitsNormalizeToAscii(string input, string expected)
    {
        Assert.Equal(expected, SettingsPageController.NormalizeDigits(input));
        Assert.Equal(expected, SettingsPageController.NormalizeDigits(expected));
    }

    [Theory]
    [InlineData("ar-EG")]
    [InlineData("fa-IR")]
    [InlineData("en-US")]
    public async Task LocalizedInputPersistsAndReopensAsInvariantDuration(string culture)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new(culture);
            using var store = new SessionStore();
            var page = new SettingsPageController(new(new SqliteSettingsRepository(store.Connections)),
                () => Task.CompletedTask, _ => Assert.Fail("Unexpected failure"));
            await page.LoadAsync();
            await page.UpdateDurationAsync(true, "٣٠", "٠٥");
            await page.UpdateDurationAsync(false, "۱۰", "۰");
            var restarted = new SettingsPageController(new(new SqliteSettingsRepository(
                new Foundation.Data.SqliteConnectionFactory(store.DatabaseFile))), () => Task.CompletedTask, _ => Assert.Fail("Unexpected failure"));
            await restarted.LoadAsync();
            Assert.Equal(TimeSpan.FromSeconds(1805), restarted.Saved!.WorkDuration);
            Assert.Equal(TimeSpan.FromMinutes(10), restarted.Saved.BreakDuration);
            Assert.Equal("30", (restarted.Saved.WorkDuration.Ticks / TimeSpan.TicksPerMinute).ToString(System.Globalization.CultureInfo.InvariantCulture));
            await restarted.UpdateDurationAsync(true, "٣.٥", "٠");
            Assert.Equal(TimeSpan.FromSeconds(1805), restarted.Saved.WorkDuration);
            Assert.Contains("invalid value", restarted.Message);
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    private static async Task SetAll(SettingsPageController page, ApplicationSettings value)
    {
        await page.UpdateDurationAsync(true, ((long)value.WorkDuration.TotalMinutes).ToString(), value.WorkDuration.Seconds.ToString());
        await page.UpdateDurationAsync(false, ((long)value.BreakDuration.TotalMinutes).ToString(), value.BreakDuration.Seconds.ToString());
        await page.UpdateAppearanceAsync(value.Appearance);
        await page.UpdateWorkColorAsync(value.WorkColor);
        await page.UpdateBreakColorAsync(value.BreakColor);
    }

    [Fact]
    public async Task RapidColorsCoalesceAndOnlyLatestSelectionSettles()
    {
        var repo = new FakeRepository { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var page = new SettingsPageController(new(repo), () => Task.CompletedTask, _ => { });
        await page.LoadAsync();
        var settled = new List<HexColor>();
        page.Settled += (field, saved) => { if (field == SettingsField.WorkColor) settled.Add(saved.WorkColor); };
        var first = page.UpdateWorkColorAsync(HexColor.Parse("#111111"));
        var skipped = page.UpdateWorkColorAsync(HexColor.Parse("#222222"));
        var last = page.UpdateWorkColorAsync(HexColor.Parse("#FFFFFF"));
        Task drain = page.DrainAsync();
        Assert.False(drain.IsCompleted);
        repo.Pending.SetResult();
        await Task.WhenAll(first, skipped, last, drain);
        Assert.Equal(2, repo.Saves);
        Assert.Equal([HexColor.Parse("#FFFFFF")], settled);
        Assert.Equal("#FFFFFF", page.Saved!.WorkColor.Value);
        Assert.False(page.IsBusy);
    }

    [Fact]
    public async Task RapidDifferentFieldsMergeWithoutOverwritingEachOther()
    {
        var repo = new FakeRepository { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var page = new SettingsPageController(new(repo), () => Task.CompletedTask, _ => { });
        await page.LoadAsync();
        Task a = page.UpdateDurationAsync(true, "42", "5");
        Task b = page.UpdateDurationAsync(false, "5", "5");
        Task c = page.UpdateAppearanceAsync(Appearance.Dark);
        Task d = page.UpdateWorkColorAsync(Custom.WorkColor);
        Task e = page.UpdateBreakColorAsync(Custom.BreakColor);
        repo.Pending.SetResult();
        await Task.WhenAll(a, b, c, d, e);
        Assert.Equal(Custom, repo.Value);
        Assert.Equal(Custom, page.Saved);
    }

    [Fact]
    public async Task RapidThemeColorEditsMergeIntoOneLatestPalette()
    {
        var repo = new FakeRepository { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var page = new SettingsPageController(new(repo), () => Task.CompletedTask, _ => { });
        await page.LoadAsync();

        Task background = page.UpdateLightColorAsync(true, false, HexColor.Parse("#112233"));
        Task foreground = page.UpdateLightColorAsync(false, true, HexColor.Parse("#DDEEFF"));
        repo.Pending.SetResult();
        await Task.WhenAll(background, foreground, page.DrainAsync());

        Assert.Equal("#112233", page.Saved!.LightTheme.Background.Value);
        Assert.Equal("#DDEEFF", page.Saved.LightTheme.Foreground.Value);
        Assert.Equal("#112233", repo.Value.LightTheme.Background.Value);
        Assert.Equal("#DDEEFF", repo.Value.LightTheme.Foreground.Value);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CancellationOnlyAppliesBeforeAcceptance(bool before)
    {
        var repo = new FakeRepository { Pending = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var page = new SettingsPageController(new(repo), () => Task.CompletedTask, _ => { });
        await page.LoadAsync();
        using var canceled = new CancellationTokenSource();
        if (before) canceled.Cancel();
        Task change = page.UpdateAppearanceAsync(Appearance.Dark, canceled.Token);
        canceled.Cancel();
        repo.Pending.SetResult();
        if (before)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => change);
            Assert.Equal(0, repo.Saves);
        }
        else
        {
            await change;
            Assert.Equal(Appearance.Dark, page.Saved!.Appearance);
            Assert.Equal(Appearance.Dark, repo.Value.Appearance);
        }
        Assert.False(page.IsBusy);
    }

    [Fact]
    public async Task SQLiteFailureRollsBackOnlyFailedFieldThenRecovers()
    {
        using var store = new SessionStore();
        var service = new SettingsService(new SqliteSettingsRepository(store.Connections));
        var runtime = new AppearanceCoordinator(service);
        var page = new SettingsPageController(service, async () => { await runtime.RefreshAsync(); }, _ => { });
        await page.LoadAsync();
        store.ExecuteRaw("CREATE TRIGGER reject_work_color BEFORE UPDATE OF work_color ON application_settings WHEN NEW.work_color = '#FFFFFF' BEGIN SELECT RAISE(ABORT, 'isolated failure'); END;");
        var settled = new List<(SettingsField, ApplicationSettings)>();
        page.Settled += (field, saved) => settled.Add((field, saved));
        await page.UpdateWorkColorAsync(Custom.WorkColor);
        await page.UpdateAppearanceAsync(Appearance.Dark);
        await page.UpdateBreakColorAsync(HexColor.Parse("#FFFF00"));
        Assert.Equal(ApplicationSettings.Default.WorkColor, settled[0].Item2.WorkColor);
        Assert.Equal(SettingsField.WorkColor, settled[0].Item1);
        Assert.Equal(ApplicationSettings.Default.WorkColor, runtime.Colors.Work);
        Assert.Equal(HexColor.Parse("#FFFF00"), runtime.Colors.Break);
        Assert.Equal(Appearance.Dark, runtime.Current);
        Assert.Equal(await service.LoadAsync(), page.Saved);
        Assert.Contains("could not save", page.Message); // Other successful fields don't hide a failure.
        store.ExecuteRaw("DROP TRIGGER reject_work_color;");
        await page.UpdateWorkColorAsync(Custom.WorkColor);
        Assert.Equal(Custom.WorkColor, runtime.Colors.Work);
        Assert.DoesNotContain("could not save", page.Message);
    }

    [Fact]
    public async Task RuntimeRetryDoesNotRewritePersistence()
    {
        var repo = new FakeRepository();
        bool fail = true;
        var page = new SettingsPageController(new(repo), () => fail ? Task.FromException(new IOException()) : Task.CompletedTask, _ => { });
        await page.LoadAsync();
        await page.UpdateAppearanceAsync(Appearance.Dark);
        Assert.Contains("live apply failed", page.Message);
        fail = false;
        await page.LoadAsync();
        Assert.Equal(1, repo.Saves);
        Assert.Equal(Appearance.Dark, page.Saved!.Appearance);
        Assert.DoesNotContain("failed", page.Message);
    }

    [Fact]
    public async Task IndividualUpdatePreservesUnrelatedExternalSavedValues()
    {
        using var store = new SessionStore();
        var service = new SettingsService(new SqliteSettingsRepository(store.Connections));
        var page = new SettingsPageController(service, () => Task.CompletedTask, _ => { });
        await page.LoadAsync();
        await service.UpdateBreakDurationAsync(TimeSpan.FromMinutes(17));
        await page.UpdateWorkColorAsync(Custom.WorkColor);
        Assert.Equal(TimeSpan.FromMinutes(17), (await service.LoadAsync()).BreakDuration);
    }

    [Fact]
    public async Task UpdateTimeFormatAsync_SavesAndSettles()
    {
        var repo = new FakeRepository();
        var page = new SettingsPageController(new(repo), () => Task.CompletedTask, _ => { });
        await page.LoadAsync();

        SettingsField? settledField = null;
        page.Settled += (field, _) => settledField = field;

        await page.UpdateTimeFormatAsync(TimeFormat.TwelveHour);

        Assert.Equal(TimeFormat.TwelveHour, page.Saved!.TimeFormat);
        Assert.Equal(TimeFormat.TwelveHour, repo.Value.TimeFormat);
        Assert.Equal(SettingsField.TimeFormat, settledField);
    }

    [Fact]
    public async Task UpdateOverlayPositionAsync_AndReset_SavesAndSettles()
    {
        var repo = new FakeRepository();
        var page = new SettingsPageController(new(repo), () => Task.CompletedTask, _ => { });
        await page.LoadAsync();

        SettingsField? settledField = null;
        page.Settled += (field, _) => settledField = field;

        await page.UpdateOverlayPositionAsync(300, 400);

        Assert.Equal(300, page.Saved!.OverlayPositionX);
        Assert.Equal(400, page.Saved.OverlayPositionY);
        Assert.Equal(300, repo.Value.OverlayPositionX);
        Assert.Equal(400, repo.Value.OverlayPositionY);
        Assert.Equal(SettingsField.OverlayPosition, settledField);

        settledField = null;
        await page.ResetOverlayPositionAsync();

        Assert.Null(page.Saved.OverlayPositionX);
        Assert.Null(page.Saved.OverlayPositionY);
        Assert.Null(repo.Value.OverlayPositionX);
        Assert.Null(repo.Value.OverlayPositionY);
        Assert.Equal(SettingsField.OverlayPosition, settledField);
    }

    [Fact]
    public async Task UpdateSectionExpandedAsync_SavesAndSettles()
    {
        var repo = new FakeRepository();
        var page = new SettingsPageController(new(repo), () => Task.CompletedTask, _ => { });
        await page.LoadAsync();

        var settledFields = new List<SettingsField>();
        page.Settled += (field, _) => settledFields.Add(field);

        await page.UpdateAppearanceExpandedAsync(true);
        Assert.True(page.Saved!.AppearanceExpanded);
        Assert.True(repo.Value.AppearanceExpanded);
        Assert.Contains(SettingsField.AppearanceExpanded, settledFields);

        await page.UpdateShortcutsExpandedAsync(true);
        Assert.True(page.Saved.ShortcutsExpanded);
        Assert.True(repo.Value.ShortcutsExpanded);
        Assert.Contains(SettingsField.ShortcutsExpanded, settledFields);

        await page.UpdateAdvancedExpandedAsync(true);
        Assert.True(page.Saved.AdvancedExpanded);
        Assert.True(repo.Value.AdvancedExpanded);
        Assert.Contains(SettingsField.AdvancedExpanded, settledFields);
    }

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
