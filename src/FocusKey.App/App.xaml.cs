using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Shell;
using FocusKey.Shell;
using FocusKey.Startup;
using System.Security.Principal;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace FocusKey;

/// <summary>
/// Application entry point: initialize and recover before showing the placeholder window,
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
            _window = new MainWindow(_startup);
            _window.ExitRequested += OnExplicitExitRequested;
            _window.AppWindow.Closing += OnAppWindowClosing;
            _window.Closed += OnMainWindowClosed;
            _shell = new BackgroundShell(new WindowsShellIntegration(), ShutdownSessionsAsync);
            _shell.ActivationRequested += OnShellActivation;
            _shell.ErrorOccurred += OnShellError;
            _shell.Exited += OnShellExited;
            _shell.Start();
            _activationSignal.Listen(_window.DispatcherQueue, () => _shell.RequestActivation(ShellActivationKind.ShowWindow));
            _startup.Logger.Info("Shell ready: tray added; Shift + F3 registered.");
            _window.Activate();
        }
        catch (Exception exception)
        {
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
        sender.Hide();
        _startup?.Logger.Info("Main window hidden; shell remains running.");
    }

    private void OnShellActivation(ShellActivationKind kind)
    {
        _startup?.Logger.Info($"Shell activation: {kind}.");
        if (kind == ShellActivationKind.ShowWindow)
        {
            ShowWindow();
        }
        // Hotkey activation is deliberately routed without UI. Phase 5 supplies its consumer.
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
    }

    private async Task ShutdownSessionsAsync()
    {
        if (_startup is null) return;
        SessionRecoveryResult result = await _startup.Sessions.ShutdownAsync();
        _startup.Logger.Info($"Session shutdown: {result.Kind}.");
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
