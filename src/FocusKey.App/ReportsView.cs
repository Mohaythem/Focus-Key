using FocusKey.Foundation.Reports;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace FocusKey;

/// <summary>Basic native Reports presentation; calculations and refresh ordering live in Foundation.</summary>
internal sealed class ReportsView : UserControl, IDisposable
{
    private readonly ReportsController _reports;
    private readonly ComboBox _period = new() { Header = "View", ItemsSource = Enum.GetNames<ReportPeriod>(), SelectedIndex = 1 };
    private readonly CalendarDatePicker _date = new() { Header = "Date in reporting period" };
    private readonly TextBlock _status = Text();
    private readonly StackPanel _results = new() { Spacing = 16 };
    private bool _rendering;

    internal ReportsView(ReportsService service, Action<Exception> report)
    {
        _reports = new((period, date, token) => Task.Run(() => service.ReadAsync(period, date, token), token), service.CurrentDate, report);
        _date.MinDate = DateValue(ReportRange.MinimumDate);
        _date.MaxDate = DateValue(ReportRange.MaximumDate);
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(_period);
        panel.Children.Add(_date);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(Button("Previous", () => _reports.MoveAsync(-1)));
        controls.Children.Add(Button("Next", () => _reports.MoveAsync(1)));
        controls.Children.Add(Button("Current period", _reports.CurrentAsync));
        controls.Children.Add(Button("Refresh reports", _reports.RefreshAsync));
        panel.Children.Add(controls);
        panel.Children.Add(_status);
        panel.Children.Add(_results);
        Content = panel;
        _period.SelectionChanged += async (_, _) =>
        {
            if (!_rendering && _period.SelectedIndex >= 0)
                await _reports.SelectAsync((ReportPeriod)_period.SelectedIndex, _reports.Date);
        };
        _date.DateChanged += async (_, _) =>
        {
            if (!_rendering && _date.Date is { } date)
                await _reports.SelectAsync(_reports.Period, DateOnly.FromDateTime(date.DateTime));
        };
        _reports.Changed += Render;
        Render();
    }

    internal Task OpenAsync() => _reports.OpenAsync();
    internal void Hide() => _reports.Hide();
    internal Task RefreshAsync() => _reports.RefreshAsync();
    public void Dispose() => _reports.Dispose();

    private void Render()
    {
        _rendering = true;
        _period.SelectedIndex = (int)_reports.Period;
        _date.Date = DateValue(_reports.Date);
        _rendering = false;
        _status.Text = _reports.IsRefreshing ? "Loading reports…" : _reports.Error ?? string.Empty;
        _results.Children.Clear();
        if (_reports.Snapshot is not { } snapshot) return;
        var totals = snapshot.Totals;
        _results.Children.Add(Text($"{snapshot.Range.Start:yyyy-MM-dd} – {snapshot.Range.End.AddDays(-1):yyyy-MM-dd}\n" +
            $"{snapshot.TimeZone.DisplayName} · Weeks start Monday.\nSessions and their full durations belong to their local start date."));
        if (totals.Started == 0) _results.Children.Add(Text("No sessions started in this period."));
        _results.Children.Add(Text($"Completed focus time: {Duration(totals.FocusTime)}\n" +
            $"Completed Work sessions: {totals.CompletedWork} of {totals.WorkStarted} started\n" +
            $"Completed Break sessions: {totals.CompletedBreak} of {totals.BreakStarted} started\n" +
            $"Completed Break time: {Duration(totals.BreakTime)}\n" +
            $"Completion rate: {(totals.CompletionRate is { } rate ? $"{rate:0.#}%" : "—")} ({totals.Completed} of {totals.Started} sessions)\n" +
            $"Stopped: {totals.Stopped} · Interrupted: {totals.Interrupted} · Running: {totals.Running}", 18));
        _results.Children.Add(Text(totals.WorkShare is { } share ?
            $"Completed time balance: Work {share:0.#}% · Break {100 - share:0.#}%" : "Completed time balance: —"));

        _results.Children.Add(Text("Completed time trend", 20));
        _results.Children.Add(Text("Bars: Work above Break; both use the same time scale within this view."));
        _results.Children.Add(Text(snapshot.Period switch
        {
            ReportPeriod.Daily => "By local start hour (repeated DST hours combined).",
            ReportPeriod.Weekly => "By local start day.",
            _ => "By Monday–Sunday calendar week, clipped to this month."
        }));
        double maximum = Math.Max(1, snapshot.Trend.Max(b => Math.Max(b.Totals.FocusTime.TotalSeconds, b.Totals.BreakTime.TotalSeconds)));
        foreach (var bucket in snapshot.Trend)
        {
            var row = new StackPanel { Spacing = 4 };
            row.Children.Add(Text($"{bucket.Label} · Work {Duration(bucket.Totals.FocusTime)} · Break {Duration(bucket.Totals.BreakTime)}"));
            row.Children.Add(Bar(bucket.Totals.FocusTime.TotalSeconds, maximum, "Work"));
            row.Children.Add(Bar(bucket.Totals.BreakTime.TotalSeconds, maximum, "Break"));
            _results.Children.Add(row);
        }
        _results.Children.Add(Text("Focus period pattern", 20));
        _results.Children.Add(Text(snapshot.LeadingFocusPeriods.Count == 0 ? "No completed Work sessions in this period." :
            $"Most completed Work starts in fixed three-hour periods: {string.Join(", ", snapshot.LeadingFocusPeriods.Select(p => $"{p.StartHour:00}:00–{p.StartHour + 3:00}:00 ({p.CompletedWork})"))}.\n" +
            "All tied periods are shown. This describes start times, not a productivity score."));
        _results.Children.Add(Text("Weekly comparison", 20));
        _results.Children.Add(Text($"Week containing selected date ({snapshot.ComparisonWeek.Start:yyyy-MM-dd} – {snapshot.ComparisonWeek.End.AddDays(-1):yyyy-MM-dd}): {Duration(snapshot.WeekFocus)}\n" +
            $"Previous week ({snapshot.ComparisonWeek.Start.AddDays(-7):yyyy-MM-dd} – {snapshot.ComparisonWeek.Start.AddDays(-1):yyyy-MM-dd}): {Duration(snapshot.PreviousWeekFocus)}\n" +
            $"Difference: {(snapshot.WeekDifference < TimeSpan.Zero ? "−" : "+")}{Duration(snapshot.WeekDifference.Duration())}\n" +
            "Full calendar weeks; the current week may be incomplete."));
    }

    private static TextBlock Text(string value = "", double size = 14) => new()
        { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = size, IsTextSelectionEnabled = true };
    private static Button Button(string label, Func<Task> action)
    {
        var button = new Button { Content = label };
        button.Click += async (_, _) => await action();
        return button;
    }
    private static ProgressBar Bar(double value, double max, string name)
    {
        var bar = new ProgressBar { Minimum = 0, Maximum = max, Value = value };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(bar, $"{name}: {value:0} seconds");
        return bar;
    }
    private static DateTimeOffset DateValue(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    private static string Duration(TimeSpan duration) => duration.TotalHours >= 1 ?
        $"{(long)duration.TotalHours}h {duration.Minutes:00}m {duration.Seconds:00}s" : $"{(long)duration.TotalMinutes}m {duration.Seconds:00}s";
}
