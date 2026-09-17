using FocusKey.Foundation.Overlay;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Shell;
using FocusKey.Foundation.Today;

namespace FocusKey.Foundation.Tests.Sessions;

public sealed class SessionPauseContinueTests
{
    [Fact]
    public async Task MultiCycle_PauseAndContinue_StartedAtRemainsImmutable_AndZeroPausedTimeCredited()
    {
        using var store = new SessionStore();
        var startInstant = TestSessions.Anchor;
        var clock = new ManualTimeProvider(startInstant);
        var engine = new SessionEngine(store.Repository, clock);

        // 1. Start a 30-minute Work session at T+0
        SessionRecord started = await engine.StartAsync(SessionType.Work);
        Assert.Equal(startInstant, started.StartedAt);
        Assert.Equal(startInstant, started.ResumedAt);
        Assert.Equal(TimeSpan.Zero, started.AccumulatedActiveDuration);
        Assert.Equal(SessionStatus.Running, started.Status);

        // 2. Run for 5 minutes (T+5m) -> remaining should be 25 minutes
        clock.Advance(TimeSpan.FromMinutes(5));
        SessionSnapshot? snap1 = await engine.GetActiveAsync();
        Assert.NotNull(snap1);
        Assert.Equal(TimeSpan.FromMinutes(25), snap1.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(5), snap1.Elapsed);

        // 3. Pause at T+5m
        SessionOutcome pause1 = await engine.PauseAsync();
        Assert.Equal(SessionOutcomeKind.Paused, pause1.Kind);
        Assert.NotNull(pause1.Session);
        Assert.Equal(SessionStatus.Paused, pause1.Session.Status);
        Assert.Equal(startInstant, pause1.Session.StartedAt); // Immutable!
        Assert.Equal(TimeSpan.FromMinutes(5), pause1.Session.AccumulatedActiveDuration);
        Assert.Equal(startInstant.AddMinutes(5), pause1.Session.PausedAt);

        // 4. Stay paused for 1 hour (T+65m) -> remaining must freeze at 25 minutes!
        clock.Advance(TimeSpan.FromHours(1));
        SessionSnapshot? pausedSnap = await engine.GetActiveAsync();
        Assert.NotNull(pausedSnap);
        Assert.True(pausedSnap.IsPaused);
        Assert.Equal(TimeSpan.FromMinutes(25), pausedSnap.Remaining); // Frozen!
        Assert.Equal(TimeSpan.FromMinutes(5), pausedSnap.Elapsed);   // Frozen!

        // CompleteIfDue must not complete while paused
        SessionOutcome dueOutcome = await engine.CompleteIfDueAsync();
        Assert.Equal(SessionOutcomeKind.Paused, dueOutcome.Kind);

        // 5. Continue at T+65m
        SessionOutcome cont1 = await engine.ContinueAsync();
        Assert.Equal(SessionOutcomeKind.Continued, cont1.Kind);
        Assert.NotNull(cont1.Session);
        Assert.Equal(SessionStatus.Running, cont1.Session.Status);
        Assert.Equal(startInstant, cont1.Session.StartedAt); // Still immutable!
        Assert.Equal(startInstant.AddMinutes(65), cont1.Session.ResumedAt);
        Assert.Equal(TimeSpan.FromMinutes(5), cont1.Session.AccumulatedActiveDuration);
        Assert.Null(cont1.Session.PausedAt);

        // PlannedEndAt must be ResumedAt + 25m = T+90m
        Assert.Equal(startInstant.AddMinutes(90), cont1.Session.PlannedEndAt);

        // 6. Run for another 10 minutes (T+75m) -> 15 active minutes total
        clock.Advance(TimeSpan.FromMinutes(10));
        SessionSnapshot? snap2 = await engine.GetActiveAsync();
        Assert.NotNull(snap2);
        Assert.Equal(TimeSpan.FromMinutes(15), snap2.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(15), snap2.Elapsed);

        // 7. Pause again at T+75m
        SessionOutcome pause2 = await engine.PauseAsync();
        Assert.Equal(SessionOutcomeKind.Paused, pause2.Kind);
        Assert.NotNull(pause2.Session);
        Assert.Equal(startInstant, pause2.Session.StartedAt); // Immutable!
        Assert.Equal(TimeSpan.FromMinutes(15), pause2.Session.AccumulatedActiveDuration); // 5m + 10m

        // 8. Stay paused for 30 minutes (T+105m)
        clock.Advance(TimeSpan.FromMinutes(30));

        // 9. Continue again at T+105m
        SessionOutcome cont2 = await engine.ContinueAsync();
        Assert.Equal(SessionOutcomeKind.Continued, cont2.Kind);
        Assert.NotNull(cont2.Session);
        Assert.Equal(startInstant, cont2.Session.StartedAt); // Immutable!
        Assert.Equal(startInstant.AddMinutes(105), cont2.Session.ResumedAt);
        Assert.Equal(TimeSpan.FromMinutes(15), cont2.Session.AccumulatedActiveDuration);
        // PlannedEndAt = T+105m + 15m = T+120m
        Assert.Equal(startInstant.AddMinutes(120), cont2.Session.PlannedEndAt);

        // 10. Run the remaining 15 minutes to completion (T+120m)
        clock.Advance(TimeSpan.FromMinutes(15));
        SessionOutcome completed = await engine.CompleteIfDueAsync();
        Assert.Equal(SessionOutcomeKind.Completed, completed.Kind);
        Assert.NotNull(completed.Session);
        Assert.Equal(SessionStatus.Completed, completed.Session.Status);

        // Verification of final session record:
        SessionRecord stored = (await store.Repository.GetAsync(started.Id))!;
        Assert.Equal(startInstant, stored.StartedAt); // Original startedAt preserved!
        Assert.Equal(startInstant.AddMinutes(120), stored.EndedAt); // Actual wall clock end
        Assert.Equal(TimeSpan.FromMinutes(120), stored.ActualDuration); // Total wall clock time
        Assert.Equal(TimeSpan.FromMinutes(30), stored.EffectiveDuration); // Exactly 30m credited, ZERO paused time credited!
    }

    [Fact]
    public async Task MultiCycle_StopWhilePaused_AccuratelyCalculatesAccumulatedDuration()
    {
        using var store = new SessionStore();
        var startInstant = TestSessions.Anchor;
        var clock = new ManualTimeProvider(startInstant);
        var engine = new SessionEngine(store.Repository, clock);

        // Start 30m Work session
        SessionRecord started = await engine.StartAsync(SessionType.Work);

        // Run 6 minutes
        clock.Advance(TimeSpan.FromMinutes(6));
        await engine.PauseAsync(); // 6m accumulated

        // Wait 20 minutes paused
        clock.Advance(TimeSpan.FromMinutes(20));

        // Resume and run 4 minutes
        await engine.ContinueAsync();
        clock.Advance(TimeSpan.FromMinutes(4)); // 6m + 4m = 10m active

        // Pause at T+30m
        SessionOutcome paused = await engine.PauseAsync();
        Assert.Equal(TimeSpan.FromMinutes(10), paused.Session!.AccumulatedActiveDuration);

        // Wait 15 minutes paused and then Stop
        clock.Advance(TimeSpan.FromMinutes(15));
        SessionOutcome stopped = await engine.StopAsync();
        Assert.Equal(SessionOutcomeKind.Stopped, stopped.Kind);

        SessionRecord record = stopped.Session!;
        Assert.Equal(SessionStatus.Stopped, record.Status);
        Assert.Equal(startInstant, record.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(10), record.EffectiveDuration); // Exactly 10m active work credited!
        Assert.Null(record.PausedAt);
        Assert.Equal(startInstant.AddMinutes(30), record.EndedAt);
    }

    [Fact]
    public async Task PausedSession_SurvivesApplicationRestart_AndRemainsResumable()
    {
        using var store = new SessionStore();
        var startInstant = TestSessions.Anchor;
        var clock = new ManualTimeProvider(startInstant);
        var engine = new SessionEngine(store.Repository, clock);

        // Start and pause
        SessionRecord started = await engine.StartAsync(SessionType.Work);
        clock.Advance(TimeSpan.FromMinutes(12));
        SessionOutcome paused = await engine.PauseAsync();
        Assert.Equal(SessionStatus.Paused, paused.Session!.Status);

        // Simulate application close and restart after 4 hours
        clock.Advance(TimeSpan.FromHours(4));
        var recovery = new SessionRecovery(store.Repository, clock);

        // Startup recovery should preserve the paused session, NOT mark it interrupted or completed
        SessionRecoveryResult recoveryResult = await recovery.RecoverStartupAsync();
        Assert.Equal(SessionRecoveryKind.StillPaused, recoveryResult.Kind);
        Assert.NotNull(recoveryResult.Session);
        Assert.Equal(SessionStatus.Paused, recoveryResult.Session.Status);
        Assert.Equal(TimeSpan.FromMinutes(12), recoveryResult.Session.AccumulatedActiveDuration);

        // Now resume the recovered paused session in the new session engine
        var newEngine = new SessionEngine(store.Repository, clock);
        SessionSnapshot? activeSnapshot = await newEngine.GetActiveAsync();
        Assert.NotNull(activeSnapshot);
        Assert.True(activeSnapshot.IsPaused);
        Assert.Equal(TimeSpan.FromMinutes(18), activeSnapshot.Remaining); // 30m - 12m = 18m frozen

        SessionOutcome continued = await newEngine.ContinueAsync();
        Assert.Equal(SessionOutcomeKind.Continued, continued.Kind);
        Assert.Equal(SessionStatus.Running, continued.Session!.Status);
        Assert.Equal(started.StartedAt, continued.Session.StartedAt); // Start time preserved
    }

    [Fact]
    public async Task PausedSession_SurvivesShutdown_AndRemainsResumable()
    {
        using var store = new SessionStore();
        var startInstant = TestSessions.Anchor;
        var clock = new ManualTimeProvider(startInstant);
        var engine = new SessionEngine(store.Repository, clock);

        await engine.StartAsync(SessionType.Work);
        clock.Advance(TimeSpan.FromMinutes(5));
        await engine.PauseAsync();

        var recovery = new SessionRecovery(store.Repository, clock);
        SessionRecoveryResult shutdownResult = await recovery.FinishShutdownAsync();
        Assert.Equal(SessionRecoveryKind.StillPaused, shutdownResult.Kind);
        Assert.Equal(SessionStatus.Paused, shutdownResult.Session!.Status);
    }

    [Fact]
    public async Task SwitchingTypeWhilePaused_FinalizesPreviousSessionAsStopped_AndStartsNewSession()
    {
        using var store = new SessionStore();
        var startInstant = TestSessions.Anchor;
        var clock = new ManualTimeProvider(startInstant);
        var engine = new SessionEngine(store.Repository, clock);
        var today = new TodayService(store.Repository, null, clock, () => TimeZoneInfo.Utc);

        var controller = new TodayController(
            today.ReadAsync,
            (type, token) => engine.StartAsync(type, token),
            (id, token) => engine.StopAsync(id, token),
            _ => { },
            (id, token) => engine.PauseAsync(id, token),
            (id, token) => engine.ContinueAsync(id, token));

        await controller.OpenAsync();

        // 1. Start Work session
        await controller.StartAsync(SessionType.Work);
        Assert.NotNull(controller.Snapshot!.Running);
        Assert.Equal(SessionType.Work, controller.Snapshot.Running.Type);

        // 2. Run for 8 minutes and Pause
        clock.Advance(TimeSpan.FromMinutes(8));
        await controller.PauseAsync();
        Assert.NotNull(controller.Snapshot!.Paused);
        Assert.Equal(SessionType.Work, controller.Snapshot.Paused.Type);

        // 3. User selects and starts Break instead
        await controller.StartAsync(SessionType.Break);

        // 4. Assert previous Work session was finalized as Stopped with 8m credited time
        SessionRecord prevWork = (await store.Repository.GetAsync(controller.Snapshot.Sessions.First(s => s.Type == SessionType.Work).Id))!;
        Assert.Equal(SessionStatus.Stopped, prevWork.Status);
        Assert.Equal(TimeSpan.FromMinutes(8), prevWork.EffectiveDuration);

        // 5. Assert Break is now running at full duration
        Assert.NotNull(controller.Snapshot.Running);
        Assert.Equal(SessionType.Break, controller.Snapshot.Running.Type);
        Assert.Equal(TimeSpan.FromMinutes(10), controller.Snapshot.Running.PlannedDuration);
    }

    [Fact]
    public async Task PausingSession_DisarmsCompletionTimer_AndEmitsNoNotification()
    {
        using var store = new SessionStore();
        var startInstant = TestSessions.Anchor;
        var clock = new ManualTimeProvider(startInstant);
        var coordinator = new SessionCoordinator(store.Repository, clock);
        await coordinator.InitializeAsync();
        int notifyCount = 0;

        using var completion = new CompletionCoordinator(
            coordinator,
            _ => { Interlocked.Increment(ref notifyCount); return Task.CompletedTask; },
            _ => { },
            clock);

        // Start session
        SessionRecord running = await completion.StartAsync(SessionType.Work);

        // Pause before deadline
        clock.Advance(TimeSpan.FromMinutes(10));
        SessionOutcome pauseOutcome = await completion.PauseAsync(running.Id);
        Assert.Equal(SessionOutcomeKind.Paused, pauseOutcome.Kind);

        // Advance past original planned end
        clock.Advance(TimeSpan.FromHours(2));
        await completion.EvaluateAsync();

        // Must NOT complete, must NOT notify
        Assert.Equal(0, notifyCount);
        SessionSnapshot? active = await coordinator.GetActiveAsync();
        Assert.NotNull(active);
        Assert.True(active.IsPaused);

        // Continue session
        SessionOutcome contOutcome = await completion.ContinueAsync(running.Id);
        Assert.Equal(SessionOutcomeKind.Continued, contOutcome.Kind);

        // Advance to new planned end (T+10m + 2h + 20m = T+2h30m)
        clock.Advance(TimeSpan.FromMinutes(20));
        await completion.EvaluateAsync();

        // Now completion must fire exactly once!
        Assert.Equal(1, notifyCount);
        SessionSnapshot? afterCompletion = await coordinator.GetActiveAsync();
        Assert.Null(afterCompletion);
    }

    [Fact]
    public async Task BreakPaused_SwitchingToWork_StartsFreshWorkSession_AndStopsBreak()
    {
        using var store = new SessionStore();
        var startInstant = TestSessions.Anchor;
        var clock = new ManualTimeProvider(startInstant);
        var engine = new SessionEngine(store.Repository, clock);
        var today = new TodayService(store.Repository, null, clock, () => TimeZoneInfo.Utc);

        var controller = new TodayController(
            today.ReadAsync,
            (type, token) => engine.StartAsync(type, token),
            (id, token) => engine.StopAsync(id, token),
            _ => { },
            (id, token) => engine.PauseAsync(id, token),
            (id, token) => engine.ContinueAsync(id, token));

        await controller.OpenAsync();

        // 1. Start Break session
        await controller.StartAsync(SessionType.Break);
        Assert.NotNull(controller.Snapshot!.Running);
        Assert.Equal(SessionType.Break, controller.Snapshot.Running.Type);

        // 2. Run for 3 minutes and Pause
        clock.Advance(TimeSpan.FromMinutes(3));
        await controller.PauseAsync();
        Assert.NotNull(controller.Snapshot!.Paused);
        Assert.Equal(SessionType.Break, controller.Snapshot.Paused.Type);

        // 3. User selects and starts Work instead
        await controller.StartAsync(SessionType.Work);

        // 4. Assert previous Break session was finalized as Stopped with 3m credited time
        SessionRecord prevBreak = (await store.Repository.GetAsync(controller.Snapshot.Sessions.First(s => s.Type == SessionType.Break).Id))!;
        Assert.Equal(SessionStatus.Stopped, prevBreak.Status);
        Assert.Equal(TimeSpan.FromMinutes(3), prevBreak.EffectiveDuration);

        // 5. Assert Work is now running at full duration (30 min)
        Assert.NotNull(controller.Snapshot.Running);
        Assert.Equal(SessionType.Work, controller.Snapshot.Running.Type);
        Assert.Equal(TimeSpan.FromMinutes(30), controller.Snapshot.Running.PlannedDuration);
    }

    [Fact]
    public async Task BidirectionalSync_PauseFromToday_ContinueFromOverlay()
    {
        using var store = new SessionStore();
        var startInstant = TestSessions.Anchor;
        var clock = new ManualTimeProvider(startInstant);
        var coordinator = new SessionCoordinator(store.Repository, clock);
        await coordinator.InitializeAsync();
        var today = new TodayService(store.Repository, null, clock, () => TimeZoneInfo.Utc);

        var todayController = new TodayController(
            today.ReadAsync,
            coordinator.StartAsync,
            coordinator.StopAsync,
            _ => { },
            coordinator.PauseAsync,
            coordinator.ContinueAsync);

        var overlayView = new TestOverlayView();
        using var overlayController = new QuickOverlayController(
            () => overlayView,
            coordinator.GetActiveAsync,
            coordinator.GetDurationsAsync,
            coordinator.StartAsync,
            coordinator.StopAsync,
            coordinator.PauseAsync,
            coordinator.ContinueAsync);

        await todayController.OpenAsync();
        await overlayController.HandleActivationAsync(ShellActivationKind.Hotkey);

        // 1. Start Work from Today
        await todayController.StartAsync(SessionType.Work);
        await overlayController.RefreshIfVisibleAsync();
        Assert.NotNull(todayController.Snapshot?.Running);
        Assert.Equal(SessionStatus.Running, overlayView.LastState?.Active?.Status);

        // 2. Advance 5 minutes and Pause from Today
        clock.Advance(TimeSpan.FromMinutes(5));
        await todayController.PauseAsync();
        await overlayController.RefreshIfVisibleAsync();

        Assert.NotNull(todayController.Snapshot?.Paused);
        Assert.Equal(SessionStatus.Paused, overlayView.LastState?.Active?.Status);
        Assert.True(overlayView.LastState?.CanStart);

        // 3. Continue from Overlay
        overlayView.RaiseStart(); // Triggers start/continue in overlay
        await todayController.RefreshAsync();

        Assert.Equal(SessionStatus.Running, overlayView.LastState?.Active?.Status);
        Assert.NotNull(todayController.Snapshot?.Running);
        Assert.Equal(SessionStatus.Running, todayController.Snapshot.Running.Status);
    }

    [Fact]
    public async Task BidirectionalSync_PauseFromOverlay_ContinueFromToday()
    {
        using var store = new SessionStore();
        var startInstant = TestSessions.Anchor;
        var clock = new ManualTimeProvider(startInstant);
        var coordinator = new SessionCoordinator(store.Repository, clock);
        await coordinator.InitializeAsync();
        var today = new TodayService(store.Repository, null, clock, () => TimeZoneInfo.Utc);

        var todayController = new TodayController(
            today.ReadAsync,
            coordinator.StartAsync,
            coordinator.StopAsync,
            _ => { },
            coordinator.PauseAsync,
            coordinator.ContinueAsync);

        var overlayView = new TestOverlayView();
        using var overlayController = new QuickOverlayController(
            () => overlayView,
            coordinator.GetActiveAsync,
            coordinator.GetDurationsAsync,
            coordinator.StartAsync,
            coordinator.StopAsync,
            coordinator.PauseAsync,
            coordinator.ContinueAsync);

        await todayController.OpenAsync();
        await overlayController.HandleActivationAsync(ShellActivationKind.Hotkey);

        // 1. Start Work from Overlay
        overlayView.RaiseStart();
        await todayController.RefreshAsync();
        Assert.Equal(SessionStatus.Running, overlayView.LastState?.Active?.Status);
        Assert.NotNull(todayController.Snapshot?.Running);

        // 2. Advance 7 minutes and Pause from Overlay
        clock.Advance(TimeSpan.FromMinutes(7));
        overlayView.RaisePause();
        await todayController.RefreshAsync();

        Assert.Equal(SessionStatus.Paused, overlayView.LastState?.Active?.Status);
        Assert.NotNull(todayController.Snapshot?.Paused);

        // 3. Continue from Today
        await todayController.ContinueAsync();
        await overlayController.RefreshIfVisibleAsync();

        Assert.NotNull(todayController.Snapshot?.Running);
        Assert.Equal(SessionStatus.Running, overlayView.LastState?.Active?.Status);
    }

    [Fact]
    public async Task SingleActiveSessionInvariant_RejectsConcurrentRunningOrPaused()
    {
        using var store = new SessionStore();
        var startInstant = TestSessions.Anchor;
        var clock = new ManualTimeProvider(startInstant);
        var engine = new SessionEngine(store.Repository, clock);

        // 1. Start a session
        SessionRecord session1 = await engine.StartAsync(SessionType.Work);

        // Attempting to start another session must fail
        await Assert.ThrowsAsync<ActiveSessionAlreadyExistsException>(() => engine.StartAsync(SessionType.Break));

        // 2. Pause the session
        await engine.PauseAsync();

        // While paused, attempting to start another session directly must also fail
        await Assert.ThrowsAsync<ActiveSessionAlreadyExistsException>(() => engine.StartAsync(SessionType.Break));

        // Attempting to insert a duplicate active session into the SQLite repository directly must throw SQLiteException
        var duplicate = new SessionRecord
        {
            Id = SessionId.New(),
            Type = SessionType.Break,
            Status = SessionStatus.Running,
            StartedAt = startInstant.AddMinutes(1),
            ResumedAt = startInstant.AddMinutes(1),
            PlannedDuration = TimeSpan.FromMinutes(10),
            AccumulatedActiveDuration = TimeSpan.Zero,
            CreatedAt = startInstant.AddMinutes(1),
        };

        await Assert.ThrowsAsync<ActiveSessionAlreadyExistsException>(() => store.Repository.AddAsync(duplicate));
    }

    private sealed class TestOverlayView : IQuickOverlayView
    {
        public QuickOverlayState? LastState { get; private set; }
        public event Action<SessionType>? SelectionRequested { add { } remove { } }
        public event Action? StartRequested;
        public event Action? StopRequested { add { } remove { } }
        public event Action? PauseRequested;
        public event Action? StartNewRequested;
        public event Action? DismissRequested { add { } remove { } }
        public void Render(QuickOverlayState state) => LastState = state;
        public void ShowAndFocus() { }
        public void Hide() { }
        public void Dispose() { }
        public void RaisePause() => PauseRequested?.Invoke();
        public void RaiseStart() => StartRequested?.Invoke();
        public void RaiseStartNew() => StartNewRequested?.Invoke();
    }
}
