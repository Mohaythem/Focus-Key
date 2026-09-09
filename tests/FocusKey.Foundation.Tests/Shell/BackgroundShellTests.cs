using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Tests.Sessions;
using FocusKey.Foundation.Shell;

namespace FocusKey.Foundation.Tests.Shell;

public sealed class BackgroundShellTests
{
    [Fact]
    public void Start_IsIdempotent_AndStartupFailureRollsBackSubscriptions()
    {
        var integration = new FakeShellIntegration { StartException = new InvalidOperationException("tray") };
        using var shell = new BackgroundShell(integration, () => Task.CompletedTask);

        Assert.Throws<InvalidOperationException>(() => shell.Start());
        Assert.Equal(1, integration.StartCount);
        Assert.Equal(1, integration.DisposeCount);
        Assert.Equal(0, integration.ActivationSubscriberCount);
        Assert.Throws<ObjectDisposedException>(() => shell.Start());

        var startedIntegration = new FakeShellIntegration();
        using var started = new BackgroundShell(startedIntegration, () => Task.CompletedTask);
        started.Start();
        started.Start();
        Assert.Equal(1, startedIntegration.StartCount);
    }

    [Fact]
    public async Task Activation_ForwardsWindowHotkeyAndMiniTimer_AndIsSuppressedDuringShutdown()
    {
        var integration = new FakeShellIntegration();
        var shutdownEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var shell = new BackgroundShell(integration, async () => { shutdownEntered.SetResult(); await release.Task; });
        var activations = new List<ShellActivationKind>();
        shell.ActivationRequested += activations.Add;
        shell.Start();

        integration.RaiseActivation(ShellActivationKind.ShowWindow);
        integration.RaiseActivation(ShellActivationKind.Hotkey);
        integration.RaiseActivation(ShellActivationKind.MiniTimer);
        Assert.Equal([ShellActivationKind.ShowWindow, ShellActivationKind.Hotkey, ShellActivationKind.MiniTimer], activations);

        Task exit = shell.ExitAsync();
        await shutdownEntered.Task;
        integration.RaiseActivation(ShellActivationKind.Hotkey);
        integration.RaiseActivation(ShellActivationKind.MiniTimer);
        Assert.Equal(3, activations.Count);
        release.SetResult();
        await exit;
    }

    [Fact]
    public async Task Exit_DisposesAfterShutdown_AndConcurrentRequestsShutdownOnce()
    {
        var integration = new FakeShellIntegration();
        var shutdown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int shutdownCalls = 0;
        using var shell = new BackgroundShell(integration, async () => { Interlocked.Increment(ref shutdownCalls); await shutdown.Task; });
        int exited = 0;
        shell.Exited += () => Interlocked.Increment(ref exited);
        shell.Start();

        Task first = shell.ExitAsync();
        Task second = shell.ExitAsync();
        Assert.Equal(0, integration.DisposeCount);
        shutdown.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(1, shutdownCalls);
        Assert.Equal(1, integration.DisposeCount);
        Assert.Equal(1, exited);
    }

    [Fact]
    public async Task FailedShutdown_PreservesResourcesAndCanRetry()
    {
        var integration = new FakeShellIntegration();
        int calls = 0;
        using var shell = new BackgroundShell(integration, () =>
            Interlocked.Increment(ref calls) == 1
                ? Task.FromException(new InvalidOperationException("db"))
                : Task.CompletedTask);
        shell.Start();
        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.ExitAsync());
        Assert.Equal(0, integration.DisposeCount);
        Assert.Equal(1, integration.ActivationSubscriberCount);
        ShellActivationKind? activation = null;
        shell.ActivationRequested += kind => activation = kind;
        shell.RequestActivation(ShellActivationKind.ShowWindow);
        Assert.Equal(ShellActivationKind.ShowWindow, activation);
        await shell.ExitAsync();
        Assert.Equal(1, integration.DisposeCount);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task NativeExitRequested_RoutesFailuresToErrorOccurred()
    {
        var integration = new FakeShellIntegration();
        var error = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var shell = new BackgroundShell(integration, () => Task.FromException(new ApplicationException("shutdown")));
        shell.ErrorOccurred += exception => error.TrySetResult(exception);
        shell.Start();
        integration.RaiseExit();
        Exception observed = await error.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsType<ApplicationException>(observed);
        Assert.Equal(0, integration.DisposeCount);
    }

    [Fact]
    public async Task NativeExitRequested_SuccessDisposesAndRaisesExited()
    {
        var integration = new FakeShellIntegration();
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var shell = new BackgroundShell(integration, () => Task.CompletedTask);
        shell.Exited += exited.SetResult;
        shell.Start();
        integration.RaiseExit();
        await exited.Task;
        Assert.Equal(1, integration.DisposeCount);
    }

    [Fact]
    public void NativeErrors_AreForwarded()
    {
        var integration = new FakeShellIntegration();
        using var shell = new BackgroundShell(integration, () => Task.CompletedTask);
        Exception? observed = null;
        shell.ErrorOccurred += exception => observed = exception;
        shell.Start();
        var expected = new InvalidOperationException("native");
        integration.RaiseError(expected);
        Assert.Same(expected, observed);
    }

    [Fact]
    public async Task ExitBeforeStart_IsRejected()
    {
        using var shell = new BackgroundShell(new FakeShellIntegration(), () => Task.CompletedTask);
        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.ExitAsync());
    }

    [Fact]
    public void Dispose_IsIdempotent_AndUnhooksAllNativeEvents()
    {
        var integration = new FakeShellIntegration();
        using var shell = new BackgroundShell(integration, () => Task.CompletedTask);
        shell.Start();
        shell.Dispose();
        shell.Dispose();
        Assert.Equal(1, integration.DisposeCount);
        Assert.Equal(0, integration.ActivationSubscriberCount);
        Assert.Equal(0, integration.ExitSubscriberCount);
        Assert.Equal(0, integration.ErrorSubscriberCount);
        int activations = 0;
        shell.ActivationRequested += _ => activations++;
        shell.RequestActivation(ShellActivationKind.ShowWindow);
        integration.RaiseActivation(ShellActivationKind.Hotkey);
        Assert.Equal(0, activations);
    }

    [Fact]
    public async Task CoordinatorShutdown_InterruptsRunningSession_AndCompletesDueSession()
    {
        using var store = new SessionStore();
        var time = new ManualTimeProvider(TestSessions.Anchor);
        var coordinator = new SessionCoordinator(store.Repository, time);
        await coordinator.InitializeAsync();
        SessionRecord running = await coordinator.StartAsync(SessionType.Work);
        time.Advance(TimeSpan.FromMinutes(5));
        var integration = new FakeShellIntegration();
        var shell = new BackgroundShell(integration, () => coordinator.ShutdownAsync());
        shell.Start();
        await shell.ExitAsync();
        SessionRecord interrupted = (await store.Repository.GetAsync(running.Id))!;
        Assert.Equal(SessionStatus.Interrupted, interrupted.Status);
        Assert.Equal(TestSessions.Anchor.AddMinutes(5), interrupted.EndedAt);
        Assert.Equal(1, integration.DisposeCount);

        using var dueStore = new SessionStore();
        var dueTime = new ManualTimeProvider(TestSessions.Anchor);
        var dueCoordinator = new SessionCoordinator(dueStore.Repository, dueTime);
        await dueCoordinator.InitializeAsync();
        SessionRecord due = await dueCoordinator.StartAsync(SessionType.Work);
        dueTime.Advance(due.PlannedDuration);
        var dueShell = new BackgroundShell(new FakeShellIntegration(), () => dueCoordinator.ShutdownAsync());
        dueShell.Start();
        await dueShell.ExitAsync();
        Assert.Equal(SessionStatus.Completed, (await dueStore.Repository.GetAsync(due.Id))!.Status);
        Assert.Equal(due.PlannedEndAt, (await dueStore.Repository.GetAsync(due.Id))!.EndedAt);
    }

    [Fact]
    public async Task CoordinatorShutdownFailure_PreservesRunningRow_ForRetry()
    {
        using var store = new SessionStore();
        SessionRecord running = TestSessions.Running();
        var repository = new LifecycleTestRepository(store.Repository);
        var coordinator = new SessionCoordinator(repository, new ManualTimeProvider(TestSessions.Anchor.AddMinutes(5)));
        await coordinator.InitializeAsync();
        await store.Repository.AddAsync(running);

        repository.FailWrites = true;
        var integration = new FakeShellIntegration();
        var shell = new BackgroundShell(integration, () => coordinator.ShutdownAsync());
        shell.Start();
        await Assert.ThrowsAsync<InvalidOperationException>(() => shell.ExitAsync());
        Assert.Equal(SessionStatus.Running, (await store.Repository.GetAsync(running.Id))!.Status);
        Assert.Equal(0, integration.DisposeCount);
        repository.FailWrites = false;
        await shell.ExitAsync();
        SessionRecord retry = (await store.Repository.GetAsync(running.Id))!;
        Assert.Equal(SessionStatus.Interrupted, retry.Status);
        Assert.Equal(1, integration.DisposeCount);
    }

    private sealed class FakeShellIntegration : IShellIntegration
    {
        public event Action<ShellActivationKind>? ActivationRequested;
        public event Action? ExitRequested;
        public event Action<Exception>? ErrorOccurred;
        public Exception? StartException { get; set; }
        public int StartCount { get; private set; }
        public int DisposeCount { get; private set; }
        public int ActivationSubscriberCount => ActivationRequested?.GetInvocationList().Length ?? 0;
        public int ExitSubscriberCount => ExitRequested?.GetInvocationList().Length ?? 0;
        public int ErrorSubscriberCount => ErrorOccurred?.GetInvocationList().Length ?? 0;
        public void Start() { StartCount++; if (StartException is not null) throw StartException; }
        public void Dispose() => DisposeCount++;
        public void RaiseActivation(ShellActivationKind kind) => ActivationRequested?.Invoke(kind);
        public void RaiseExit() => ExitRequested?.Invoke();
        public void RaiseError(Exception exception) => ErrorOccurred?.Invoke(exception);
    }
}
