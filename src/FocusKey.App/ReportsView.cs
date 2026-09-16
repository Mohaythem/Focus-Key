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
    private readonly Action<Exception> _report;
    private bool _rendering;
    private SessionColors _colors = SessionColors.From(ApplicationSettings.Default);
    private Contrast _contrast = Contrast.Standard;
    internal void ApplyColors(SessionColors colors) { _colors = colors; Render(); }

    internal ReportsView(ReportsService service, Action<Exception> report)
    {
        _report = report;
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

        var panel = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };

        // 1. Top header row: "Reports" title on left, segmented period selector on right (exact Figma layout)
        var topHeader = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 4) };
        topHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        topHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "Reports",
            Style = (Style)Application.Current.Resources["FkPageTitleText"],
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetHeadingLevel(title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
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
        navRow.Children.Add(NavButton(new FontIcon { Glyph = "\uE76B", FontSize = 12 }, () => _reports.MoveAsync(-1), "Previous period", "Previous period"));
        navRow.Children.Add(_date);
        navRow.Children.Add(NavButton(new FontIcon { Glyph = "\uE76C", FontSize = 12 }, () => _reports.MoveAsync(1), "Next period", "Next period"));
        navRow.Children.Add(NavButton("Current", _reports.CurrentAsync, "Current period", "Current period"));
        navRow.Children.Add(NavButton(new FontIcon { Glyph = "\uE72C", FontSize = 12 }, _reports.RefreshAsync, "Refresh reports", "Refresh"));
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
        try
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
            string rangeText = ReportsFormatting.FormatDateRange(snapshot.Range.Start, snapshot.Range.End.AddDays(-1));
            _dateSubtitle.Text = $"{rangeText}  ·  {snapshot.TimeZone.DisplayName}";

            // 3-column metric tiles matching Figma layout
            var metrics = new Grid { ColumnSpacing = 8 };
            for (var i = 0; i < 3; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var (card1, val1) = Metric("Focus Time", ReportsFormatting.FormatDuration(totals.FocusTime), "work sessions", 0);
            var (card2, val2) = Metric("Break Time", ReportsFormatting.FormatDuration(totals.BreakTime), "break sessions", 1);
            var (card3, val3) = Metric("Completion Rate", totals.CompletionRate is { } rate ? ReportsFormatting.FormatRate(rate) : "—", "sessions finished", 2);
            metrics.Children.Add(card1);
            metrics.Children.Add(card2);
            metrics.Children.Add(card3);

            metrics.SizeChanged += (_, _) =>
            {
                if (metrics.ActualWidth <= 0) return;
                bool compact = metrics.ActualWidth < 540;
                double fontSize = compact ? 21 : 30;
                var pad = new Thickness(compact ? 12 : 22, compact ? 16 : 20, compact ? 12 : 22, compact ? 16 : 20);
                card1.Padding = pad; val1.FontSize = fontSize;
                card2.Padding = pad; val2.FontSize = fontSize;
                card3.Padding = pad; val3.FontSize = fontSize;
            };

            _results.Children.Add(metrics);

            // Responsive main content below metrics:
            // Wide (>= 900 DIP): Side-by-side (73% Focus Activity chart / 27% secondary right rail with Streaks & Insight)
            // Restored / narrow (< 900 DIP): Graceful collapse into stacked composition
            var contentGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                RowSpacing = 12,
                ColumnSpacing = 12
            };

            var chart = ChartCard(snapshot);
            var sideRail = SideRailCard(snapshot);
            var stackedStreaks = StreaksCard(snapshot.Streaks);
            var stackedInsight = InsightCard(snapshot);

            contentGrid.Children.Add(chart);
            contentGrid.Children.Add(sideRail);
            contentGrid.Children.Add(stackedStreaks);
            contentGrid.Children.Add(stackedInsight);

            bool? lastWide = null;
            Action updateLayout = () =>
            {
                double width = contentGrid.ActualWidth;
                bool isWide = width <= 0 || width >= 900;
                if (lastWide == isWide) return;
                lastWide = isWide;

                contentGrid.ColumnDefinitions.Clear();
                contentGrid.RowDefinitions.Clear();

                if (isWide)
                {
                    // Wide desktop side-by-side mode (~73% chart, ~27% secondary rail)
                    contentGrid.ColumnSpacing = 12;
                    contentGrid.RowSpacing = 0;
                    contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(73, GridUnitType.Star) });
                    contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27, GridUnitType.Star) });
                    contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    Grid.SetColumn(chart, 0);
                    Grid.SetRow(chart, 0);
                    chart.Visibility = Visibility.Visible;

                    Grid.SetColumn(sideRail, 1);
                    Grid.SetRow(sideRail, 0);
                    sideRail.Visibility = Visibility.Visible;

                    stackedStreaks.Visibility = Visibility.Collapsed;
                    stackedInsight.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // Restored / narrow stacked mode
                    contentGrid.ColumnSpacing = 0;
                    contentGrid.RowSpacing = 12;
                    contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    Grid.SetColumn(chart, 0);
                    Grid.SetRow(chart, 0);
                    chart.Visibility = Visibility.Visible;

                    Grid.SetColumn(stackedStreaks, 0);
                    Grid.SetRow(stackedStreaks, 1);
                    stackedStreaks.Visibility = Visibility.Visible;

                    Grid.SetColumn(stackedInsight, 0);
                    Grid.SetRow(stackedInsight, 2);
                    stackedInsight.Visibility = Visibility.Visible;

                    sideRail.Visibility = Visibility.Collapsed;
                }
            };

            contentGrid.SizeChanged += (_, _) => updateLayout();
            updateLayout();

            _results.Children.Add(contentGrid);
        }
        catch (Exception ex)
        {
            _report(ex);
        }
    }

    private FrameworkElement SideRailCard(ReportsSnapshot snapshot)
    {
        var grid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 0: Current Streak
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 1: Divider 1
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 2: Longest Streak
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // 3: Divider 2
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // 4: Insight

        // 1. Current Streak
        var curPanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 8) };
        var curHeader = Presentation.DimText("CURRENT STREAK", 11);
        if (Application.Current?.Resources["FkSectionText"] is Style secStyle1) curHeader.Style = secStyle1;
        curPanel.Children.Add(curHeader);

        var curValue = new TextBlock
        {
            Text = ReportsFormatting.FormatStreak(snapshot.Streaks.CurrentStreak),
            FontSize = 26,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            Language = "en-US",
            FlowDirection = FlowDirection.LeftToRight,
            TextReadingOrder = TextReadingOrder.UseFlowDirection,
            Margin = new Thickness(0, 1, 0, 0)
        };
        if (Application.Current?.Resources["FkMetricValueText"] is Style metricStyle1)
        {
            curValue.Style = metricStyle1;
            curValue.FontSize = 26;
            curValue.Margin = new Thickness(0, 1, 0, 0);
        }
        curPanel.Children.Add(curValue);
        AutomationProperties.SetName(curPanel, $"Current Streak, {curValue.Text}");
        Grid.SetRow(curPanel, 0);
        grid.Children.Add(curPanel);

        // Divider 1
        var div1 = new Border
        {
            Height = 1,
            Background = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this),
            Margin = new Thickness(0, 16, 0, 16)
        };
        Grid.SetRow(div1, 1);
        grid.Children.Add(div1);

        // 2. Longest Streak
        var longPanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 8, 0, 8) };
        var longHeader = Presentation.DimText("LONGEST STREAK", 11);
        if (Application.Current?.Resources["FkSectionText"] is Style secStyle2) longHeader.Style = secStyle2;
        longPanel.Children.Add(longHeader);

        var longValue = new TextBlock
        {
            Text = ReportsFormatting.FormatStreak(snapshot.Streaks.LongestStreak),
            FontSize = 26,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            Language = "en-US",
            FlowDirection = FlowDirection.LeftToRight,
            TextReadingOrder = TextReadingOrder.UseFlowDirection,
            Margin = new Thickness(0, 1, 0, 0)
        };
        if (Application.Current?.Resources["FkMetricValueText"] is Style metricStyle2)
        {
            longValue.Style = metricStyle2;
            longValue.FontSize = 26;
            longValue.Margin = new Thickness(0, 1, 0, 0);
        }
        longPanel.Children.Add(longValue);
        AutomationProperties.SetName(longPanel, $"Longest Streak, {longValue.Text}");
        Grid.SetRow(longPanel, 2);
        grid.Children.Add(longPanel);

        // Divider 2
        var div2 = new Border
        {
            Height = 1,
            Background = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this),
            Margin = new Thickness(0, 16, 0, 16)
        };
        Grid.SetRow(div2, 3);
        grid.Children.Add(div2);

        // 3. Insight
        var insightPanel = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        var insightHeader = Presentation.DimText("INSIGHT", 11);
        if (Application.Current?.Resources["FkSectionText"] is Style secStyle3) insightHeader.Style = secStyle3;
        insightPanel.Children.Add(insightHeader);

        string insightText = GenerateInsightText(snapshot);
        var insightContent = Presentation.Text(insightText, 12);
        if (Application.Current?.Resources["FkMutedText"] is Style muted) insightContent.Style = muted;
        insightContent.TextWrapping = TextWrapping.Wrap;
        insightContent.LineHeight = 22;
        insightPanel.Children.Add(insightContent);
        Grid.SetRow(insightPanel, 4);
        grid.Children.Add(insightPanel);

        var card = Card(grid, 22);
        card.Padding = new Thickness(24, 22, 24, 22);
        card.VerticalAlignment = VerticalAlignment.Stretch;
        return card;
    }

    private FrameworkElement StreaksCard(StreakStatistics streaks)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Left: Current Streak
        var left = new StackPanel { Spacing = 2 };
        var curValue = new TextBlock
        {
            Text = ReportsFormatting.FormatStreak(streaks.CurrentStreak),
            FontSize = 20,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            Language = "en-US",
            FlowDirection = FlowDirection.LeftToRight,
            TextReadingOrder = TextReadingOrder.UseFlowDirection,
            Margin = new Thickness(0, 0, 0, 2)
        };
        if (Application.Current?.Resources["FkMetricValueText"] is Style metricStyle)
        {
            curValue.Style = metricStyle;
            curValue.FontSize = 20;
            curValue.Margin = new Thickness(0, 0, 0, 2);
        }
        left.Children.Add(curValue);
        var curLabel = Presentation.Text("Current Streak", 12);
        curLabel.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        left.Children.Add(curLabel);
        AutomationProperties.SetName(left, $"Current Streak, {curValue.Text}");
        grid.Children.Add(left);

        // Divider
        var divider = new Border
        {
            Width = 1,
            Background = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this),
            Margin = new Thickness(24, 4, 24, 4)
        };
        Grid.SetColumn(divider, 1);
        grid.Children.Add(divider);

        // Right: Longest Streak
        var right = new StackPanel { Spacing = 2 };
        var longValue = new TextBlock
        {
            Text = ReportsFormatting.FormatStreak(streaks.LongestStreak),
            FontSize = 20,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            Language = "en-US",
            FlowDirection = FlowDirection.LeftToRight,
            TextReadingOrder = TextReadingOrder.UseFlowDirection,
            Margin = new Thickness(0, 0, 0, 2)
        };
        if (Application.Current?.Resources["FkMetricValueText"] is Style metricStyle2)
        {
            longValue.Style = metricStyle2;
            longValue.FontSize = 20;
            longValue.Margin = new Thickness(0, 0, 0, 2);
        }
        right.Children.Add(longValue);
        var longLabel = Presentation.Text("Longest Streak", 12);
        longLabel.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        right.Children.Add(longLabel);
        AutomationProperties.SetName(right, $"Longest Streak, {longValue.Text}");
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        var card = Card(grid, 20);
        card.Padding = new Thickness(22, 18, 22, 18);
        return card;
    }

    private FrameworkElement ChartCard(ReportsSnapshot snapshot)
    {
        var body = new StackPanel { Spacing = 14 };

        // Header: title + subtitle on left, work swatch / legend on right
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Spacing = 2 };
        var title = Presentation.Text("Focus Activity", 15);
        title.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
        titleStack.Children.Add(title);

        string subtitleText = snapshot.Period == ReportPeriod.Weekly
            ? "Last 7 days (today on the right)"
            : "Weekly breakdown for the selected month";
        var subtitle = Presentation.DimText(subtitleText, 11);
        titleStack.Children.Add(subtitle);
        header.Children.Add(titleStack);

        var palette = ReportsPalette.Resolve(ActualTheme, _contrast);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        legend.Children.Add(Swatch("Focus Time", palette.Work));
        Grid.SetColumn(legend, 1);
        header.Children.Add(legend);

        body.Children.Add(header);

        var chart = new ReportsChart(snapshot.Trend, snapshot.Period, palette);
        AutomationProperties.SetName(chart, "Focus activity trend chart");
        body.Children.Add(chart);

        var card2 = Card(body, 22);
        card2.Padding = new Thickness(24, 22, 24, 22);
        card2.VerticalAlignment = VerticalAlignment.Stretch;
        return card2;
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

        var card = Card(body, 20);
        card.Padding = new Thickness(24, 18, 24, 18);
        return card;
    }

    private static string GenerateInsightText(ReportsSnapshot s)
    {
        if (s.Totals.Started == 0)
            return "No sessions recorded in this period. Completed time and focus patterns will appear here once a session is recorded.";

        if (s.Totals.Completed == 0 && s.Totals.FocusTime == TimeSpan.Zero)
            return string.Create(CultureInfo.InvariantCulture,
                $"{s.Totals.Started} {(s.Totals.Started == 1 ? "session was" : "sessions were")} started in this period, but none completed yet. Completed sessions will show patterns and comparisons here.");

        if (s.Period == ReportPeriod.Weekly)
        {
            var topBucket = s.Trend.Count > 0 ? s.Trend.OrderByDescending(b => b.Totals.FocusTime).FirstOrDefault() : null;
            string peakStr = "";
            if (topBucket != null && topBucket.Totals.FocusTime > TimeSpan.Zero &&
                DateOnly.TryParseExact(topBucket.Label, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var topDate))
            {
                string topDayName = ReportsFormatting.FormatDayOfWeekLong(topDate);
                string topDuration = ReportsFormatting.FormatDuration(topBucket.Totals.FocusTime);
                peakStr = $"Most of your focus time this week occurred on {topDayName} ({topDuration}). ";
            }

            TimeSpan dailyAvg = TimeSpan.FromTicks(s.Totals.FocusTime.Ticks / 7);
            string diffStr = s.WeekDifference > TimeSpan.Zero
                ? $" Up {ReportsFormatting.FormatDuration(s.WeekDifference)} compared to last week."
                : s.WeekDifference < TimeSpan.Zero
                    ? $" Down {ReportsFormatting.FormatDuration(s.WeekDifference.Duration())} compared to last week."
                    : "";
            return string.Create(CultureInfo.InvariantCulture,
                $"{peakStr}Total focus: {ReportsFormatting.FormatDuration(s.Totals.FocusTime)} across 7 days (daily average: {ReportsFormatting.FormatDuration(dailyAvg)}).{diffStr}");
        }

        // Monthly
        var monthlyTopBucket = s.Trend.Count > 0 ? s.Trend.OrderByDescending(b => b.Totals.FocusTime).FirstOrDefault() : null;
        string topStr = monthlyTopBucket != null && monthlyTopBucket.Totals.FocusTime > TimeSpan.Zero
            ? $"{monthlyTopBucket.Label} had your highest focus output ({ReportsFormatting.FormatDuration(monthlyTopBucket.Totals.FocusTime)}). "
            : "";
        return string.Create(CultureInfo.InvariantCulture,
            $"{topStr}Total focus for the month: {ReportsFormatting.FormatDuration(s.Totals.FocusTime)} across {s.Totals.CompletedWork} completed sessions (completion rate {ReportsFormatting.FormatRate(s.Totals.CompletionRate ?? 0)}).");
    }
    private (Border Card, TextBlock Value) Metric(string label, string value, string sub, int column)
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
        if (Application.Current?.Resources["FkMetricValueText"] is Style metricStyle)
        {
            valueText.Style = metricStyle;
            valueText.FontSize = 30;
            valueText.Margin = new Thickness(0, 0, 0, 4);
        }
        p.Children.Add(valueText);
        var labelText = Presentation.Text(label, 12);
        labelText.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        p.Children.Add(labelText);
        p.Children.Add(Presentation.DimText(sub, 11));
        var b = Card(p, 20);
        b.Padding = new Thickness(22, 20, 22, 20);
        Grid.SetColumn(b, column);
        return (b, valueText);
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

    private static Button NavButton(object content, Func<Task> action, string name, string? tooltip = null)
    {
        var b = new Button
        {
            Content = content,
            MinWidth = 32,
            Height = 32,
            FontSize = 12,
            Padding = new Thickness(8, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = (CornerRadius)(Application.Current?.Resources["FkControlRadius"] ?? new CornerRadius(4)),
        };
        AutomationProperties.SetName(b, name);
        if (!string.IsNullOrEmpty(tooltip)) ToolTipService.SetToolTip(b, tooltip);
        b.Click += async (_, _) => await action();
        return b;
    }

    private static DateTimeOffset DateValue(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    internal static string Duration(TimeSpan d) => Presentation.Duration(d);
}
