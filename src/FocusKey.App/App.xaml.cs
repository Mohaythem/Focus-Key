using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Shell;
using FocusKey.Foundation.Overlay;
using FocusKey.Foundation.Settings;
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
    private QuickOverlayWindow? _quickOverlayWindow;
    private CompletionCoordinator? _completion;
    private bool _isExiting;
    private Appearance? _appliedAppearance;

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
                exception => _startup?.Logger.Error("Main-page operation failed.", exception), RefreshSettingsAsync);
            _startup.Appearance.Changed += OnAppearanceChanged;
            _startup.Appearance.ColorsChanged += OnColorsChanged;
            ApplyAppearance(_startup.Appearance.Current);
            ApplyColors();
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
                _window?.RefreshPages();
            };
            _quickOverlay = new QuickOverlayController(CreateQuickOverlay,
                _startup.Sessions.GetActiveAsync, _startup.Sessions.GetDurationsAsync, StartSessionAsync);
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
        try
        {
            if (_startup is not null)
            {
                await _startup.Appearance.RefreshAsync();
                ApplyAppearance(_startup.Appearance.Current);
                ApplyColors();
            }
            if (kind == ShellActivationKind.ShowWindow)
            {
                ShowWindow();
            }
            else if (_quickOverlay is not null)
            {
                await _quickOverlay.HandleActivationAsync(kind);
            }
        }
        catch (Exception exception)
        {
            OnShellError(exception);
        }
    }

    private IQuickOverlayView CreateQuickOverlay()
    {
        var window = new QuickOverlayWindow();
        window.ApplyAppearance(_startup!.Appearance.Current);
        window.ApplyColors(_startup.Appearance.Colors);
        _quickOverlayWindow = window;
        _startup.Logger.Info($"Quick overlay created with appearance {_startup.Appearance.Current}; Work {_startup.Appearance.Colors.Work}, Break {_startup.Appearance.Colors.Break}.");
        return window;
    }

    private void OnAppearanceChanged(Appearance appearance)
    {
        if (_window is null) return;
        if (_window.DispatcherQueue.HasThreadAccess)
        {
            ApplyAppearance(appearance);
            return;
        }
        if (!_window.DispatcherQueue.TryEnqueue(() => ApplyAppearance(appearance)))
            _startup?.Logger.Warning($"Could not dispatch appearance {appearance} to native surfaces.");
    }

    private async Task RefreshSettingsAsync()
    {
        if (_startup is null || _isExiting) return;
        var startup = _startup;
        await Task.Run(() => startup.Appearance.RefreshAsync());
        if (_startup is null || _isExiting) return;
        ApplyAppearance(_startup.Appearance.Current);
        ApplyColors();
        if (_quickOverlay is not null) await _quickOverlay.RefreshIfVisibleAsync();
    }

    private void OnColorsChanged(SessionColors colors)
    {
        if (_window is null) return;
        if (_window.DispatcherQueue.HasThreadAccess) ApplyColors();
        else if (!_window.DispatcherQueue.TryEnqueue(ApplyColors))
            _startup?.Logger.Warning("Could not dispatch session colors to native surfaces.");
    }

    private void ApplyColors()
    {
        if (_startup is null || _isExiting) return;
        var colors = _startup.Appearance.Colors;
        _window?.ApplyColors(colors);
        _quickOverlayWindow?.ApplyColors(colors);
        _startup.Logger.Info($"Session colors applied: Work {colors.Work}, Break {colors.Break}; foregrounds {SessionColors.Foreground(colors.Work)}, {SessionColors.Foreground(colors.Break)}.");
    }

    private void ApplyAppearance(Appearance appearance)
    {
        _window?.ApplyAppearance(appearance);
        _quickOverlayWindow?.ApplyAppearance(appearance);
        if (_appliedAppearance == appearance) return;
        _appliedAppearance = appearance;
        _startup?.Logger.Info($"Appearance applied to native surfaces: {appearance}.");
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
        _window?.RefreshPages();
        return session;
    }

    private async Task<SessionOutcome> StopSessionAsync(SessionId expectedId, CancellationToken cancellationToken)
    {
        SessionOutcome result = await _completion!.StopAsync(expectedId, cancellationToken);
        _window?.RefreshPages();
        if (_quickOverlay is not null) await _quickOverlay.RefreshIfVisibleAsync();
        return result;
    }

    private async Task ShutdownSessionsAsync()
    {
        if (_window is not null) await _window.FlushSettingsAsync();
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
        catch { _isExiting = false; _window?.ResumeSettings(); _window?.OpenToday(); throw; }
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
                        _window?.RefreshPages();
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
        if (_startup is not null) _startup.Appearance.Changed -= OnAppearanceChanged;
        if (_startup is not null) _startup.Appearance.ColorsChanged -= OnColorsChanged;
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
