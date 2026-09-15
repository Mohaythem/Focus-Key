using System.Globalization;
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
using WinRT.Interop;
using Microsoft.UI.Xaml.Media.Animation;

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

    internal void SetActivityCollapsed(bool collapsed)
    {
        _activityCollapsed = collapsed;
        UpdateActivityVisuals();
    }

    internal void ApplyColors(SessionColors colors)
    {
        _colors = colors;
        _reports.ApplyColors(colors);
        RenderRunning();
    }
    internal event Action? OverlayRequested;
    private void OnOverlayClick(object sender, RoutedEventArgs args) => OverlayRequested?.Invoke();
    internal event Action? ExitRequested;
    internal event Action<GlobalShortcut>? GlobalShortcutUpdated;

    internal void ApplyShortcut(GlobalShortcut shortcut)
    {
        string text = shortcut.ToString();
        if (HeroOverlayShortcutHint is not null) HeroOverlayShortcutHint.Text = text;
        if (SidebarOverlayShortcutHint is not null) SidebarOverlayShortcutHint.Text = text;
    }

    private Appearance _appearance = Appearance.System;
    private Contrast _contrast = Contrast.Standard;
    private ThemePalette? _lightPalette;
    private ThemePalette? _darkPalette;

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
        // System appearance leaves the native caption controls under Windows ownership.
        // The content still uses the resolved light/dark palette above, but any previous
        // explicit caption colors must be cleared when the user returns to System.
        WindowAppearance.ApplyTitleBar(AppWindow, _appearance == Appearance.System ? null : palette);

        var targetTheme = WindowAppearance.ToElementTheme(_appearance);
        MainSurface.RequestedTheme = targetTheme;

        _reports?.RefreshVisuals(_contrast);
        if (_today is not null) Render();
    }

    internal MainWindow(StartupContext startup,
        Func<SessionType, CancellationToken, Task<SessionRecord>> start,
        Func<SessionId, CancellationToken, Task<SessionOutcome>> stop, Action<Exception> report,
        Func<Task> refreshSettings,
        WindowsShellIntegration shellIntegration)
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
            sc =>
            {
                ApplyShortcut(sc);
                GlobalShortcutUpdated?.Invoke(sc);
            });
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
            bool narrow = MainSurface.ActualWidth < 740;
            NavColumn.Width = new GridLength(narrow ? 180 : 216);
            PageContent.Padding = new Thickness(narrow ? 24 : 40, 28, narrow ? 24 : 40, 36);
        };
        _today = new TodayController(startup.Today.ReadAsync, start, stop, report);
        _today.Changed += Render;
        _displayTimer = DispatcherQueue.CreateTimer();
        _displayTimer.IsRepeating = false;
        _displayTimer.Tick += OnDisplayTick;
        Closed += (_, _) => { _visible = false; _displayTimer.Stop(); _today.Dispose(); _reports.Dispose(); };
        UpdateActivityVisuals();
        Render();
        startup.Logger.Info("Today main window created.");
    }

    internal async void OpenToday()
    {
        _settings.CommitPendingDurations();
        _visible = true;
        _reports.Hide();
        await _today.OpenAsync();
    }

    internal void HideToday()
    {
        _settings.CommitPendingDurations();
        _visible = false;
        _displayTimer.Stop();
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

    private async void OnTodayClick(object sender, RoutedEventArgs args) { _settings.CommitPendingDurations(); _reports.Hide(); await _today.NavigateAsync(MainPage.Today); }
    private async void OnReportsClick(object sender, RoutedEventArgs args) => await OpenReportsAsync();
    private async void OnSettingsClick(object sender, RoutedEventArgs args)
    {
        _settings.CommitPendingDurations();
        _reports.Hide();
        await _today.NavigateAsync(MainPage.Settings);
        await _settings.OpenAsync();
    }
    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await _today.RefreshAsync();
    private async void OnStartWorkClick(object sender, RoutedEventArgs args) => await _today.StartAsync(SessionType.Work);
    private async void OnStartBreakClick(object sender, RoutedEventArgs args) => await _today.StartAsync(SessionType.Break);
    private async void OnStopClick(object sender, RoutedEventArgs args) => await _today.StopAsync();
    private void OnExitClick(object sender, RoutedEventArgs args) => ExitRequested?.Invoke();

    private void UpdateActivityVisuals()
    {
        if (ActivityChevron is not null)
            ActivityChevron.Glyph = _activityCollapsed ? "\uE70D" : "\uE70E";
        if (ActivityContentPanel is not null)
            ActivityContentPanel.Visibility = _activityCollapsed ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnToggleActivityClick(object sender, RoutedEventArgs args)
    {
        _activityCollapsed = !_activityCollapsed;
        UpdateActivityVisuals();
        if (_settingsService is not null)
        {
            try
            {
                await _settingsService.UpdateActivityCollapsedAsync(_activityCollapsed);
            }
            catch (Exception ex)
            {
                _startupReport?.Invoke(ex);
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
        if (WorkChoiceCard is null || BreakChoiceCard is null) return;

        double workBgAlpha = _isWorkHovered ? (isDark ? 0.20 : 0.14) : (isDark ? 0.12 : 0.08);
        double workBorderAlpha = _isWorkHovered ? (isDark ? 0.55 : 0.40) : (isDark ? 0.35 : 0.25);
        WorkChoiceCard.Background = SessionColorBrush.CreateAlpha(_colors.Work, workBgAlpha);
        WorkChoiceCard.BorderBrush = SessionColorBrush.CreateAlpha(_colors.Work, workBorderAlpha);
        WorkChoiceDot.Fill = SessionColorBrush.Create(_colors.Work);
        WorkChoiceMode.Foreground = Presentation.ThemeBrush("FkSecondary", CurrentCard);

        StartWorkButton.Background = SessionColorBrush.CreateElevated(_colors.Work, isDark, _isWorkHovered);
        StartWorkButton.BorderBrush = SessionColorBrush.CreateAlpha(_colors.Work, _isWorkHovered ? (isDark ? 0.85 : 0.75) : (isDark ? 0.55 : 0.45));
        StartWorkButton.BorderThickness = new Thickness(1);
        StartWorkButton.Foreground = Presentation.ThemeBrush("FkForeground", CurrentCard);

        double breakBgAlpha = _isBreakHovered ? (isDark ? 0.20 : 0.14) : (isDark ? 0.12 : 0.08);
        double breakBorderAlpha = _isBreakHovered ? (isDark ? 0.55 : 0.40) : (isDark ? 0.35 : 0.25);
        BreakChoiceCard.Background = SessionColorBrush.CreateAlpha(_colors.Break, breakBgAlpha);
        BreakChoiceCard.BorderBrush = SessionColorBrush.CreateAlpha(_colors.Break, breakBorderAlpha);
        BreakChoiceDot.Fill = SessionColorBrush.Create(_colors.Break);
        BreakChoiceMode.Foreground = Presentation.ThemeBrush("FkSecondary", CurrentCard);

        StartBreakButton.Background = SessionColorBrush.CreateElevated(_colors.Break, isDark, _isBreakHovered);
        StartBreakButton.BorderBrush = SessionColorBrush.CreateAlpha(_colors.Break, _isBreakHovered ? (isDark ? 0.85 : 0.75) : (isDark ? 0.55 : 0.45));
        StartBreakButton.BorderThickness = new Thickness(1);
        StartBreakButton.Foreground = Presentation.ThemeBrush("FkForeground", CurrentCard);

        var durations = _today.Snapshot?.Durations ?? SessionDurations.Default;
        var (workNum, workUnit) = TodayFormatting.FormatLauncherDurationParts(durations.Work);
        var (brkNum, brkUnit) = TodayFormatting.FormatLauncherDurationParts(durations.Break);

        WorkDurationNumber.Text = workNum;
        WorkDurationUnit.Text = workUnit;
        BreakDurationNumber.Text = brkNum;
        BreakDurationUnit.Text = brkUnit;

        bool canStart = !_today.IsStarting && !_today.IsStopping && !_today.IsRefreshing && _today.Error is null;
        StartWorkButton.IsEnabled = canStart;
        StartBreakButton.IsEnabled = canStart;
    }

    private void Render()
    {
        bool isToday = _today.Page == MainPage.Today;
        TodayNav.IsChecked = isToday;
        ReportsNav.IsChecked = _today.Page == MainPage.Reports;
        SettingsNav.IsChecked = _today.Page == MainPage.Settings;
        PageTitle.Text = _today.Page.ToString();
        PageTitle.Visibility = _today.Page == MainPage.Reports ? Visibility.Collapsed : Visibility.Visible;
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
            ToolTipService.SetToolTip(DayLabel, $"{snapshot.TimeZone.DisplayName}. Sessions grouped by local start date.");
            FocusValue.Text = Presentation.Duration(snapshot.WorkTime);
            WorkValue.Text = snapshot.CompletedWorkCount.ToString(CultureInfo.InvariantCulture);
            BreakValue.Text = Presentation.Duration(snapshot.BreakTime);
            BreakDetail.Text = "today";
            CompletionValue.Text = snapshot.CompletionRate is { } rate ? rate.ToString("0", CultureInfo.InvariantCulture) + "%" : "—";
            ActivityRows.ItemsSource = snapshot.Sessions.Select(session =>
            {
                string started = TimeZoneInfo.ConvertTime(session.StartedAt, snapshot.TimeZone).ToString("HH:mm", CultureInfo.InvariantCulture);
                string duration = session.ActualDuration is { } actual ? Presentation.Duration(actual) : $"{Presentation.Duration(session.PlannedDuration)} planned";
                var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 12, 0, 12) };
                foreach (var width in new[] { new GridLength(40), new GridLength(4), new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(72) })
                    row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
                var color = session.Type == SessionType.Work ? _colors.Work : _colors.Break;
                var timeText = Presentation.Text(started, 11, true);
                timeText.FontFamily = new FontFamily("Consolas");
                timeText.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(timeText);
                var dot = new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center,
                    Background = SessionColorBrush.Create(color) };
                Grid.SetColumn(dot, 1); row.Children.Add(dot);
                var type = new TextBlock { Text = session.Type.ToString(), FontSize = 12, VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)Application.Current.Resources["FkText"] };
                Grid.SetColumn(type, 2); row.Children.Add(type);
                var time = Presentation.Text(duration, 11, true);
                time.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(time, 3); row.Children.Add(time);
                var status = Presentation.StatusText(session.Status, 11);
                status.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(status, 4); row.Children.Add(status);
                return row;
            }).ToArray();
            EmptyActivity.Visibility = snapshot.Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        RenderRunning();
        ScheduleDisplay();
    }

    private void RenderRunning()
    {
        bool hasRunning = _today.Snapshot?.Running is not null;

        // Sidebar active indicator
        ActiveIndicator.Visibility = hasRunning ? Visibility.Visible : Visibility.Collapsed;
        if (hasRunning)
        {
            var indicatorColor = _today.Snapshot!.Running!.Type == SessionType.Work ? _colors.Work : _colors.Break;
            ActiveDot.Fill = SessionColorBrush.Create(indicatorColor);
        }

        bool stateChanged = _hasRenderedRunning && (_lastHasRunning != hasRunning);
        _lastHasRunning = hasRunning;
        _hasRenderedRunning = true;

        if (_today.Snapshot?.Running is not { } running)
        {
            ActiveContent.Visibility = Visibility.Collapsed;
            IdleContent.Visibility = Visibility.Visible;
            CurrentCard.ClearValue(Border.BackgroundProperty);
            CurrentCard.Padding = new Thickness(28, 24, 28, 24);
            CurrentCard.MinHeight = 180;

            PaintLauncherCards(IsCurrentThemeDark());

            if (stateChanged)
            {
                AnimateTransition(IdleContent);
            }
            return;
        }

        IdleContent.Visibility = Visibility.Collapsed;
        ActiveContent.Visibility = Visibility.Visible;

        var snapshot = SessionSnapshot.For(running, DateTimeOffset.UtcNow);
        var remaining = TimeSpan.FromSeconds(Math.Ceiling(snapshot.Remaining.TotalSeconds));
        var color = running.Type == SessionType.Work ? _colors.Work : _colors.Break;
        var foreground = Presentation.Stroke(color);
        CurrentCard.Background = SessionColorBrush.Create(color);
        CurrentCard.Padding = new Thickness(28, 24, 28, 24);
        CurrentCard.MinHeight = 180;
        CurrentHeading.Foreground = RunningType.Foreground = RunningText.Foreground = RunningHint.Foreground = foreground;
        RunningType.Text = running.Type.ToString();
        RunningType.Visibility = Visibility.Visible;
        RunningText.FontSize = 40;
        RunningText.FontFamily = new FontFamily("Consolas");
        RunningText.Text = snapshot.HasReachedPlannedEnd ? "00:00" : string.Create(CultureInfo.InvariantCulture, $"{(long)remaining.TotalMinutes:00}:{remaining.Seconds:00}");
        RunningHint.Text = snapshot.HasReachedPlannedEnd ? "Finishing…" : "remaining";
        SessionProgress.Value = 100 * snapshot.Elapsed.TotalSeconds / snapshot.PlannedDuration.TotalSeconds;
        SessionProgress.Foreground = foreground;
        SessionProgress.Visibility = StopButton.Visibility = Visibility.Visible;
        StopButton.IsEnabled = !_today.IsRefreshing && !_today.IsStopping && !_today.IsStarting && _today.Error is null;

        if (stateChanged)
        {
            AnimateTransition(ActiveContent);
        }
    }

    private static void AnimateTransition(UIElement target)
    {
        try
        {
            if (new UISettings().AnimationsEnabled)
            {
                var animation = new DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = new Duration(TimeSpan.FromMilliseconds(200)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var storyboard = new Storyboard();
                storyboard.Children.Add(animation);
                Storyboard.SetTarget(animation, target);
                Storyboard.SetTargetProperty(animation, "Opacity");
                storyboard.Begin();
                return;
            }
        }
        catch
        {
        }
        target.Opacity = 1.0;
    }
    private async void OnDisplayTick(DispatcherQueueTimer sender, object args)
    {
        if (!_visible || _today.Page != MainPage.Today || _today.IsRefreshing) return;
        if (_today.Snapshot is { } snapshot &&
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.Local).DateTime) != snapshot.Date)
            await _today.RefreshAsync();
        else { RenderRunning(); ScheduleDisplay(); }
    }

    private void ScheduleDisplay()
    {
        _displayTimer.Stop();
        if (!_visible || _today.Page != MainPage.Today || _today.IsRefreshing || _today.Error is not null || _today.Snapshot is not { } snapshot) return;
        // A visible Running timer only redraws from timestamps; it never queries persistence.
        // With no Running session, wake once at the next local day, not on a polling interval.
        TimeSpan wait = snapshot.Running is null ? snapshot.NextDayAt - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(1);
        _displayTimer.Interval = wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
        _displayTimer.Start();
    }

    private static string Duration(TimeSpan duration) =>
        duration.TotalHours >= 1 ? $"{(long)duration.TotalHours}h {duration.Minutes:00}m {duration.Seconds:00}s" :
        $"{(long)duration.TotalMinutes}m {duration.Seconds:00}s";
}
