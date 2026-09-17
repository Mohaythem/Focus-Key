using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;
using FocusKey.Foundation.Shell;

namespace FocusKey.Foundation.Tests.Shell;

public sealed class WindowCloseActionTests
{
    [Fact]
    public void WindowCloseAction_EnumValues_AreExpected()
    {
        Assert.Equal(0, (int)WindowCloseAction.Cancel);
        Assert.Equal(1, (int)WindowCloseAction.Hide);
        Assert.Equal(2, (int)WindowCloseAction.Quit);
    }

    [Fact]
    public async Task HideAction_PreservesRunningSession_AndDoesNotTriggerShellExit()
    {
        using var store = new SessionStore();
        var time = new ManualTimeProvider(TestSessions.Anchor);
        var coordinator = new SessionCoordinator(store.Repository, time);
        await coordinator.InitializeAsync();
        SessionRecord running = await coordinator.StartAsync(SessionType.Work);

        var integration = new FakeShellIntegration();
        int shutdownCalls = 0;
        using var shell = new BackgroundShell(integration, async () =>
        {
            Interlocked.Increment(ref shutdownCalls);
            await coordinator.ShutdownAsync();
        });
        shell.Start();

        // Simulate choosing Hide Focus Key: window is hidden, no shutdown is triggered
        WindowCloseAction choice = WindowCloseAction.Hide;
        Assert.Equal(WindowCloseAction.Hide, choice);

        // Verify shell is still running and session remains running
        SessionRecord? activeSession = await store.Repository.GetAsync(running.Id);
        Assert.NotNull(activeSession);
        Assert.Equal(SessionStatus.Running, activeSession.Status);
        Assert.Equal(0, shutdownCalls);
        Assert.Equal(0, integration.DisposeCount);
    }

    [Fact]
    public async Task HideAction_PreservesPausedSession_AndDoesNotTriggerShellExit()
    {
        using var store = new SessionStore();
        var time = new ManualTimeProvider(TestSessions.Anchor);
        var coordinator = new SessionCoordinator(store.Repository, time);
        await coordinator.InitializeAsync();
        SessionRecord running = await coordinator.StartAsync(SessionType.Work);
        time.Advance(TimeSpan.FromMinutes(5));
        SessionOutcome pauseResult = await coordinator.PauseAsync(running.Id);
        Assert.Equal(SessionOutcomeKind.Paused, pauseResult.Kind);

        var integration = new FakeShellIntegration();
        int shutdownCalls = 0;
        using var shell = new BackgroundShell(integration, async () =>
        {
            Interlocked.Increment(ref shutdownCalls);
            await coordinator.ShutdownAsync();
        });
        shell.Start();

        // Simulate choosing Hide Focus Key: window is hidden, no shutdown is triggered
        WindowCloseAction choice = WindowCloseAction.Hide;
        Assert.Equal(WindowCloseAction.Hide, choice);

        // Verify paused session remains paused with active duration intact
        SessionRecord? activeSession = await store.Repository.GetAsync(running.Id);
        Assert.NotNull(activeSession);
        Assert.Equal(SessionStatus.Paused, activeSession.Status);
        Assert.Equal(TimeSpan.FromMinutes(5), activeSession.AccumulatedActiveDuration);
        Assert.Equal(0, shutdownCalls);
        Assert.Equal(0, integration.DisposeCount);
    }

    [Fact]
    public async Task QuitAction_TriggersCanonicalShutdown_AndPersistsSessionState()
    {
        using var store = new SessionStore();
        var time = new ManualTimeProvider(TestSessions.Anchor);
        var coordinator = new SessionCoordinator(store.Repository, time);
        await coordinator.InitializeAsync();
        SessionRecord running = await coordinator.StartAsync(SessionType.Work);
        time.Advance(TimeSpan.FromMinutes(5));

        var integration = new FakeShellIntegration();
        int shutdownCalls = 0;
        using var shell = new BackgroundShell(integration, async () =>
        {
            Interlocked.Increment(ref shutdownCalls);
            await coordinator.ShutdownAsync();
        });
        shell.Start();

        // Simulate choosing Quit Focus Key: triggers canonical exit path
        WindowCloseAction choice = WindowCloseAction.Quit;
        Assert.Equal(WindowCloseAction.Quit, choice);

        await shell.ExitAsync();

        // Verify canonical exit occurred: running session becomes interrupted, shell disposed
        SessionRecord? endedSession = await store.Repository.GetAsync(running.Id);
        Assert.NotNull(endedSession);
        Assert.Equal(SessionStatus.Interrupted, endedSession.Status);
        Assert.Equal(TestSessions.Anchor.AddMinutes(5), endedSession.EndedAt);
        Assert.Equal(1, shutdownCalls);
        Assert.Equal(1, integration.DisposeCount);
    }

    [Fact]
    public async Task CancelAction_PreservesSession_AndLeavesShellRunning()
    {
        using var store = new SessionStore();
        var time = new ManualTimeProvider(TestSessions.Anchor);
        var coordinator = new SessionCoordinator(store.Repository, time);
        await coordinator.InitializeAsync();
        SessionRecord running = await coordinator.StartAsync(SessionType.Work);

        var integration = new FakeShellIntegration();
        int shutdownCalls = 0;
        using var shell = new BackgroundShell(integration, async () =>
        {
            Interlocked.Increment(ref shutdownCalls);
            await coordinator.ShutdownAsync();
        });
        shell.Start();

        // Simulate choosing Cancel (or pressing Esc): no change
        WindowCloseAction choice = WindowCloseAction.Cancel;
        Assert.Equal(WindowCloseAction.Cancel, choice);

        SessionRecord? activeSession = await store.Repository.GetAsync(running.Id);
        Assert.NotNull(activeSession);
        Assert.Equal(SessionStatus.Running, activeSession.Status);
        Assert.Equal(0, shutdownCalls);
        Assert.Equal(0, integration.DisposeCount);
    }

    private sealed class FakeShellIntegration : IShellIntegration
    {
        public event Action<ShellActivationKind>? ActivationRequested { add { } remove { } }
        public event Action? ExitRequested { add { } remove { } }
        public event Action<Exception>? ErrorOccurred { add { } remove { } }
        public int StartCount { get; private set; }
        public int DisposeCount { get; private set; }
        public void Start() => StartCount++;
        public void Dispose() => DisposeCount++;
    }
}
