using System.Globalization;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Today;
using FocusKey.Startup;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using Windows.UI.ViewManagement;
using FocusKey.Shell;
using FocusKey.Foundation.Shell;
using WinRT.Interop;
using Microsoft.UI.Xaml.Media.Animation;
using VirtualKey = Windows.System.VirtualKey;
using Microsoft.UI.Xaml.Input;

namespace FocusKey;

/// <summary>Functional Today, Reports and Settings surfaces.</summary>
public sealed partial class MainWindow : Window
{
    private readonly TodayController _today;
    private readonly ReportsView _reports;
    private readonly SettingsView _settings;
    private readonly DispatcherQueueTimer _displayTimer;
    private bool _visible;
    private bool _hasRenderedRunning;
    private bool _lastHasRunning;
    private SessionColors _colors = SessionColors.From(ApplicationSettings.Default);
    private readonly SettingsService? _settingsService;
    private readonly Action<Exception>? _startupReport;
    private bool _activityCollapsed = true;
    private bool _isWorkHovered;
    private bool _isBreakHovered;
    private SessionType _selectedIdleType = SessionType.Work;
    private int _uiScalePercent = UiScaleLevels.DefaultPercent;
    private DispatcherQueueTimer? _scaleHudTimer;
    private Storyboard? _scaleHudFadeOut;

    public int UiScalePercent => _uiScalePercent;

    internal void SetActivityCollapsed(bool collapsed)
    {
        _activityCollapsed = collapsed;
    }

    internal void ApplyColors(SessionColors colors)
    {
        _colors = colors;
        _reports.ApplyColors(colors);
        RenderRunning();
    }
    internal event Action? OverlayRequested;
    private void OnOverlayClick(object sender, RoutedEventArgs args)
    {
        CloseNavDrawer();
        OverlayRequested?.Invoke();
    }
    internal event Action? ExitRequested;
    internal event Action<GlobalShortcut>? GlobalShortcutUpdated;

    internal void ApplyShortcut(GlobalShortcut shortcut)
    {
        string text = shortcut.ToString();
        if (OverlayNavButton is not null)
            ToolTipService.SetToolTip(OverlayNavButton, $"Quick Overlay ({text})");
    }

    private Appearance _appearance = Appearance.System;
    private Contrast _contrast = Contrast.Standard;
    private ThemePalette? _lightPalette;
    private ThemePalette? _darkPalette;
    private TimeFormat _timeFormat = TimeFormat.TwentyFourHour;

    internal void ApplyTimeFormat(TimeFormat format)
    {
        _timeFormat = format;
        if (_today is not null) Render();
    }

    public void ApplyUiScale(int percent, bool persist = true)
    {
        if (DispatcherQueue is not null && !DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(() => ApplyUiScale(percent, persist));
            return;
        }

        int clamped = UiScaleLevels.IsValid(percent)
            ? percent
            : (percent < UiScaleLevels.MinPercent ? UiScaleLevels.MinPercent : (percent > UiScaleLevels.MaxPercent ? UiScaleLevels.MaxPercent : UiScaleLevels.DefaultPercent));

        _uiScalePercent = clamped;

        ShowScaleHud(clamped);
        UpdateSidebarDimensions(MainSurface?.ActualWidth > 0 ? MainSurface.ActualWidth : 880);
        UpdatePageWidths();
        _reports?.ApplyUiScale(clamped);
        _settings?.ApplyUiScale(clamped);

        if (persist)
        {
            PersistUiScale(clamped);
        }
    }

    private async void PersistUiScale(int percent)
    {
        try
        {
            if (_settings is not null)
            {
                await _settings.UpdateUiScaleAsync(percent);
            }
            else if (_settingsService is not null)
            {
                await _settingsService.UpdateUiScalePercentAsync(percent);
            }
        }
        catch (Exception ex)
        {
            _startupReport?.Invoke(ex);
        }
    }

    private void ShowScaleHud(int scalePercent)
    {
        if (ScaleHudOverlay is null || ScaleHudText is null) return;

        ScaleHudText.Text = $"UI scale: {scalePercent}%";

        _scaleHudFadeOut?.Stop();
        _scaleHudTimer?.Stop();

        ScaleHudOverlay.Visibility = Visibility.Visible;
        ScaleHudOverlay.Opacity = 1.0;

        if (_scaleHudTimer is null)
        {
            _scaleHudTimer = DispatcherQueue.CreateTimer();
            _scaleHudTimer.Interval = TimeSpan.FromMilliseconds(1500);
            _scaleHudTimer.IsRepeating = false;
            _scaleHudTimer.Tick += (_, _) => StartHudFadeOut();
        }

        _scaleHudTimer.Start();
    }

    private void StartHudFadeOut()
    {
        if (ScaleHudOverlay is null || ScaleHudOverlay.Visibility != Visibility.Visible) return;

        var animation = new DoubleAnimation
        {
            From = ScaleHudOverlay.Opacity,
            To = 0.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(250))
        };
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, ScaleHudOverlay);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Completed += (_, _) =>
        {
            ScaleHudOverlay.Visibility = Visibility.Collapsed;
        };
        _scaleHudFadeOut = storyboard;
        storyboard.Begin();
    }

    internal void ApplyAppearance(Appearance appearance, ThemePalette? lightPalette = null, ThemePalette? darkPalette = null, Contrast contrast = Contrast.Standard)
    {
        _appearance = appearance;
        _contrast = contrast;
        if (lightPalette is not null) _lightPalette = lightPalette;
        if (darkPalette is not null) _darkPalette = darkPalette;
        UpdateAppearance();
    }

    private void UpdateAppearance()
    {
        bool isDark = _appearance switch
        {
            Appearance.Dark => true,
            Appearance.Light => false,
            _ => MainSurface.ActualTheme == ElementTheme.Dark,
        };

        ThemePalette palette = isDark
            ? (_darkPalette ?? ThemePalette.DefaultDark)
            : (_lightPalette ?? ThemePalette.DefaultLight);

        MainSurface.Background = SessionColorBrush.Create(palette.Background);
        NavGrid.Background = SessionColorBrush.Create(palette.Sidebar);
        NavGrid.BorderBrush = SessionColorBrush.Create(palette.Border);
        if (NavDrawerPane is not null)
        {
            NavDrawerPane.Background = SessionColorBrush.Create(palette.Sidebar);
            NavDrawerPane.BorderBrush = SessionColorBrush.Create(palette.Border);
        }

        WindowAppearance.ApplyTitleBar(AppWindow, _appearance == Appearance.System ? null : palette);

        var targetTheme = WindowAppearance.ToElementTheme(_appearance);
        MainSurface.RequestedTheme = targetTheme;

        _reports?.RefreshVisuals(_contrast);
        _settings?.RefreshVisuals(_contrast);
        if (_today is not null) Render();
    }

    internal MainWindow(StartupContext startup,
        Func<SessionType, CancellationToken, Task<SessionRecord>> start,
        Func<SessionId, CancellationToken, Task<SessionOutcome>> stop, Action<Exception> report,
        Func<Task> refreshSettings,
        WindowsShellIntegration shellIntegration,
        Func<SessionId, CancellationToken, Task<SessionOutcome>>? pause = null,
        Func<SessionId, CancellationToken, Task<SessionOutcome>>? @continue = null,
        FocusKey.Shell.ISoundPlayer? soundPlayer = null)
    {
        InitializeComponent();
        _settingsService = startup.Settings;
        _startupReport = report;
        _reports = new ReportsView(startup.Reports, report);
        ReportsHost.Content = _reports;
        _settings = new SettingsView(
            startup.Settings,
            startup.History,
            startup.WindowsStartup,
            refreshSettings,
            () => _reports.RefreshAsync(),
            () => WindowNative.GetWindowHandle(this),
            report,
            (GlobalShortcut sc, out string? err) => shellIntegration.TryUpdateHotkey(sc, out err),
            (GlobalShortcut sc, out string? err) => shellIntegration.TryUpdateMainWindowHotkey(sc, out err),
            sc =>
            {
                ApplyShortcut(sc);
                GlobalShortcutUpdated?.Invoke(sc);
            },
            sc =>
            {
                // Main window shortcut updated
            },
            () => soundPlayer?.PreviewStartTick(),
            () => soundPlayer?.PreviewCompletionBell(),
            percent => ApplyUiScale(percent, persist: false));
        SettingsHost.Content = _settings;
        var hwnd = WindowNative.GetWindowHandle(this);
        startup.Logger.Info($"Main window HWND: {hwnd}.");
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1.0;
        var hIcon = NativeMethods.LoadIcon(NativeMethods.GetModuleHandle(null), (IntPtr)NativeMethods.IDI_APPLICATION);
        AppWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon));
        AppWindow.Resize(new SizeInt32((int)Math.Ceiling(880 * scale), (int)Math.Ceiling(660 * scale)));
        MainSurface.ActualThemeChanged += (_, _) => { if (_appearance == Appearance.System) UpdateAppearance(); };
        MainSurface.SizeChanged += (_, _) =>
        {
            double w = MainSurface.ActualWidth;
            double factor = UiScaleLevels.ToFactor(_uiScalePercent);
            double effectiveWidth = UiScaleLevels.CalculateEffectiveWidth(w, factor);
            bool narrow = effectiveWidth < TodayAdaptiveLayoutHelper.BreakpointNavCompact;
            UpdateSidebarDimensions(w);
            PageContent.Padding = new Thickness((narrow ? 20 : 32) * factor, 24 * factor, (narrow ? 20 : 32) * factor, 32 * factor);
            UpdatePageWidths();
        };
        PageScrollViewer.SizeChanged += (_, _) => UpdatePageWidths();
        _today = new TodayController(startup.Today.ReadAsync, start, stop, report, pause, @continue);
        _today.Changed += Render;
        _displayTimer = DispatcherQueue.CreateTimer();
        _displayTimer.IsRepeating = false;
        _displayTimer.Tick += OnDisplayTick;
        Closed += (_, _) => { _visible = false; _displayTimer.Stop(); _scaleHudTimer?.Stop(); _scaleHudFadeOut?.Stop(); _today.Dispose(); _reports.Dispose(); };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated)
            {
                if (_today.Page == MainPage.Today) FocusTodayContext();
            }
        };
        MainSurface.Loaded += (_, _) =>
        {
            if (_today.Page == MainPage.Today) FocusTodayContext();
        };
        Render();
        UpdateSidebarDimensions(MainSurface.ActualWidth > 0 ? MainSurface.ActualWidth : 880);
        UpdatePageWidths();
        FocusTodayContext();
        startup.Logger.Info("Today main window created.");
    }

    internal async void OpenToday()
    {
        _settings.CommitPendingDurations();
        _visible = true;
        _reports.Hide();
        CloseNavDrawer();
        UpdatePageWidths();
        await _today.OpenAsync();
        FocusTodayContext();
    }

    internal void HideToday()
    {
        _settings.CommitPendingDurations();
        _visible = false;
        _displayTimer.Stop();
        CloseNavDrawer();
        _today.Hide();
        _reports.Hide();
    }

    internal void RefreshPages() => DispatcherQueue.TryEnqueue(async () =>
    {
        await _today.RefreshAsync();
        await _reports.RefreshAsync();
    });

    internal Task FlushSettingsAsync()
    {
        _settings.IsEnabled = false;
        return _settings.FlushAsync();
    }
    internal void ResumeSettings() => _settings.IsEnabled = true;

    internal async Task OpenReportsAsync(bool scrollToChart = false)
    {
        _settings.CommitPendingDurations();
        CloseNavDrawer();
        UpdatePageWidths();
        await _today.NavigateAsync(MainPage.Reports);
        await _reports.OpenAsync();
        if (scrollToChart)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(600);
                DispatcherQueue.TryEnqueue(() =>
                {
                    PageScrollViewer?.UpdateLayout();
                    PageScrollViewer?.ChangeView(null, 420, null, false);
                });
            });
        }
    }

    private async void OnTodayClick(object sender, RoutedEventArgs args)
    {
        CloseNavDrawer();
        _settings.CommitPendingDurations();
        _reports.Hide();
        UpdatePageWidths();
        await _today.NavigateAsync(MainPage.Today);
        FocusTodayContext();
    }

    private async void OnReportsClick(object sender, RoutedEventArgs args)
    {
        CloseNavDrawer();
        UpdatePageWidths();
        await OpenReportsAsync();
    }

    internal async Task OpenSettingsAsync()
    {
        CloseNavDrawer();
        _settings.CommitPendingDurations();
        _reports.Hide();
        await _today.NavigateAsync(MainPage.Settings);
        await _settings.OpenAsync();
    }

    private async void OnSettingsClick(object sender, RoutedEventArgs args)
    {
        await OpenSettingsAsync();
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await _today.RefreshAsync();

    private void OnHamburgerClick(object sender, RoutedEventArgs args)
    {
        if (NavDrawerOverlay is not null)
        {
            NavDrawerOverlay.Visibility = Visibility.Visible;
            DrawerTodayNav?.Focus(FocusState.Keyboard);
        }
    }

    private void OnNavDrawerBackdropTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e)
    {
        CloseNavDrawer();
    }

    private void OnNavDrawerCloseClick(object sender, RoutedEventArgs args)
    {
        CloseNavDrawer();
    }

    private void CloseNavDrawer()
    {
        if (NavDrawerOverlay is not null && NavDrawerOverlay.Visibility == Visibility.Visible)
        {
            NavDrawerOverlay.Visibility = Visibility.Collapsed;
            if (HamburgerButton is not null && HamburgerButton.Visibility == Visibility.Visible)
            {
                HamburgerButton.Focus(FocusState.Keyboard);
            }
        }
    }

    private void OnNavDrawerOverlayPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CloseNavDrawer();
        }
    }

    private void OnNavGridPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var focused = FocusManager.GetFocusedElement(MainSurface.XamlRoot);
        Control?[] items = [TodayNav, ReportsNav, OverlayNavButton, SettingsNav, ExitButton];
        int currentIndex = -1;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] is not null && ReferenceEquals(focused, items[i]))
            {
                currentIndex = i;
                break;
            }
        }

        if (e.Key == VirtualKey.Down)
        {
            e.Handled = true;
            if (currentIndex == -1)
            {
                TodayNav?.Focus(FocusState.Keyboard);
            }
            else if (currentIndex < items.Length - 1)
            {
                items[currentIndex + 1]?.Focus(FocusState.Keyboard);
            }
        }
        else if (e.Key == VirtualKey.Up)
        {
            e.Handled = true;
            if (currentIndex == -1)
            {
                ExitButton?.Focus(FocusState.Keyboard);
            }
            else if (currentIndex > 0)
            {
                items[currentIndex - 1]?.Focus(FocusState.Keyboard);
            }
        }
        else if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            if (e.KeyStatus.WasKeyDown) return;
            e.Handled = true;
            if (currentIndex != -1 && items[currentIndex] is { } target)
            {
                ActivateNavItem(target);
            }
        }
        else if (e.Key == VirtualKey.Right)
        {
            e.Handled = true;
            if (_today.Page == MainPage.Today)
            {
                FocusTodayContext();
            }
        }
    }

    private void ActivateNavItem(Control item)
    {
        if (ReferenceEquals(item, TodayNav)) OnTodayClick(TodayNav, new RoutedEventArgs());
        else if (ReferenceEquals(item, ReportsNav)) OnReportsClick(ReportsNav, new RoutedEventArgs());
        else if (ReferenceEquals(item, OverlayNavButton)) OnOverlayClick(OverlayNavButton, new RoutedEventArgs());
        else if (ReferenceEquals(item, SettingsNav)) OnSettingsClick(SettingsNav, new RoutedEventArgs());
        else if (ReferenceEquals(item, ExitButton)) OnExitClick(ExitButton, new RoutedEventArgs());
    }

    private void OnNavDrawerPanePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            CloseNavDrawer();
            return;
        }

        var focused = FocusManager.GetFocusedElement(MainSurface.XamlRoot);
        Control?[] items = [DrawerCloseButton, DrawerTodayNav, DrawerReportsNav, DrawerOverlayButton, DrawerSettingsNav, DrawerExitButton];
        int currentIndex = -1;
        for (int i = 0; i < items.Length; i++)
        {
            if (items[i] is not null && ReferenceEquals(focused, items[i]))
            {
                currentIndex = i;
                break;
            }
        }

        if (e.Key == VirtualKey.Down)
        {
            e.Handled = true;
            if (currentIndex == -1)
            {
                DrawerTodayNav?.Focus(FocusState.Keyboard);
            }
            else if (currentIndex < items.Length - 1)
            {
                items[currentIndex + 1]?.Focus(FocusState.Keyboard);
            }
        }
        else if (e.Key == VirtualKey.Up)
        {
            e.Handled = true;
            if (currentIndex == -1)
            {
                DrawerExitButton?.Focus(FocusState.Keyboard);
            }
            else if (currentIndex > 0)
            {
                items[currentIndex - 1]?.Focus(FocusState.Keyboard);
            }
        }
        else if (e.Key is VirtualKey.Enter or VirtualKey.Space)
        {
            if (e.KeyStatus.WasKeyDown) return;
            e.Handled = true;
            if (currentIndex != -1 && items[currentIndex] is { } target)
            {
                ActivateDrawerNavItem(target);
            }
        }
    }

    private void ActivateDrawerNavItem(Control item)
    {
        if (ReferenceEquals(item, DrawerCloseButton)) CloseNavDrawer();
        else if (ReferenceEquals(item, DrawerTodayNav)) OnTodayClick(DrawerTodayNav, new RoutedEventArgs());
        else if (ReferenceEquals(item, DrawerReportsNav)) OnReportsClick(DrawerReportsNav, new RoutedEventArgs());
        else if (ReferenceEquals(item, DrawerOverlayButton)) OnOverlayClick(DrawerOverlayButton, new RoutedEventArgs());
        else if (ReferenceEquals(item, DrawerSettingsNav)) OnSettingsClick(DrawerSettingsNav, new RoutedEventArgs());
        else if (ReferenceEquals(item, DrawerExitButton)) OnExitClick(DrawerExitButton, new RoutedEventArgs());
    }

    private void OnMainSurfacePreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var focused = FocusManager.GetFocusedElement(MainSurface.XamlRoot);

        if (IsTextInput(focused) || IsTextInput(e.OriginalSource))
        {
            return;
        }

        // Global UI scaling / zoom shortcuts across all pages
        bool isCtrl = (NativeMethods.GetKeyState(0x11) & 0x8000) != 0;
        bool isAlt = (NativeMethods.GetKeyState(0x12) & 0x8000) != 0;
        bool isWin = (NativeMethods.GetKeyState(0x5B) & 0x8000) != 0 || (NativeMethods.GetKeyState(0x5C) & 0x8000) != 0;

        if (isCtrl && !isAlt && !isWin)
        {
            int rawKey = (int)e.Key;
            int rawOriginalKey = (int)e.OriginalKey;

            bool isZoomIn = rawKey == 187 || rawOriginalKey == 187 ||
                            rawKey == 0xBB || rawOriginalKey == 0xBB ||
                            e.Key == VirtualKey.Add || rawKey == 0x6B || rawOriginalKey == 0x6B;

            bool isZoomOut = rawKey == 189 || rawOriginalKey == 189 ||
                             rawKey == 0xBD || rawOriginalKey == 0xBD ||
                             e.Key == VirtualKey.Subtract || rawKey == 0x6D || rawOriginalKey == 0x6D;

            bool isReset = e.Key == VirtualKey.Number0 || e.Key == VirtualKey.NumberPad0 ||
                           rawKey == 0x30 || rawKey == 0x60 ||
                           rawOriginalKey == 0x30 || rawOriginalKey == 0x60;

            if (isZoomIn)
            {
                e.Handled = true;
                int next = UiScaleLevels.NextLevel(_uiScalePercent);
                ApplyUiScale(next, persist: true);
                return;
            }
            if (isZoomOut)
            {
                e.Handled = true;
                int prev = UiScaleLevels.PreviousLevel(_uiScalePercent);
                ApplyUiScale(prev, persist: true);
                return;
            }
            if (isReset)
            {
                e.Handled = true;
                ApplyUiScale(UiScaleLevels.DefaultPercent, persist: true);
                return;
            }
        }

        if (NavDrawerOverlay is not null && NavDrawerOverlay.Visibility == Visibility.Visible)
        {
            return;
        }

        if (IsDescendantOf(focused as DependencyObject, NavGrid))
        {
            return;
        }

        if (_today.Page != MainPage.Today)
        {
            return;
        }

        if (_today.Snapshot?.Active is not { } active)
        {
            switch (e.Key)
            {
                case VirtualKey.Left:
                    e.Handled = true;
                    SelectIdleType(SessionType.Work);
                    WorkChoiceCard?.Focus(FocusState.Keyboard);
                    break;
                case VirtualKey.Right:
                    e.Handled = true;
                    SelectIdleType(SessionType.Break);
                    BreakChoiceCard?.Focus(FocusState.Keyboard);
                    break;
                case VirtualKey.Enter:
                case VirtualKey.Space:
                    if (e.KeyStatus.WasKeyDown) break;
                    e.Handled = true;
                    _ = _today.StartAsync(_selectedIdleType);
                    break;
            }
        }
        else if (active.Status == SessionStatus.Running)
        {
            switch (e.Key)
            {
                case VirtualKey.Enter:
                case VirtualKey.Space:
                    if (e.KeyStatus.WasKeyDown) break;
                    e.Handled = true;
                    _ = _today.PauseAsync();
                    break;
            }
        }
        else if (active.Status == SessionStatus.Paused)
        {
            switch (e.Key)
            {
                case VirtualKey.Left:
                    e.Handled = true;
                    ContinueButton?.Focus(FocusState.Keyboard);
                    break;
                case VirtualKey.Right:
                    e.Handled = true;
                    StartNewButton?.Focus(FocusState.Keyboard);
                    break;
                case VirtualKey.Enter:
                case VirtualKey.Space:
                    if (e.KeyStatus.WasKeyDown) break;
                    e.Handled = true;
                    if (ReferenceEquals(focused, StartNewButton))
                    {
                        _ = _today.StartNewAsync();
                    }
                    else
                    {
                        _ = _today.ContinueAsync();
                    }
                    break;
            }
        }
    }

    private static bool IsDescendantOf(DependencyObject? element, DependencyObject parent)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, parent)) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private static bool IsTextInput(object? element)
    {
        if (element is TextBox or PasswordBox or RichEditBox or AutoSuggestBox)
            return true;

        if (element is DependencyObject d)
        {
            DependencyObject? current = d;
            while (current is not null)
            {
                if (current is TextBox or PasswordBox or RichEditBox or AutoSuggestBox)
                    return true;

                current = VisualTreeHelper.GetParent(current);
            }
        }

        return false;
    }

    private void FocusTodayContext()
    {
        if (!_visible || _today.Page != MainPage.Today) return;

        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_visible || _today.Page != MainPage.Today) return;

            if (_today.Snapshot?.Active is { } active)
            {
                if (active.Status == SessionStatus.Paused)
                {
                    ContinueButton?.Focus(FocusState.Keyboard);
                }
                else
                {
                    PauseButton?.Focus(FocusState.Keyboard);
                }
            }
            else
            {
                if (_selectedIdleType == SessionType.Work)
                {
                    WorkChoiceCard?.Focus(FocusState.Keyboard);
                }
                else
                {
                    BreakChoiceCard?.Focus(FocusState.Keyboard);
                }
            }
        });
    }

    private void SelectIdleType(SessionType type)
    {
        _selectedIdleType = type;
        PaintLauncherCards(IsCurrentThemeDark());
    }

    private void OnWorkCardClick(object sender, RoutedEventArgs args)
    {
        SelectIdleType(SessionType.Work);
        WorkChoiceCard?.Focus(FocusState.Keyboard);
    }

    private void OnBreakCardClick(object sender, RoutedEventArgs args)
    {
        SelectIdleType(SessionType.Break);
        BreakChoiceCard?.Focus(FocusState.Keyboard);
    }

    private async void OnStartIdleClick(object sender, RoutedEventArgs args) => await _today.StartAsync(_selectedIdleType);
    private async void OnPauseClick(object sender, RoutedEventArgs args) => await _today.PauseAsync();
    private async void OnContinueClick(object sender, RoutedEventArgs args) => await _today.ContinueAsync();
    private async void OnStartNewClick(object sender, RoutedEventArgs args) => await _today.StartNewAsync();
    private async void OnStopClick(object sender, RoutedEventArgs args) => await _today.StopAsync();
    private void OnExitClick(object sender, RoutedEventArgs args)
    {
        CloseNavDrawer();
        ExitRequested?.Invoke();
    }

    private bool _isShowingCloseDialog;

    internal async Task<WindowCloseAction> ShowCloseDecisionDialogAsync()
    {
        if (_isShowingCloseDialog) return WindowCloseAction.Cancel;
        if (this.Content?.XamlRoot is null) return WindowCloseAction.Hide;

        _isShowingCloseDialog = true;
        try
        {
            ElementTheme targetTheme = _appearance switch
            {
                Appearance.Dark => ElementTheme.Dark,
                Appearance.Light => ElementTheme.Light,
                _ => (this.Content as FrameworkElement)?.ActualTheme ?? ElementTheme.Default
            };

            var dialog = new ContentDialog
            {
                Title = "Close Focus Key?",
                Content = "Hide Focus Key to keep it running in the system tray, or quit the app completely.",
                PrimaryButtonText = "Hide Focus Key",
                SecondaryButtonText = "Quit Focus Key",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                RequestedTheme = targetTheme,
                XamlRoot = this.Content.XamlRoot
            };

            ContentDialogResult result = await dialog.ShowAsync();
            return result switch
            {
                ContentDialogResult.Primary => WindowCloseAction.Hide,
                ContentDialogResult.Secondary => WindowCloseAction.Quit,
                _ => WindowCloseAction.Cancel
            };
        }
        catch (Exception exception)
        {
            _startupReport?.Invoke(exception);
            return WindowCloseAction.Cancel;
        }
        finally
        {
            _isShowingCloseDialog = false;
        }
    }

    private void UpdatePageWidths()
    {
        if (PageScrollViewer is null || PageContent is null) return;
        if (PageScrollViewer.ActualWidth > 0)
        {
            double factor = UiScaleLevels.ToFactor(_uiScalePercent);
            double w = MainSurface?.ActualWidth ?? PageScrollViewer.ActualWidth;
            double effectiveWindow = UiScaleLevels.CalculateEffectiveWidth(w, factor);
            bool narrow = effectiveWindow < TodayAdaptiveLayoutHelper.BreakpointNavCompact;
            PageContent.Padding = new Thickness((narrow ? 20 : 32) * factor, 24 * factor, (narrow ? 20 : 32) * factor, 32 * factor);
            PageContent.Width = PageScrollViewer.ActualWidth;
            double available = PageScrollViewer.ActualWidth - PageContent.Padding.Left - PageContent.Padding.Right;
            if (available > 0)
            {
                double effectiveAvailable = UiScaleLevels.CalculateEffectiveWidth(available, factor);
                if (TodayPanel is not null)
                {
                    TodayPanel.Width = Math.Min(TodayAdaptiveLayoutHelper.MaxContentWidth * factor, available);

                    var composition = TodayAdaptiveLayoutHelper.ResolveTodayComposition(effectiveAvailable);
                    bool isTwoColumn = composition == TodayCompositionMode.TwoColumn;

                    if (LeftColumnDef is not null && RightColumnDef is not null &&
                        TopRowDef is not null && BottomRowDef is not null &&
                        LeftStackPanel is not null && ActivityCard is not null)
                    {
                        if (isTwoColumn)
                        {
                            LeftColumnDef.Width = new GridLength(62, GridUnitType.Star);
                            RightColumnDef.Width = new GridLength(38, GridUnitType.Star);
                            TopRowDef.Height = GridLength.Auto;
                            BottomRowDef.Height = new GridLength(0);

                            Grid.SetColumn(LeftStackPanel, 0);
                            Grid.SetRow(LeftStackPanel, 0);
                            Grid.SetColumnSpan(LeftStackPanel, 1);

                            Grid.SetColumn(ActivityCard, 1);
                            Grid.SetRow(ActivityCard, 0);
                            Grid.SetColumnSpan(ActivityCard, 1);

                            ActivityCard.MinHeight = Math.Round(490 * factor);
                            if (ActivityScrollViewer is not null)
                                ActivityScrollViewer.MaxHeight = Math.Round(420 * factor);

                            double timerSize = Math.Round(72 * factor);
                            if (RunningText is not null) RunningText.FontSize = timerSize;
                            if (IdleDurationText is not null) IdleDurationText.FontSize = timerSize;
                        }
                        else
                        {
                            LeftColumnDef.Width = new GridLength(1, GridUnitType.Star);
                            RightColumnDef.Width = new GridLength(0);
                            TopRowDef.Height = GridLength.Auto;
                            BottomRowDef.Height = GridLength.Auto;

                            Grid.SetColumn(LeftStackPanel, 0);
                            Grid.SetRow(LeftStackPanel, 0);
                            Grid.SetColumnSpan(LeftStackPanel, 2);

                            Grid.SetColumn(ActivityCard, 0);
                            Grid.SetRow(ActivityCard, 1);
                            Grid.SetColumnSpan(ActivityCard, 2);

                            ActivityCard.MinHeight = 0;
                            if (ActivityScrollViewer is not null)
                                ActivityScrollViewer.MaxHeight = double.PositiveInfinity;

                            double timerSize = Math.Round((effectiveAvailable < 500 ? 52 : (effectiveAvailable < 650 ? 60 : 68)) * factor);
                            if (RunningText is not null) RunningText.FontSize = timerSize;
                            if (IdleDurationText is not null) IdleDurationText.FontSize = timerSize;
                        }

                        if (SessionHeroCard is not null)
                        {
                            SessionHeroCard.MinHeight = Math.Round(310 * factor);
                            SessionHeroCard.Padding = new Thickness(28 * factor, 20 * factor, 28 * factor, 20 * factor);
                        }
                        if (TodaySummaryCard is not null)
                        {
                            TodaySummaryCard.Padding = new Thickness(24 * factor, 18 * factor, 24 * factor, 18 * factor);
                        }
                        if (ActivityCard is not null)
                        {
                            ActivityCard.Padding = new Thickness(22 * factor, 18 * factor, 22 * factor, 18 * factor);
                        }
                        if (StartIdleButton is not null)
                        {
                            StartIdleButton.Height = Math.Round(36 * factor);
                            StartIdleButton.MinWidth = Math.Round(130 * factor);
                            StartIdleButton.FontSize = 13.0 * factor;
                        }
                        if (PauseButton is not null)
                        {
                            PauseButton.Height = Math.Round(36 * factor);
                            PauseButton.MinWidth = Math.Round(120 * factor);
                            PauseButton.FontSize = 13.0 * factor;
                        }
                        if (ContinueButton is not null)
                        {
                            ContinueButton.Height = Math.Round(36 * factor);
                            ContinueButton.MinWidth = Math.Round(120 * factor);
                            ContinueButton.FontSize = 13.0 * factor;
                        }
                        if (StartNewButton is not null)
                        {
                            StartNewButton.Height = Math.Round(36 * factor);
                            StartNewButton.MinWidth = Math.Round(110 * factor);
                            StartNewButton.FontSize = 13.0 * factor;
                        }
                        if (WorkChoiceCard is not null)
                        {
                            WorkChoiceCard.Padding = new Thickness(16 * factor, 7 * factor, 16 * factor, 7 * factor);
                        }
                        if (BreakChoiceCard is not null)
                        {
                            BreakChoiceCard.Padding = new Thickness(16 * factor, 7 * factor, 16 * factor, 7 * factor);
                        }
                        if (WorkChoiceMode is not null) WorkChoiceMode.FontSize = 12.5 * factor;
                        if (WorkChoiceDuration is not null) WorkChoiceDuration.FontSize = 12.0 * factor;
                        if (BreakChoiceMode is not null) BreakChoiceMode.FontSize = 12.5 * factor;
                        if (BreakChoiceDuration is not null) BreakChoiceDuration.FontSize = 12.0 * factor;
                        if (FocusValue is not null) FocusValue.FontSize = Math.Round(24 * factor);
                        if (WorkValue is not null) WorkValue.FontSize = Math.Round(24 * factor);
                        if (BreakValue is not null) BreakValue.FontSize = Math.Round(24 * factor);
                        if (CompletionValue is not null) CompletionValue.FontSize = Math.Round(24 * factor);
                    }
                }
                if (ReportsHost is not null)
                    ReportsHost.Width = Math.Min(1220 * factor, available);
            }
        }
    }

    private bool IsCurrentThemeDark() => _appearance switch
    {
        Appearance.Dark => true,
        Appearance.Light => false,
        _ => MainSurface.ActualTheme == ElementTheme.Dark,
    };

    private void OnWorkCardPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isWorkHovered = true;
        PaintLauncherCards(IsCurrentThemeDark());
    }

    private void OnWorkCardPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isWorkHovered = false;
        PaintLauncherCards(IsCurrentThemeDark());
    }

    private void OnBreakCardPointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isBreakHovered = true;
        PaintLauncherCards(IsCurrentThemeDark());
    }

    private void OnBreakCardPointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        _isBreakHovered = false;
        PaintLauncherCards(IsCurrentThemeDark());
    }

    private void PaintLauncherCards(bool isDark)
    {
        if (WorkChoiceCard is null || BreakChoiceCard is null || StartIdleButton is null) return;

        bool isWorkSelected = _selectedIdleType == SessionType.Work;
        var selectedColor = isWorkSelected ? _colors.Work : _colors.Break;

        // 1. Work Card
        if (isWorkSelected)
        {
            double bgAlpha = _isWorkHovered ? (isDark ? 0.22 : 0.16) : (isDark ? 0.16 : 0.10);
            double borderAlpha = _isWorkHovered ? (isDark ? 0.85 : 0.70) : (isDark ? 0.70 : 0.55);
            WorkChoiceCard.Background = SessionColorBrush.CreateAlpha(_colors.Work, bgAlpha);
            WorkChoiceCard.BorderBrush = SessionColorBrush.CreateAlpha(_colors.Work, borderAlpha);
            WorkChoiceCard.BorderThickness = new Thickness(1.5);
            WorkChoiceDot.Fill = SessionColorBrush.Create(_colors.Work);
            WorkChoiceDot.Opacity = 1.0;
            WorkChoiceMode.Foreground = Presentation.ThemeBrush("FkForeground", isDark);
            WorkChoiceDuration.Foreground = Presentation.ThemeBrush("FkForeground", isDark);
        }
        else
        {
            double bgAlpha = _isWorkHovered ? (isDark ? 0.08 : 0.04) : 0.0;
            WorkChoiceCard.Background = SessionColorBrush.CreateAlpha(_colors.Work, bgAlpha);
            WorkChoiceCard.BorderBrush = _isWorkHovered
                ? SessionColorBrush.CreateAlpha(_colors.Work, isDark ? 0.40 : 0.30)
                : Presentation.ThemeBrush("FkBorder", isDark);
            WorkChoiceCard.BorderThickness = new Thickness(1.0);
            WorkChoiceDot.Fill = SessionColorBrush.Create(_colors.Work);
            WorkChoiceDot.Opacity = _isWorkHovered ? 0.75 : 0.45;
            WorkChoiceMode.Foreground = Presentation.ThemeBrush("FkSecondary", isDark);
            WorkChoiceDuration.Foreground = Presentation.ThemeBrush("FkSecondary", isDark);
        }

        // 2. Break Card
        if (!isWorkSelected)
        {
            double bgAlpha = _isBreakHovered ? (isDark ? 0.22 : 0.16) : (isDark ? 0.16 : 0.10);
            double borderAlpha = _isBreakHovered ? (isDark ? 0.85 : 0.70) : (isDark ? 0.70 : 0.55);
            BreakChoiceCard.Background = SessionColorBrush.CreateAlpha(_colors.Break, bgAlpha);
            BreakChoiceCard.BorderBrush = SessionColorBrush.CreateAlpha(_colors.Break, borderAlpha);
            BreakChoiceCard.BorderThickness = new Thickness(1.5);
            BreakChoiceDot.Fill = SessionColorBrush.Create(_colors.Break);
            BreakChoiceDot.Opacity = 1.0;
            BreakChoiceMode.Foreground = Presentation.ThemeBrush("FkForeground", isDark);
            BreakChoiceDuration.Foreground = Presentation.ThemeBrush("FkForeground", isDark);
        }
        else
        {
            double bgAlpha = _isBreakHovered ? (isDark ? 0.08 : 0.04) : 0.0;
            BreakChoiceCard.Background = SessionColorBrush.CreateAlpha(_colors.Break, bgAlpha);
            BreakChoiceCard.BorderBrush = _isBreakHovered
                ? SessionColorBrush.CreateAlpha(_colors.Break, isDark ? 0.40 : 0.30)
                : Presentation.ThemeBrush("FkBorder", isDark);
            BreakChoiceCard.BorderThickness = new Thickness(1.0);
            BreakChoiceDot.Fill = SessionColorBrush.Create(_colors.Break);
            BreakChoiceDot.Opacity = _isBreakHovered ? 0.75 : 0.45;
            BreakChoiceMode.Foreground = Presentation.ThemeBrush("FkSecondary", isDark);
            BreakChoiceDuration.Foreground = Presentation.ThemeBrush("FkSecondary", isDark);
        }

        string workDur = WorkChoiceDuration?.Text ?? "25 min";
        string breakDur = BreakChoiceDuration?.Text ?? "10 min";
        AutomationProperties.SetName(WorkChoiceCard, isWorkSelected ? $"Work Session, {workDur}, Selected" : $"Select Work Session, {workDur}");
        AutomationProperties.SetName(BreakChoiceCard, !isWorkSelected ? $"Break Session, {breakDur}, Selected" : $"Select Break Session, {breakDur}");

        // 3. Durations & Subtitles in Idle Hero Body
        var durations = _today?.Snapshot?.Durations ?? SessionDurations.Default;
        var chosenDuration = isWorkSelected ? durations.Work : durations.Break;
        if (IdleDurationText is not null)
        {
            IdleDurationText.Text = string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", (int)chosenDuration.TotalMinutes, chosenDuration.Seconds);
        }
        if (IdleSubtitleText is not null)
        {
            IdleSubtitleText.Text = isWorkSelected ? "Ready when you are" : "Take a quick rest";
        }

        // 4. Start Button
        StartIdleButton.Background = SessionColorBrush.Create(selectedColor);
        StartIdleButton.BorderBrush = SessionColorBrush.Create(selectedColor);
        StartIdleButton.Foreground = SessionColorBrush.Create(SessionColors.Foreground(selectedColor));
    }

    private void AnimateTransition(UIElement target)
    {
        var animation = new DoubleAnimation
        {
            From = 0.0,
            To = 1.0,
            Duration = new Duration(TimeSpan.FromMilliseconds(150))
        };
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, "Opacity");
        storyboard.Begin();
    }

    private void Render()
    {
        bool isToday = _today.Page == MainPage.Today;
        TodayNav.IsChecked = isToday;
        if (DrawerTodayNav is not null) DrawerTodayNav.IsChecked = isToday;
        ReportsNav.IsChecked = _today.Page == MainPage.Reports;
        if (DrawerReportsNav is not null) DrawerReportsNav.IsChecked = _today.Page == MainPage.Reports;
        SettingsNav.IsChecked = _today.Page == MainPage.Settings;
        if (DrawerSettingsNav is not null) DrawerSettingsNav.IsChecked = _today.Page == MainPage.Settings;
        TodayPanel.Visibility = isToday ? Visibility.Visible : Visibility.Collapsed;
        SettingsHost.Visibility = _today.Page == MainPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        ReportsHost.Visibility = _today.Page == MainPage.Reports ? Visibility.Visible : Visibility.Collapsed;
        LoadStatus.Text = _today.IsRefreshing ? "Loading…" : _today.IsStarting ? "Starting…" : _today.IsStopping ? "Stopping…" : string.Empty;
        RefreshButton.IsEnabled = !_today.IsRefreshing && !_today.IsStopping && !_today.IsStarting;
        ErrorText.Text = _today.Error ?? string.Empty;
        ErrorText.Visibility = _today.Error is null ? Visibility.Collapsed : Visibility.Visible;

        if (_today.Snapshot is { } snapshot)
        {
            DayLabel.Text = snapshot.Date.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture);
            ToolTipService.SetToolTip(DayLabel, snapshot.TimeZone.DisplayName);

            // Update duration labels on selector buttons
            if (WorkChoiceDuration is not null)
                WorkChoiceDuration.Text = Presentation.Duration(snapshot.Durations.Work);
            if (BreakChoiceDuration is not null)
                BreakChoiceDuration.Text = Presentation.Duration(snapshot.Durations.Break);

            // Daily Summary 2x2 Metrics
            FocusValue.Text = Presentation.Duration(snapshot.WorkTime);
            WorkValue.Text = snapshot.CompletedWorkCount.ToString(CultureInfo.InvariantCulture);
            BreakValue.Text = Presentation.Duration(snapshot.BreakTime);
            CompletionValue.Text = snapshot.CompletionRate is { } rate ? rate.ToString("0", CultureInfo.InvariantCulture) + "%" : "—";

            // Activity Card
            if (ActivityCountText is not null)
            {
                ActivityCountText.Text = snapshot.Sessions.Count switch
                {
                    0 => "0 sessions",
                    1 => "1 session",
                    _ => $"{snapshot.Sessions.Count} sessions",
                };
            }

            if (snapshot.Sessions.Count == 0)
            {
                if (EmptyActivityPanel is not null) EmptyActivityPanel.Visibility = Visibility.Visible;
                if (ActivityScrollViewer is not null) ActivityScrollViewer.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (EmptyActivityPanel is not null) EmptyActivityPanel.Visibility = Visibility.Collapsed;
                if (ActivityScrollViewer is not null) ActivityScrollViewer.Visibility = Visibility.Visible;

                ActivityRows.ItemsSource = snapshot.Sessions.Select(session =>
                {
                    string started = TodayFormatting.FormatClockTime(session.StartedAt, snapshot.TimeZone, _timeFormat);
                    string duration = session.ActualDuration is { } actual ? Presentation.Duration(actual) : $"{Presentation.Duration(session.PlannedDuration)} planned";
                    var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 7, 8, 7) };
                    foreach (var width in new[] { new GridLength(42), new GridLength(6), new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(72) })
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });

                    var color = session.Type == SessionType.Work ? _colors.Work : _colors.Break;
                    var timeText = Presentation.Text(started, 11, true);
                    timeText.FontFamily = new FontFamily("Consolas");
                    timeText.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(timeText);

                    var dot = new Border
                    {
                        Width = 5,
                        Height = 5,
                        CornerRadius = new CornerRadius(2.5),
                        VerticalAlignment = VerticalAlignment.Center,
                        Background = SessionColorBrush.Create(color)
                    };
                    Grid.SetColumn(dot, 1); row.Children.Add(dot);

                    var type = new TextBlock
                    {
                        Text = session.Type.ToString(),
                        FontSize = 12,
                        FontWeight = Microsoft.UI.Text.FontWeights.Medium,
                        VerticalAlignment = VerticalAlignment.Center,
                        Style = (Style)Application.Current.Resources["FkText"],
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        TextWrapping = TextWrapping.NoWrap
                    };
                    Grid.SetColumn(type, 2); row.Children.Add(type);

                    var time = Presentation.Text(duration, 11, true);
                    time.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetColumn(time, 3); row.Children.Add(time);

                    var status = Presentation.StatusText(session.Status, 11);
                    status.VerticalAlignment = VerticalAlignment.Center;
                    status.TextTrimming = TextTrimming.CharacterEllipsis;
                    status.TextWrapping = TextWrapping.NoWrap;
                    Grid.SetColumn(status, 4); row.Children.Add(status);
                    return row;
                }).ToArray();
            }
        }

        RenderRunning();
        ScheduleDisplay();
    }

    private void UpdateActiveNavIndicator()
    {
        bool hasActive = _today.Snapshot?.Active is not null;
        if (!hasActive)
        {
            if (TodayExpandedDot is not null) TodayExpandedDot.Visibility = Visibility.Collapsed;
            if (TodayCompactDot is not null) TodayCompactDot.Visibility = Visibility.Collapsed;
            if (HamburgerActiveDot is not null) HamburgerActiveDot.Visibility = Visibility.Collapsed;
            if (DrawerTodayDot is not null) DrawerTodayDot.Visibility = Visibility.Collapsed;
            return;
        }

        var activeType = _today.Snapshot!.Active!.Type;
        var indicatorColor = activeType == SessionType.Work ? _colors.Work : _colors.Break;
        var dotBrush = SessionColorBrush.Create(indicatorColor);
        double dotOpacity = _today.Snapshot!.Active!.Status == SessionStatus.Paused ? 0.50 : 1.0;

        double windowWidth = MainSurface?.ActualWidth ?? 1200;
        var navMode = TodayAdaptiveLayoutHelper.ResolveNavMode(windowWidth);

        if (TodayExpandedDot is not null)
        {
            TodayExpandedDot.Fill = dotBrush;
            TodayExpandedDot.Opacity = dotOpacity;
            TodayExpandedDot.Visibility = navMode == AdaptiveNavMode.Expanded ? Visibility.Visible : Visibility.Collapsed;
        }

        if (TodayCompactDot is not null)
        {
            TodayCompactDot.Fill = dotBrush;
            TodayCompactDot.Opacity = dotOpacity;
            TodayCompactDot.Visibility = navMode == AdaptiveNavMode.Compact ? Visibility.Visible : Visibility.Collapsed;
        }

        if (HamburgerActiveDot is not null)
        {
            HamburgerActiveDot.Fill = dotBrush;
            HamburgerActiveDot.Opacity = dotOpacity;
            HamburgerActiveDot.Visibility = navMode == AdaptiveNavMode.Collapsed ? Visibility.Visible : Visibility.Collapsed;
        }

        if (DrawerTodayDot is not null)
        {
            DrawerTodayDot.Fill = dotBrush;
            DrawerTodayDot.Opacity = dotOpacity;
            DrawerTodayDot.Visibility = Visibility.Visible;
        }
    }

    private void RenderRunning()
    {
        bool hasActive = _today.Snapshot?.Active is not null;

        UpdateActiveNavIndicator();

        bool stateChanged = _hasRenderedRunning && (_lastHasRunning != hasActive);
        _lastHasRunning = hasActive;
        _hasRenderedRunning = true;

        if (SessionHeroCard is null || ActiveContent is null || IdleContent is null) return;

        bool isDark = IsCurrentThemeDark();

        if (_today.Snapshot?.Active is not { } active)
        {
            ActiveContent.Visibility = Visibility.Collapsed;
            IdleContent.Visibility = Visibility.Visible;
            SessionHeroCard.ClearValue(Border.BackgroundProperty);
            SessionHeroCard.ClearValue(Border.BorderBrushProperty);

            PaintLauncherCards(isDark);

            if (stateChanged)
            {
                AnimateTransition(IdleContent);
                FocusTodayContext();
            }
            return;
        }

        IdleContent.Visibility = Visibility.Collapsed;
        ActiveContent.Visibility = Visibility.Visible;

        var snapshot = SessionSnapshot.For(active, DateTimeOffset.UtcNow);
        var remaining = TimeSpan.FromSeconds(Math.Ceiling(snapshot.Remaining.TotalSeconds));
        var color = active.Type == SessionType.Work ? _colors.Work : _colors.Break;
        bool isPaused = active.Status == SessionStatus.Paused;

        SessionHeroCard.Background = SessionColorBrush.CreateTint(color, isDark, 0.06);
        SessionHeroCard.BorderBrush = SessionColorBrush.CreateSemanticBorder(color, 0.40);

        ActiveTypeDot.Fill = SessionColorBrush.Create(color);
        CurrentHeading.Text = active.Type == SessionType.Work ? "WORK SESSION" : "BREAK SESSION";
        CurrentHeading.Foreground = Presentation.ThemeBrush("FkSecondary", isDark);

        ActiveStatusBadge.Text = isPaused ? "PAUSED" : "RUNNING";
        ActiveStatusBadge.Foreground = isPaused
            ? Presentation.ThemeBrush("FkSecondary", isDark)
            : SessionColorBrush.Create(color);

        RunningText.Text = string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", (int)remaining.TotalMinutes, remaining.Seconds);
        RunningHint.Text = isPaused ? "paused" : "remaining";
        RunningHint.Foreground = isPaused
            ? Presentation.ThemeBrush("FkSecondary", isDark)
            : SessionColorBrush.Create(color);

        double totalSecs = active.PlannedDuration.TotalSeconds;
        double remSecs = Math.Max(0, remaining.TotalSeconds);
        double pct = totalSecs > 0 ? Math.Clamp((totalSecs - remSecs) / totalSecs, 0, 1) : 0;
        SessionProgress.Value = pct * 100.0;
        SessionProgress.Foreground = SessionColorBrush.Create(color);

        if (isPaused)
        {
            PauseButton.Visibility = Visibility.Collapsed;
            ContinueButton.Visibility = Visibility.Visible;
            StartNewButton.Visibility = Visibility.Visible;

            ContinueButton.Background = SessionColorBrush.Create(color);
            ContinueButton.BorderBrush = SessionColorBrush.Create(color);
            ContinueButton.Foreground = SessionColorBrush.Create(SessionColors.Foreground(color));

            StartNewButton.Background = Presentation.ThemeBrush("FkSurface2", isDark);
            StartNewButton.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", isDark);
            StartNewButton.Foreground = Presentation.ThemeBrush("FkForeground", isDark);
        }
        else
        {
            ContinueButton.Visibility = Visibility.Collapsed;
            StartNewButton.Visibility = Visibility.Collapsed;
            PauseButton.Visibility = Visibility.Visible;

            PauseButton.Background = SessionColorBrush.Create(color);
            PauseButton.BorderBrush = SessionColorBrush.Create(color);
            PauseButton.Foreground = SessionColorBrush.Create(SessionColors.Foreground(color));
        }

        if (stateChanged)
        {
            AnimateTransition(ActiveContent);
            FocusTodayContext();
        }
    }

    private void OnDisplayTick(DispatcherQueueTimer sender, object args)
    {
        if (!_visible) return;
        if (_today.Snapshot?.Active is not null)
        {
            RenderRunning();
            ScheduleDisplay();
        }
    }

    private void ScheduleDisplay()
    {
        _displayTimer.Stop();
        if (!_visible) return;
        if (_today.Snapshot?.Active is not { } active) return;

        var snapshot = SessionSnapshot.For(active, DateTimeOffset.UtcNow);
        var remaining = snapshot.Remaining;
        if (remaining <= TimeSpan.Zero) return;

        var nextTick = snapshot.ObservedAt.AddSeconds(1);
        var wait = nextTick - DateTimeOffset.UtcNow;
        _displayTimer.Interval = wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
        _displayTimer.Start();
    }

    private void UpdateSidebarDimensions(double windowWidth)
    {
        if (windowWidth <= 0 || NavColumn is null || NavGrid is null) return;

        double factor = UiScaleLevels.ToFactor(_uiScalePercent);
        double effectiveWidth = UiScaleLevels.CalculateEffectiveWidth(windowWidth, factor);
        var navMode = TodayAdaptiveLayoutHelper.ResolveNavMode(effectiveWidth);

        if (navMode == AdaptiveNavMode.Collapsed)
        {
            // 1. Narrow mode (< 740 DIP effective)
            NavColumn.Width = new GridLength(0);
            NavGrid.Visibility = Visibility.Collapsed;
            if (HamburgerButton is not null) HamburgerButton.Visibility = Visibility.Visible;
            UpdateActiveNavIndicator();
            return;
        }

        // Close drawer if expanding out of narrow mode
        CloseNavDrawer();
        if (HamburgerButton is not null) HamburgerButton.Visibility = Visibility.Collapsed;
        NavGrid.Visibility = Visibility.Visible;

        if (NavDrawerPane is not null)
        {
            NavDrawerPane.Width = Math.Round(240 * factor);
        }

        if (navMode == AdaptiveNavMode.Compact)
        {
            // 2. Compact mode (740 to 1059 DIP effective): 54 DIP rail with centered icons only
            NavColumn.Width = new GridLength(Math.Round(54 * factor));
            NavGrid.Padding = new Thickness(4 * factor, 16 * factor, 4 * factor, 14 * factor);

            if (TopNavSection is not null) TopNavSection.Spacing = 16 * factor;
            if (TopNavItems is not null) TopNavItems.Spacing = 4 * factor;
            if (BottomNavSection is not null) BottomNavSection.Spacing = 4 * factor;

            if (NavHeaderPanel is not null) NavHeaderPanel.Padding = new Thickness(0);
            if (NavBrandTitle is not null) NavBrandTitle.Visibility = Visibility.Collapsed;

            ApplyCompactNavItem(TodayNav, TodayIconContainer, TodayIcon, TodayLabel, factor);
            ApplyCompactNavItem(ReportsNav, ReportsIconContainer, ReportsIcon, ReportsLabel, factor);
            ApplyCompactNavItem(OverlayNavButton, OverlayIconContainer, OverlayIcon, OverlayLabel, factor);
            ApplyCompactNavItem(SettingsNav, SettingsIconContainer, SettingsIcon, SettingsLabel, factor);
            ApplyCompactNavItem(ExitButton, ExitIconContainer, ExitIcon, ExitLabel, factor);
        }
        else
        {
            // 3. Expanded mode (>= 1060 DIP effective): 220 DIP pane with icons + labels
            NavColumn.Width = new GridLength(Math.Round(220 * factor));
            NavGrid.Padding = new Thickness(10 * factor, 20 * factor, 10 * factor, 16 * factor);

            if (TopNavSection is not null) TopNavSection.Spacing = 20 * factor;
            if (TopNavItems is not null) TopNavItems.Spacing = 3 * factor;
            if (BottomNavSection is not null) BottomNavSection.Spacing = 3 * factor;

            if (NavHeaderPanel is not null) NavHeaderPanel.Padding = new Thickness(10 * factor, 0, 10 * factor, 0);
            if (NavBrandTitle is not null)
            {
                NavBrandTitle.Visibility = Visibility.Visible;
                NavBrandTitle.FontSize = 13.0 * factor;
            }

            ApplyExpandedNavItem(TodayNav, TodayIconContainer, TodayIcon, TodayLabel, factor);
            ApplyExpandedNavItem(ReportsNav, ReportsIconContainer, ReportsIcon, ReportsLabel, factor);
            ApplyExpandedNavItem(OverlayNavButton, OverlayIconContainer, OverlayIcon, OverlayLabel, factor);
            ApplyExpandedNavItem(SettingsNav, SettingsIconContainer, SettingsIcon, SettingsLabel, factor);
            ApplyExpandedNavItem(ExitButton, ExitIconContainer, ExitIcon, ExitLabel, factor);
        }

        UpdateActiveNavIndicator();
    }

    private static void ApplyCompactNavItem(Control? button, FrameworkElement? iconContainer, FontIcon? icon, TextBlock? label, double factor = 1.0)
    {
        if (button is not null)
        {
            button.MinHeight = Math.Round(38 * factor);
            button.Padding = new Thickness(0);
            button.HorizontalContentAlignment = HorizontalAlignment.Center;
        }
        if (iconContainer is not null)
        {
            iconContainer.Width = Math.Round(36 * factor);
            iconContainer.Height = Math.Round(36 * factor);
        }
        if (icon is not null)
        {
            icon.FontSize = 17.5 * factor;
        }
        if (label is not null)
        {
            label.Visibility = Visibility.Collapsed;
        }
    }

    private static void ApplyExpandedNavItem(Control? button, FrameworkElement? iconContainer, FontIcon? icon, TextBlock? label, double factor = 1.0)
    {
        if (button is not null)
        {
            button.MinHeight = Math.Round(40 * factor);
            button.Padding = new Thickness(10 * factor, 6 * factor, 10 * factor, 6 * factor);
            button.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        }
        if (iconContainer is not null)
        {
            iconContainer.Width = Math.Round(28 * factor);
            iconContainer.Height = Math.Round(28 * factor);
        }
        if (icon is not null)
        {
            icon.FontSize = 18.0 * factor;
        }
        if (label is not null)
        {
            label.Visibility = Visibility.Visible;
            label.FontSize = 13.0 * factor;
        }
    }
}
