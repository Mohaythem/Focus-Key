using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Shell;
using FocusKey.Foundation.Overlay;
using FocusKey.Overlay;
using FocusKey.Shell;
using FocusKey.Startup;
using System.Security.Principal;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace FocusKey;

/// <summary>
/// Application entry point: initialize and recover before showing the Today window,
/// record failures, and persist session shutdown before closing.
/// </summary>
public partial class App : Application
{
    private StartupContext? _startup;
    private MainWindow? _window;
    private bool _allowClose;
    private SingleInstanceLease? _ownership;
    private BackgroundShell? _shell;
    private InstanceActivationSignal? _activationSignal;
    private QuickOverlayController? _quickOverlay;
    private CompletionCoordinator? _completion;
    private bool _isExiting;

    public App()
    {
        InitializeComponent();

        UnhandledException += OnXamlUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Same user and interactive Windows session, regardless of data-root override.
            // Acquire before bootstrap: a second launch must never recover the owner's session.
            using var identity = WindowsIdentity.GetCurrent();
            string user = identity.User?.Value ?? throw new InvalidOperationException("Cannot identify the current Windows user.");
            string instanceName = $@"Local\FocusKey.Shell.{user}";
            _activationSignal = new InstanceActivationSignal(instanceName + ".Activation");
            _ownership = SingleInstanceLease.TryAcquire(instanceName);
            if (_ownership is null)
            {
                _activationSignal.Send();
                _activationSignal.Dispose();
                Exit();
                return;
            }

            _startup = await FoundationBootstrap.RunAsync();
            _startup.Logger.Info("Single-instance shell ownership acquired.");
            _window = new MainWindow(_startup, StopSessionAsync,
                exception => _startup?.Logger.Error("Today operation failed.", exception));
            _window.ExitRequested += OnExplicitExitRequested;
            _window.AppWindow.Closing += OnAppWindowClosing;
            _window.Closed += OnMainWindowClosed;
            var integration = new WindowsShellIntegration();
            _completion = new CompletionCoordinator(_startup.Sessions, session => NotifyCompletedAsync(integration, session),
                exception => _startup?.Logger.Error("Completion coordination failed.", exception));
            integration.ClockChangedOrResumed += _completion.RequestEvaluation;
            integration.ClockChangedOrResumed += () =>
            {
                TimeZoneInfo.ClearCachedData();
                _window?.RefreshToday();
            };
            _quickOverlay = new QuickOverlayController(() => new QuickOverlayWindow(),
                _startup.Sessions.GetActiveAsync, StartSessionAsync);
            _quickOverlay.ErrorOccurred += exception => _startup?.Logger.Error("Quick overlay operation failed.", exception);
            _shell = new BackgroundShell(integration, ShutdownSessionsAsync);
            _shell.ActivationRequested += OnShellActivation;
            _shell.ErrorOccurred += OnShellError;
            _shell.Exited += OnShellExited;
            _shell.Start();
            await _completion.EvaluateAsync();
            _activationSignal.Listen(_window.DispatcherQueue, () => _shell.RequestActivation(ShellActivationKind.ShowWindow));
            _startup.Logger.Info("Shell ready: tray added; Shift + F3 registered.");
            _window.Activate();
            _window.OpenToday();
        }
        catch (Exception exception)
        {
            _completion?.Dispose();
            _shell?.Dispose();
            _activationSignal?.Dispose();
            _startup?.Dispose();
            _ownership?.Dispose();
            FatalError.ReportStartupFailure(exception);
            Environment.Exit(1);
        }
    }

    private void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        _window?.HideToday();
        sender.Hide();
        _startup?.Logger.Info("Main window hidden; shell remains running.");
    }

    private async void OnShellActivation(ShellActivationKind kind)
    {
        _startup?.Logger.Info($"Shell activation: {kind}.");
        if (kind == ShellActivationKind.ShowWindow)
        {
            ShowWindow();
        }
        else if (_quickOverlay is not null)
        {
            try { await _quickOverlay.HandleActivationAsync(kind); }
            catch (Exception exception) { OnShellError(exception); }
        }
    }

    private void ShowWindow()
    {
        if (_window is null) return;
        _window.AppWindow.Show();
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.Restore();
        }
        _window.Activate();
        _window.OpenToday();
    }

    private async Task<SessionRecord> StartSessionAsync(SessionType type, CancellationToken cancellationToken)
    {
        SessionRecord session = await _completion!.StartAsync(type, cancellationToken);
        _window?.RefreshToday();
        return session;
    }

    private async Task<SessionOutcome> StopSessionAsync(SessionId expectedId, CancellationToken cancellationToken)
    {
        SessionOutcome result = await _completion!.StopAsync(expectedId, cancellationToken);
        if (_quickOverlay is not null) await _quickOverlay.RefreshIfVisibleAsync();
        return result;
    }

    private async Task ShutdownSessionsAsync()
    {
        _quickOverlay?.Dismiss();
        _window?.HideToday();
        if (_startup is null) return;
        _isExiting = true;
        try
        {
            SessionRecoveryResult result = _completion is not null
                ? await _completion.ShutdownAsync()
                : await _startup.Sessions.ShutdownAsync();
            _startup.Logger.Info($"Session shutdown: {result.Kind}.");
        }
        catch { _isExiting = false; _window?.OpenToday(); throw; }
    }

    private Task NotifyCompletedAsync(WindowsShellIntegration integration, SessionRecord session)
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (_window is null || !_window.DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                try
                {
                    integration.NotifyCompleted(session);
                    _startup?.Logger.Info($"Completion notification submitted: {session.Id} {session.Type}; ended={session.EndedAt:O}.");
                }
                finally
                {
                    if (!_isExiting)
                    {
                        _window?.RefreshToday();
                        if (_quickOverlay is not null) await _quickOverlay.RefreshIfVisibleAsync();
                    }
                }
                done.SetResult();
            }
            catch (Exception exception) { done.SetException(exception); }
        })) done.SetException(new InvalidOperationException("Cannot dispatch completion notification to the Windows shell."));
        return done.Task;
    }

    private void OnShellError(Exception exception)
    {
        _startup?.Logger.Error("Windows shell operation failed.", exception);
        ShowWindow();
        FatalError.ReportShellFailure(exception);
    }

    private async void OnExplicitExitRequested()
    {
        try { if (_shell is not null) await _shell.ExitAsync(); }
        catch (Exception exception) { OnShellError(exception); }
    }

    private void OnShellExited()
    {
        _quickOverlay?.Dispose();
        _startup?.Logger.Info("Shell stopped: tray removed; hotkey unregistered.");
        // Unwind the native tray callback before destroying the WinUI window.
        if (_window is null || !_window.DispatcherQueue.TryEnqueue(() =>
        {
            _allowClose = true;
            _window.Close();
        }))
        {
            ReleaseResources();
            Exit();
        }
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        _startup?.Logger.Info("Main window closed. Focus Key shutting down.");
        ReleaseResources();
    }

    private void ReleaseResources()
    {
        _completion?.Dispose();
        _quickOverlay?.Dispose();
        _shell?.Dispose();
        _activationSignal?.Dispose();
        _startup?.Dispose();
        _startup = null;
        _ownership?.Dispose();
        _ownership = null;
    }

    private void OnXamlUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        Record("Unhandled XAML exception.", args.Exception);
    }

    private void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs args)
    {
        Record("Unhandled application exception.", args.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        Record("Unobserved task exception.", args.Exception);
        args.SetObserved();
    }

    private void Record(string message, Exception? exception)
    {
        if (_startup is not null)
        {
            _startup.Logger.Error(message, exception);
            return;
        }

        FatalError.WriteFallbackReport(message, exception);
    }
}
