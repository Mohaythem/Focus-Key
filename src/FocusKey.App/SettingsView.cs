using System.Globalization;
using FocusKey.Foundation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace FocusKey;

/// <summary>Compact native editor; the controller and existing service own validation and saving.</summary>
internal sealed class SettingsView : UserControl
{
    private readonly SettingsPageController _controller;
    private readonly StackPanel _fields = new() { Spacing = 12 };
    private readonly ContentControl _editor = new();
    private readonly TextBox _workMinutes = Number("Work minutes");
    private readonly TextBox _workSeconds = Number("Work seconds");
    private readonly TextBox _breakMinutes = Number("Break minutes");
    private readonly TextBox _breakSeconds = Number("Break seconds");
    private readonly ComboBox _appearance = new() { ItemsSource = Enum.GetNames<Appearance>(), MinWidth = 160 };
    private readonly ColorPicker _workColor = Picker("Work color picker");
    private readonly ColorPicker _breakColor = Picker("Break color picker");
    private readonly Button _workButton = new();
    private readonly Button _breakButton = new();
    private readonly Button _save = new() { Content = "Save settings" };
    private readonly Button _reload = new() { Content = "Reload saved values" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private ApplicationSettings? _displayed;

    internal SettingsView(SettingsService settings, Func<Task> refresh, Action<Exception> report)
    {
        _controller = new(settings, refresh, report);
        AutomationProperties.SetName(_appearance, "Appearance");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(_status, "SettingsStatus");
        _fields.Children.Add(Row("Work duration", DurationFields(_workMinutes, _workSeconds)));
        _fields.Children.Add(Row("Break duration", DurationFields(_breakMinutes, _breakSeconds)));
        _fields.Children.Add(Row("Appearance", _appearance));
        ConfigureColor(_workButton, _workColor, "Work color");
        ConfigureColor(_breakButton, _breakColor, "Break color");
        _fields.Children.Add(Row("Work color", _workButton));
        _fields.Children.Add(Row("Break color", _breakButton));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(_save);
        actions.Children.Add(_reload);
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = "Changes apply when saved. Durations affect future sessions only.\nReopening this page discards unsaved edits.", TextWrapping = TextWrapping.Wrap });
        _editor.Content = _fields;
        panel.Children.Add(_editor);
        panel.Children.Add(actions);
        panel.Children.Add(_status);
        Content = panel;
        _save.Click += async (_, _) => await _controller.SaveAsync(ReadDraft);
        _reload.Click += async (_, _) => await OpenAsync();
        _controller.Changed += Render;
        Render();
    }

    internal Task OpenAsync() => _controller.LoadAsync();

    private ApplicationSettings ReadDraft() => new()
    {
        WorkDuration = SettingsPageController.Duration(_workMinutes.Text, _workSeconds.Text),
        BreakDuration = SettingsPageController.Duration(_breakMinutes.Text, _breakSeconds.Text),
        Appearance = (Appearance)_appearance.SelectedIndex,
        WorkColor = ColorValue(_workColor), BreakColor = ColorValue(_breakColor),
    };

    private void Render()
    {
        if (_controller.Saved is { } saved && !ReferenceEquals(saved, _displayed))
        {
            SetDuration(saved.WorkDuration, _workMinutes, _workSeconds);
            SetDuration(saved.BreakDuration, _breakMinutes, _breakSeconds);
            _appearance.SelectedIndex = (int)saved.Appearance;
            _workColor.Color = SessionColorBrush.Create(saved.WorkColor).Color;
            _breakColor.Color = SessionColorBrush.Create(saved.BreakColor).Color;
        }
        _displayed = _controller.Saved;
        _editor.IsEnabled = _save.IsEnabled = !_controller.IsBusy && _controller.Saved is not null;
        _reload.IsEnabled = !_controller.IsBusy;
        _status.Text = _controller.Message;
    }

    private static void SetDuration(TimeSpan duration, TextBox minutes, TextBox seconds)
    {
        minutes.Text = (duration.Ticks / TimeSpan.TicksPerMinute).ToString(CultureInfo.InvariantCulture);
        seconds.Text = duration.Seconds.ToString(CultureInfo.InvariantCulture);
    }

    private static HexColor ColorValue(ColorPicker picker) =>
        HexColor.Parse($"#{picker.Color.R:X2}{picker.Color.G:X2}{picker.Color.B:X2}");

    private static void ConfigureColor(Button button, ColorPicker picker, string name)
    {
        var flyout = new Flyout { Content = picker };
        button.Flyout = flyout;
        button.MinWidth = 160;
        void Preview()
        {
            var color = ColorValue(picker);
            var preview = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            preview.Children.Add(new Microsoft.UI.Xaml.Shapes.Rectangle { Width = 18, Height = 18,
                Fill = SessionColorBrush.Create(color), Stroke = SessionColorBrush.Create(SessionColors.Foreground(color)), StrokeThickness = 1 });
            preview.Children.Add(new TextBlock { Text = color.Value });
            button.Content = preview;
            AutomationProperties.SetName(button, $"{name}, {color.Value}, choose color");
        }
        picker.ColorChanged += (_, _) => Preview();
        Preview();
    }

    private static ColorPicker Picker(string name)
    {
        var picker = new ColorPicker { IsAlphaEnabled = false, IsAlphaSliderVisible = false,
            IsAlphaTextInputVisible = false, IsColorSpectrumVisible = true, IsColorSliderVisible = true,
            IsColorChannelTextInputVisible = true, IsHexInputVisible = true, Width = 280 };
        AutomationProperties.SetName(picker, name);
        return picker;
    }

    private static TextBox Number(string name)
    {
        var box = new TextBox { Width = 85 };
        AutomationProperties.SetName(box, name);
        return box;
    }
    private static StackPanel DurationFields(TextBox minutes, TextBox seconds)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        panel.Children.Add(minutes);
        panel.Children.Add(new TextBlock { Text = "min", VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(seconds);
        panel.Children.Add(new TextBlock { Text = "sec", VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }
    private static Grid Row(string label, FrameworkElement control)
    {
        var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }
}
