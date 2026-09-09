using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Today;
using FocusKey.Startup;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace FocusKey;

/// <summary>Functional Today, Reports and Settings surfaces.</summary>
public sealed partial class MainWindow : Window
{
    private readonly TodayController _today;
    private readonly ReportsView _reports;
    private readonly SettingsView _settings;
    private readonly DispatcherQueueTimer _displayTimer;
    private bool _visible;
    private SessionColors _colors = SessionColors.From(ApplicationSettings.Default);
    internal void ApplyColors(SessionColors colors)
    {
        _colors = colors;
        _reports.ApplyColors(colors);
        RenderRunning();
    }
    internal event Action? MiniTimerRequested;
    private void OnMiniTimerClick(object sender, RoutedEventArgs args) => MiniTimerRequested?.Invoke();
    internal event Action? ExitRequested;

    internal void ApplyAppearance(Appearance appearance) =>
        WindowAppearance.Apply(MainSurface, AppWindow, appearance);

    internal MainWindow(StartupContext startup,
        Func<SessionId, CancellationToken, Task<SessionOutcome>> stop, Action<Exception> report,
        Func<Task> refreshSettings)
    {
        InitializeComponent();
        _reports = new ReportsView(startup.Reports, report);
        ReportsHost.Content = _reports;
        _settings = new SettingsView(startup.Settings, refreshSettings, report);
        SettingsHost.Content = _settings;
        AppWindow.Resize(new SizeInt32(900, 720));
        _today = new TodayController(startup.Today.ReadAsync, stop, report);
        _today.Changed += Render;
        _displayTimer = DispatcherQueue.CreateTimer();
        _displayTimer.IsRepeating = false;
        _displayTimer.Tick += OnDisplayTick;
        Closed += (_, _) => { _visible = false; _displayTimer.Stop(); _today.Dispose(); _reports.Dispose(); };
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
        _settings.CommitPendingDurations();
        _settings.IsEnabled = false;
        return _settings.FlushAsync();
    }
    internal void ResumeSettings() => _settings.IsEnabled = true;

    private async void OnTodayClick(object sender, RoutedEventArgs args) { _settings.CommitPendingDurations(); _reports.Hide(); await _today.NavigateAsync(MainPage.Today); }
    private async void OnReportsClick(object sender, RoutedEventArgs args)
    {
        _settings.CommitPendingDurations();
        await _today.NavigateAsync(MainPage.Reports);
        await _reports.OpenAsync();
    }
    private async void OnSettingsClick(object sender, RoutedEventArgs args)
    {
        _settings.CommitPendingDurations();
        _reports.Hide();
        await _today.NavigateAsync(MainPage.Settings);
        await _settings.OpenAsync();
    }
    private async void OnRefreshClick(object sender, RoutedEventArgs args) => await _today.RefreshAsync();
    private async void OnStopClick(object sender, RoutedEventArgs args) => await _today.StopAsync();
    private void OnExitClick(object sender, RoutedEventArgs args) => ExitRequested?.Invoke();

    private void Render()
    {
        bool isToday = _today.Page == MainPage.Today;
        TodayNav.IsChecked = isToday;
        ReportsNav.IsChecked = _today.Page == MainPage.Reports;
        SettingsNav.IsChecked = _today.Page == MainPage.Settings;
        PageTitle.Text = _today.Page.ToString();
        TodayPanel.Visibility = isToday ? Visibility.Visible : Visibility.Collapsed;
        SettingsHost.Visibility = _today.Page == MainPage.Settings ? Visibility.Visible : Visibility.Collapsed;
        ReportsHost.Visibility = _today.Page == MainPage.Reports ? Visibility.Visible : Visibility.Collapsed;
        LoadStatus.Text = _today.IsRefreshing ? "Loading…" : _today.IsStopping ? "Stopping…" : string.Empty;
        RefreshButton.IsEnabled = !_today.IsRefreshing && !_today.IsStopping;
        ErrorText.Text = _today.Error ?? string.Empty;
        ErrorText.Visibility = _today.Error is null ? Visibility.Collapsed : Visibility.Visible;
        if (_today.Snapshot is { } snapshot)
        {
            DayLabel.Text = $"{snapshot.Date:dddd, d MMMM yyyy} · {snapshot.TimeZone.DisplayName}\nSessions grouped by local start date.";
            SummaryText.Text = $"Completed Work sessions: {snapshot.CompletedWorkCount}\n" +
                $"Completed Break sessions: {snapshot.CompletedBreakCount}\n" +
                $"Focus time: {Duration(snapshot.WorkTime)}\nBreak time: {Duration(snapshot.BreakTime)}\n" +
                $"Completion rate: {(snapshot.CompletionRate is { } rate ? $"{rate:0}%" : "—")}";
            ActivityRows.ItemsSource = snapshot.Sessions.Select(session =>
            {
                string started = TimeZoneInfo.ConvertTime(session.StartedAt, snapshot.TimeZone).ToString("HH:mm");
                string duration = session.ActualDuration is { } actual ? Duration(actual) : $"{Duration(session.PlannedDuration)} planned";
                return $"{started}   {session.Type}   {session.Status}   {duration}";
            }).ToArray();
            EmptyActivity.Visibility = snapshot.Sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        RenderRunning();
        ScheduleDisplay();
    }

    private void RenderRunning()
    {
        if (_today.Snapshot?.Running is not { } running)
        {
            RunningText.Text = "No running session.";
            SessionProgress.Visibility = StopButton.Visibility = Visibility.Collapsed;
            return;
        }
        var snapshot = SessionSnapshot.For(running, DateTimeOffset.UtcNow);
        var remaining = TimeSpan.FromSeconds(Math.Ceiling(snapshot.Remaining.TotalSeconds));
        RunningText.Text = snapshot.HasReachedPlannedEnd ? $"{running.Type} · Finishing…" :
            $"{running.Type} · {(long)remaining.TotalMinutes:00}:{remaining.Seconds:00} remaining";
        SessionProgress.Value = 100 * snapshot.Elapsed.TotalSeconds / snapshot.PlannedDuration.TotalSeconds;
        SessionProgress.Foreground = SessionColorBrush.Create(running.Type == SessionType.Work ? _colors.Work : _colors.Break);
        SessionProgress.Visibility = StopButton.Visibility = Visibility.Visible;
        StopButton.IsEnabled = !_today.IsRefreshing && !_today.IsStopping && _today.Error is null;
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
