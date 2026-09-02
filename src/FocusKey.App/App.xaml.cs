using FocusKey.Startup;
using Microsoft.UI.Xaml;

namespace FocusKey;

/// <summary>
/// Application entry point. Phase 0 responsibility: bring up the foundation, show the
/// placeholder window, record failures, and shut down cleanly.
/// </summary>
public partial class App : Application
{
    private StartupContext? _startup;
    private Window? _window;

    public App()
    {
        InitializeComponent();

        UnhandledException += OnXamlUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            _startup = FoundationBootstrap.Run();
        }
        catch (Exception exception)
        {
            // A foundation that cannot initialize must say so instead of vanishing.
            FatalError.ReportStartupFailure(exception);
            Environment.Exit(1);
            return;
        }

        _window = new MainWindow(_startup);
        _window.Closed += OnMainWindowClosed;
        _window.Activate();
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
