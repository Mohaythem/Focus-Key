using System.Globalization;
using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;

namespace FocusKey;

/// <summary>Proportional completed-time bars. The report snapshot remains the only data source.</summary>
internal sealed class ReportsChart : Grid
{
    private const double PlotHeight = 180;

    internal ReportsChart(IReadOnlyList<ReportBucket> rawTrend, double maximum, ReportsPalette palette)
    {
        ColumnSpacing = 12;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(PlotHeight) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var trend = PrepareBuckets(rawTrend);
        double effectiveMax = trend.Count == 0 ? 0 : trend.Max(b => Math.Max(b.Totals.FocusTime.TotalSeconds, b.Totals.BreakTime.TotalSeconds));
        double scale = NiceScale(effectiveMax);

        var axis = new Grid();
        var plot = new Grid();

        if (effectiveMax <= 0)
        {
            // When no completed sessions exist, draw only the baseline and a calm empty notice.
            var zeroLabel = Presentation.DimText(Presentation.Duration(TimeSpan.Zero), 10);
            zeroLabel.HorizontalAlignment = HorizontalAlignment.Right;
            zeroLabel.VerticalAlignment = VerticalAlignment.Bottom;
            axis.Children.Add(zeroLabel);

            var baseline = new Border
            {
                Style = Application.Current?.Resources["FkChartGridLine"] as Style,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            plot.Children.Add(baseline);

            var emptyNotice = Presentation.DimText("No focus activity recorded for this period", 12);
            emptyNotice.HorizontalAlignment = HorizontalAlignment.Center;
            emptyNotice.VerticalAlignment = VerticalAlignment.Center;
            plot.Children.Add(emptyNotice);
        }
        else
        {
            foreach (double fraction in new[] { 0.0, 0.5, 1.0 })
            {
                var label = Presentation.DimText(Presentation.Duration(TimeSpan.FromSeconds(scale * (1 - fraction))), 10);
                label.HorizontalAlignment = HorizontalAlignment.Right;
                label.VerticalAlignment = fraction == 0 ? VerticalAlignment.Top : fraction == 1 ? VerticalAlignment.Bottom : VerticalAlignment.Center;
                axis.Children.Add(label);
                var line = new Border
                {
                    Style = Application.Current?.Resources["FkChartGridLine"] as Style,
                    VerticalAlignment = label.VerticalAlignment
                };
                plot.Children.Add(line);
            }

            var groups = new Grid { ColumnSpacing = 8 };
            var labels = new Grid { ColumnSpacing = 8, Margin = new Thickness(0, 8, 0, 0) };
            var barPairs = new List<(Grid Pair, Border? WorkBar, Border? BreakBar)>();
            for (int i = 0; i < trend.Count; i++)
            {
                groups.ColumnDefinitions.Add(new ColumnDefinition());
                labels.ColumnDefinitions.Add(new ColumnDefinition());
                var bucket = trend[i];
                var pair = new Grid { ColumnSpacing = 4, HorizontalAlignment = HorizontalAlignment.Center };
                var col0 = new ColumnDefinition { Width = new GridLength(28) };
                var col1 = new ColumnDefinition { Width = new GridLength(28) };
                pair.ColumnDefinitions.Add(col0);
                pair.ColumnDefinitions.Add(col1);
                var workBar = AddBar(pair, bucket.Totals.FocusTime.TotalSeconds, scale, palette.Work, "Work", bucket.Label, 0, 28);
                var breakBar = AddBar(pair, bucket.Totals.BreakTime.TotalSeconds, scale, palette.Break, "Break", bucket.Label, 1, 28);
                barPairs.Add((pair, workBar, breakBar));
                Grid.SetColumn(pair, i);
                groups.Children.Add(pair);

                // Caption:
                // - 8-block daily view: show starting hour "00:00", "03:00", etc.
                // - weekly view (7 days): show "Mon", "Tue", etc.
                // - monthly view (calendar weeks): show "W1", "W2", etc.
                string caption = rawTrend.Count == 24
                    ? string.Create(CultureInfo.InvariantCulture, $"{i * 3:00}:00")
                    : DateOnly.TryParseExact(bucket.Label, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                        ? day.ToString("ddd", CultureInfo.InvariantCulture)
                        : $"W{i + 1}";
                var text = Presentation.DimText(caption, 10);
                text.HorizontalAlignment = HorizontalAlignment.Center;
                ToolTipService.SetToolTip(text, bucket.Label);
                Grid.SetColumn(text, i);
                labels.Children.Add(text);
            }

            // Responsively adjust bar widths to fill chart comfortably without becoming chunky
            groups.SizeChanged += (_, args) =>
            {
                double width = args.NewSize.Width;
                if (width <= 0 || trend.Count == 0) return;
                double colWidth = width / trend.Count;
                double dynamicBarWidth = Math.Clamp(Math.Floor((colWidth - 14) / 2.5), 20, 34);
                foreach (var item in barPairs)
                {
                    item.Pair.ColumnDefinitions[0].Width = new GridLength(dynamicBarWidth);
                    item.Pair.ColumnDefinitions[1].Width = new GridLength(dynamicBarWidth);
                    if (item.WorkBar is not null) item.WorkBar.Width = dynamicBarWidth;
                    if (item.BreakBar is not null) item.BreakBar.Width = dynamicBarWidth;
                }
            };
            plot.Children.Add(groups);
            Grid.SetRow(labels, 1);
            Grid.SetColumn(labels, 1);
            Children.Add(labels);
        }

        Children.Add(axis);
        Grid.SetColumn(plot, 1);
        Children.Add(plot);
    }

    private static IReadOnlyList<ReportBucket> PrepareBuckets(IReadOnlyList<ReportBucket> trend)
    {
        if (trend.Count != 24) return trend;
        var blocks = new List<ReportBucket>(8);
        for (int i = 0; i < 8; i++)
        {
            int startHour = i * 3;
            int endHour = startHour + 3;
            var window = trend.Skip(startHour).Take(3).ToArray();
            TimeSpan focus = TimeSpan.FromTicks(window.Sum(b => b.Totals.FocusTime.Ticks));
            TimeSpan rest = TimeSpan.FromTicks(window.Sum(b => b.Totals.BreakTime.Ticks));
            int started = window.Sum(b => b.Totals.Started);
            int workStarted = window.Sum(b => b.Totals.WorkStarted);
            int breakStarted = window.Sum(b => b.Totals.BreakStarted);
            int completedWork = window.Sum(b => b.Totals.CompletedWork);
            int completedBreak = window.Sum(b => b.Totals.CompletedBreak);
            int stopped = window.Sum(b => b.Totals.Stopped);
            int interrupted = window.Sum(b => b.Totals.Interrupted);
            int running = window.Sum(b => b.Totals.Running);
            var totals = new ReportTotals(started, workStarted, breakStarted, completedWork, completedBreak, stopped, interrupted, running, focus, rest);
            string label = string.Create(CultureInfo.InvariantCulture, $"{startHour:00}:00–{endHour:00}:00");
            blocks.Add(new ReportBucket(label, totals));
        }
        return blocks;
    }

    private static double NiceScale(double maxSeconds)
    {
        if (maxSeconds <= 0) return 60;
        double minutes = Math.Ceiling(maxSeconds / 60.0);
        double niceMinutes;
        if (minutes <= 5) niceMinutes = 5;
        else if (minutes <= 10) niceMinutes = 10;
        else if (minutes <= 15) niceMinutes = 15;
        else if (minutes <= 20) niceMinutes = 20;
        else if (minutes <= 30) niceMinutes = 30;
        else if (minutes <= 45) niceMinutes = 45;
        else if (minutes <= 60) niceMinutes = 60;
        else if (minutes <= 90) niceMinutes = 90;
        else if (minutes <= 120) niceMinutes = 120;
        else if (minutes <= 180) niceMinutes = 180;
        else if (minutes <= 240) niceMinutes = 240;
        else niceMinutes = Math.Ceiling(minutes / 60.0) * 60;
        return niceMinutes * 60;
    }

    private static Border? AddBar(Grid parent, double value, double maximum, HexColor color, string kind, string label, int column, double initialWidth = 28)
    {
        if (value <= 0) return null;

        var stroke = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", parent);
        var bar = new Border
        {
            Width = initialWidth,
            Height = Math.Max(4, PlotHeight * value / maximum),
            Background = SessionColorBrush.Create(color),
            BorderBrush = stroke,
            BorderThickness = new Thickness(1, 1, 1, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            CornerRadius = new CornerRadius(3, 3, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        string description = $"{label} · {kind}: {Presentation.Duration(TimeSpan.FromSeconds(value))}";
        ToolTipService.SetToolTip(bar, description);
        AutomationProperties.SetName(bar, description);
        Grid.SetColumn(bar, column);
        parent.Children.Add(bar);
        return bar;
    }
}
