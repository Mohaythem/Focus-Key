using System.Globalization;
using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Settings;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
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
    private const double TopHeadroom = 28;
    private const double PlotAreaHeight = 260;
    private const double TotalPlotHeight = TopHeadroom + PlotAreaHeight; // 288 DIP

    internal ReportsChart(IReadOnlyList<ReportBucket> trend, ReportPeriod period, ReportsPalette palette)
    {
        Language = "en-US";
        FlowDirection = FlowDirection.LeftToRight;

        ColumnSpacing = 8;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(TotalPlotHeight) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        double effectiveMax = trend.Count == 0 ? 0 : trend.Max(b => b.Totals.FocusTime.TotalSeconds);
        int ceilingHours = ReportsService.ComputeCeilingHours(effectiveMax);

        // 1. Y-Axis column (Column 0): ticks from 0h up to ceilingHours in 5h steps
        var axisCanvas = new Canvas
        {
            Width = 46,
            Height = TotalPlotHeight,
            Language = "en-US",
            FlowDirection = FlowDirection.LeftToRight
        };
        for (int h = 0; h <= ceilingHours; h += 5)
        {
            double fraction = (double)h / ceilingHours;
            double y = TopHeadroom + PlotAreaHeight * (1.0 - fraction);
            var label = new TextBlock
            {
                Text = ReportsFormatting.FormatAxisHour(h),
                FontSize = 11,
                FontWeight = FontWeights.Normal,
                Foreground = Presentation.ThemeBrush("FkSecondary", this),
                Width = 40,
                TextAlignment = TextAlignment.Right,
                Language = "en-US",
                FlowDirection = FlowDirection.LeftToRight,
                TextReadingOrder = TextReadingOrder.UseFlowDirection
            };
            Canvas.SetLeft(label, 0);
            Canvas.SetTop(label, y - 8);
            axisCanvas.Children.Add(label);
        }
        Children.Add(axisCanvas);

        // 2. Plot Host (Column 1, Row 0): Grid lines + Focus bars
        var plotGrid = new Grid { Height = TotalPlotHeight, HorizontalAlignment = HorizontalAlignment.Stretch };
        Grid.SetColumn(plotGrid, 1);
        Children.Add(plotGrid);

        // Background canvas for dashed rectangular grid lines
        var gridCanvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        plotGrid.Children.Add(gridCanvas);

        var hLines = new List<Line>();
        var vLines = new List<Line>();
        var gridStroke = Presentation.ThemeBrush("FkSecondary", this);
        const double gridOpacity = 0.38;

        // Horizontal lines at each 5h tick
        for (int h = 0; h <= ceilingHours; h += 5)
        {
            double fraction = (double)h / ceilingHours;
            double y = TopHeadroom + PlotAreaHeight * (1.0 - fraction);
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
                Y1 = TopHeadroom,
                Y2 = TopHeadroom + PlotAreaHeight,
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

            string durationText = ReportsFormatting.FormatBarDuration(bucket.Totals.FocusTime);
            if (!string.IsNullOrEmpty(durationText))
            {
                var durationLabel = new TextBlock
                {
                    Text = durationText,
                    FontSize = 11,
                    FontFamily = new FontFamily("Consolas"),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Presentation.ThemeBrush("FkForeground", this),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Language = "en-US",
                    FlowDirection = FlowDirection.LeftToRight,
                    TextReadingOrder = TextReadingOrder.UseFlowDirection,
                    Margin = new Thickness(0, 0, 0, 4)
                };
                barStack.Children.Add(durationLabel);
            }

            if (focusSecs > 0)
            {
                double rawHeight = (focusSecs / (ceilingHours * 3600.0)) * PlotAreaHeight;
                double barHeight = Math.Max(4, Math.Min(PlotAreaHeight, rawHeight));
                var bar = new Border
                {
                    Height = barHeight,
                    Width = 44, // Dynamically adjusted on SizeChanged
                    Background = SessionColorBrush.Create(palette.Work),
                    CornerRadius = new CornerRadius(4, 4, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Bottom
                };
                barBorders.Add(bar);
                barStack.Children.Add(bar);
            }

            colContainer.Children.Add(barStack);

            // Native tooltip & accessibility
            string tooltip = ReportsFormatting.FormatTooltip(bucket, period);
            ToolTipService.SetToolTip(colContainer, tooltip);
            AutomationProperties.SetName(colContainer, tooltip);

            Grid.SetColumn(colContainer, i);
            barsGrid.Children.Add(colContainer);
        }

        plotGrid.Children.Add(barsGrid);

        // Calm empty notice if zero activity across entire period
        if (effectiveMax <= 0)
        {
            var emptyNotice = Presentation.DimText("No focus activity recorded for this period", 12);
            emptyNotice.HorizontalAlignment = HorizontalAlignment.Center;
            emptyNotice.VerticalAlignment = VerticalAlignment.Center;
            emptyNotice.Margin = new Thickness(0, TopHeadroom, 0, 0);
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

                // Update bar widths: ~72% of column width, clamped comfortably
                double dynamicBarWidth = Math.Clamp(Math.Floor(colW * 0.72), 24, 108);
                foreach (var bar in barBorders)
                {
                    bar.Width = dynamicBarWidth;
                }
            }
        };

        // 3. X-Axis Day/Week Labels (Row 1, Column 1)
        var labelsGrid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
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
                Spacing = 2
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
                    FontSize = 11,
                    FontWeight = isRightmost ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = Presentation.ThemeBrush(isRightmost ? "FkForeground" : "FkSecondary", this),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Language = "en-US",
                    FlowDirection = FlowDirection.LeftToRight,
                    TextReadingOrder = TextReadingOrder.UseFlowDirection
                };
                var dayText = new TextBlock
                {
                    Text = ReportsFormatting.FormatDayOfWeek(day),
                    FontSize = 10,
                    FontWeight = isRightmost ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = Presentation.ThemeBrush(isRightmost ? "FkForeground" : "FkSecondary", this),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Language = "en-US",
                    FlowDirection = FlowDirection.LeftToRight,
                    TextReadingOrder = TextReadingOrder.UseFlowDirection
                };
                labelStack.Children.Add(dateText);
                labelStack.Children.Add(dayText);
            }
            else
            {
                // Monthly view: W1, W2, etc.
                var weekText = new TextBlock
                {
                    Text = ReportsFormatting.FormatMonthWeek(i + 1),
                    FontSize = 11,
                    FontWeight = isRightmost ? FontWeights.SemiBold : FontWeights.Normal,
                    Foreground = Presentation.ThemeBrush(isRightmost ? "FkForeground" : "FkSecondary", this),
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
