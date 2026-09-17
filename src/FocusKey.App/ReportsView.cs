using System.Globalization;
using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Text;

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
    private Button? _nextButton;
    private bool? _lastBuiltYearEligible;
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

        // Build segmented period selector (Year segment hidden initially until eligibility confirmed)
        BuildPeriodSelector(false);

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
        _nextButton = NavButton(new FontIcon { Glyph = "\uE76C", FontSize = 12 }, () => _reports.MoveAsync(1), "Next period", "Next period");
        navRow.Children.Add(_nextButton);
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

    private void BuildPeriodSelector(bool isYearEligible)
    {
        _periodSelector.Children.Clear();
        var border = new Border
        {
            Style = (Style)Application.Current.Resources["FkSegmentContainer"],
        };
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var period in Enum.GetValues<ReportPeriod>())
        {
            if (period == ReportPeriod.Yearly && !isYearEligible)
            {
                continue; // Do not display Year button if user has < 1 year history
            }

            string label = period switch
            {
                ReportPeriod.Weekly => "Week",
                ReportPeriod.Monthly => "Month",
                ReportPeriod.Yearly => "Year",
                _ => period.ToString()
            };

            var btn = new Button
            {
                Content = label,
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
        _lastBuiltYearEligible = isYearEligible;
    }

    private void UpdatePeriodHighlight()
    {
        if (_periodSelector.Children.Count == 0) return;
        var border = (Border)_periodSelector.Children[0];
        var stack = (StackPanel)border.Child;
        var activeStyle = (Style)Application.Current.Resources["FkSegmentActive"];
        var inactiveStyle = (Style)Application.Current.Resources["FkSegmentInactive"];
        foreach (var child in stack.Children)
        {
            if (child is Button btn && btn.Tag is ReportPeriod p)
            {
                bool active = p == _reports.Period;
                btn.Style = active ? activeStyle : inactiveStyle;
                btn.Opacity = 1.0;
            }
        }
    }

    private void UpdateNavigationState()
    {
        if (_nextButton is null) return;
        bool isNextDisabled = _reports.Period == ReportPeriod.Yearly && _reports.Date.Year >= _reports.CurrentDate().Year;
        _nextButton.IsEnabled = !isNextDisabled;
        _nextButton.Opacity = isNextDisabled ? 0.4 : 1.0;
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
            if (_reports.Snapshot is { } snap && snap.IsYearEligible != _lastBuiltYearEligible)
            {
                BuildPeriodSelector(snap.IsYearEligible);
            }
            UpdatePeriodHighlight();
            UpdateNavigationState();
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
            string rangeText = snapshot.Period == ReportPeriod.Yearly
                ? $"Calendar Year {snapshot.Range.Start.Year}  ·  Jan 1 – Dec 31"
                : ReportsFormatting.FormatDateRange(snapshot.Range.Start, snapshot.Range.End.AddDays(-1));
            _dateSubtitle.Text = $"{rangeText}  ·  {snapshot.TimeZone.DisplayName}";

            // 3-column metric tiles matching design hierarchy: Summary -> Main Chart -> Useful Insights
            var metrics = new Grid { ColumnSpacing = 8 };
            for (var i = 0; i < 3; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var (card1, val1) = Metric("Focus Time", ReportsFormatting.FormatDuration(totals.FocusTime), "total focus", 0);
            var (card2, val2) = Metric("Work Sessions", totals.CompletedWork.ToString(CultureInfo.InvariantCulture), totals.WorkStarted > totals.CompletedWork ? $"{totals.WorkStarted} started" : "completed", 1);
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
            // Wide (>= 860 DIP): Dominant Chart Hero (~75%) + Secondary Insights Rail (~25%)
            // Restored / narrow (< 860 DIP): Natural reflow into stacked composition
            var contentGrid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                RowSpacing = 12,
                ColumnSpacing = 12
            };

            var chart = ChartCard(snapshot);
            var insightsRail = InsightsRailCard(snapshot);

            contentGrid.Children.Add(chart);
            contentGrid.Children.Add(insightsRail);

            bool? lastWide = null;
            Action updateLayout = () =>
            {
                double width = contentGrid.ActualWidth;
                bool isWide = width <= 0 || width >= 860;
                if (lastWide == isWide) return;
                lastWide = isWide;

                contentGrid.ColumnDefinitions.Clear();
                contentGrid.RowDefinitions.Clear();

                if (isWide)
                {
                    // Dominant chart hero (~75%) + Insights rail (~25%)
                    contentGrid.ColumnSpacing = 12;
                    contentGrid.RowSpacing = 0;
                    contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(75, GridUnitType.Star) });
                    contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25, GridUnitType.Star) });
                    contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    Grid.SetColumn(chart, 0);
                    Grid.SetRow(chart, 0);
                    Grid.SetColumnSpan(chart, 1);

                    Grid.SetColumn(insightsRail, 1);
                    Grid.SetRow(insightsRail, 0);
                    Grid.SetColumnSpan(insightsRail, 1);
                }
                else
                {
                    // Restored / narrow stacked mode
                    contentGrid.ColumnSpacing = 0;
                    contentGrid.RowSpacing = 12;
                    contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    Grid.SetColumn(chart, 0);
                    Grid.SetRow(chart, 0);
                    Grid.SetColumnSpan(chart, 1);

                    Grid.SetColumn(insightsRail, 0);
                    Grid.SetRow(insightsRail, 1);
                    Grid.SetColumnSpan(insightsRail, 1);
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

    private FrameworkElement InsightsRailCard(ReportsSnapshot snapshot)
    {
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var contentHost = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch };
        mainGrid.Children.Add(contentHost);

        void RebuildInsights(double actualWidth)
        {
            contentHost.Children.Clear();
            contentHost.ColumnDefinitions.Clear();
            contentHost.RowDefinitions.Clear();

            var outerPanel = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };

            // Header: "INSIGHTS"
            var header = Presentation.DimText("INSIGHTS", 11);
            if (Application.Current?.Resources["FkSectionText"] is Style secStyle) header.Style = secStyle;
            header.Margin = new Thickness(0, 2, 0, 14);
            outerPanel.Children.Add(header);

            if (snapshot.Totals.Started == 0 && snapshot.Totals.FocusTime == TimeSpan.Zero)
            {
                var emptyPanel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
                var emptyTitle = Presentation.Text("No session activity", 13);
                emptyTitle.FontWeight = FontWeights.SemiBold;
                var emptyDesc = Presentation.DimText("Focus time, streaks, peak days, and period comparisons will appear here once you record sessions.", 11);
                emptyDesc.TextWrapping = TextWrapping.Wrap;
                emptyDesc.LineHeight = 18;
                emptyPanel.Children.Add(emptyTitle);
                emptyPanel.Children.Add(emptyDesc);
                outerPanel.Children.Add(emptyPanel);
                contentHost.Children.Add(outerPanel);
                return;
            }

            var items = new List<UIElement>();

            // 1. Period comparison
            var curPeriod = snapshot.Totals.FocusTime;
            var prevPeriod = snapshot.PreviousPeriodDuration;
            string periodName = snapshot.Period switch
            {
                ReportPeriod.Weekly => "last week",
                ReportPeriod.Monthly => "last month",
                ReportPeriod.Yearly => "last year",
                _ => "previous period"
            };

            // Only show comparison for Yearly when prior-year data exists
            bool showComparison = snapshot.Period == ReportPeriod.Yearly
                ? (prevPeriod is { } pp && pp > TimeSpan.Zero)
                : (prevPeriod > TimeSpan.Zero || curPeriod > TimeSpan.Zero);

            if (showComparison)
            {
                var diff = snapshot.PeriodDifference;
                string diffValue;
                string diffLabel;

                if (diff > TimeSpan.Zero)
                {
                    diffValue = $"↑ {ReportsFormatting.FormatDuration(diff)}";
                    diffLabel = $"More focus than {periodName}";
                }
                else if (diff < TimeSpan.Zero)
                {
                    diffValue = $"↓ {ReportsFormatting.FormatDuration(diff.Duration())}";
                    diffLabel = $"Less focus than {periodName}";
                }
                else
                {
                    diffValue = "= 0m";
                    diffLabel = $"Same focus as {periodName}";
                }

                items.Add(CreateInsightItem(diffValue, diffLabel));
            }

            // 2. Streak
            int currentStreak = snapshot.Streaks.CurrentStreak;
            string streakValue = $"🔥 {currentStreak} {(currentStreak == 1 ? "day" : "days")}";
            string streakLabel = "Current streak";
            string? streakSub = snapshot.Streaks.LongestStreak > currentStreak
                ? $"Best: {snapshot.Streaks.LongestStreak} days"
                : null;
            items.Add(CreateInsightItem(streakValue, streakLabel, streakSub));

            // 3. Strongest day / week / month
            if (snapshot.Trend.Count > 0)
            {
                var topBucket = snapshot.Trend.OrderByDescending(b => b.Totals.FocusTime).FirstOrDefault();
                if (topBucket != null && topBucket.Totals.FocusTime > TimeSpan.Zero)
                {
                    string strongestValue;
                    string strongestLabel;

                    if (snapshot.Period == ReportPeriod.Weekly &&
                        DateOnly.TryParseExact(topBucket.Label, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var topDate))
                    {
                        strongestValue = ReportsFormatting.FormatDayOfWeekAbbrev(topDate);
                        strongestLabel = $"Strongest day ({ReportsFormatting.FormatDuration(topBucket.Totals.FocusTime)})";
                    }
                    else if (snapshot.Period == ReportPeriod.Yearly &&
                        DateOnly.TryParseExact(topBucket.Label + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var topMonth))
                    {
                        strongestValue = ReportsFormatting.FormatYearMonthLong(topMonth.Month);
                        strongestLabel = $"Strongest month ({ReportsFormatting.FormatDuration(topBucket.Totals.FocusTime)})";
                    }
                    else
                    {
                        int weekIndex = 1;
                        for (int b = 0; b < snapshot.Trend.Count; b++)
                        {
                            if (snapshot.Trend[b] == topBucket) { weekIndex = b + 1; break; }
                        }
                        strongestValue = $"Week {weekIndex}";
                        strongestLabel = $"Strongest week ({ReportsFormatting.FormatDuration(topBucket.Totals.FocusTime)})";
                    }

                    items.Add(CreateInsightItem(strongestValue, strongestLabel));
                }
            }

            // 4. Consistency / active days or months
            if (snapshot.Period == ReportPeriod.Weekly)
            {
                int activeDays = snapshot.Trend.Count(b => b.Totals.FocusTime > TimeSpan.Zero);
                if (activeDays > 0)
                {
                    string activeValue = $"{activeDays} of 7 days";
                    string activeLabel = "Active focus days";
                    TimeSpan dailyAvg = TimeSpan.FromTicks(snapshot.Totals.FocusTime.Ticks / 7);
                    string activeSub = $"Daily avg: {ReportsFormatting.FormatDuration(dailyAvg)}";
                    items.Add(CreateInsightItem(activeValue, activeLabel, activeSub));
                }
            }
            else if (snapshot.Period == ReportPeriod.Yearly)
            {
                int activeMonths = snapshot.Trend.Count(b => b.Totals.FocusTime > TimeSpan.Zero);
                if (activeMonths > 0)
                {
                    int elapsedMonths = snapshot.Range.Start.Year == _reports.CurrentDate().Year
                        ? _reports.CurrentDate().Month
                        : 12;
                    string activeValue = $"{activeMonths} of {elapsedMonths} {(elapsedMonths == 1 ? "month" : "months")}";
                    string activeLabel = "Active focus months";
                    TimeSpan monthlyAvg = TimeSpan.FromTicks(snapshot.Totals.FocusTime.Ticks / Math.Max(1, elapsedMonths));
                    string activeSub = $"Monthly avg: {ReportsFormatting.FormatDuration(monthlyAvg)}";
                    items.Add(CreateInsightItem(activeValue, activeLabel, activeSub));
                }

                if (snapshot.Totals.CompletedWork > 0)
                {
                    string sessionValue = $"{snapshot.Totals.CompletedWork} completed";
                    string sessionLabel = "Work sessions finished";
                    items.Add(CreateInsightItem(sessionValue, sessionLabel));
                }
            }
            else
            {
                if (snapshot.Totals.CompletedWork > 0)
                {
                    string sessionValue = $"{snapshot.Totals.CompletedWork} completed";
                    string sessionLabel = "Work sessions finished";
                    items.Add(CreateInsightItem(sessionValue, sessionLabel));
                }
            }

            bool twoColumns = actualWidth >= 420;
            if (twoColumns && items.Count > 1)
            {
                // Wide stacked layout: 2 columns
                var itemsGrid = new Grid { ColumnSpacing = 24, RowSpacing = 16 };
                itemsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                itemsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                for (int i = 0; i < items.Count; i++)
                {
                    int row = i / 2;
                    int col = i % 2;
                    while (itemsGrid.RowDefinitions.Count <= row)
                        itemsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    Grid.SetRow((FrameworkElement)items[i], row);
                    Grid.SetColumn((FrameworkElement)items[i], col);
                    itemsGrid.Children.Add(items[i]);
                }
                outerPanel.Children.Add(itemsGrid);
            }
            else
            {
                // Single column vertical layout (in side rail or narrow stacked)
                var itemsStack = new StackPanel { Spacing = 0 };
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0)
                    {
                        var div = new Border
                        {
                            Style = Application.Current?.Resources["FkChartGridLine"] as Style,
                            Margin = new Thickness(0, 12, 0, 12)
                        };
                        itemsStack.Children.Add(div);
                    }
                    itemsStack.Children.Add(items[i]);
                }
                outerPanel.Children.Add(itemsStack);
            }

            contentHost.Children.Add(outerPanel);
        }

        double lastWidth = -1;
        mainGrid.SizeChanged += (_, args) =>
        {
            double w = args.NewSize.Width;
            if (w <= 0) return;
            bool wasTwo = lastWidth >= 420;
            bool isTwo = w >= 420;
            if (lastWidth < 0 || wasTwo != isTwo)
            {
                lastWidth = w;
                RebuildInsights(w);
            }
        };

        RebuildInsights(0);

        var card = Card(mainGrid, 22);
        card.Padding = new Thickness(22, 20, 22, 20);
        card.VerticalAlignment = VerticalAlignment.Stretch;
        return card;
    }

    private UIElement CreateInsightItem(string value, string label, string? subtext = null)
    {
        var itemPanel = new StackPanel { Spacing = 2 };
        var valBlock = new TextBlock
        {
            Text = value,
            FontSize = 20,
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.SemiBold,
            Language = "en-US",
            FlowDirection = FlowDirection.LeftToRight,
            TextReadingOrder = TextReadingOrder.UseFlowDirection,
            Margin = new Thickness(0, 0, 0, 2)
        };
        if (Application.Current?.Resources["FkMetricValueText"] is Style metricStyle)
        {
            valBlock.Style = metricStyle;
            valBlock.FontSize = 20;
            valBlock.Margin = new Thickness(0, 0, 0, 2);
        }

        var labelBlock = Presentation.Text(label, 12);
        labelBlock.FontWeight = FontWeights.Medium;
        itemPanel.Children.Add(valBlock);
        itemPanel.Children.Add(labelBlock);

        if (!string.IsNullOrEmpty(subtext))
        {
            var subBlock = Presentation.DimText(subtext, 11);
            itemPanel.Children.Add(subBlock);
        }

        AutomationProperties.SetName(itemPanel, $"{label}, {value}");
        return itemPanel;
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
        title.FontWeight = FontWeights.SemiBold;
        titleStack.Children.Add(title);

        string subtitleText = snapshot.Period switch
        {
            ReportPeriod.Weekly => "Last 7 days (today on the right)",
            ReportPeriod.Monthly => "Weekly breakdown for the selected month",
            ReportPeriod.Yearly => $"Monthly breakdown for {snapshot.Range.Start.Year}",
            _ => string.Empty
        };
        var subtitle = Presentation.DimText(subtitleText, 11);
        titleStack.Children.Add(subtitle);
        header.Children.Add(titleStack);

        var palette = ReportsPalette.Resolve(ActualTheme, _contrast);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        legend.Children.Add(Swatch("Focus Time", palette.Work));
        Grid.SetColumn(legend, 1);
        header.Children.Add(legend);

        body.Children.Add(header);

        var chart = new ReportsChart(snapshot.Trend, snapshot.Period, palette, _reports.CurrentDate());
        AutomationProperties.SetName(chart, "Focus activity trend chart");
        body.Children.Add(chart);

        var card2 = Card(body, 22);
        card2.Padding = new Thickness(24, 22, 24, 22);
        card2.VerticalAlignment = VerticalAlignment.Stretch;
        return card2;
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
