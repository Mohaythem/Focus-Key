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
    private SessionType _selectedIdleType = SessionType.Work;

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
    private void OnOverlayClick(object sender, RoutedEventArgs args) => OverlayRequested?.Invoke();
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
        _settings?.RefreshVisuals(_contrast);
        if (_today is not null) Render();
    }

    internal MainWindow(StartupContext startup,
        Func<SessionType, CancellationToken, Task<SessionRecord>> start,
        Func<SessionId, CancellationToken, Task<SessionOutcome>> stop, Action<Exception> report,
        Func<Task> refreshSettings,
        WindowsShellIntegration shellIntegration,
        Func<SessionId, CancellationToken, Task<SessionOutcome>>? pause = null,
        Func<SessionId, CancellationToken, Task<SessionOutcome>>? @continue = null)
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
            double w = MainSurface.ActualWidth;
            bool narrow = w < 740;
            UpdateSidebarDimensions(w);
            PageContent.Padding = new Thickness(narrow ? 24 : 40, 28, narrow ? 24 : 40, 36);
            UpdatePageWidths();
        };
        PageScrollViewer.SizeChanged += (_, _) => UpdatePageWidths();
        _today = new TodayController(startup.Today.ReadAsync, start, stop, report, pause, @continue);
        _today.Changed += Render;
        _displayTimer = DispatcherQueue.CreateTimer();
        _displayTimer.IsRepeating = false;
        _displayTimer.Tick += OnDisplayTick;
        Closed += (_, _) => { _visible = false; _displayTimer.Stop(); _today.Dispose(); _reports.Dispose(); };
        Render();
        UpdateSidebarDimensions(MainSurface.ActualWidth > 0 ? MainSurface.ActualWidth : 880);
        UpdatePageWidths();
        startup.Logger.Info("Today main window created.");
    }

    internal async void OpenToday()
    {
        _settings.CommitPendingDurations();
        _visible = true;
        _reports.Hide();
        UpdatePageWidths();
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

    private async void OnTodayClick(object sender, RoutedEventArgs args) { _settings.CommitPendingDurations(); _reports.Hide(); UpdatePageWidths(); await _today.NavigateAsync(MainPage.Today); }
    private async void OnReportsClick(object sender, RoutedEventArgs args) { UpdatePageWidths(); await OpenReportsAsync(); }
    private async void OnSettingsClick(object sender, RoutedEventArgs args)
    {
        _settings.CommitPendingDurations();
        _reports.Hide();
        await _today.NavigateAsync(MainPage.Settings);
        await _settings.OpenAsync();
    }
    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await _today.RefreshAsync();
    private void SelectIdleType(SessionType type)
    {
        _selectedIdleType = type;
        PaintLauncherCards(IsCurrentThemeDark());
    }
    private void OnWorkCardClick(object sender, RoutedEventArgs args) => SelectIdleType(SessionType.Work);
    private void OnBreakCardClick(object sender, RoutedEventArgs args) => SelectIdleType(SessionType.Break);
    private void OnWorkCardPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => SelectIdleType(SessionType.Work);
    private void OnBreakCardPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e) => SelectIdleType(SessionType.Break);
    private void OnWorkCardTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => SelectIdleType(SessionType.Work);
    private void OnBreakCardTapped(object sender, Microsoft.UI.Xaml.Input.TappedRoutedEventArgs e) => SelectIdleType(SessionType.Break);
    private async void OnStartIdleClick(object sender, RoutedEventArgs args) => await _today.StartAsync(_selectedIdleType);
    private async void OnPauseClick(object sender, RoutedEventArgs args) => await _today.PauseAsync();
    private async void OnContinueClick(object sender, RoutedEventArgs args) => await _today.ContinueAsync();
    private async void OnStartNewClick(object sender, RoutedEventArgs args) => await _today.StartNewAsync();
    private async void OnStopClick(object sender, RoutedEventArgs args) => await _today.StopAsync();
    private void OnExitClick(object sender, RoutedEventArgs args) => ExitRequested?.Invoke();

    private void UpdatePageWidths()
    {
        if (PageScrollViewer is null || PageContent is null) return;
        if (PageScrollViewer.ActualWidth > 0)
        {
            PageContent.Width = PageScrollViewer.ActualWidth;
            double available = PageScrollViewer.ActualWidth - PageContent.Padding.Left - PageContent.Padding.Right;
            if (available > 0)
            {
                if (TodayPanel is not null)
                {
                    TodayPanel.Width = Math.Min(1240, available);

                    // Responsive layout for Today Hero + Summary
                    // Wide (>= 860 DIP available / >= 1100 DIP window): side-by-side 2/3 + 1/3
                    // Medium / Narrow (< 860 DIP available): Hero full width, Summary below Hero
                    bool isWide = available >= 860;

                    if (HeroColumnDef is not null && SummaryColumnDef is not null &&
                        HeroRowDef is not null && SummaryRowDef is not null &&
                        SessionHeroCard is not null && TodaySummaryCard is not null)
                    {
                        if (isWide)
                        {
                            HeroColumnDef.Width = new GridLength(2, GridUnitType.Star);
                            SummaryColumnDef.Width = new GridLength(1, GridUnitType.Star);
                            HeroRowDef.Height = GridLength.Auto;
                            SummaryRowDef.Height = new GridLength(0);

                            Grid.SetColumn(SessionHeroCard, 0);
                            Grid.SetRow(SessionHeroCard, 0);
                            Grid.SetColumnSpan(SessionHeroCard, 1);

                            Grid.SetColumn(TodaySummaryCard, 1);
                            Grid.SetRow(TodaySummaryCard, 0);
                            Grid.SetColumnSpan(TodaySummaryCard, 1);

                            SessionHeroCard.MinHeight = 380;
                            TodaySummaryCard.MinHeight = 380;
                            if (RunningText is not null) RunningText.FontSize = 80;
                        }
                        else
                        {
                            HeroColumnDef.Width = new GridLength(1, GridUnitType.Star);
                            SummaryColumnDef.Width = new GridLength(0);
                            HeroRowDef.Height = GridLength.Auto;
                            SummaryRowDef.Height = GridLength.Auto;

                            Grid.SetColumn(SessionHeroCard, 0);
                            Grid.SetRow(SessionHeroCard, 0);
                            Grid.SetColumnSpan(SessionHeroCard, 2);

                            Grid.SetColumn(TodaySummaryCard, 0);
                            Grid.SetRow(TodaySummaryCard, 1);
                            Grid.SetColumnSpan(TodaySummaryCard, 2);

                            SessionHeroCard.MinHeight = 320;
                            TodaySummaryCard.MinHeight = 0;
                            if (RunningText is not null) RunningText.FontSize = available < 580 ? 56 : 68;
                        }
                    }
                }
                if (ReportsHost is not null)
                    ReportsHost.Width = Math.Min(1220, available);
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
            double bgAlpha = _isWorkHovered ? (isDark ? 0.20 : 0.14) : (isDark ? 0.14 : 0.09);
            double borderAlpha = _isWorkHovered ? (isDark ? 0.85 : 0.70) : (isDark ? 0.65 : 0.50);
            WorkChoiceCard.Background = SessionColorBrush.CreateAlpha(_colors.Work, bgAlpha);
            WorkChoiceCard.BorderBrush = SessionColorBrush.CreateAlpha(_colors.Work, borderAlpha);
            WorkChoiceCard.BorderThickness = new Thickness(1.5);
            WorkChoiceDot.Fill = SessionColorBrush.Create(_colors.Work);
            WorkChoiceDot.Opacity = 1.0;
            WorkChoiceMode.Foreground = Presentation.ThemeBrush("FkForeground", isDark);
            WorkDurationNumber.Foreground = Presentation.ThemeBrush("FkForeground", isDark);
        }
        else
        {
            double bgAlpha = _isWorkHovered ? (isDark ? 0.08 : 0.04) : (isDark ? 0.03 : 0.015);
            WorkChoiceCard.Background = SessionColorBrush.CreateAlpha(_colors.Work, bgAlpha);
            WorkChoiceCard.BorderBrush = _isWorkHovered
                ? SessionColorBrush.CreateAlpha(_colors.Work, isDark ? 0.40 : 0.30)
                : Presentation.ThemeBrush("FkBorder", isDark);
            WorkChoiceCard.BorderThickness = new Thickness(1.0);
            WorkChoiceDot.Fill = SessionColorBrush.Create(_colors.Work);
            WorkChoiceDot.Opacity = _isWorkHovered ? 0.75 : 0.40;
            WorkChoiceMode.Foreground = Presentation.ThemeBrush("FkSecondary", isDark);
            WorkDurationNumber.Foreground = Presentation.ThemeBrush("FkSecondary", isDark);
        }

        // 2. Break Card
        if (!isWorkSelected)
        {
            double bgAlpha = _isBreakHovered ? (isDark ? 0.20 : 0.14) : (isDark ? 0.14 : 0.09);
            double borderAlpha = _isBreakHovered ? (isDark ? 0.85 : 0.70) : (isDark ? 0.65 : 0.50);
            BreakChoiceCard.Background = SessionColorBrush.CreateAlpha(_colors.Break, bgAlpha);
            BreakChoiceCard.BorderBrush = SessionColorBrush.CreateAlpha(_colors.Break, borderAlpha);
            BreakChoiceCard.BorderThickness = new Thickness(1.5);
            BreakChoiceDot.Fill = SessionColorBrush.Create(_colors.Break);
            BreakChoiceDot.Opacity = 1.0;
            BreakChoiceMode.Foreground = Presentation.ThemeBrush("FkForeground", isDark);
            BreakDurationNumber.Foreground = Presentation.ThemeBrush("FkForeground", isDark);
        }
        else
        {
            double bgAlpha = _isBreakHovered ? (isDark ? 0.08 : 0.04) : (isDark ? 0.03 : 0.015);
            BreakChoiceCard.Background = SessionColorBrush.CreateAlpha(_colors.Break, bgAlpha);
            BreakChoiceCard.BorderBrush = _isBreakHovered
                ? SessionColorBrush.CreateAlpha(_colors.Break, isDark ? 0.40 : 0.30)
                : Presentation.ThemeBrush("FkBorder", isDark);
            BreakChoiceCard.BorderThickness = new Thickness(1.0);
            BreakChoiceDot.Fill = SessionColorBrush.Create(_colors.Break);
            BreakChoiceDot.Opacity = _isBreakHovered ? 0.75 : 0.40;
            BreakChoiceMode.Foreground = Presentation.ThemeBrush("FkSecondary", isDark);
            BreakDurationNumber.Foreground = Presentation.ThemeBrush("FkSecondary", isDark);
        }

        // 3. Durations
        var durations = _today.Snapshot?.Durations ?? SessionDurations.Default;
        var (workNum, workUnit) = TodayFormatting.FormatLauncherDurationParts(durations.Work);
        var (brkNum, brkUnit) = TodayFormatting.FormatLauncherDurationParts(durations.Break);

        WorkDurationNumber.Text = workNum;
        WorkDurationUnit.Text = workUnit;
        BreakDurationNumber.Text = brkNum;
        BreakDurationUnit.Text = brkUnit;

        // 4. Start Button
        bool canStart = !_today.IsStarting && !_today.IsStopping && !_today.IsRefreshing && _today.Error is null;
        StartIdleButton.Content = "Start";
        StartIdleButton.Background = SessionColorBrush.CreateElevated(selectedColor, isDark, false);
        StartIdleButton.BorderBrush = SessionColorBrush.CreateAlpha(selectedColor, isDark ? 0.70 : 0.50);
        StartIdleButton.BorderThickness = new Thickness(1);
        StartIdleButton.Foreground = Presentation.ThemeBrush("FkForeground", isDark);
        StartIdleButton.IsEnabled = canStart;
        AutomationProperties.SetName(StartIdleButton, isWorkSelected ? "Start Work Session" : "Start Break Session");
    }

    private void Render()
    {
        bool isToday = _today.Page == MainPage.Today;
        TodayNav.IsChecked = isToday;
        ReportsNav.IsChecked = _today.Page == MainPage.Reports;
        SettingsNav.IsChecked = _today.Page == MainPage.Settings;
        PageTitle.Text = _today.Page.ToString();
        PageTitle.Visibility = _today.Page == MainPage.Settings ? Visibility.Visible : Visibility.Collapsed;
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
            FocusValue.Text = Presentation.Duration(snapshot.WorkTime);
            WorkValue.Text = snapshot.CompletedWorkCount.ToString(CultureInfo.InvariantCulture);
            BreakValue.Text = Presentation.Duration(snapshot.BreakTime);
            CompletionValue.Text = snapshot.CompletionRate is { } rate ? rate.ToString("0", CultureInfo.InvariantCulture) + "%" : "—";
            ActivityRows.ItemsSource = snapshot.Sessions.Select(session =>
            {
                string started = TodayFormatting.FormatClockTime(session.StartedAt, snapshot.TimeZone, _timeFormat);
                string duration = session.ActualDuration is { } actual ? Presentation.Duration(actual) : $"{Presentation.Duration(session.PlannedDuration)} planned";
                var row = new Grid { ColumnSpacing = 16, Padding = new Thickness(0, 10, 0, 10) };
                foreach (var width in new[] { new GridLength(44), new GridLength(6), new GridLength(1, GridUnitType.Star), GridLength.Auto, new GridLength(72) })
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
        bool hasActive = _today.Snapshot?.Active is not null;

        // Sidebar active indicator
        if (ActiveIndicator is not null)
            ActiveIndicator.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;
        if (hasActive && ActiveDot is not null)
        {
            var indicatorColor = _today.Snapshot!.Active!.Type == SessionType.Work ? _colors.Work : _colors.Break;
            ActiveDot.Fill = SessionColorBrush.Create(indicatorColor);
            ActiveDot.Opacity = _today.Snapshot!.Active!.Status == SessionStatus.Paused ? 0.45 : 1.0;
        }

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
        ActiveStatusBadge.Foreground = isPaused ? Presentation.ThemeBrush("FkSecondary", isDark) : SessionColorBrush.Create(color);

        RunningText.Text = isPaused
            ? string.Create(CultureInfo.InvariantCulture, $"{(long)remaining.TotalMinutes:00}:{remaining.Seconds:00}")
            : (snapshot.HasReachedPlannedEnd ? "00:00" : string.Create(CultureInfo.InvariantCulture, $"{(long)remaining.TotalMinutes:00}:{remaining.Seconds:00}"));
        RunningText.Foreground = Presentation.ThemeBrush("FkForeground", isDark);

        RunningHint.Text = isPaused ? "paused" : (snapshot.HasReachedPlannedEnd ? "finishing…" : "remaining");
        RunningHint.Foreground = Presentation.ThemeBrush("FkSecondary", isDark);

        SessionProgress.Value = Math.Clamp(100 * snapshot.Elapsed.TotalSeconds / snapshot.PlannedDuration.TotalSeconds, 0.0, 100.0);
        SessionProgress.Foreground = SessionColorBrush.Create(color);
        SessionProgress.Background = Presentation.ThemeBrush("FkSurface2", isDark);
        SessionProgress.Visibility = Visibility.Visible;

        bool canAct = !_today.IsRefreshing && !_today.IsStopping && !_today.IsStarting && _today.Error is null;

        if (isPaused)
        {
            PauseButton.Visibility = Visibility.Collapsed;
            StopButton.Visibility = Visibility.Collapsed;

            ContinueButton.Visibility = Visibility.Visible;
            ContinueButton.IsEnabled = canAct;
            ContinueButton.Background = SessionColorBrush.CreateElevated(color, isDark, false);
            ContinueButton.BorderBrush = SessionColorBrush.CreateAlpha(color, isDark ? 0.70 : 0.50);
            ContinueButton.BorderThickness = new Thickness(1);
            ContinueButton.Foreground = Presentation.ThemeBrush("FkForeground", isDark);

            StartNewButton.Visibility = Visibility.Visible;
            StartNewButton.IsEnabled = canAct;
            StartNewButton.Background = Presentation.ThemeBrush("FkSurface2", isDark);
            StartNewButton.BorderBrush = Presentation.ThemeBrush("FkBorder", isDark);
            StartNewButton.BorderThickness = new Thickness(1);
            StartNewButton.Foreground = Presentation.ThemeBrush("FkSecondary", isDark);
        }
        else
        {
            ContinueButton.Visibility = Visibility.Collapsed;
            StartNewButton.Visibility = Visibility.Collapsed;

            PauseButton.Visibility = Visibility.Visible;
            PauseButton.IsEnabled = canAct;
            PauseButton.Background = SessionColorBrush.CreateElevated(color, isDark, false);
            PauseButton.BorderBrush = SessionColorBrush.CreateAlpha(color, isDark ? 0.70 : 0.50);
            PauseButton.BorderThickness = new Thickness(1);
            PauseButton.Foreground = Presentation.ThemeBrush("FkForeground", isDark);

            StopButton.Visibility = Visibility.Visible;
            StopButton.IsEnabled = canAct;
            StopButton.Background = Presentation.ThemeBrush("FkSurface2", isDark);
            StopButton.BorderBrush = Presentation.ThemeBrush("FkBorder", isDark);
            StopButton.BorderThickness = new Thickness(1);
            StopButton.Foreground = Presentation.ThemeBrush("FkSecondary", isDark);
        }

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

    private void UpdateSidebarDimensions(double windowWidth)
    {
        if (windowWidth <= 0 || NavColumn is null || NavGrid is null) return;

        bool narrow = windowWidth < 740;

        double sidebarWidth;
        double iconContainerSize;
        double iconFontSize;
        double labelFontSize;
        double buttonMinHeight;
        Thickness buttonPadding;
        Thickness navGridPadding;
        double navItemSpacing;
        double navSectionSpacing;
        double itemInnerSpacing;
        double brandFontSize;
        Thickness brandHeaderPadding;

        if (narrow)
        {
            // 1. Narrow / Minimum practical size (< 740 DIP, down to 640-680 DIP)
            // Bounded compact width between 168 DIP and 176 DIP (e.g. 171-172 DIP at 680 DIP)
            double tNarrow = Math.Clamp((windowWidth - 640.0) / 100.0, 0.0, 1.0);
            sidebarWidth = Math.Round(168.0 + 8.0 * tNarrow);

            iconContainerSize = 24;
            iconFontSize = 15.5;
            labelFontSize = 12.5;
            buttonMinHeight = 36;
            buttonPadding = new Thickness(8, 4, 8, 4);
            navGridPadding = new Thickness(8, 16, 8, 12);
            navItemSpacing = 2;
            navSectionSpacing = 16;
            itemInnerSpacing = 8;
            brandFontSize = 12.5;
            brandHeaderPadding = new Thickness(8, 0, 8, 0);
        }
        else if (windowWidth < 1360)
        {
            // 2. Restored / Medium Desktop (740 to 1360 DIP, e.g. 1000x720 window)
            // Bounded intermediate width smoothly scaling between 218 DIP and 244 DIP (228 DIP at 1000 DIP)
            double tRestored = (windowWidth - 740.0) / (1360.0 - 740.0);
            sidebarWidth = Math.Round(218.0 + 26.0 * tRestored);

            iconContainerSize = 28;
            iconFontSize = 17.5;
            labelFontSize = 13.0;
            buttonMinHeight = 40;
            buttonPadding = new Thickness(10, 6, 10, 6);
            navGridPadding = new Thickness(10, 18, 10, 14);
            navItemSpacing = 3;
            navSectionSpacing = 20;
            itemInnerSpacing = 10;
            brandFontSize = 13.0;
            brandHeaderPadding = new Thickness(10, 0, 10, 0);
        }
        else
        {
            // 3. Maximized / Wide Desktop (>= 1360 DIP, up to 1920x1080 and larger)
            // Substantial desktop rail smoothly scaling from 244 DIP to 272 DIP (max bound at 272 DIP)
            double tWide = Math.Clamp((windowWidth - 1360.0) / (1920.0 - 1360.0), 0.0, 1.0);
            sidebarWidth = Math.Round(244.0 + 28.0 * tWide);

            iconContainerSize = 32;
            iconFontSize = 20.0;
            labelFontSize = 13.5;
            buttonMinHeight = 44;
            buttonPadding = new Thickness(12, 6, 12, 6);
            navGridPadding = new Thickness(14, 24, 14, 20);
            navItemSpacing = 4;
            navSectionSpacing = 24;
            itemInnerSpacing = 12;
            brandFontSize = 14.0;
            brandHeaderPadding = new Thickness(12, 0, 12, 0);
        }

        NavColumn.Width = new GridLength(sidebarWidth);
        NavGrid.Padding = navGridPadding;

        if (TopNavSection is not null) TopNavSection.Spacing = navSectionSpacing;
        if (TopNavItems is not null) TopNavItems.Spacing = navItemSpacing;
        if (BottomNavSection is not null) BottomNavSection.Spacing = navItemSpacing;

        if (NavHeaderPanel is not null) NavHeaderPanel.Padding = brandHeaderPadding;
        if (NavBrandTitle is not null) NavBrandTitle.FontSize = brandFontSize;

        ApplyNavItemDimensions(TodayNav, TodayNavContent, TodayIconContainer, TodayIcon, TodayLabel,
            buttonMinHeight, buttonPadding, itemInnerSpacing, iconContainerSize, iconFontSize, labelFontSize);
        ApplyNavItemDimensions(ReportsNav, ReportsNavContent, ReportsIconContainer, ReportsIcon, ReportsLabel,
            buttonMinHeight, buttonPadding, itemInnerSpacing, iconContainerSize, iconFontSize, labelFontSize);
        ApplyNavItemDimensions(OverlayNavButton, OverlayNavContent, OverlayIconContainer, OverlayIcon, OverlayLabel,
            buttonMinHeight, buttonPadding, itemInnerSpacing, iconContainerSize, iconFontSize, labelFontSize);
        ApplyNavItemDimensions(SettingsNav, SettingsNavContent, SettingsIconContainer, SettingsIcon, SettingsLabel,
            buttonMinHeight, buttonPadding, itemInnerSpacing, iconContainerSize, iconFontSize, labelFontSize);
        ApplyNavItemDimensions(ExitButton, ExitNavContent, ExitIconContainer, ExitIcon, ExitLabel,
            buttonMinHeight, buttonPadding, itemInnerSpacing, iconContainerSize, iconFontSize, labelFontSize);
    }

    private static void ApplyNavItemDimensions(
        Control? button, StackPanel? content, Border? iconContainer, FontIcon? icon, TextBlock? label,
        double minHeight, Thickness padding, double innerSpacing, double containerSize, double iconSize, double labelSize)
    {
        if (button is not null)
        {
            button.MinHeight = minHeight;
            button.Padding = padding;
        }
        if (content is not null)
        {
            content.Spacing = innerSpacing;
        }
        if (iconContainer is not null)
        {
            iconContainer.Width = containerSize;
            iconContainer.Height = containerSize;
        }
        if (icon is not null)
        {
            icon.FontSize = iconSize;
        }
        if (label is not null)
        {
            label.FontSize = labelSize;
        }
    }
}
