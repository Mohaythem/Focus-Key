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
    private readonly TextBlock _dateSubtitle;
    private readonly TextBlock _status;
    private readonly StackPanel _results = new() { Spacing = 12, HorizontalAlignment = HorizontalAlignment.Stretch };
    private bool _rendering;
    private SessionColors _colors = SessionColors.From(ApplicationSettings.Default);
    private Contrast _contrast = Contrast.Standard;
    internal void ApplyColors(SessionColors colors) { _colors = colors; Render(); }

    internal ReportsView(ReportsService service, Action<Exception> report)
    {
        Language = "en-US";
        FlowDirection = FlowDirection.LeftToRight;
        HorizontalAlignment = HorizontalAlignment.Stretch;

        _reports = new((period, date, token) => Task.Run(() => service.ReadAsync(period, date, token), token), service.CurrentDate, report);
        _dateSubtitle = Presentation.DimText("", 11);
        _status = Presentation.Text("", 12, true);
        _status.TextWrapping = TextWrapping.Wrap;
        AutomationProperties.SetLiveSetting(_status, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        _date.MinDate = DateValue(ReportRange.MinimumDate); _date.MaxDate = DateValue(ReportRange.MaximumDate);
        AutomationProperties.SetName(_date, "Date in reporting period");

        // Build segmented period selector per reference
        BuildPeriodSelector();

        var panel = new StackPanel { Spacing = 14, HorizontalAlignment = HorizontalAlignment.Stretch };

        // 1. Top header row: "Reports" title on left, segmented period selector on right (exact Figma layout)
        var topHeader = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 4) };
        topHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = Presentation.Text("Reports", 16);
        title.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        title.CharacterSpacing = -10;
        topHeader.Children.Add(title);

        _periodSelector.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_periodSelector, 1);
        topHeader.Children.Add(_periodSelector);

        // 2. Sub-header row: date range description on left, navigation controls on right
        var navGrid = new Grid { ColumnSpacing = 12, RowSpacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        navGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        navGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _dateSubtitle.VerticalAlignment = VerticalAlignment.Center;
        navGrid.Children.Add(_dateSubtitle);

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
        navGrid.Children.Add(navRow);

        navGrid.SizeChanged += (_, _) =>
        {
            bool narrow = navGrid.ActualWidth > 0 && navGrid.ActualWidth < 540;
            Grid.SetRow(navRow, narrow ? 1 : 0);
            Grid.SetColumn(navRow, narrow ? 0 : 1);
            Grid.SetColumnSpan(navRow, narrow ? 2 : 1);
            navRow.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        };

        panel.Children.Add(topHeader);
        panel.Children.Add(navGrid);
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
    internal void RefreshVisuals(Contrast? contrast = null)
    {
        if (contrast.HasValue) _contrast = contrast.Value;
        UpdatePeriodHighlight();
        Render();
    }

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
            _dateSubtitle.Text = string.Empty;
            if (!_reports.IsRefreshing && _reports.Error is null)
                _results.Children.Add(Card(Presentation.Text("Choose a reporting period to view completed sessions.")));
            return;
        }

        var totals = snapshot.Totals;

        // Date range subtitle in subheader
        string rangeText = snapshot.Period == ReportPeriod.Daily
            ? string.Create(CultureInfo.InvariantCulture, $"{snapshot.Range.Start:yyyy-MM-dd}")
            : string.Create(CultureInfo.InvariantCulture, $"{snapshot.Range.Start:yyyy-MM-dd} – {snapshot.Range.End.AddDays(-1):yyyy-MM-dd}");
        _dateSubtitle.Text = $"{rangeText}  ·  {snapshot.TimeZone.DisplayName}";

        // 3-column metric tiles matching Figma layout
        var metrics = new Grid { ColumnSpacing = 8 };
        for (var i = 0; i < 3; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        metrics.Children.Add(Metric("Focus Time", Duration(totals.FocusTime), "work sessions", 0));
        metrics.Children.Add(Metric("Break Time", Duration(totals.BreakTime), "break sessions", 1));
        metrics.Children.Add(Metric("Completion Rate", totals.CompletionRate is { } rate ? string.Create(CultureInfo.InvariantCulture, $"{rate:0.#}%") : "—", "sessions finished", 2));
        _results.Children.Add(metrics);
        _results.Children.Add(ChartCard(snapshot));
        _results.Children.Add(InsightCard(snapshot));
    }

    private UIElement ChartCard(ReportsSnapshot snapshot)
    {
        var body = new StackPanel { Spacing = 14 };

        // Header: title + legend
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = Presentation.Text("FOCUS ACTIVITY", 11, true);
        title.Style = (Style)Application.Current.Resources["FkSectionText"];
        header.Children.Add(title);

        var palette = ReportsPalette.Resolve(ActualTheme, _contrast);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16 };
        legend.Children.Add(Swatch("Work", palette.Work));
        legend.Children.Add(Swatch("Break", palette.Break));
        Grid.SetColumn(legend, 1);
        header.Children.Add(legend);

        body.Children.Add(header);

        var max = snapshot.Trend.Count == 0 ? 0 : snapshot.Trend.Max(b => Math.Max(b.Totals.FocusTime.TotalSeconds, b.Totals.BreakTime.TotalSeconds));
        var chart = new ReportsChart(snapshot.Trend, max, palette);
        AutomationProperties.SetName(chart, "Focus activity trend chart");
        body.Children.Add(chart);

        // Concise summary line replaces the verbose Expander; per-bucket values remain in bar tooltips.
        var totals = snapshot.Totals;
        if (totals.FocusTime > TimeSpan.Zero || totals.BreakTime > TimeSpan.Zero)
        {
            var summary = Presentation.DimText(
                string.Create(CultureInfo.InvariantCulture,
                    $"Total: {Duration(totals.FocusTime)} focus · {Duration(totals.BreakTime)} break"), 11);
            summary.Margin = new Thickness(0, 2, 0, 0);
            body.Children.Add(summary);
        }

        return Card(body, 20);
    }

    private FrameworkElement InsightCard(ReportsSnapshot s)
    {
        var body = new StackPanel { Spacing = 6 };

        var title = Presentation.DimText("INSIGHT");
        title.Style = (Style)Application.Current.Resources["FkSectionText"];
        body.Children.Add(title);

        string insightText = GenerateInsightText(s);
        var content = Presentation.Text(insightText, 12);
        if (Application.Current?.Resources["FkMutedText"] is Style muted) content.Style = muted;
        content.TextWrapping = TextWrapping.Wrap;
        content.LineHeight = 20;
        body.Children.Add(content);

        return Card(body, 16);
    }

    private static string GenerateInsightText(ReportsSnapshot s)
    {
        if (s.Totals.Started == 0)
            return "No sessions recorded in this period. Completed time and focus patterns will appear here once a session is recorded.";

        if (s.Totals.Completed == 0)
            return string.Create(CultureInfo.InvariantCulture,
                $"{s.Totals.Started} {(s.Totals.Started == 1 ? "session was" : "sessions were")} started in this period, but none completed yet. Completed sessions will show patterns and comparisons here.");

        if (s.Period == ReportPeriod.Daily)
        {
            if (s.LeadingFocusPeriods.Count > 0)
            {
                var peak = s.LeadingFocusPeriods[0];
                return string.Create(CultureInfo.InvariantCulture,
                    $"Most of your completed Work sessions today occurred between {peak.StartHour:00}:00 and {peak.StartHour + 3:00}:00 ({peak.CompletedWork} completed {(peak.CompletedWork == 1 ? "session" : "sessions")}). Total focus: {Duration(s.Totals.FocusTime)}.");
            }
            return string.Create(CultureInfo.InvariantCulture,
                $"You completed {Duration(s.Totals.FocusTime)} of focus time today with a completion rate of {(s.Totals.CompletionRate ?? 0):0.#}%.");
        }

        if (s.Period == ReportPeriod.Weekly)
        {
            string peakStr = s.LeadingFocusPeriods.Count > 0
                ? string.Create(CultureInfo.InvariantCulture, $"Most of your completed Work sessions this week occurred between {s.LeadingFocusPeriods[0].StartHour:00}:00 and {s.LeadingFocusPeriods[0].StartHour + 3:00}:00. ")
                : "";
            string diffStr = s.WeekDifference > TimeSpan.Zero
                ? $" Up {Duration(s.WeekDifference)} compared to last week."
                : s.WeekDifference < TimeSpan.Zero
                    ? $" Down {Duration(s.WeekDifference.Duration())} compared to last week."
                    : "";
            return string.Create(CultureInfo.InvariantCulture,
                $"{peakStr}Total focus: {Duration(s.WeekFocus)} across {s.Totals.CompletedWork} completed sessions (completion rate {(s.Totals.CompletionRate ?? 0):0.#}%).{diffStr}");
        }

        // Monthly
        var topBucket = s.Trend.Count > 0 ? s.Trend.OrderByDescending(b => b.Totals.FocusTime).FirstOrDefault() : null;
        string topStr = topBucket != null && topBucket.Totals.FocusTime > TimeSpan.Zero
            ? $"{topBucket.Label} had your highest focus output ({Duration(topBucket.Totals.FocusTime)}). "
            : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"{topStr}Total focus for the month: {Duration(s.Totals.FocusTime)} across {s.Totals.CompletedWork} completed sessions (completion rate {(s.Totals.CompletionRate ?? 0):0.#}%).");
    }
    private UIElement Metric(string label, string value, string sub, int column)
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
            Margin = new Thickness(0, 0, 0, 4)
        };
        if (Application.Current?.Resources["FkMetricText"] is Style metricStyle)
        {
            valueText.Style = metricStyle;
            valueText.FontSize = 28;
            valueText.Margin = new Thickness(0, 0, 0, 4);
        }
        p.Children.Add(valueText);
        var labelText = Presentation.Text(label, 12);
        labelText.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        p.Children.Add(labelText);
        p.Children.Add(Presentation.DimText(sub, 11));
        var b = Card(p, 20);
        b.Padding = new Thickness(20, 22, 20, 22);
        Grid.SetColumn(b, column);
        return b;
    }

    private static UIElement Swatch(string label, FocusKey.Foundation.Settings.HexColor color)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        p.Children.Add(new Border
        {
            Width = 10,
            Height = 10,
            Background = SessionColorBrush.Create(color),
            BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", p),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2)
        });
        p.Children.Add(Presentation.Text(label, 11, true));
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
