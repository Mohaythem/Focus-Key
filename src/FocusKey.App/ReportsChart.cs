using System.Globalization;
using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Settings;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace FocusKey;

/// <summary>
/// Reports Focus Activity chart matching the approved rectangular grid visual reference.
/// Features 5-hour horizontal grid intervals, vertical day/week separators, dynamic Y-axis scaling,
/// substantial centered focus bars, and exact duration labels above non-zero bars.
/// </summary>
internal sealed class ReportsChart : Grid
{
    private const double BaseTopHeadroom = 30;
    private const double BasePlotAreaHeight = 300;

    internal ReportsChart(IReadOnlyList<ReportBucket> trend, ReportPeriod period, ReportsPalette palette, DateOnly? currentDate = null, double scaleFactor = 1.0)
    {
        Language = "en-US";
        FlowDirection = FlowDirection.LeftToRight;

        double factor = Math.Clamp(scaleFactor, 0.8, 1.5);
        double topHeadroom = Math.Round(BaseTopHeadroom * factor);
        double plotAreaHeight = Math.Round(BasePlotAreaHeight * factor);
        double totalPlotHeight = topHeadroom + plotAreaHeight;
        double axisColWidth = Math.Round(46 * factor);

        ColumnSpacing = Math.Round(8 * factor);
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(axisColWidth) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(totalPlotHeight) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        double effectiveMax = trend.Count == 0 ? 0 : trend.Max(b => b.Totals.FocusTime.TotalSeconds);
        int stepHours = period switch
        {
            ReportPeriod.Yearly => ReportsService.ComputeYearlyStepHours(effectiveMax),
            ReportPeriod.Monthly => 10,
            _ => 5
        };
        int ceilingHours = ReportsService.ComputeCeilingHours(effectiveMax, stepHours);

        bool isDark = palette.IsDark;
        var fgBrush = Presentation.ThemeBrush("FkForeground", isDark);
        var secBrush = Presentation.ThemeBrush("FkSecondary", isDark);

        // 1. Y-Axis column (Column 0): ticks from 0h up to ceilingHours in stepHours steps
        var axisCanvas = new Canvas
        {
            Width = axisColWidth,
            Height = totalPlotHeight,
            Language = "en-US",
            FlowDirection = FlowDirection.LeftToRight
        };
        for (int h = 0; h <= ceilingHours; h += stepHours)
        {
            double fraction = (double)h / ceilingHours;
            double y = topHeadroom + plotAreaHeight * (1.0 - fraction);
            var label = new TextBlock
            {
                Text = ReportsFormatting.FormatAxisHour(h),
                Style = Application.Current?.Resources["FkMutedText"] as Style,
                FontSize = Math.Round(11 * factor),
                FontWeight = FontWeights.Normal,
                Foreground = secBrush,
                Width = Math.Round(40 * factor),
                TextAlignment = TextAlignment.Right,
                Language = "en-US",
                FlowDirection = FlowDirection.LeftToRight,
                TextReadingOrder = TextReadingOrder.UseFlowDirection
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - Math.Round(8 * factor));
            axisCanvas.Children.Add(label);
        }
        Children.Add(axisCanvas);

        // 2. Plot Host (Column 1, Row 0): Grid lines + Focus bars
        var plotGrid = new Grid { Height = totalPlotHeight, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(plotGrid, 1);
        Children.Add(plotGrid);

        // Background canvas for dashed rectangular grid lines
        var gridCanvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        plotGrid.Children.Add(gridCanvas);

        var hLines = new List<Line>();
        var vLines = new List<Line>();
        var gridStroke = secBrush;
        const double gridOpacity = 0.38;

        // Horizontal lines at each stepHours tick
        for (int h = 0; h <= ceilingHours; h += stepHours)
        {
            double fraction = (double)h / ceilingHours;
            double y = topHeadroom + plotAreaHeight * (1.0 - fraction);
            var line = new Line
            {
                X1 = 0,
                X2 = 100, // Updated dynamically on SizeChanged
                Y1 = y,
                Y2 = y,
                Stroke = gridStroke,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                Opacity = gridOpacity
            };
            hLines.Add(line);
            gridCanvas.Children.Add(line);
        }

        // Vertical lines separating day/week columns (plus left and right boundary lines)
        int count = trend.Count;
        const double vGridOpacity = 0.18;
        for (int c = 0; c <= count; c++)
        {
            var line = new Line
            {
                X1 = 0,
                X2 = 0,
                Y1 = topHeadroom,
                Y2 = topHeadroom + plotAreaHeight,
                Stroke = gridStroke,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 3, 3 },
                Opacity = vGridOpacity
            };
            vLines.Add(line);
            gridCanvas.Children.Add(line);
        }

        // Bars grid
        var barsGrid = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        var barBorders = new List<Border>();

        for (int i = 0; i < count; i++)
        {
            barsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bucket = trend[i];
            double focusSecs = bucket.Totals.FocusTime.TotalSeconds;

            var colContainer = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) // Ensures hit-testing for tooltips
            };

            // Single substantial focus bar with rounded top corners + duration label above
            var barStack = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            string durationText = period == ReportPeriod.Yearly
                ? ReportsFormatting.FormatYearlyBarDuration(bucket.Totals.FocusTime)
                : ReportsFormatting.FormatBarDuration(bucket.Totals.FocusTime);
            if (!string.IsNullOrEmpty(durationText))
            {
                var durationLabel = new TextBlock
                {
                    Text = durationText,
                    Style = Application.Current?.Resources["FkText"] as Style,
                    FontSize = Math.Round((period == ReportPeriod.Yearly ? 10 : 11) * factor),
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = fgBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Language = "en-US",
                    FlowDirection = FlowDirection.LeftToRight,
                    TextReadingOrder = TextReadingOrder.UseFlowDirection,
                    Margin = new Thickness(0, 0, 0, Math.Round(4 * factor))
                };
                barStack.Children.Add(durationLabel);
            }

            if (focusSecs > 0)
            {
                double rawHeight = (focusSecs / (ceilingHours * 3600.0)) * plotAreaHeight;
                double barHeight = Math.Max(Math.Round(4 * factor), Math.Min(plotAreaHeight, rawHeight));
                var bar = new Border
                {
                    Height = barHeight,
                    Width = period == ReportPeriod.Yearly ? Math.Round(28 * factor) : Math.Round(44 * factor), // Dynamically adjusted on SizeChanged
                    Background = SessionColorBrush.Create(palette.Work),
                    CornerRadius = new CornerRadius(Math.Round(4 * factor), Math.Round(4 * factor), 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                barBorders.Add(bar);
                barStack.Children.Add(bar);
            }

            colContainer.Children.Add(barStack);

            // Native tooltip & accessibility with safe top clamping
            string tooltip = ReportsFormatting.FormatTooltip(bucket, period);
            bool isNearTop = focusSecs > 0 && ceilingHours > 0 && (focusSecs / (ceilingHours * 3600.0)) >= 0.80;
            var toolTipObj = new ToolTip
            {
                Content = tooltip,
                Placement = isNearTop ? PlacementMode.Bottom : PlacementMode.Top,
                VerticalOffset = isNearTop ? Math.Round(8 * factor) : Math.Round(-4 * factor)
            };
            ToolTipService.SetToolTip(colContainer, toolTipObj);
            AutomationProperties.SetName(colContainer, tooltip);

            Grid.SetColumn(colContainer, i);
            barsGrid.Children.Add(colContainer);
        }

        plotGrid.Children.Add(barsGrid);

        // Calm empty notice if zero activity across entire period
        if (effectiveMax <= 0)
        {
            var emptyNotice = Presentation.DimText("No focus activity recorded for this period", Math.Round(12 * factor));
            emptyNotice.Foreground = secBrush;
            emptyNotice.HorizontalAlignment = HorizontalAlignment.Center;
            emptyNotice.VerticalAlignment = VerticalAlignment.Center;
            emptyNotice.Margin = new Thickness(0, topHeadroom, 0, 0);
            plotGrid.Children.Add(emptyNotice);
        }

        // Responsive resize: adjust line lengths and bar widths
        plotGrid.SizeChanged += (_, args) =>
        {
            double width = args.NewSize.Width;
            if (width <= 0) return;

            // Update horizontal lines
            foreach (var hl in hLines)
            {
                hl.X2 = width;
            }

            // Update vertical lines
            if (count > 0)
            {
                double colW = width / count;
                for (int c = 0; c < vLines.Count; c++)
                {
                    double x = Math.Min(width, c * colW);
                    vLines[c].X1 = x;
                    vLines[c].X2 = x;
                }

                // Update bar widths: ~72% for Weekly, ~58% for Monthly, ~65% for Yearly, clamped comfortably
                double fillRatio = period switch
                {
                    ReportPeriod.Yearly => 0.65,
                    ReportPeriod.Monthly => 0.58,
                    _ => 0.72
                };
                double maxBarWidth = Math.Round((period switch
                {
                    ReportPeriod.Yearly => 64,
                    ReportPeriod.Monthly => 128,
                    _ => 116
                }) * factor);
                double minBarWidth = Math.Round((period == ReportPeriod.Yearly ? 14 : 24) * factor);
                double dynamicBarWidth = Math.Clamp(Math.Floor(colW * fillRatio), minBarWidth, maxBarWidth);
                foreach (var bar in barBorders)
                {
                    bar.Width = dynamicBarWidth;
                }
            }
        };

        // 3. X-Axis Day/Week Labels (Row 1, Column 1)
        var labelsGrid = new Grid { Margin = new Thickness(0, Math.Round(8 * factor), 0, 0) };
        Grid.SetRow(labelsGrid, 1);
        Grid.SetColumn(labelsGrid, 1);

        for (int i = 0; i < count; i++)
        {
            labelsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var bucket = trend[i];
            bool isRightmost = (i == count - 1);

            var labelStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                Spacing = Math.Round(2 * factor)
            };

            if (period == ReportPeriod.Weekly &&
                DateOnly.TryParseExact(bucket.Label, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
            {
                // Two lines per visual reference:
                // Line 1: Sep 6
                // Line 2: (Sun)
                // Rightmost day (Today) has bold emphasis
                var dateText = new TextBlock
                {
                    Text = ReportsFormatting.FormatDayDate(day),
                    FontSize = Math.Round(11 * factor),
                    FontWeight = isRightmost ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = isRightmost ? fgBrush : secBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Language = "en-US",
                    FlowDirection = FlowDirection.LeftToRight,
                    TextReadingOrder = TextReadingOrder.UseFlowDirection
                };
                var dayText = new TextBlock
                {
                    Text = ReportsFormatting.FormatDayOfWeek(day),
                    FontSize = Math.Round(10 * factor),
                    FontWeight = isRightmost ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = isRightmost ? fgBrush : secBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Language = "en-US",
                    FlowDirection = FlowDirection.LeftToRight,
                    TextReadingOrder = TextReadingOrder.UseFlowDirection
                };
                labelStack.Children.Add(dateText);
                labelStack.Children.Add(dayText);
            }
            else if (period == ReportPeriod.Yearly)
            {
                bool isCurrentMonth = false;
                if (DateOnly.TryParseExact(bucket.Label + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var monthDate))
                {
                    isCurrentMonth = currentDate.HasValue &&
                        monthDate.Year == currentDate.Value.Year &&
                        monthDate.Month == currentDate.Value.Month;
                }

                var monthText = new TextBlock
                {
                    Text = ReportsFormatting.FormatYearMonth(i + 1),
                    FontSize = Math.Round(11 * factor),
                    FontWeight = isCurrentMonth ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = isCurrentMonth ? fgBrush : secBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Language = "en-US",
                    FlowDirection = FlowDirection.LeftToRight,
                    TextReadingOrder = TextReadingOrder.UseFlowDirection
                };
                labelStack.Children.Add(monthText);
            }
            else
            {
                // Monthly view: W1, W2, etc.
                var weekText = new TextBlock
                {
                    Text = ReportsFormatting.FormatMonthWeek(i + 1),
                    FontSize = Math.Round(11 * factor),
                    FontWeight = isRightmost ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = isRightmost ? fgBrush : secBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Language = "en-US",
                    FlowDirection = FlowDirection.LeftToRight,
                    TextReadingOrder = TextReadingOrder.UseFlowDirection
                };
                labelStack.Children.Add(weekText);
            }

            Grid.SetColumn(labelStack, i);
            labelsGrid.Children.Add(labelStack);
        }

        Children.Add(labelsGrid);
    }
}
