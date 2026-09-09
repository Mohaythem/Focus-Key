using System.Globalization;
using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Automation;

namespace FocusKey;

/// <summary>Proportional completed-time bars. The report snapshot remains the only data source.</summary>
internal sealed class ReportsChart : Grid
{
    private const double PlotHeight = 144;

    internal ReportsChart(IReadOnlyList<ReportBucket> trend, double maximum, SessionColors colors)
    {
        ColumnSpacing = 12;
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(PlotHeight) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        double scale = Math.Max(60, maximum);

        var axis = new Grid();
        var plot = new Grid();
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
        Children.Add(axis);
        Grid.SetColumn(plot, 1);
        Children.Add(plot);
        var groups = new Grid { ColumnSpacing = 4 };
        var labels = new Grid { ColumnSpacing = 4, Margin = new Thickness(0, 8, 0, 0) };
        for (int i = 0; i < trend.Count; i++)
        {
            groups.ColumnDefinitions.Add(new ColumnDefinition());
            labels.ColumnDefinitions.Add(new ColumnDefinition());
            var bucket = trend[i];
            var pair = new Grid { ColumnSpacing = 3, MaxWidth = 28, HorizontalAlignment = HorizontalAlignment.Center };
            pair.ColumnDefinitions.Add(new ColumnDefinition());
            pair.ColumnDefinitions.Add(new ColumnDefinition());
            AddBar(pair, bucket.Totals.FocusTime.TotalSeconds, scale, colors.Work, "Work", bucket.Label, 0);
            AddBar(pair, bucket.Totals.BreakTime.TotalSeconds, scale, colors.Break, "Break", bucket.Label, 1);
            Grid.SetColumn(pair, i);
            groups.Children.Add(pair);
            // At 24 hourly groups, label every third hour; full values remain in the details table.
            string caption = trend.Count == 24 ? (i % 3 == 0 ? i.ToString("00", CultureInfo.InvariantCulture) : "") :
                DateOnly.TryParseExact(bucket.Label, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                    ? day.ToString("ddd", CultureInfo.InvariantCulture) : $"W{i + 1}";
            var text = Presentation.DimText(caption, 10);
            text.HorizontalAlignment = HorizontalAlignment.Center;
            ToolTipService.SetToolTip(text, bucket.Label);
            Grid.SetColumn(text, i);
            labels.Children.Add(text);
        }
        plot.Children.Add(groups);
        Grid.SetRow(labels, 1);
        Grid.SetColumn(labels, 1);
        Children.Add(labels);
    }

    private static void AddBar(Grid parent, double value, double maximum, HexColor color, string kind, string label, int column)
    {
        var bar = new Border
        {
            Height = Math.Max(value > 0 ? 2 : 0, PlotHeight * value / maximum),
            Background = SessionColorBrush.Create(color),
            VerticalAlignment = VerticalAlignment.Bottom,
            CornerRadius = new CornerRadius(2, 2, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        string description = $"{label} · {kind}: {Presentation.Duration(TimeSpan.FromSeconds(value))}";
        ToolTipService.SetToolTip(bar, description);
        AutomationProperties.SetName(bar, description);
        Grid.SetColumn(bar, column);
        parent.Children.Add(bar);
    }
}
