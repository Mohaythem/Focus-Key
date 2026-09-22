using FocusKey.Foundation.Data;
using FocusKey.Foundation.History;
using FocusKey.Foundation.Overlay;
using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Today;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class RuntimeSmokeWorkflowTests
{
    [Fact]
    public async Task CompleteAppWorkflow_ExecuteEndToEnd_MaintainsInvariantsAndPersistedState()
    {
        using var store = new SessionStore();
        var startTime = new DateTimeOffset(2026, 9, 22, 10, 0, 0, TimeSpan.Zero);
        var clock = new ManualTimeProvider(startTime);

        // 1. App Startup & Settings Bootstrap
        var settingsRepo = new SqliteSettingsRepository(store.Connections);
        var settingsService = new SettingsService(settingsRepo);
        var initialSettings = await settingsService.LoadAsync();
        Assert.Equal(100, initialSettings.UiScalePercent);
        Assert.True(initialSettings.SessionSoundsEnabled);
        Assert.True(initialSettings.StartSoundEnabled);
        Assert.True(initialSettings.CompletionSoundEnabled);

        // 2. Start Work Session (25 min)
        var durations = new SessionDurations(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
        var coordinator = new SessionCoordinator(store.Repository, clock, durations);
        await coordinator.InitializeAsync();
        var todayService = new TodayService(store.Repository, clock);
        var reportsService = new ReportsService(store.Repository, null, clock);

        SessionRecord workSession = await coordinator.StartAsync(SessionType.Work);
        Assert.Equal(SessionStatus.Running, workSession.Status);
        Assert.Equal(SessionType.Work, workSession.Type);
        Assert.Equal(TimeSpan.FromMinutes(25), workSession.PlannedDuration);
        Assert.Equal(startTime, workSession.StartedAt);

        // Advance 10 minutes
        clock.Advance(TimeSpan.FromMinutes(10));
        SessionSnapshot? runningSnap = await coordinator.GetActiveAsync();
        Assert.NotNull(runningSnap);
        Assert.False(runningSnap.IsPaused);
        Assert.Equal(TimeSpan.FromMinutes(15), runningSnap.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(10), runningSnap.Elapsed);

        // 3. Pause Work Session
        SessionOutcome pauseOutcome = await coordinator.PauseAsync(workSession.Id);
        Assert.Equal(SessionOutcomeKind.Paused, pauseOutcome.Kind);
        Assert.NotNull(pauseOutcome.Session);
        Assert.Equal(SessionStatus.Paused, pauseOutcome.Session.Status);
        Assert.Equal(TimeSpan.FromMinutes(10), pauseOutcome.Session.AccumulatedActiveDuration);

        // Stay paused for 20 minutes
        clock.Advance(TimeSpan.FromMinutes(20));
        SessionSnapshot? pausedSnap = await coordinator.GetActiveAsync();
        Assert.NotNull(pausedSnap);
        Assert.True(pausedSnap.IsPaused);
        Assert.Equal(TimeSpan.FromMinutes(15), pausedSnap.Remaining);

        // 4. Continue Work Session
        SessionOutcome continueOutcome = await coordinator.ContinueAsync(workSession.Id);
        Assert.Equal(SessionOutcomeKind.Continued, continueOutcome.Kind);
        Assert.NotNull(continueOutcome.Session);
        Assert.Equal(SessionStatus.Running, continueOutcome.Session.Status);

        // Advance 5 minutes more
        clock.Advance(TimeSpan.FromMinutes(5));

        // 5. User Stop Work Session
        SessionOutcome stopOutcome = await coordinator.StopAsync(workSession.Id);
        Assert.Equal(SessionOutcomeKind.Stopped, stopOutcome.Kind);
        Assert.NotNull(stopOutcome.Session);
        Assert.Equal(SessionStatus.Stopped, stopOutcome.Session.Status);
        Assert.Equal(TimeSpan.FromMinutes(15), stopOutcome.Session.EffectiveDuration);

        // 6. Start Break Session (5 min) and allow Natural Completion
        clock.Advance(TimeSpan.FromMinutes(2));
        var breakStartTime = clock.GetUtcNow();
        SessionRecord breakSession = await coordinator.StartAsync(SessionType.Break);
        Assert.Equal(SessionStatus.Running, breakSession.Status);
        Assert.Equal(SessionType.Break, breakSession.Type);

        // Advance past planned end (6 minutes)
        clock.Advance(TimeSpan.FromMinutes(6));
        SessionOutcome completionOutcome = await coordinator.CompleteIfDueAsync();
        Assert.Equal(SessionOutcomeKind.Completed, completionOutcome.Kind);
        Assert.NotNull(completionOutcome.Session);
        Assert.Equal(SessionStatus.Completed, completionOutcome.Session.Status);
        Assert.Equal(breakStartTime + TimeSpan.FromMinutes(5), completionOutcome.Session.EndedAt);
        Assert.Equal(TimeSpan.FromMinutes(5), completionOutcome.Session.EffectiveDuration);

        // 7. Today Page Query Verification
        TodaySnapshot todaySnap = await todayService.ReadAsync();
        Assert.Equal(2, todaySnap.Sessions.Count);
        Assert.Equal(TimeSpan.FromMinutes(15), todaySnap.WorkTime);
        Assert.Equal(TimeSpan.FromMinutes(5), todaySnap.BreakTime);
        Assert.Equal(0, todaySnap.CompletedWorkCount);
        Assert.Equal(1, todaySnap.CompletedBreakCount);

        // 8. Reports Query Verification (Weekly, Monthly, Yearly)
        ReportsSnapshot weeklyReports = await reportsService.ReadAsync(ReportPeriod.Weekly, DateOnly.FromDateTime(clock.GetUtcNow().DateTime));
        Assert.Equal(TimeSpan.FromMinutes(15), weeklyReports.Totals.FocusTime);
        Assert.Equal(7, weeklyReports.Trend.Count);

        ReportsSnapshot monthlyReports = await reportsService.ReadAsync(ReportPeriod.Monthly, DateOnly.FromDateTime(clock.GetUtcNow().DateTime));
        Assert.Equal(TimeSpan.FromMinutes(15), monthlyReports.Totals.FocusTime);
        Assert.Equal(4, monthlyReports.Trend.Count); // Exactly 4 weekly buckets

        // 9. Settings Persistence (Scale, Sounds, Collapsible Sections, Format)
        await settingsService.UpdateUiScalePercentAsync(125);
        await settingsService.UpdateSessionSoundsEnabledAsync(true);
        await settingsService.UpdateStartSoundEnabledAsync(true);
        await settingsService.UpdateCompletionSoundEnabledAsync(true);
        await settingsService.UpdateAppearanceExpandedAsync(true);
        await settingsService.UpdateShortcutsExpandedAsync(false);
        await settingsService.UpdateAdvancedExpandedAsync(true);
        await settingsService.UpdateTimeFormatAsync(TimeFormat.TwelveHour);

        ApplicationSettings updatedSettings = await settingsService.LoadAsync();
        Assert.Equal(125, updatedSettings.UiScalePercent);
        Assert.True(updatedSettings.AppearanceExpanded);
        Assert.False(updatedSettings.ShortcutsExpanded);
        Assert.True(updatedSettings.AdvancedExpanded);
        Assert.Equal(TimeFormat.TwelveHour, updatedSettings.TimeFormat);

        // 10. Simulate App Restart & Recovery
        // Start a session and pause it, then simulate app restart
        clock.Advance(TimeSpan.FromMinutes(5));
        SessionRecord sessionBeforeRestart = await coordinator.StartAsync(SessionType.Work);
        clock.Advance(TimeSpan.FromMinutes(8));
        await coordinator.PauseAsync(sessionBeforeRestart.Id);

        // App closes and restarts: Create new engine & recovery against the same database
        var restartedCoordinator = new SessionCoordinator(store.Repository, clock, durations);
        SessionRecoveryResult recoveryResult = await restartedCoordinator.InitializeAsync();
        Assert.Equal(SessionRecoveryKind.StillPaused, recoveryResult.Kind);
        Assert.NotNull(recoveryResult.Session);
        Assert.Equal(SessionStatus.Paused, recoveryResult.Session.Status);
        Assert.Equal(TimeSpan.FromMinutes(8), recoveryResult.Session.AccumulatedActiveDuration);

        // Resume and complete after restart
        SessionOutcome postRestartContinue = await restartedCoordinator.ContinueAsync(sessionBeforeRestart.Id);
        Assert.Equal(SessionOutcomeKind.Continued, postRestartContinue.Kind);
        clock.Advance(TimeSpan.FromMinutes(17)); // 8 + 17 = 25 min total
        SessionOutcome postRestartComplete = await restartedCoordinator.CompleteIfDueAsync();
        Assert.Equal(SessionOutcomeKind.Completed, postRestartComplete.Kind);
        Assert.Equal(TimeSpan.FromMinutes(25), postRestartComplete.Session?.EffectiveDuration);
    }
}
