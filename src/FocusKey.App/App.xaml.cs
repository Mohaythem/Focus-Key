using FocusKey.Foundation.Sessions;
using FocusKey.Startup;
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
    private Window? _window;
    private bool _allowClose;
    private bool _shutdownInProgress;

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
            _startup = await FoundationBootstrap.RunAsync();
        }
        catch (Exception exception)
        {
            // A foundation that cannot initialize must say so instead of vanishing.
            FatalError.ReportStartupFailure(exception);
            Environment.Exit(1);
            return;
        }

        _window = new MainWindow(_startup);
        _window.AppWindow.Closing += OnAppWindowClosing;
        _window.Closed += OnMainWindowClosed;
        _window.Activate();
    }

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;
        if (_shutdownInProgress || _startup is null)
        {
            return;
        }

        _shutdownInProgress = true;
        try
        {
            SessionRecoveryResult shutdown = await _startup.Sessions.ShutdownAsync();
            _startup.Logger.Info($"Session shutdown: {shutdown.Kind}.");

            Window window = _window ?? throw new InvalidOperationException("The main window is unavailable.");
            if (!window.DispatcherQueue.TryEnqueue(() =>
            {
                _allowClose = true;
                window.Close();
            }))
            {
                throw new InvalidOperationException("The window dispatcher could not schedule shutdown.");
            }
        }
        catch (Exception exception)
        {
            _shutdownInProgress = false;
            _startup.Logger.Error("Session shutdown failed.", exception);
            FatalError.ReportShutdownFailure(exception);
        }
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        _startup?.Logger.Info("Main window closed. Focus Key shutting down.");
        _startup?.Dispose();
        _startup = null;
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
