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
using Microsoft.UI.Xaml.Media;

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
    private WindowsShellIntegration? _shellIntegration;
    private InstanceActivationSignal? _activationSignal;
    private QuickOverlayController? _quickOverlay;
    private QuickOverlayWindow? _quickOverlayWindow;
    private CompletionCoordinator? _completion;
    private bool _isExiting;
    private Appearance? _appliedAppearance;
    private SoundPlayerService? _sounds;
    private volatile bool _sessionSoundsEnabled = true;
    private volatile bool _startSoundEnabled = true;
    private volatile bool _completionSoundEnabled = true;
    private NativeMethods.SubclassProc? _windowSubclassProc;

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
            bool isStartupLaunch = StartupArguments.IsStartupLaunch(Environment.GetCommandLineArgs());

            // Same user and interactive Windows session, regardless of data-root override.
            // Acquire before bootstrap: a second launch must never recover the owner's session.
            using var identity = WindowsIdentity.GetCurrent();
            string user = identity.User?.Value ?? throw new InvalidOperationException("Cannot identify the current Windows user.");
            string? instanceOverride = Environment.GetEnvironmentVariable("FOCUSKEY_INSTANCE_NAME");
            string instanceName = string.IsNullOrWhiteSpace(instanceOverride) ? $@"Local\FocusKey.Shell.{user}" : instanceOverride;
            _activationSignal = new InstanceActivationSignal(instanceName + ".Activation");
            _ownership = SingleInstanceLease.TryAcquire(instanceName);
            if (_ownership is null)
            {
                if (!isStartupLaunch)
                {
                    _activationSignal.Send();
                }
                _startup?.Dispose();
                Environment.Exit(0);
                return;
            }

            _startup = await FoundationBootstrap.RunAsync();
            var initialSettings = await _startup.Settings.LoadAsync();
            _sessionSoundsEnabled = initialSettings.SessionSoundsEnabled;
            _startSoundEnabled = initialSettings.StartSoundEnabled;
            _completionSoundEnabled = initialSettings.CompletionSoundEnabled;
            _sounds = new SoundPlayerService(
                () => _sessionSoundsEnabled,
                () => _startSoundEnabled,
                () => _completionSoundEnabled);
            _startup.Logger.Info("Single-instance shell ownership acquired.");
            var integration = new WindowsShellIntegration(initialSettings.GlobalShortcut, initialSettings.MainWindowShortcut);
            _shellIntegration = integration;
            ApplyThemePalettes(_startup.Appearance.LightPalette, _startup.Appearance.DarkPalette);
            _window = new MainWindow(_startup, StartSessionAsync, StopSessionAsync,
                exception => _startup?.Logger.Error("Main-page operation failed.", exception), RefreshSettingsAsync,
                integration, PauseSessionAsync, ContinueSessionAsync, _sounds);
            _window.ApplyShortcut(initialSettings.GlobalShortcut);
            _window.GlobalShortcutUpdated += shortcut => _quickOverlayWindow?.ApplyShortcut(shortcut);
            _window.SetActivityCollapsed(initialSettings.ActivityCollapsed);
            _window.ApplyTimeFormat(initialSettings.TimeFormat);
            _window.ApplyUiScale(initialSettings.UiScalePercent, persist: false);
            _startup.Appearance.Changed += OnAppearanceChanged;
            _startup.Appearance.ColorsChanged += OnColorsChanged;
            _startup.Appearance.PalettesChanged += OnPalettesChanged;
            ApplyAppearance(_startup.Appearance.Current);
            ApplyColors();
            _window.OverlayRequested += () => OnShellActivation(ShellActivationKind.Hotkey);
            _window.ExitRequested += OnExplicitExitRequested;
            _window.AppWindow.Closing += OnAppWindowClosing;
            _window.AppWindow.Changed += OnAppWindowChanged;
            _window.Closed += OnMainWindowClosed;
            var mainWindowHwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
            _windowSubclassProc = MainWindowSubclassProc;
            NativeMethods.SetWindowSubclass(mainWindowHwnd, _windowSubclassProc, (UIntPtr)1, IntPtr.Zero);
            _completion = new CompletionCoordinator(_startup.Sessions, session => NotifyCompletedAsync(integration, session),
                exception => _startup?.Logger.Error("Completion coordination failed.", exception));
            integration.ClockChangedOrResumed += _completion.RequestEvaluation;
            integration.ClockChangedOrResumed += () =>
            {
                TimeZoneInfo.ClearCachedData();
                _window?.RefreshPages();
                if (_quickOverlay is not null) _ = _quickOverlay.RefreshIfVisibleAsync();
            };
            _quickOverlay = new QuickOverlayController(CreateQuickOverlay,
                _startup.Sessions.GetActiveAsync, _startup.Sessions.GetDurationsAsync, StartSessionAsync, StopSessionAsync,
                PauseSessionAsync, ContinueSessionAsync);
            _quickOverlay.ErrorOccurred += exception => _startup?.Logger.Error("Quick overlay operation failed.", exception);
            _shell = new BackgroundShell(integration, ShutdownSessionsAsync);
            _shell.ActivationRequested += OnShellActivation;
            _shell.ErrorOccurred += OnShellError;
            _shell.Exited += OnShellExited;
            _shell.Start();
            await _completion.EvaluateAsync();
            _activationSignal.Listen(_window.DispatcherQueue, () => _shell.RequestActivation(ShellActivationKind.ShowWindow));
            _startup.Logger.Info($"Shell ready: tray added; {integration.CurrentShortcut} registered.");
            if (!isStartupLaunch)
            {
                _window.Activate();
                var cmdArgs = Environment.GetCommandLineArgs();
                if (cmdArgs.Any(a => a.Equals("--reports-chart", StringComparison.OrdinalIgnoreCase)))
                {
                    await _window.OpenReportsAsync(scrollToChart: true);
                }
                else if (cmdArgs.Any(a => a.Equals("--reports", StringComparison.OrdinalIgnoreCase)))
                {
                    await _window.OpenReportsAsync();
                }
                else if (cmdArgs.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
                {
                    await _window.OpenSettingsAsync();
                }
                else
                {
                    _window.OpenToday();
                }
            }
            else
            {
                _startup.Logger.Info("Startup launch detected (--startup): starting quietly in system tray.");
                _window.AppWindow.Hide();
            }
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

    private bool _isCloseDialogShowing;

    private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose)
        {
            return;
        }

        args.Cancel = true;

        if (_window is null || _isCloseDialogShowing || !sender.IsVisible)
        {
            return;
        }

        _isCloseDialogShowing = true;
        try
        {
            WindowCloseAction choice = await _window.ShowCloseDecisionDialogAsync();
            switch (choice)
            {
                case WindowCloseAction.Hide:
                    _window.HideToday();
                    sender.Hide();
                    _startup?.Logger.Info("Main window hidden by user choice; shell remains running.");
                    break;

                case WindowCloseAction.Quit:
                    _startup?.Logger.Info("Application quit requested from main window close dialog.");
                    OnExplicitExitRequested();
                    break;

                case WindowCloseAction.Cancel:
                default:
                    _startup?.Logger.Info("Main window close canceled by user.");
                    break;
            }
        }
        catch (Exception exception)
        {
            _startup?.Logger.Error("Error displaying close decision dialog.", exception);
        }
        finally
        {
            _isCloseDialogShowing = false;
        }
    }

    private void OnAppWindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
    {
        if (sender.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized)
        {
            _window?.HideToday();
            sender.Hide();
            _startup?.Logger.Info("Main window minimized to tray.");
        }
    }

    private IntPtr MainWindowSubclassProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if ((uMsg == NativeMethods.WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == NativeMethods.SC_MINIMIZE) ||
            (uMsg == NativeMethods.WM_SIZE && wParam.ToInt64() == NativeMethods.SIZE_MINIMIZED))
        {
            _window?.HideToday();
            _window?.AppWindow.Hide();
            _startup?.Logger.Info("Main window minimized to tray.");
            return IntPtr.Zero;
        }

        if (uMsg == NativeMethods.WM_DESTROY)
        {
            if (_windowSubclassProc is not null)
            {
                NativeMethods.RemoveWindowSubclass(hWnd, _windowSubclassProc, uIdSubclass);
            }
        }

        return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
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
            else if (kind is ShellActivationKind.Hotkey or ShellActivationKind.MiniTimer)
            {
                if (_isExiting || _startup is null) return;
                if (_quickOverlay is not null)
                {
                    await _quickOverlay.HandleActivationAsync(kind);
                }
            }
        }
        catch (Exception exception)
        {
            OnShellError(exception);
        }
    }

    private IQuickOverlayView CreateQuickOverlay()
    {
        var window = new QuickOverlayWindow(
            () => _startup!.Settings.LoadAsync(),
            (x, y) => _startup!.Settings.UpdateOverlayPositionAsync(x, y),
            message => _startup?.Logger.Info(message));
        bool isDark = _startup!.Appearance.Current switch
        {
            Appearance.Dark => true,
            Appearance.Light => false,
            _ => (_window?.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark,
        };
        ThemePalette palette = isDark ? _startup.Appearance.DarkPalette : _startup.Appearance.LightPalette;
        window.ApplyAppearance(_startup.Appearance.Current, palette, _startup.Appearance.Contrast);
        window.ApplyColors(_startup.Appearance.Colors);
        if (_shellIntegration is not null) window.ApplyShortcut(_shellIntegration.CurrentShortcut);
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
        var currentSettings = await startup.Settings.LoadAsync();
        _sessionSoundsEnabled = currentSettings.SessionSoundsEnabled;
        _startSoundEnabled = currentSettings.StartSoundEnabled;
        _completionSoundEnabled = currentSettings.CompletionSoundEnabled;
        _window?.ApplyTimeFormat(currentSettings.TimeFormat);
        _window?.ApplyUiScale(currentSettings.UiScalePercent, persist: false);
        _quickOverlayWindow?.ApplyPosition(currentSettings.OverlayPositionX, currentSettings.OverlayPositionY);
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

    private void OnPalettesChanged(ThemePalette light, ThemePalette dark)
    {
        if (_window is null) return;
        void Update()
        {
            ApplyThemePalettes(light, dark);
            if (_startup is not null) ApplyAppearance(_startup.Appearance.Current);
        }
        if (_window.DispatcherQueue.HasThreadAccess) Update();
        else _window.DispatcherQueue.TryEnqueue(Update);
    }

    internal static void ApplyThemePalettes(ThemePalette light, ThemePalette dark)
    {
        if (Application.Current?.Resources.ThemeDictionaries is { } dicts)
        {
            if (dicts.TryGetValue("Light", out object? lightObj) && lightObj is ResourceDictionary lightDict)
            {
                SetBrush(lightDict, "FkBackground", light.Background);
                SetBrush(lightDict, "FkSidebar", light.Sidebar);
                SetBrush(lightDict, "FkSurface", light.Surface);
                SetBrush(lightDict, "FkSurface2", light.Surface2);
                SetBrush(lightDict, "FkOverlay", light.Surface);
                SetBrush(lightDict, "FkBorder", light.Border);
                SetBrush(lightDict, "CardStrokeColorDefaultBrush", light.Border);
                SetBrush(lightDict, "FkForeground", light.Foreground);
                SetBrush(lightDict, "FkSecondary", light.Secondary);
                SetBrush(lightDict, "FkDim", light.Dim);
                SetBrush(lightDict, "FkAccent", light.Accent);
            }
            if (dicts.TryGetValue("Dark", out object? darkObj) && darkObj is ResourceDictionary darkDict)
            {
                SetBrush(darkDict, "FkBackground", dark.Background);
                SetBrush(darkDict, "FkSidebar", dark.Sidebar);
                SetBrush(darkDict, "FkSurface", dark.Surface);
                SetBrush(darkDict, "FkSurface2", dark.Surface2);
                SetBrush(darkDict, "FkOverlay", dark.Surface);
                SetBrush(darkDict, "FkBorder", dark.Border);
                SetBrush(darkDict, "CardStrokeColorDefaultBrush", dark.Border);
                SetBrush(darkDict, "FkForeground", dark.Foreground);
                SetBrush(darkDict, "FkSecondary", dark.Secondary);
                SetBrush(darkDict, "FkDim", dark.Dim);
                SetBrush(darkDict, "FkAccent", dark.Accent);
            }
        }
    }

    private static void SetBrush(ResourceDictionary dict, string key, HexColor color)
    {
        var winColor = Windows.UI.Color.FromArgb(255,
            Convert.ToByte(color.Value.Substring(1, 2), 16),
            Convert.ToByte(color.Value.Substring(3, 2), 16),
            Convert.ToByte(color.Value.Substring(5, 2), 16));

        if (dict.TryGetValue(key, out object? existing) && existing is SolidColorBrush brush)
        {
            brush.Color = winColor;
        }
        else
        {
            dict[key] = new SolidColorBrush(winColor);
        }
    }

    private void ApplyAppearance(Appearance appearance)
    {
        var light = _startup?.Appearance.LightPalette;
        var dark = _startup?.Appearance.DarkPalette;
        var contrast = _startup?.Appearance.Contrast ?? Contrast.Standard;
        _window?.ApplyAppearance(appearance, light, dark, contrast);
        bool isDark = appearance switch
        {
            Appearance.Dark => true,
            Appearance.Light => false,
            _ => (_window?.Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark,
        };
        ThemePalette? activePalette = isDark ? dark : light;
        _quickOverlayWindow?.ApplyAppearance(appearance, activePalette, contrast);
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
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(_window);
        NativeMethods.ForceForeground(hwnd);
        _window.OpenToday();
    }

    private async Task<SessionRecord> StartSessionAsync(SessionType type, CancellationToken cancellationToken)
    {
        SessionRecord session = await _completion!.StartAsync(type, cancellationToken);
        _sounds?.PlayStartTick();
        _window?.RefreshPages();
        if (_quickOverlay is not null) await _quickOverlay.RefreshIfVisibleAsync();
        return session;
    }

    private async Task<SessionOutcome> StopSessionAsync(SessionId expectedId, CancellationToken cancellationToken)
    {
        SessionOutcome result = await _completion!.StopAsync(expectedId, cancellationToken);
        if (result.Kind == SessionOutcomeKind.Stopped)
        {
            _sounds?.PlayStop();
        }
        _window?.RefreshPages();
        if (_quickOverlay is not null) await _quickOverlay.RefreshIfVisibleAsync();
        return result;
    }

    private async Task<SessionOutcome> PauseSessionAsync(SessionId expectedId, CancellationToken cancellationToken)
    {
        SessionOutcome result = await _completion!.PauseAsync(expectedId, cancellationToken);
        if (result.Kind == SessionOutcomeKind.Paused)
        {
            _sounds?.PlayPause();
        }
        _window?.RefreshPages();
        if (_quickOverlay is not null) await _quickOverlay.RefreshIfVisibleAsync();
        return result;
    }

    private async Task<SessionOutcome> ContinueSessionAsync(SessionId expectedId, CancellationToken cancellationToken)
    {
        SessionOutcome result = await _completion!.ContinueAsync(expectedId, cancellationToken);
        if (result.Kind == SessionOutcomeKind.Continued)
        {
            _sounds?.PlayContinue();
        }
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
                    _sounds?.PlayNaturalCompletionBell();
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
        if (_startup is not null) _startup.Appearance.PalettesChanged -= OnPalettesChanged;
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
