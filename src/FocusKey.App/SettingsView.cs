using System.Globalization;
using FocusKey.Foundation.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

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
    private readonly Button _reload = new() { Content = "Reload saved values" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private bool _applying;
    private bool _workDirty, _breakDirty;

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
        actions.Children.Add(_reload);
        var panel = new StackPanel { Spacing = 16 };
        panel.Children.Add(new TextBlock { Text = "Theme and colors save and apply automatically.\nDurations save when you leave the row or press Enter, and affect future sessions only.", TextWrapping = TextWrapping.Wrap });
        _editor.Content = _fields;
        panel.Children.Add(_editor);
        panel.Children.Add(actions);
        panel.Children.Add(_status);
        Content = panel;
        _reload.Click += async (_, _) => await OpenAsync();
        _appearance.SelectionChanged += async (_, _) =>
        {
            if (!_applying) await _controller.UpdateAppearanceAsync((Appearance)_appearance.SelectedIndex);
        };
        _workColor.ColorChanged += async (_, _) =>
        {
            if (!_applying) await _controller.UpdateWorkColorAsync(ColorValue(_workColor));
        };
        _breakColor.ColorChanged += async (_, _) =>
        {
            if (!_applying) await _controller.UpdateBreakColorAsync(ColorValue(_breakColor));
        };
        WireDuration(_workMinutes, _workSeconds, true);
        WireDuration(_breakMinutes, _breakSeconds, false);
        _controller.Loaded += saved =>
        {
            _workDirty = _breakDirty = false;
            foreach (var field in Enum.GetValues<SettingsField>()) SetField(field, saved);
        };
        _controller.Settled += SetField;
        _controller.Changed += Render;
        Render();
    }

    internal Task OpenAsync() => _controller.LoadAsync();

    private void WireDuration(TextBox minutes, TextBox seconds, bool work)
    {
        foreach (var box in new[] { minutes, seconds })
        {
            box.TextChanged += (_, _) =>
            {
                if (!_applying) { if (work) _workDirty = true; else _breakDirty = true; Render(); }
            };
            box.KeyDown += async (_, args) =>
            {
                if (args.Key == VirtualKey.Enter) { args.Handled = true; await CommitDurationAsync(work); }
            };
            box.LostFocus += (_, _) => DispatcherQueue.TryEnqueue(async () =>
            {
                var focused = XamlRoot is null ? null : FocusManager.GetFocusedElement(XamlRoot);
                if (!ReferenceEquals(focused, minutes) && !ReferenceEquals(focused, seconds)) await CommitDurationAsync(work);
            });
        }
    }

    private Task CommitDurationAsync(bool work)
    {
        if (work ? !_workDirty : !_breakDirty) return Task.CompletedTask;
        if (work) _workDirty = false; else _breakDirty = false;
        return _controller.UpdateDurationAsync(work, work ? _workMinutes.Text : _breakMinutes.Text,
            work ? _workSeconds.Text : _breakSeconds.Text);
    }

    internal void CommitPendingDurations()
    {
        _ = CommitDurationAsync(true);
        _ = CommitDurationAsync(false);
    }

    internal async Task FlushAsync()
    {
        CommitPendingDurations();
        await _controller.DrainAsync();
    }

    private void SetField(SettingsField field, ApplicationSettings saved)
    {
        _applying = true;
        try
        {
            switch (field)
            {
                case SettingsField.WorkDuration when !_workDirty: SetDuration(saved.WorkDuration, _workMinutes, _workSeconds); break;
                case SettingsField.BreakDuration when !_breakDirty: SetDuration(saved.BreakDuration, _breakMinutes, _breakSeconds); break;
                case SettingsField.Appearance: _appearance.SelectedIndex = (int)saved.Appearance; break;
                case SettingsField.WorkColor: _workColor.Color = SessionColorBrush.Create(saved.WorkColor).Color; break;
                case SettingsField.BreakColor: _breakColor.Color = SessionColorBrush.Create(saved.BreakColor).Color; break;
            }
        }
        finally { _applying = false; }
    }

    private void Render()
    {
        _editor.IsEnabled = !_controller.IsLoading && _controller.Saved is not null;
        _reload.IsEnabled = !_controller.IsLoading;
        _status.Text = (_workDirty || _breakDirty ? "Duration edit not yet applied. Leave the row or press Enter. " : "") + _controller.Message;
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
        // Numeric editors use an explicit font/language instead of keyboard-dependent font fallback.
        var scope = new InputScope();
        scope.Names.Add(new InputScopeName { NameValue = InputScopeNameValue.Number });
        var box = new TextBox { Width = 85, FontFamily = new FontFamily("Segoe UI"),
            Language = "en-US", FlowDirection = FlowDirection.LeftToRight, InputScope = scope };
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
