using FocusKey.Foundation.Overlay;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Shell;
using FocusKey.Foundation.Tests.Sessions;

namespace FocusKey.Foundation.Tests.Overlay;

public sealed class QuickOverlayControllerTests
{
    [Theory]
    [InlineData(null, "--:--")]
    [InlineData(19, "0:19")]
    [InlineData(600, "10:00")]
    [InlineData(2825, "47:05")]
    [InlineData(5415, "1:30:15")]
    public void DurationFormatterDisplaysCurrentWholeSecondValue(int? seconds, string expected) =>
        Assert.Equal(expected, QuickOverlayDurationFormatter.Format(
            seconds is null ? null : TimeSpan.FromSeconds(seconds.Value)));

    [Fact]
    public async Task HotkeyCreatesAndShowsOneView_AndShowWindowIsIgnored()
    {
        var view = new FakeView();
        int creates = 0;
        using var controller = New(view, () => { creates++; return view; });

        await controller.HandleActivationAsync(ShellActivationKind.ShowWindow);
        Assert.Equal(0, creates);
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        Assert.Equal(1, creates);
        Assert.Equal(1, view.ShowCount);
        Assert.Equal(new QuickOverlayState(SessionType.Work, false, true, null)
            { Durations = SessionDurations.Default }, view.LastState);
    }

    [Fact]
    public async Task RepeatedAndInflightActivationReusesViewAndDoesNotReadTwice()
    {
        var view = new FakeView();
        var read = new TaskCompletionSource<SessionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        using var controller = New(view, getActive: _ => { reads++; return read.Task; });
        Task first = controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        await view.WaitFor(s => s.IsBusy);
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        Assert.Equal(2, view.ShowCount);
        Assert.Equal(1, reads);
        read.SetResult(null);
        await first;
    }

    [Fact]
    public async Task SelectionAndStartUseExactDelegate()
    {
        var view = new FakeView();
        SessionType? selected = null;
        using var controller = New(view, start: (type, _) => { selected = type; return Task.FromResult(TestSessions.Running(type: type)); });
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        view.RaiseSelection(SessionType.Break);
        Assert.Equal(SessionType.Break, view.LastState.Selected);
        view.RaiseStart();
        await view.WaitFor(s => s.Active is not null && !s.IsBusy);
        Assert.Equal(SessionType.Break, selected);
    }

    [Fact]
    public async Task RealCoordinatorUsesWorkAndBreakDefaults()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var coordinator = new SessionCoordinator(store.Repository, clock);
        await coordinator.InitializeAsync();
        var view = new FakeView();
        using var controller = New(view, getActive: coordinator.GetActiveAsync, start: coordinator.StartAsync);
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        await controller.StartAsync();
        SessionRecord work = (await store.Repository.GetRunningAsync())!;
        Assert.Equal(SessionType.Work, work.Type);
        Assert.Equal(TestSessions.WorkLength, work.PlannedDuration);
        await coordinator.StopAsync();
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        view.RaiseSelection(SessionType.Break);
        await controller.StartAsync();
        SessionRecord rest = (await store.Repository.GetRunningAsync())!;
        Assert.Equal(SessionType.Break, rest.Type);
        Assert.Equal(TestSessions.BreakLength, rest.PlannedDuration);
    }

    [Fact]
    public async Task ExistingRunningShowsTimerAndDoesNotStart()
    {
        var view = new FakeView();
        int starts = 0;
        using var controller = New(view, getActive: _ => Task.FromResult<SessionSnapshot?>(SessionSnapshot.For(TestSessions.Running(), TestSessions.Anchor)), start: (_, _) => { starts++; return Task.FromResult(TestSessions.Running()); });
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        Assert.False(view.LastState.CanStart);
        Assert.NotNull(view.LastState.Active);
        view.RaiseStart();
        Assert.Equal(0, starts);
    }

    [Fact]
    public async Task StartRaceRefreshesWinnerAndCanRetryAfterItEnds()
    {
        var view = new FakeView();
        SessionSnapshot? current = null;
        int starts = 0;
        using var controller = New(view, getActive: _ => Task.FromResult(current), start: (type, _) =>
        {
            var record = TestSessions.Running(type: ++starts == 1 ? SessionType.Break : type);
            current = SessionSnapshot.For(record, TestSessions.Anchor);
            return starts == 1 ? Task.FromException<SessionRecord>(new ActiveSessionAlreadyExistsException(record.Id)) : Task.FromResult(record);
        });
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        await controller.StartAsync();
        Assert.Equal(SessionType.Break, view.LastState.Active!.Type);
        Assert.False(view.LastState.CanStart);
        current = null;
        controller.Dismiss();
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        Assert.True(view.LastState.CanStart);
        await controller.StartAsync();
        Assert.Equal(2, starts);
        Assert.Equal(SessionType.Work, view.LastState.Active!.Type);
    }

    [Fact]
    public async Task DismissedOverlayCannotStartAndDismissEventHides()
    {
        var view = new FakeView(); int starts = 0;
        using var controller = New(view, start: (_, _) => { starts++; return Task.FromResult(TestSessions.Running()); });
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        view.RaiseDismiss();
        view.RaiseStart();
        Assert.Equal(1, view.HideCount);
        Assert.Equal(0, starts);
    }

    [Fact]
    public async Task CloseReopenResetsSelectionAndReadsFreshState()
    {
        var view = new FakeView(); int reads = 0;
        using var controller = New(view, getActive: _ => Task.FromResult<SessionSnapshot?>(reads++ == 0 ? null : SessionSnapshot.For(TestSessions.Running(type: SessionType.Break), TestSessions.Anchor)));
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        view.RaiseSelection(SessionType.Break); view.RaiseDismiss();
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        Assert.Equal(SessionType.Work, view.LastState.Selected);
        Assert.False(view.LastState.CanStart);
        Assert.NotNull(view.LastState.Active);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task StaleReadAfterHideAndReopenCannotClobberNewState()
    {
        var view = new FakeView();
        var oldRead = new TaskCompletionSource<SessionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var newRead = new TaskCompletionSource<SessionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        using var controller = New(view, getActive: _ => ++reads == 1 ? oldRead.Task : newRead.Task);
        Task first = controller.HandleActivationAsync(ShellActivationKind.Hotkey); await view.WaitFor(s => s.IsBusy);
        view.RaiseDismiss();
        Task second = controller.HandleActivationAsync(ShellActivationKind.Hotkey); await view.WaitFor(s => s.IsBusy);
        newRead.SetResult(null); await second;
        oldRead.SetResult(SessionSnapshot.For(TestSessions.Running(type: SessionType.Break), TestSessions.Anchor)); await first;
        Assert.Equal(new QuickOverlayState(SessionType.Work, false, true, null)
            { Durations = SessionDurations.Default }, view.LastState);
    }

    [Fact]
    public async Task DuplicateStartRequestsWhilePendingCallStartOnce_AndEscapeDoesNotCancelAcceptedStart()
    {
        var view = new FakeView(); var release = new TaskCompletionSource<SessionRecord>(TaskCreationOptions.RunContinuationsAsynchronously); int starts = 0;
        using var controller = New(view, start: (_, _) => { starts++; return release.Task; });
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        Task accepted = controller.StartAsync();
        Task duplicate = controller.StartAsync();
        await view.WaitFor(s => s.IsBusy);
        view.RaiseDismiss();
        Assert.Equal(1, starts);
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        Assert.False(view.LastState.CanStart);
        Assert.True(view.LastState.IsBusy);
        release.SetResult(TestSessions.Running());
        await accepted;
        Assert.Equal(1, view.HideCount);
        Assert.NotNull(view.LastState.Active);
        await duplicate;
    }

    [Fact]
    public async Task SQLiteRaceRejectsSecondStartWithoutDuplicateRows()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var coordinator = new SessionCoordinator(store.Repository, clock);
        await coordinator.InitializeAsync();
        var view = new FakeView();
        using var controller = New(view, getActive: coordinator.GetActiveAsync, start: coordinator.StartAsync);
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        SessionRecord external = await coordinator.StartAsync(SessionType.Break);
        view.RaiseStart();
        await view.WaitFor(s => s.Active is not null && !s.IsBusy);
        Assert.False(view.LastState.CanStart);
        Assert.Equal(external.Id, (await store.Repository.GetRunningAsync())!.Id);
        Assert.Equal(1L, store.ScalarRaw<long>("SELECT COUNT(*) FROM sessions"));
    }

    [Fact]
    public async Task FailureAllowsRetryAndReportsError()
    {
        var view = new FakeView(); var errors = new List<Exception>(); int starts = 0;
        using var controller = New(view, start: (_, _) => starts++ == 0 ? Task.FromException<SessionRecord>(new InvalidOperationException()) : Task.FromResult(TestSessions.Running()));
        controller.ErrorOccurred += errors.Add;
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey); view.RaiseStart();
        await view.WaitFor(s => s.CanStart && s.Feedback is not null);
        Assert.Single(errors); view.RaiseStart(); await view.WaitFor(s => s.Active is not null && !s.IsBusy); Assert.Equal(2, starts);
    }

    [Fact]
    public async Task ReadFailureReportsErrorAndReopenRetries()
    {
        var view = new FakeView(); var errors = new List<Exception>(); int reads = 0;
        using var controller = New(view, getActive: _ => ++reads == 1 ? Task.FromException<SessionSnapshot?>(new InvalidOperationException()) : Task.FromResult<SessionSnapshot?>(null));
        controller.ErrorOccurred += errors.Add;
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey); Assert.Single(errors);
        view.RaiseDismiss(); await controller.HandleActivationAsync(ShellActivationKind.Hotkey); Assert.True(view.LastState.CanStart); Assert.Equal(2, reads);
    }

    [Fact]
    public async Task DisposeUnsubscribesAndInFlightResultsCannotReopen()
    {
        var view = new FakeView(); var read = new TaskCompletionSource<SessionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = New(view, getActive: _ => read.Task); Task activation = controller.HandleActivationAsync(ShellActivationKind.Hotkey); int rendersBeforeDispose = view.RenderCount; controller.Dispose();
        Assert.Equal(1, view.DisposeCount); view.RaiseSelection(SessionType.Break); view.RaiseStart(); Assert.Equal(rendersBeforeDispose, view.RenderCount);
        read.SetResult(null); await activation; Assert.Equal(1, view.HideCount);
    }

    [Fact]
    public async Task RefreshWhenHiddenDoesNotCreateViewOrRead()
    {
        var view = new FakeView(); int creates = 0; int reads = 0;
        using var controller = New(view, create: () => { creates++; return view; }, getActive: _ => { reads++; return Task.FromResult<SessionSnapshot?>(null); });
        await controller.RefreshIfVisibleAsync();
        Assert.Equal(0, creates); Assert.Equal(0, reads);
    }

    [Fact]
    public async Task RefreshAfterCompletionClearsRunningFeedback()
    {
        var view = new FakeView(); int reads = 0;
        using var controller = New(view, getActive: _ => Task.FromResult<SessionSnapshot?>(reads++ == 0 ? SessionSnapshot.For(TestSessions.Running(), TestSessions.Anchor) : null));
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        Assert.NotNull(view.LastState.Active);
        await controller.RefreshIfVisibleAsync();
        Assert.True(view.LastState.CanStart);
        Assert.Null(view.LastState.Feedback);
    }

    [Fact]
    public async Task ActivationDisplaysCurrentDurationsAndRefreshObservesRuntimeChanges()
    {
        var view = new FakeView();
        SessionDurations current = new(TimeSpan.FromMinutes(42), TimeSpan.FromSeconds(95));
        int reads = 0;
        using var controller = New(view, getDurations: _ =>
        {
            reads++;
            return Task.FromResult(current);
        });

        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        Assert.Equal(current, view.LastState.Durations);

        current = new SessionDurations(TimeSpan.FromMinutes(55), TimeSpan.FromMinutes(8));
        await controller.RefreshIfVisibleAsync();

        Assert.Equal(current, view.LastState.Durations);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task RepeatedHotkeyRefreshesDurationsAfterInitialObservationCompletes()
    {
        var view = new FakeView();
        SessionDurations current = SessionDurations.Default;
        using var controller = New(view, getDurations: _ => Task.FromResult(current));

        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        current = new SessionDurations(TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5));
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);

        Assert.Equal(current, view.LastState.Durations);
        Assert.Equal(2, view.ShowCount);
    }

    [Fact]
    public async Task StaleRefreshCannotOverwriteDismissalAndReopen()
    {
        var view = new FakeView();
        var stale = new TaskCompletionSource<SessionSnapshot?>(TaskCreationOptions.RunContinuationsAsynchronously);
        int reads = 0;
        using var controller = New(view, getActive: _ => ++reads == 1 ? Task.FromResult<SessionSnapshot?>(SessionSnapshot.For(TestSessions.Running(), TestSessions.Anchor)) : reads == 2 ? stale.Task : Task.FromResult<SessionSnapshot?>(null));
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        Task refresh = controller.RefreshIfVisibleAsync();
        view.RaiseDismiss();
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        stale.SetResult(SessionSnapshot.For(TestSessions.Running(type: SessionType.Break), TestSessions.Anchor));
        await refresh;
        Assert.True(view.LastState.CanStart);
        Assert.Null(view.LastState.Feedback);
    }

    [Theory]
    [InlineData(SessionType.Work, 75)]
    [InlineData(SessionType.Break, 12)]
    public async Task StartTransitionsSameViewToTimerAndStopReturnsSelection(SessionType type, int seconds)
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock,
            new SessionDurations(TimeSpan.FromSeconds(75), TimeSpan.FromSeconds(12)));
        await sessions.InitializeAsync();
        var view = new FakeView();
        using var controller = New(view, getActive: sessions.GetActiveAsync, start: sessions.StartAsync,
            getDurations: sessions.GetDurationsAsync, stop: sessions.StopAsync);
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        controller.Select(type);
        await controller.StartAsync();
        Assert.Equal(0, view.HideCount);
        Assert.Equal(1, view.ShowCount);
        var active = view.LastState.Active!;
        Assert.Equal(type, active.Type);
        Assert.Equal(TimeSpan.FromSeconds(seconds), active.PlannedDuration);
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(TimeSpan.FromSeconds(seconds - 2), Foundation.MiniTimer.MiniTimerController.RemainingAt(active, clock.GetUtcNow()));
        controller.Dismiss();
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        Assert.Equal(active.Id, view.LastState.Active!.Id);
        await controller.StopAsync();
        Assert.Null(view.LastState.Active);
        Assert.True(view.LastState.CanStart);
        Assert.Equal(SessionStatus.Stopped, (await store.Repository.GetAsync(active.Id))!.Status);
    }

    [Fact]
    public async Task NaturalCompletionRefreshReturnsSelectionWithoutHidingOrStartingAnotherSession()
    {
        using var store = new SessionStore();
        var clock = new ManualTimeProvider(TestSessions.Anchor);
        var sessions = new SessionCoordinator(store.Repository, clock);
        await sessions.InitializeAsync();
        var work = await sessions.StartAsync(SessionType.Work);
        var view = new FakeView();
        using var controller = New(view, getActive: sessions.GetActiveAsync, stop: sessions.StopAsync);
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        clock.Advance(TimeSpan.FromHours(1));
        await sessions.CompleteIfDueAsync();
        await controller.RefreshIfVisibleAsync();
        Assert.True(view.LastState.CanStart);
        Assert.Null(view.LastState.Active);
        Assert.Equal(0, view.HideCount);
        Assert.Equal(work.PlannedEndAt, (await store.Repository.GetAsync(work.Id))!.EndedAt);
    }

    [Fact]
    public async Task StaleStopCannotStopReplacementSession()
    {
        using var store = new SessionStore();
        var sessions = new SessionCoordinator(store.Repository, new ManualTimeProvider(TestSessions.Anchor));
        await sessions.InitializeAsync();
        var work = await sessions.StartAsync(SessionType.Work);
        var view = new FakeView();
        using var controller = New(view, getActive: sessions.GetActiveAsync, stop: sessions.StopAsync);
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        await sessions.StopAsync(work.Id);
        var rest = await sessions.StartAsync(SessionType.Break);
        await controller.StopAsync();
        Assert.Equal(rest.Id, view.LastState.Active!.Id);
        Assert.Equal(rest.Id, (await store.Repository.GetRunningAsync())!.Id);
    }

    [Fact]
    public async Task DuplicateStopAndHideWhileStoppingDoNotCancelOrReopen()
    {
        using var store = new SessionStore();
        var sessions = new SessionCoordinator(store.Repository, new ManualTimeProvider(TestSessions.Anchor));
        await sessions.InitializeAsync();
        var work = await sessions.StartAsync(SessionType.Work);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int stops = 0;
        var view = new FakeView();
        using var controller = New(view, getActive: sessions.GetActiveAsync, stop: async (id, token) =>
        { stops++; await release.Task; return await sessions.StopAsync(id, token); });
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        var accepted = controller.StopAsync();
        await controller.StopAsync();
        controller.Dismiss();
        release.SetResult(); await accepted;
        Assert.Equal(1, stops); Assert.Equal(1, view.ShowCount); Assert.Equal(1, view.HideCount);
        Assert.Equal(SessionStatus.Stopped, (await store.Repository.GetAsync(work.Id))!.Status);
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        Assert.True(view.LastState.CanStart);
    }

    [Fact]
    public async Task StopFailureKeepsIdentityAndAllowsRetry()
    {
        using var store = new SessionStore();
        var sessions = new SessionCoordinator(store.Repository, new ManualTimeProvider(TestSessions.Anchor));
        await sessions.InitializeAsync();
        var work = await sessions.StartAsync(SessionType.Work);
        bool fail = true;
        var view = new FakeView();
        using var controller = New(view, getActive: sessions.GetActiveAsync, stop: (id, token) =>
            fail ? Task.FromException<SessionOutcome>(new IOException()) : sessions.StopAsync(id, token));
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        await controller.StopAsync();
        Assert.Equal(work.Id, view.LastState.Active!.Id);
        Assert.False(view.LastState.IsBusy); Assert.Contains("Could not stop", view.LastState.Feedback);
        fail = false; await controller.StopAsync();
        Assert.True(view.LastState.CanStart); Assert.Null(view.LastState.Active);
    }

    [Fact]
    public async Task FailedRefreshDisablesStaleStopAndReopenRetries()
    {
        bool fail = false;
        var view = new FakeView();
        using var controller = New(view, getActive: _ => fail ? Task.FromException<SessionSnapshot?>(new IOException()) :
            Task.FromResult<SessionSnapshot?>(SessionSnapshot.For(TestSessions.Running(), TestSessions.Anchor)));
        await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        fail = true; await controller.RefreshIfVisibleAsync();
        Assert.Null(view.LastState.Active); Assert.False(view.LastState.CanStart);
        fail = false; await controller.HandleActivationAsync(ShellActivationKind.Hotkey);
        Assert.NotNull(view.LastState.Active);
    }

    private static QuickOverlayController New(FakeView view, Func<IQuickOverlayView>? create = null,
        Func<CancellationToken, Task<SessionSnapshot?>>? getActive = null,
        Func<CancellationToken, Task<SessionDurations>>? getDurations = null,
        Func<SessionType, CancellationToken, Task<SessionRecord>>? start = null,
        Func<SessionId, CancellationToken, Task<SessionOutcome>>? stop = null)
    {
        SessionSnapshot? current = null;
        return new(create ?? (() => view), getActive ?? (_ => Task.FromResult(current)),
            getDurations ?? (_ => Task.FromResult(SessionDurations.Default)),
            async (type, token) =>
            {
                var record = start is null ? TestSessions.Running(type: type) : await start(type, token);
                current = SessionSnapshot.For(record, TestSessions.Anchor);
                return record;
            }, stop ?? ((_, _) => Task.FromException<SessionOutcome>(new NotSupportedException())));
    }
    private sealed class FakeView : IQuickOverlayView
    {
        internal readonly List<QuickOverlayState> States = [];
        internal readonly TaskCompletionSource Hidden = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int ShowCount, HideCount, DisposeCount, RenderCount;
        internal QuickOverlayState LastState => States[^1];
        internal Task WaitFor(Func<QuickOverlayState, bool> p) { var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); foreach (var s in States) if (p(s)) t.TrySetResult(); if (!t.Task.IsCompleted) _watchers.Add((p,t)); return t.Task; }
        private readonly List<(Func<QuickOverlayState,bool> P, TaskCompletionSource T)> _watchers = [];
        public event Action<SessionType>? SelectionRequested; public event Action? StartRequested; public event Action? StopRequested; public event Action? DismissRequested;
        public void Render(QuickOverlayState state) { States.Add(state); RenderCount++; foreach (var w in _watchers.ToArray()) if (w.P(state)) w.T.TrySetResult(); }
        public void ShowAndFocus() => ShowCount++;
        public void Hide() { HideCount++; Hidden.TrySetResult(); }
        public void RaiseSelection(SessionType t) => SelectionRequested?.Invoke(t); public void RaiseStart() => StartRequested?.Invoke(); public void RaiseDismiss() => DismissRequested?.Invoke();
        public void RaiseStop() => StopRequested?.Invoke();
        public void Dispose() => DisposeCount++;
    }
}
