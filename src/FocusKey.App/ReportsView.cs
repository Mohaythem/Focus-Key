using System.Globalization;
using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;

namespace FocusKey;

/// <summary>Native Reports presentation; calculations and refresh ordering live in Foundation.</summary>
internal sealed class ReportsView : UserControl, IDisposable
{
    private readonly ReportsController _reports;
    private readonly StackPanel _periodSelector = new() { Orientation = Orientation.Horizontal, Spacing = 0 };
    private readonly CalendarDatePicker _date = new()
    {
        Width = 140,
        Height = 32,
        FontSize = 12,
        Language = "en-US",
        FlowDirection = FlowDirection.LeftToRight,
        CalendarIdentifier = Windows.Globalization.CalendarIdentifiers.Gregorian,
        DateFormat = "{year.full}-{month.integer(2)}-{day.integer(2)}",
        VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _status;
    private readonly StackPanel _results = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    private bool _rendering;
    private SessionColors _colors = SessionColors.From(ApplicationSettings.Default);
    internal void ApplyColors(SessionColors colors) { _colors = colors; Render(); }

    internal ReportsView(ReportsService service, Action<Exception> report)
    {
        Language = "en-US";
        FlowDirection = FlowDirection.LeftToRight;
        HorizontalAlignment = HorizontalAlignment.Stretch;

        _reports = new((period, date, token) => Task.Run(() => service.ReadAsync(period, date, token), token), service.CurrentDate, report);
        _status = Presentation.Text("", 12, true);
        _status.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _date.MinDate = DateValue(ReportRange.MinimumDate); _date.MaxDate = DateValue(ReportRange.MaximumDate);
        AutomationProperties.SetName(_date, "Date in reporting period");

        // Build segmented period selector per reference
        BuildPeriodSelector();

        var panel = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };

        // Header row: segmented period selector + navigation controls
        var headerGrid = new Grid { ColumnSpacing = 12, RowSpacing = 8 };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        headerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        headerGrid.Children.Add(_periodSelector);

        var navRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        navRow.Children.Add(NavButton("‹", () => _reports.MoveAsync(-1), "Previous period"));
        navRow.Children.Add(_date);
        navRow.Children.Add(NavButton("›", () => _reports.MoveAsync(1), "Next period"));
        navRow.Children.Add(NavButton("Today", _reports.CurrentAsync, "Current period"));
        navRow.Children.Add(NavButton("↻", _reports.RefreshAsync, "Refresh reports"));
        Grid.SetColumn(navRow, 1);
        headerGrid.Children.Add(navRow);

        headerGrid.SizeChanged += (_, _) =>
        {
            bool narrow = headerGrid.ActualWidth > 0 && headerGrid.ActualWidth < 540;
            Grid.SetRow(navRow, narrow ? 1 : 0);
            Grid.SetColumn(navRow, narrow ? 0 : 1);
            Grid.SetColumnSpan(navRow, narrow ? 2 : 1);
            navRow.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        };

        panel.Children.Add(headerGrid);
        panel.Children.Add(_status);
        panel.Children.Add(_results);
        Content = panel;

        _date.DateChanged += async (_, _) => { if (!_rendering && _date.Date is { } date) await _reports.SelectAsync(_reports.Period, DateOnly.FromDateTime(date.DateTime)); };
        _reports.Changed += Render;
        ActualThemeChanged += (_, _) =>
        {
            UpdatePeriodHighlight();
            Render();
        };
        Render();
    }

    private void BuildPeriodSelector()
    {
        _periodSelector.Children.Clear();
        var border = new Border
        {
            Style = (Style)Application.Current.Resources["FkSegmentContainer"],
        };
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var period in Enum.GetValues<ReportPeriod>())
        {
            var btn = new Button
            {
                Content = period.ToString(),
                Tag = period,
                Style = (Style)Application.Current.Resources["FkSegmentInactive"],
            };
            AutomationProperties.SetName(btn, $"{period} reports");
            btn.Click += async (s, _) =>
            {
                if (!_rendering && s is Button b && b.Tag is ReportPeriod p)
                    await _reports.SelectAsync(p, _reports.Date);
            };
            stack.Children.Add(btn);
        }
        border.Child = stack;
        _periodSelector.Children.Add(border);
    }

    private void UpdatePeriodHighlight()
    {
        if (_periodSelector.Children.Count == 0) return;
        var border = (Border)_periodSelector.Children[0];
        var stack = (StackPanel)border.Child;
        var activeStyle = (Style)Application.Current.Resources["FkSegmentActive"];
        var inactiveStyle = (Style)Application.Current.Resources["FkSegmentInactive"];
        foreach (Button btn in stack.Children.Cast<Button>())
        {
            bool active = btn.Tag is ReportPeriod p && p == _reports.Period;
            btn.Style = active ? activeStyle : inactiveStyle;
        }
    }

    internal Task OpenAsync() => _reports.OpenAsync(); internal void Hide() => _reports.Hide(); internal Task RefreshAsync() => _reports.RefreshAsync(); public void Dispose() => _reports.Dispose();

    private void Render()
    {
        _rendering = true;
        _date.Date = DateValue(_reports.Date);
        UpdatePeriodHighlight();
        _rendering = false;
        _status.Text = _reports.IsRefreshing ? "Loading reports…" : _reports.Error ?? string.Empty;
        _results.Children.Clear();

        if (_reports.Snapshot is not { } snapshot)
        {
            if (!_reports.IsRefreshing && _reports.Error is null)
                _results.Children.Add(Card(Presentation.Text("Choose a reporting period to view completed sessions.")));
            return;
        }

        var totals = snapshot.Totals;

        // Date range subtitle
        _results.Children.Add(Presentation.DimText(
            string.Create(CultureInfo.InvariantCulture, $"{snapshot.Range.Start:yyyy-MM-dd} – {snapshot.Range.End.AddDays(-1):yyyy-MM-dd}  ·  {snapshot.TimeZone.DisplayName}")));

        // 3-column metric tiles with semantic accents
        var metrics = new Grid { ColumnSpacing = 10 };
        for (var i = 0; i < 3; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metrics.Children.Add(Metric("Focus Time", Duration(totals.FocusTime), "work sessions", 0, _colors.Work));
        metrics.Children.Add(Metric("Break Time", Duration(totals.BreakTime), "break sessions", 1, _colors.Break));
        metrics.Children.Add(Metric("Completion Rate", totals.CompletionRate is { } rate ? string.Create(CultureInfo.InvariantCulture, $"{rate:0.#}%") : "—", "sessions finished", 2, null));
        _results.Children.Add(metrics);

        if (totals.Started == 0)
            _results.Children.Add(Card(Presentation.Text("No sessions started in this period. Completed time and trends will appear here once a session is recorded.")));

        _results.Children.Add(ChartCard(snapshot));

        // Lower section: 2-column balanced layout with subtle secondary surface hierarchy
        var lowerGrid = new Grid { ColumnSpacing = 12, RowSpacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
        lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lowerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        lowerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        lowerGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var insight = InsightCard(snapshot);
        var outcomes = OutcomesCard(totals);
        Grid.SetColumn(insight, 0);
        Grid.SetColumn(outcomes, 1);
        lowerGrid.Children.Add(insight);
        lowerGrid.Children.Add(outcomes);

        lowerGrid.SizeChanged += (_, _) =>
        {
            bool stackCols = lowerGrid.ActualWidth > 0 && lowerGrid.ActualWidth < 560;
            Grid.SetColumn(outcomes, stackCols ? 0 : 1);
            Grid.SetRow(outcomes, stackCols ? 1 : 0);
            Grid.SetColumnSpan(insight, stackCols ? 2 : 1);
            Grid.SetColumnSpan(outcomes, stackCols ? 2 : 1);
        };

        _results.Children.Add(lowerGrid);
    }

    private UIElement ChartCard(ReportsSnapshot snapshot)
    {
        var body = new StackPanel { Spacing = 12 };

        // Header: title + legend
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = Presentation.Text("FOCUS ACTIVITY", 11, true);
        title.Style = (Style)Application.Current.Resources["FkSectionText"];
        header.Children.Add(title);

        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        legend.Children.Add(Swatch("Work", _colors.Work));
        legend.Children.Add(Swatch("Break", _colors.Break));
        Grid.SetColumn(legend, 1);
        header.Children.Add(legend);

        body.Children.Add(header);

        var max = snapshot.Trend.Count == 0 ? 0 : snapshot.Trend.Max(b => Math.Max(b.Totals.FocusTime.TotalSeconds, b.Totals.BreakTime.TotalSeconds));
        var chart = new ReportsChart(snapshot.Trend, max, _colors);
        AutomationProperties.SetName(chart, "Completed time trend. Expand time details for every period.");
        body.Children.Add(chart);

        var details = new Expander
        {
            Header = "View time details",
            IsExpanded = false,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 4, 0, 0)
        };
        var rows = new StackPanel { Spacing = 4 };
        foreach (var b in snapshot.Trend)
            rows.Children.Add(Presentation.DimText(string.Create(CultureInfo.InvariantCulture, $"{b.Label}: Work {Duration(b.Totals.FocusTime)}, Break {Duration(b.Totals.BreakTime)}")));
        details.Content = rows;
        body.Children.Add(details);

        return Card(body, 20);
    }

    private FrameworkElement InsightCard(ReportsSnapshot s)
    {
        var body = new StackPanel { Spacing = 12 };

        // Header
        var title = Presentation.DimText("BALANCE & PATTERNS");
        title.Style = (Style)Application.Current.Resources["FkSectionText"];
        body.Children.Add(title);

        // Work/Break Balance with visual ratio bar
        var balanceStack = new StackPanel { Spacing = 6 };
        balanceStack.Children.Add(Presentation.Text("Work / Break Balance", 12));
        if (s.Totals.WorkShare is { } share)
        {
            double workPct = Math.Clamp(share, 0, 100);
            double breakPct = 100 - workPct;
            var barGrid = new Grid { Height = 8, CornerRadius = new CornerRadius(4) };
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(workPct, GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(breakPct, GridUnitType.Star) });
            var workBar = new Border { Background = SessionColorBrush.Create(_colors.Work), CornerRadius = new CornerRadius(4, 0, 0, 4) };
            var breakBar = new Border { Background = SessionColorBrush.Create(_colors.Break), CornerRadius = new CornerRadius(0, 4, 4, 0) };
            if (breakPct <= 0) workBar.CornerRadius = new CornerRadius(4);
            if (workPct <= 0) breakBar.CornerRadius = new CornerRadius(4);
            Grid.SetColumn(workBar, 0);
            Grid.SetColumn(breakBar, 1);
            barGrid.Children.Add(workBar);
            barGrid.Children.Add(breakBar);
            balanceStack.Children.Add(barGrid);
            balanceStack.Children.Add(Presentation.DimText(
                string.Create(CultureInfo.InvariantCulture, $"Work {share:0.#}%  ·  Break {100 - share:0.#}%"), 11));
        }
        else
        {
            balanceStack.Children.Add(Presentation.DimText("No completed time in this period.", 11));
        }
        body.Children.Add(balanceStack);

        // Focus pattern
        var patternStack = new StackPanel { Spacing = 4 };
        patternStack.Children.Add(Presentation.Text("Peak Focus Time", 12));
        var pattern = s.LeadingFocusPeriods.Count == 0
            ? "No completed Work sessions recorded yet."
            : string.Create(CultureInfo.InvariantCulture, $"Most completed Work: {string.Join(", ", s.LeadingFocusPeriods.Select(x => string.Create(CultureInfo.InvariantCulture, $"{x.StartHour:00}:00–{x.StartHour + 3:00}:00 ({x.CompletedWork})")))}");
        patternStack.Children.Add(Presentation.DimText(pattern, 11));
        body.Children.Add(patternStack);

        // Weekly comparison
        var weekStack = new StackPanel { Spacing = 4 };
        weekStack.Children.Add(Presentation.Text("Weekly Comparison", 12));
        var weekDiffStr = s.WeekDifference < TimeSpan.Zero ? $"−{Duration(s.WeekDifference.Duration())}" : $"+{Duration(s.WeekDifference.Duration())}";
        weekStack.Children.Add(Presentation.DimText(
            string.Create(CultureInfo.InvariantCulture, $"This week: {Duration(s.WeekFocus)}  ·  Previous: {Duration(s.PreviousWeekFocus)}  ·  {weekDiffStr}"), 11));
        body.Children.Add(weekStack);

        return Presentation.CardSubtle(body, 20);
    }

    private FrameworkElement OutcomesCard(ReportTotals totals)
    {
        var body = new StackPanel { Spacing = 12 };

        var title = Presentation.DimText("SESSION OUTCOMES");
        title.Style = (Style)Application.Current.Resources["FkSectionText"];
        body.Children.Add(title);

        // Work sessions completed / started
        var workStack = new StackPanel { Spacing = 4 };
        workStack.Children.Add(Presentation.Text("Work Sessions", 12));
        workStack.Children.Add(Presentation.DimText(
            string.Create(CultureInfo.InvariantCulture, $"{totals.CompletedWork} completed of {totals.WorkStarted} started"), 11));
        body.Children.Add(workStack);

        // Break sessions completed / started
        var breakStack = new StackPanel { Spacing = 4 };
        breakStack.Children.Add(Presentation.Text("Break Sessions", 12));
        breakStack.Children.Add(Presentation.DimText(
            string.Create(CultureInfo.InvariantCulture, $"{totals.CompletedBreak} completed of {totals.BreakStarted} started"), 11));
        body.Children.Add(breakStack);

        // Session status counts
        var statusGrid = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        statusGrid.Children.Add(OutcomeStatusTile("Completed", totals.CompletedWork + totals.CompletedBreak, FocusKey.Foundation.Sessions.SessionStatus.Completed, 0));
        statusGrid.Children.Add(OutcomeStatusTile("Stopped", totals.Stopped, FocusKey.Foundation.Sessions.SessionStatus.Stopped, 1));
        statusGrid.Children.Add(OutcomeStatusTile("Interrupted", totals.Interrupted, FocusKey.Foundation.Sessions.SessionStatus.Interrupted, 2));
        body.Children.Add(statusGrid);

        return Presentation.CardSubtle(body, 20);
    }

    private static FrameworkElement OutcomeStatusTile(string label, int count, FocusKey.Foundation.Sessions.SessionStatus status, int column)
    {
        var p = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center };
        var num = new TextBlock
        {
            Text = count.ToString(CultureInfo.InvariantCulture),
            FontSize = 18,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Language = "en-US",
            FlowDirection = FlowDirection.LeftToRight,
            Style = (Style)Application.Current.Resources["FkText"]
        };
        p.Children.Add(num);
        var lbl = Presentation.StatusText(status, 10);
        lbl.HorizontalAlignment = HorizontalAlignment.Center;
        p.Children.Add(lbl);

        var border = new Border
        {
            Child = p,
            Padding = new Thickness(10, 8, 10, 8),
            Style = (Style)Application.Current.Resources["FkBadge"]
        };
        Grid.SetColumn(border, column);
        return border;
    }

    private UIElement Metric(string label, string value, string sub, int column, HexColor? dotColor)
    {
        var p = new StackPanel { Spacing = 4 };
        var valueText = new TextBlock
        {
            Text = value,
            FontSize = 28,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            Language = "en-US",
            FlowDirection = FlowDirection.LeftToRight,
            TextReadingOrder = TextReadingOrder.UseFlowDirection,
        };
        if (Application.Current?.Resources["FkMetricText"] is Style metricStyle)
        {
            valueText.Style = metricStyle;
            valueText.FontSize = 28;
        }
        p.Children.Add(valueText);
        var labelRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        if (dotColor is { } color)
        {
            labelRow.Children.Add(new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = SessionColorBrush.Create(color),
                VerticalAlignment = VerticalAlignment.Center
            });
        }
        var labelText = Presentation.Text(label, 12);
        labelText.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        labelRow.Children.Add(labelText);
        p.Children.Add(labelRow);
        p.Children.Add(Presentation.DimText(sub, 11));
        var b = Card(p, 20);
        Grid.SetColumn(b, column);
        return b;
    }

    private static UIElement Swatch(string label, FocusKey.Foundation.Settings.HexColor color)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5 };
        p.Children.Add(new Border { Width = 8, Height = 8, Background = SessionColorBrush.Create(color), CornerRadius = new CornerRadius(1) });
        p.Children.Add(Presentation.Text(label, 10, true));
        return p;
    }

    private static Border Card(UIElement child, double padding = 20)
    {
        var b = new Border { Child = child, Padding = new Thickness(padding) };
        if (Application.Current?.Resources["FkCard"] is Style s) b.Style = s;
        return b;
    }

    private static Button NavButton(string label, Func<Task> action, string name)
    {
        var b = new Button
        {
            Content = label,
            MinWidth = 32,
            Height = 32,
            FontSize = 12,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetName(b, name);
        b.Click += async (_, _) => await action();
        return b;
    }

    private static DateTimeOffset DateValue(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    internal static string Duration(TimeSpan d) => Presentation.Duration(d);
}
