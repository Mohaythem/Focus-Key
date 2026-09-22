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
    private double _scaleFactor = 1.0;
    private int _uiScalePercent = UiScaleLevels.DefaultPercent;
    private readonly TextBlock _title;
    private readonly Grid _topHeader;
    private readonly Grid _navGrid;
    private readonly StackPanel _navRow;
    private readonly StackPanel _mainPanel;
    private readonly List<Button> _navButtons = new();
    private readonly List<FontIcon> _navIcons = new();

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

        // Build segmented period selector (Year segment hidden initially unless eligible)
        BuildPeriodSelector(false);

        _mainPanel = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };

        // 1. Top header row: "Reports" title on left, segmented period selector on right (exact Figma layout)
        _topHeader = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 4) };
        _topHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _topHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _title = new TextBlock
        {
            Text = "Reports",
            Style = (Style)Application.Current.Resources["FkPageTitleText"],
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetHeadingLevel(_title, Microsoft.UI.Xaml.Automation.Peers.AutomationHeadingLevel.Level1);
        _topHeader.Children.Add(_title);

        _periodSelector.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_periodSelector, 1);
        _topHeader.Children.Add(_periodSelector);

        // 2. Sub-header row: date range description on left, navigation controls on right
        _navGrid = new Grid { ColumnSpacing = 12, RowSpacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        _navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _navGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _navGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _dateSubtitle.VerticalAlignment = VerticalAlignment.Center;
        _navGrid.Children.Add(_dateSubtitle);

        _navRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        _navRow.Children.Add(NavButton(new FontIcon { Glyph = "\uE76B", FontSize = 12 }, () => _reports.MoveAsync(-1), "Previous period", "Previous period"));
        _navRow.Children.Add(_date);
        _nextButton = NavButton(new FontIcon { Glyph = "\uE76C", FontSize = 12 }, () => _reports.MoveAsync(1), "Next period", "Next period");
        _navRow.Children.Add(_nextButton);
        _navRow.Children.Add(NavButton("Current", _reports.CurrentAsync, "Current period", "Current period"));
        _navRow.Children.Add(NavButton(new FontIcon { Glyph = "\uE72C", FontSize = 12 }, _reports.RefreshAsync, "Refresh reports", "Refresh"));
        Grid.SetColumn(_navRow, 1);
        _navGrid.Children.Add(_navRow);

        _navGrid.SizeChanged += (_, _) =>
        {
            double factor = _scaleFactor;
            bool narrow = _navGrid.ActualWidth > 0 && _navGrid.ActualWidth < 540 * factor;
            Grid.SetRow(_navRow, narrow ? 1 : 0);
            Grid.SetColumn(_navRow, narrow ? 0 : 1);
            Grid.SetColumnSpan(_navRow, narrow ? 2 : 1);
            _navRow.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        };

        _mainPanel.Children.Add(_topHeader);
        _mainPanel.Children.Add(_navGrid);
        _mainPanel.Children.Add(_status);
        _mainPanel.Children.Add(_results);
        Content = _mainPanel;

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
            Padding = new Thickness(Math.Round(2 * _scaleFactor))
        };
        var stack = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Math.Round(2 * _scaleFactor) };
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
                FontSize = Math.Round(12 * _scaleFactor),
                Padding = new Thickness(Math.Round(12 * _scaleFactor), Math.Round(6 * _scaleFactor), Math.Round(12 * _scaleFactor), Math.Round(6 * _scaleFactor)),
                MinHeight = Math.Round(28 * _scaleFactor)
            };
            AutomationProperties.SetName(btn, $"{period} reports");
            AutomationProperties.SetItemType(btn, "Radio");
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

    private void ScalePeriodSelector()
    {
        if (_periodSelector.Children.Count == 0) return;
        if (_periodSelector.Children[0] is Border border)
        {
            border.Padding = new Thickness(Math.Round(2 * _scaleFactor));
            if (border.Child is StackPanel stack)
            {
                stack.Spacing = Math.Round(2 * _scaleFactor);
                foreach (var child in stack.Children)
                {
                    if (child is Button btn)
                    {
                        btn.FontSize = Math.Round(12 * _scaleFactor);
                        btn.Padding = new Thickness(Math.Round(12 * _scaleFactor), Math.Round(6 * _scaleFactor), Math.Round(12 * _scaleFactor), Math.Round(6 * _scaleFactor));
                        btn.MinHeight = Math.Round(28 * _scaleFactor);
                    }
                }
            }
        }
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
                AutomationProperties.SetItemStatus(btn, active ? "Selected" : "Not Selected");
                AutomationProperties.SetName(btn, $"{p} reports, {(active ? "Selected" : "Not Selected")}");
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

    internal void ApplyUiScale(int percent)
    {
        int clamped = UiScaleLevels.IsValid(percent) ? percent : UiScaleLevels.DefaultPercent;
        _uiScalePercent = clamped;
        _scaleFactor = UiScaleLevels.ToFactor(clamped);

        UpdateStaticScales();
        Render();
    }

    private void UpdateStaticScales()
    {
        double factor = _scaleFactor;
        _title.FontSize = Math.Round(28 * factor);
        _topHeader.Margin = new Thickness(0, 0, 0, Math.Round(4 * factor));

        _navGrid.ColumnSpacing = Math.Round(12 * factor);
        _navGrid.RowSpacing = Math.Round(8 * factor);
        _navRow.Spacing = Math.Round(6 * factor);
        _results.Spacing = Math.Round(12 * factor);
        _mainPanel.Spacing = Math.Round(16 * factor);

        _date.Width = Math.Round(140 * factor);
        _date.Height = Math.Round(32 * factor);
        _date.FontSize = Math.Round(12 * factor);

        _dateSubtitle.FontSize = Math.Round(11 * factor);
        _status.FontSize = Math.Round(12 * factor);

        foreach (var btn in _navButtons)
        {
            btn.MinWidth = Math.Round(32 * factor);
            btn.Height = Math.Round(32 * factor);
            btn.FontSize = Math.Round(12 * factor);
            btn.Padding = new Thickness(Math.Round(8 * factor), 0, Math.Round(8 * factor), 0);
        }

        foreach (var icon in _navIcons)
        {
            icon.FontSize = Math.Round(12 * factor);
        }

        ScalePeriodSelector();
    }

    private void Render()
    {
        try
        {
            _rendering = true;
            _date.Date = DateValue(_reports.Date);
            bool showYear = _reports.Snapshot?.IsYearEligible ?? false;
            if (showYear != _lastBuiltYearEligible)
            {
                BuildPeriodSelector(showYear);
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
                    _results.Children.Add(Card(Presentation.Text("Choose a reporting period to view completed sessions.", Math.Round(13 * _scaleFactor)), Math.Round(20 * _scaleFactor)));
                return;
            }

            var totals = snapshot.Totals;

            // Date range subtitle in subheader
            string rangeText = snapshot.Period == ReportPeriod.Yearly
                ? $"Calendar Year {snapshot.Range.Start.Year}  ·  Jan 1 – Dec 31"
                : ReportsFormatting.FormatDateRange(snapshot.Range.Start, snapshot.Range.End.AddDays(-1));
            _dateSubtitle.Text = $"{rangeText}  ·  {snapshot.TimeZone.DisplayName}";

            // 3-column metric tiles matching design hierarchy: Summary -> Main Chart -> Useful Insights
            var metrics = new Grid { ColumnSpacing = Math.Round(8 * _scaleFactor) };
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
                double factor = _scaleFactor;
                bool compact = metrics.ActualWidth < 540 * factor;
                double fontSize = Math.Round((compact ? 21 : 30) * factor);
                var pad = new Thickness(
                    Math.Round((compact ? 12 : 22) * factor),
                    Math.Round((compact ? 16 : 20) * factor),
                    Math.Round((compact ? 12 : 22) * factor),
                    Math.Round((compact ? 16 : 20) * factor));
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
                RowSpacing = Math.Round(12 * _scaleFactor),
                ColumnSpacing = Math.Round(12 * _scaleFactor)
            };

            var chart = ChartCard(snapshot);
            var insightsRail = InsightsRailCard(snapshot);

            contentGrid.Children.Add(chart);
            contentGrid.Children.Add(insightsRail);

            bool? lastWide = null;
            Action updateLayout = () =>
            {
                double width = contentGrid.ActualWidth;
                double factor = _scaleFactor;
                bool isWide = width <= 0 || width >= 860 * factor;
                if (lastWide == isWide) return;
                lastWide = isWide;

                contentGrid.ColumnDefinitions.Clear();
                contentGrid.RowDefinitions.Clear();

                if (isWide)
                {
                    // Dominant chart hero (~72%) + Insights rail (~28%)
                    contentGrid.ColumnSpacing = Math.Round(16 * factor);
                    contentGrid.RowSpacing = 0;
                    contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72, GridUnitType.Star) });
                    contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28, GridUnitType.Star) });
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
                    contentGrid.RowSpacing = Math.Round(16 * factor);
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
        double factor = _scaleFactor;
        var mainGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var contentHost = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        mainGrid.Children.Add(contentHost);

        void RebuildInsights(double actualWidth)
        {
            contentHost.Children.Clear();
            contentHost.ColumnDefinitions.Clear();
            contentHost.RowDefinitions.Clear();

            if (snapshot.Totals.Started == 0 && snapshot.Totals.FocusTime == TimeSpan.Zero)
            {
                var outerPanel = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                var header = Presentation.DimText("INSIGHTS", Math.Round(11 * factor));
                if (Application.Current?.Resources["FkSectionText"] is Style secStyle) header.Style = secStyle;
                header.Margin = new Thickness(0, Math.Round(2 * factor), 0, Math.Round(14 * factor));
                outerPanel.Children.Add(header);

                var emptyPanel = new StackPanel { Spacing = Math.Round(6 * factor), Margin = new Thickness(0, Math.Round(4 * factor), 0, 0) };
                var emptyTitle = Presentation.Text("No session activity", Math.Round(13 * factor));
                emptyTitle.FontWeight = FontWeights.SemiBold;
                var emptyDesc = Presentation.DimText("Focus time, streaks, peak days, and period comparisons will appear here once you record sessions.", Math.Round(11 * factor));
                emptyDesc.TextWrapping = TextWrapping.Wrap;
                emptyDesc.LineHeight = Math.Round(18 * factor);
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

                items.Add(CreateInsightItem(diffValue, diffLabel, null, factor));
            }

            // 2. Streaks (distinct, balanced statistics for current and longest streak)
            items.Add(CreateStreaksInsightGroup(snapshot.Streaks.CurrentStreak, snapshot.Streaks.LongestStreak, factor));

            // 3. Strongest day / week / month
            if (snapshot.Trend.Count > 0)
            {
                var topBucket = snapshot.Trend.OrderByDescending(b => b.Totals.FocusTime).FirstOrDefault();
                if (topBucket != null && topBucket.Totals.FocusTime > TimeSpan.Zero)
                {
                    string strongestValue;
                    string strongestLabel;
                    string strongestSub = ReportsFormatting.FormatDuration(topBucket.Totals.FocusTime);

                    if (snapshot.Period == ReportPeriod.Weekly &&
                        DateOnly.TryParseExact(topBucket.Label, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var topDate))
                    {
                        strongestValue = ReportsFormatting.FormatDayOfWeekAbbrev(topDate);
                        strongestLabel = "Strongest day";
                    }
                    else if (snapshot.Period == ReportPeriod.Yearly &&
                        DateOnly.TryParseExact(topBucket.Label + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var topMonth))
                    {
                        strongestValue = ReportsFormatting.FormatYearMonthLong(topMonth.Month);
                        strongestLabel = "Strongest month";
                    }
                    else
                    {
                        int weekIndex = 1;
                        for (int b = 0; b < snapshot.Trend.Count; b++)
                        {
                            if (snapshot.Trend[b] == topBucket) { weekIndex = b + 1; break; }
                        }
                        strongestValue = $"Week {weekIndex}";
                        strongestLabel = "Strongest week";
                    }

                    items.Add(CreateInsightItem(strongestValue, strongestLabel, strongestSub, factor));
                }
            }

            // 4. Consistency / active days, weeks, or months
            if (snapshot.Period == ReportPeriod.Weekly)
            {
                int activeDays = snapshot.Trend.Count(b => b.Totals.FocusTime > TimeSpan.Zero);
                if (activeDays > 0)
                {
                    string activeValue = $"{activeDays} of 7 days";
                    string activeLabel = "Active focus days";
                    TimeSpan dailyAvg = TimeSpan.FromTicks(snapshot.Totals.FocusTime.Ticks / 7);
                    string activeSub = $"Daily avg: {ReportsFormatting.FormatDuration(dailyAvg)}";
                    items.Add(CreateInsightItem(activeValue, activeLabel, activeSub, factor));
                }
            }
            else if (snapshot.Period == ReportPeriod.Monthly)
            {
                int totalWeeks = snapshot.Trend.Count;
                int activeWeeks = snapshot.Trend.Count(b => b.Totals.FocusTime > TimeSpan.Zero);
                if (activeWeeks > 0 && totalWeeks > 0)
                {
                    string activeValue = $"{activeWeeks} of {totalWeeks} {(totalWeeks == 1 ? "week" : "weeks")}";
                    string activeLabel = "Active focus weeks";
                    TimeSpan weeklyAvg = TimeSpan.FromTicks(snapshot.Totals.FocusTime.Ticks / totalWeeks);
                    string activeSub = $"Weekly avg: {ReportsFormatting.FormatDuration(weeklyAvg)}";
                    items.Add(CreateInsightItem(activeValue, activeLabel, activeSub, factor));
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
                    items.Add(CreateInsightItem(activeValue, activeLabel, activeSub, factor));
                }
            }

            bool twoColumns = actualWidth >= 420 * factor;
            if (twoColumns && items.Count > 1)
            {
                // Wide stacked layout: 2 columns
                var outerPanel = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
                var header = Presentation.DimText("INSIGHTS", Math.Round(11 * factor));
                if (Application.Current?.Resources["FkSectionText"] is Style secStyle) header.Style = secStyle;
                header.Margin = new Thickness(0, Math.Round(2 * factor), 0, Math.Round(14 * factor));
                outerPanel.Children.Add(header);

                var itemsGrid = new Grid { ColumnSpacing = Math.Round(24 * factor), RowSpacing = Math.Round(16 * factor) };
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
                contentHost.Children.Add(outerPanel);
            }
            else
            {
                // Single column vertical layout (Side rail or narrow stacked)
                var railGrid = new Grid
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };

                // Row 0: Header
                railGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var header = Presentation.DimText("INSIGHTS", Math.Round(11 * factor));
                if (Application.Current?.Resources["FkSectionText"] is Style secStyle) header.Style = secStyle;
                header.Margin = new Thickness(0, Math.Round(2 * factor), 0, Math.Round(8 * factor));
                Grid.SetRow(header, 0);
                railGrid.Children.Add(header);

                int rowIndex = 1;
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0)
                    {
                        // Divider row
                        railGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                        var div = new Border
                        {
                            Height = 1,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            Background = (Application.Current?.Resources["CardStrokeColorDefaultBrush"] as Brush) ?? Presentation.ThemeBrush("FkBorder", true),
                            Opacity = 0.5,
                            Margin = new Thickness(0, Math.Round(4 * factor), 0, Math.Round(4 * factor))
                        };
                        Grid.SetRow(div, rowIndex);
                        railGrid.Children.Add(div);
                        rowIndex++;
                    }

                    // Item zone row with 1* height
                    railGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                    var itemElem = (FrameworkElement)items[i];
                    itemElem.VerticalAlignment = VerticalAlignment.Center;
                    Grid.SetRow(itemElem, rowIndex);
                    railGrid.Children.Add(itemElem);
                    rowIndex++;
                }

                contentHost.Children.Add(railGrid);
            }
        }

        double lastWidth = -1;
        mainGrid.SizeChanged += (_, args) =>
        {
            double w = args.NewSize.Width;
            if (w <= 0) return;
            bool wasTwo = lastWidth >= 420 * factor;
            bool isTwo = w >= 420 * factor;
            if (lastWidth < 0 || wasTwo != isTwo)
            {
                lastWidth = w;
                RebuildInsights(w);
            }
        };

        RebuildInsights(0);

        var card = Card(mainGrid, Math.Round(22 * factor));
        card.Padding = new Thickness(Math.Round(24 * factor), Math.Round(20 * factor), Math.Round(24 * factor), Math.Round(20 * factor));
        card.VerticalAlignment = VerticalAlignment.Stretch;
        return card;
    }

    private UIElement CreateStreaksInsightGroup(int currentStreak, int longestStreak, double factor = 1.0)
    {
        var groupPanel = new StackPanel { Spacing = Math.Round(4 * factor) };
        var header = Presentation.DimText("STREAKS", Math.Round(11 * factor));
        if (Application.Current?.Resources["FkSectionText"] is Style secStyle) header.Style = secStyle;
        header.Margin = new Thickness(0, 0, 0, Math.Round(2 * factor));
        groupPanel.Children.Add(header);

        var grid = new Grid { ColumnSpacing = Math.Round(16 * factor) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var currentCol = new StackPanel { Spacing = Math.Round(2 * factor) };
        var currentVal = CreateValueTextBlock($"{currentStreak} {(currentStreak == 1 ? "day" : "days")}", factor);
        var currentLbl = Presentation.Text("Current", Math.Round(12 * factor));
        currentLbl.FontWeight = FontWeights.Medium;
        currentCol.Children.Add(currentVal);
        currentCol.Children.Add(currentLbl);
        Grid.SetColumn(currentCol, 0);

        var longestCol = new StackPanel { Spacing = Math.Round(2 * factor) };
        var longestVal = CreateValueTextBlock($"{longestStreak} {(longestStreak == 1 ? "day" : "days")}", factor);
        var longestLbl = Presentation.Text("Longest", Math.Round(12 * factor));
        longestLbl.FontWeight = FontWeights.Medium;
        longestCol.Children.Add(longestVal);
        longestCol.Children.Add(longestLbl);
        Grid.SetColumn(longestCol, 1);

        grid.Children.Add(currentCol);
        grid.Children.Add(longestCol);
        groupPanel.Children.Add(grid);

        AutomationProperties.SetName(groupPanel, $"Streaks. Current streak: {currentStreak} days, Longest streak: {longestStreak} days");
        return groupPanel;
    }

    private static TextBlock CreateValueTextBlock(string value, double factor = 1.0)
    {
        var valBlock = new TextBlock
        {
            Text = value,
            FontSize = Math.Round(20 * factor),
            FontFamily = new FontFamily("Consolas"),
            FontWeight = FontWeights.SemiBold,
            Language = "en-US",
            FlowDirection = FlowDirection.LeftToRight,
            TextReadingOrder = TextReadingOrder.UseFlowDirection,
            Margin = new Thickness(0, 0, 0, Math.Round(2 * factor))
        };
        if (Application.Current?.Resources["FkMetricValueText"] is Style metricStyle)
        {
            valBlock.Style = metricStyle;
            valBlock.FontSize = Math.Round(20 * factor);
            valBlock.Margin = new Thickness(0, 0, 0, Math.Round(2 * factor));
        }
        return valBlock;
    }

    private UIElement CreateInsightItem(string value, string label, string? subtext = null, double factor = 1.0)
    {
        var itemPanel = new StackPanel { Spacing = Math.Round(2 * factor) };
        var valBlock = CreateValueTextBlock(value, factor);
        var labelBlock = Presentation.Text(label, Math.Round(12 * factor));
        labelBlock.FontWeight = FontWeights.Medium;
        itemPanel.Children.Add(valBlock);
        itemPanel.Children.Add(labelBlock);

        if (!string.IsNullOrEmpty(subtext))
        {
            var subBlock = Presentation.DimText(subtext, Math.Round(11 * factor));
            itemPanel.Children.Add(subBlock);
        }

        AutomationProperties.SetName(itemPanel, $"{label}, {value}{(string.IsNullOrEmpty(subtext) ? "" : $", {subtext}")}");
        return itemPanel;
    }


    private FrameworkElement ChartCard(ReportsSnapshot snapshot)
    {
        double factor = _scaleFactor;
        var body = new StackPanel { Spacing = Math.Round(14 * factor) };

        // Header: title + subtitle on left, work swatch / legend on right
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titleStack = new StackPanel { Spacing = Math.Round(2 * factor) };
        var title = Presentation.Text("Focus Activity", Math.Round(15 * factor));
        title.FontWeight = FontWeights.SemiBold;
        titleStack.Children.Add(title);

        string subtitleText = snapshot.Period switch
        {
            ReportPeriod.Weekly => "Last 7 days (today on the right)",
            ReportPeriod.Monthly => "Weekly breakdown for the selected month",
            ReportPeriod.Yearly => $"Monthly breakdown for {snapshot.Range.Start.Year}",
            _ => string.Empty
        };
        var subtitle = Presentation.DimText(subtitleText, Math.Round(11 * factor));
        titleStack.Children.Add(subtitle);
        header.Children.Add(titleStack);

        var palette = ReportsPalette.Resolve(ActualTheme, _contrast);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Math.Round(12 * factor), VerticalAlignment = VerticalAlignment.Center };
        legend.Children.Add(Swatch("Focus Time", palette.Work, factor));
        Grid.SetColumn(legend, 1);
        header.Children.Add(legend);

        body.Children.Add(header);

        var chart = new ReportsChart(snapshot.Trend, snapshot.Period, palette, _reports.CurrentDate(), factor);
        AutomationProperties.SetName(chart, $"Focus activity trend chart, {subtitleText}");
        body.Children.Add(chart);

        var card2 = Card(body, Math.Round(22 * factor));
        card2.Padding = new Thickness(Math.Round(24 * factor), Math.Round(22 * factor), Math.Round(24 * factor), Math.Round(22 * factor));
        card2.VerticalAlignment = VerticalAlignment.Stretch;
        AutomationProperties.SetName(card2, $"Focus Activity Chart for {subtitleText}. Total focus time: {ReportsFormatting.FormatDuration(snapshot.Totals.FocusTime)}, {snapshot.Totals.CompletedWork} completed work sessions.");
        return card2;
    }
    private (Border Card, TextBlock Value) Metric(string label, string value, string sub, int column)
    {
        double factor = _scaleFactor;
        var p = new StackPanel { Spacing = Math.Round(4 * factor) };
        var valueText = new TextBlock
        {
            Text = value,
            FontSize = Math.Round(30 * factor),
            FontFamily = new FontFamily("Consolas"),
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            Language = "en-US",
            FlowDirection = FlowDirection.LeftToRight,
            TextReadingOrder = TextReadingOrder.UseFlowDirection,
            Margin = new Thickness(0, 0, 0, Math.Round(4 * factor))
        };
        if (Application.Current?.Resources["FkMetricValueText"] is Style metricStyle)
        {
            valueText.Style = metricStyle;
            valueText.FontSize = Math.Round(30 * factor);
            valueText.Margin = new Thickness(0, 0, 0, Math.Round(4 * factor));
        }
        p.Children.Add(valueText);
        var labelText = Presentation.Text(label, Math.Round(12 * factor));
        labelText.FontWeight = Microsoft.UI.Text.FontWeights.Medium;
        p.Children.Add(labelText);
        p.Children.Add(Presentation.DimText(sub, Math.Round(11 * factor)));
        var b = Card(p, Math.Round(20 * factor));
        b.Padding = new Thickness(Math.Round(22 * factor), Math.Round(20 * factor), Math.Round(22 * factor), Math.Round(20 * factor));
        AutomationProperties.SetName(b, $"{label}: {value}, {sub}");
        Grid.SetColumn(b, column);
        return (b, valueText);
    }

    private static UIElement Swatch(string label, FocusKey.Foundation.Settings.HexColor color, double factor = 1.0)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Math.Round(6 * factor) };
        p.Children.Add(new Border
        {
            Width = Math.Round(10 * factor),
            Height = Math.Round(10 * factor),
            Background = SessionColorBrush.Create(color),
            BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", p),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Math.Round(2 * factor))
        });
        p.Children.Add(Presentation.Text(label, Math.Round(11 * factor), true));
        return p;
    }

    private static Border Card(UIElement child, double padding = 20)
    {
        var b = new Border { Child = child, Padding = new Thickness(padding) };
        if (Application.Current?.Resources["FkCard"] is Style s) b.Style = s;
        return b;
    }

    private Button NavButton(object content, Func<Task> action, string name, string? tooltip = null)
    {
        var b = new Button
        {
            Content = content,
            MinWidth = Math.Round(32 * _scaleFactor),
            Height = Math.Round(32 * _scaleFactor),
            FontSize = Math.Round(12 * _scaleFactor),
            Padding = new Thickness(Math.Round(8 * _scaleFactor), 0, Math.Round(8 * _scaleFactor), 0),
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = (CornerRadius)(Application.Current?.Resources["FkControlRadius"] ?? new CornerRadius(4)),
        };
        AutomationProperties.SetName(b, name);
        if (!string.IsNullOrEmpty(tooltip)) ToolTipService.SetToolTip(b, tooltip);
        b.Click += async (_, _) => await action();
        _navButtons.Add(b);
        if (content is FontIcon icon) _navIcons.Add(icon);
        return b;
    }

    private static DateTimeOffset DateValue(DateOnly date) => new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
    internal static string Duration(TimeSpan d) => Presentation.Duration(d);
}
