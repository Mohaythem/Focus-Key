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
    private readonly StackPanel _fields = new() { Spacing = 20 };
    private readonly ContentControl _editor = new();
    private readonly TextBox _workMinutes = Number("Work minutes");
    private readonly TextBox _workSeconds = Number("Work seconds");
    private readonly TextBox _breakMinutes = Number("Break minutes");
    private readonly TextBox _breakSeconds = Number("Break seconds");
    private readonly ComboBox _appearance = new() { ItemsSource = Enum.GetNames<Appearance>(), MinWidth = 140, FontSize = 12 };
    private readonly ColorPicker _workColor = Picker("Work color picker");
    private readonly ColorPicker _breakColor = Picker("Break color picker");
    private readonly Button _workButton = new();
    private readonly Button _breakButton = new();

    private static readonly string[] LightPresetNames =
    [
        ..ThemePresets.LightPresets.Select(p => p.DisplayName),
        "Custom"
    ];

    private static readonly string[] DarkPresetNames =
    [
        ..ThemePresets.DarkPresets.Select(p => p.DisplayName),
        "Custom"
    ];

    private readonly ComboBox _lightPreset = new() { ItemsSource = LightPresetNames, MinWidth = 200, FontSize = 12 };
    private readonly ColorPicker _lightBgColor = Picker("Light background color picker");
    private readonly ColorPicker _lightFgColor = Picker("Light foreground color picker");
    private readonly ColorPicker _lightAccentColor = Picker("Light accent color picker");
    private readonly Button _lightBgButton = new();
    private readonly Button _lightFgButton = new();
    private readonly Button _lightAccentButton = new();

    private readonly ComboBox _darkPreset = new() { ItemsSource = DarkPresetNames, MinWidth = 200, FontSize = 12 };
    private readonly ColorPicker _darkBgColor = Picker("Dark background color picker");
    private readonly ColorPicker _darkFgColor = Picker("Dark foreground color picker");
    private readonly ColorPicker _darkAccentColor = Picker("Dark accent color picker");
    private readonly Button _darkBgButton = new();
    private readonly Button _darkFgButton = new();
    private readonly Button _darkAccentButton = new();

    private readonly Button _reload = new() { Content = "Reload saved values", FontSize = 12, Padding = new Thickness(12, 6, 12, 6) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap };
    private bool _applying;
    private bool _workDirty, _breakDirty;

    internal SettingsView(SettingsService settings, Func<Task> refresh, Action<Exception> report)
    {
        _controller = new(settings, refresh, report);
        AutomationProperties.SetName(_appearance, "Appearance");
        AutomationProperties.SetName(_lightPreset, "Light theme preset");
        AutomationProperties.SetName(_darkPreset, "Dark theme preset");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(_status, "SettingsStatus");
        if (Application.Current?.Resources["FkMutedText"] is Style statusStyle) _status.Style = statusStyle;
        _status.FontSize = 11;

        HorizontalAlignment = HorizontalAlignment.Stretch;

        // 1. APPEARANCE
        var appearance = new StackPanel { Spacing = 0 };
        appearance.Children.Add(Row("Color scheme", "Controls whether the application uses light, dark, or system theme", _appearance, true));
        _fields.Children.Add(Section("APPEARANCE", appearance));

        // 2. LIGHT THEME
        var lightTheme = new StackPanel { Spacing = 0 };
        ConfigureColor(_lightBgButton, _lightBgColor, "Light background");
        ConfigureColor(_lightFgButton, _lightFgColor, "Light foreground");
        ConfigureColor(_lightAccentButton, _lightAccentColor, "Light accent");
        lightTheme.Children.Add(Row("Preset", "Curated light theme palette", _lightPreset));
        lightTheme.Children.Add(Row("Background", "Light page and window background", _lightBgButton));
        lightTheme.Children.Add(Row("Foreground", "Light primary text and icons", _lightFgButton));
        lightTheme.Children.Add(Row("Accent", "Light interactive accent and highlights", _lightAccentButton, true));
        _fields.Children.Add(Section("LIGHT THEME", lightTheme));

        // 3. DARK THEME
        var darkTheme = new StackPanel { Spacing = 0 };
        ConfigureColor(_darkBgButton, _darkBgColor, "Dark background");
        ConfigureColor(_darkFgButton, _darkFgColor, "Dark foreground");
        ConfigureColor(_darkAccentButton, _darkAccentColor, "Dark accent");
        darkTheme.Children.Add(Row("Preset", "Curated dark theme palette", _darkPreset));
        darkTheme.Children.Add(Row("Background", "Dark page and window background", _darkBgButton));
        darkTheme.Children.Add(Row("Foreground", "Dark primary text and icons", _darkFgButton));
        darkTheme.Children.Add(Row("Accent", "Dark interactive accent and highlights", _darkAccentButton, true));
        _fields.Children.Add(Section("DARK THEME", darkTheme));

        // 4. SESSION COLORS
        var colors = new StackPanel { Spacing = 0 };
        ConfigureColor(_workButton, _workColor, "Work color");
        ConfigureColor(_breakButton, _breakColor, "Break color");
        colors.Children.Add(Row("Work color", "Used for work session indicators and timer", _workButton));
        colors.Children.Add(Row("Break color", "Used for break session indicators and timer", _breakButton, true));
        _fields.Children.Add(Section("SESSION COLORS", colors));

        // 5. SESSIONS
        var sessions = new StackPanel { Spacing = 0 };
        sessions.Children.Add(Row("Work duration", null, DurationFields(_workMinutes, _workSeconds)));
        sessions.Children.Add(Row("Break duration", null, DurationFields(_breakMinutes, _breakSeconds), true));
        _fields.Children.Add(Section("SESSIONS", sessions));

        // 6. SHORTCUT
        var shortcut = new StackPanel { Spacing = 0 };
        var kbdBadge = new Border
        {
            Style = Application.Current?.Resources["FkBadge"] as Style,
            Padding = new Thickness(10, 4, 10, 4),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock
            {
                Text = "Shift + F3",
                Style = Application.Current?.Resources["FkBadgeText"] as Style,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        shortcut.Children.Add(Row("Open overlay", "Global keyboard shortcut", kbdBadge, true));
        _fields.Children.Add(Section("SHORTCUT", shortcut));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actions.Children.Add(_reload);
        var panel = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };
        var help = Presentation.Text("Themes, presets, and colors save and apply automatically.\nDurations save when you leave the row or press Enter, and affect future sessions only.", 11, true);
        help.TextWrapping = TextWrapping.Wrap;
        panel.Children.Add(help);
        _editor.Content = _fields;
        _editor.HorizontalContentAlignment = HorizontalAlignment.Stretch;
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

        _lightPreset.SelectionChanged += async (_, _) =>
        {
            if (_applying) return;
            int idx = _lightPreset.SelectedIndex;
            if (idx >= 0 && idx < ThemePresets.LightPresets.Count)
            {
                await _controller.UpdateLightPresetAsync(ThemePresets.LightPresets[idx].Id);
            }
        };
        _lightBgColor.ColorChanged += async (_, _) =>
        {
            if (!_applying) await _controller.UpdateLightColorAsync(true, false, ColorValue(_lightBgColor));
        };
        _lightFgColor.ColorChanged += async (_, _) =>
        {
            if (!_applying) await _controller.UpdateLightColorAsync(false, true, ColorValue(_lightFgColor));
        };
        _lightAccentColor.ColorChanged += async (_, _) =>
        {
            if (!_applying) await _controller.UpdateLightColorAsync(false, false, ColorValue(_lightAccentColor));
        };

        _darkPreset.SelectionChanged += async (_, _) =>
        {
            if (_applying) return;
            int idx = _darkPreset.SelectedIndex;
            if (idx >= 0 && idx < ThemePresets.DarkPresets.Count)
            {
                await _controller.UpdateDarkPresetAsync(ThemePresets.DarkPresets[idx].Id);
            }
        };
        _darkBgColor.ColorChanged += async (_, _) =>
        {
            if (!_applying) await _controller.UpdateDarkColorAsync(true, false, ColorValue(_darkBgColor));
        };
        _darkFgColor.ColorChanged += async (_, _) =>
        {
            if (!_applying) await _controller.UpdateDarkColorAsync(false, true, ColorValue(_darkFgColor));
        };
        _darkAccentColor.ColorChanged += async (_, _) =>
        {
            if (!_applying) await _controller.UpdateDarkColorAsync(false, false, ColorValue(_darkAccentColor));
        };

        WireDuration(_workMinutes, _workSeconds, true);
        WireDuration(_breakMinutes, _breakSeconds, false);
        _controller.Loaded += saved =>
        {
            _workDirty = _breakDirty = false;
            _applying = true;
            try
            {
                foreach (var field in Enum.GetValues<SettingsField>()) SetField(field, saved);
                SetThemeFields(saved.LightTheme ?? ThemeConfiguration.DefaultLight, _lightPreset, _lightBgColor, _lightFgColor, _lightAccentColor, false);
                SetThemeFields(saved.DarkTheme ?? ThemeConfiguration.DefaultDark, _darkPreset, _darkBgColor, _darkFgColor, _darkAccentColor, true);
            }
            finally { _applying = false; }
        };
        _controller.Settled += SetField;
        _controller.Changed += Render;
        ActualThemeChanged += (_, _) => Render();
        Render();
    }

    internal Task OpenAsync() => _controller.LoadAsync();

    private void WireDuration(TextBox minutes, TextBox seconds, bool work)
    {
        foreach (var box in new[] { minutes, seconds })
        {
            box.TextChanging += (_, _) =>
            {
                string normalized = SettingsPageController.NormalizeDigits(box.Text);
                if (normalized == box.Text) return;
                int start = box.SelectionStart, length = box.SelectionLength;
                box.Text = normalized;
                box.Select(Math.Min(start, normalized.Length), Math.Min(length, normalized.Length - Math.Min(start, normalized.Length)));
            };
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
                case SettingsField.LightPreset:
                case SettingsField.LightBackground:
                case SettingsField.LightForeground:
                case SettingsField.LightAccent:
                    SetThemeFields(saved.LightTheme ?? ThemeConfiguration.DefaultLight, _lightPreset, _lightBgColor, _lightFgColor, _lightAccentColor, false);
                    break;
                case SettingsField.DarkPreset:
                case SettingsField.DarkBackground:
                case SettingsField.DarkForeground:
                case SettingsField.DarkAccent:
                    SetThemeFields(saved.DarkTheme ?? ThemeConfiguration.DefaultDark, _darkPreset, _darkBgColor, _darkFgColor, _darkAccentColor, true);
                    break;
            }
        }
        finally { _applying = false; }
    }

    private void SetThemeFields(ThemeConfiguration config, ComboBox presetCombo,
        ColorPicker bgPicker, ColorPicker fgPicker, ColorPicker accentPicker, bool isDark)
    {
        bgPicker.Color = SessionColorBrush.Create(config.Background).Color;
        fgPicker.Color = SessionColorBrush.Create(config.Foreground).Color;
        accentPicker.Color = SessionColorBrush.Create(config.Accent).Color;

        var presets = isDark ? ThemePresets.DarkPresets : ThemePresets.LightPresets;
        int idx = -1;
        for (int i = 0; i < presets.Count; i++)
        {
            if (string.Equals(presets[i].Id, config.Preset, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }
        presetCombo.SelectedIndex = idx >= 0 ? idx : presets.Count;
    }

    private void Render()
    {
        _editor.IsEnabled = !_controller.IsLoading && _controller.Saved is not null;
        _reload.IsEnabled = !_controller.IsLoading;
        _reload.Visibility = _controller.IsLoading || _controller.Saved is null ? Visibility.Visible : Visibility.Collapsed;
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
        button.Style = Application.Current?.Resources["FkColorButton"] as Style;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.HorizontalAlignment = HorizontalAlignment.Right;
        void Preview()
        {
            var color = ColorValue(picker);
            var preview = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            var swatch = new Border
            {
                Style = Application.Current?.Resources["FkSwatchBorder"] as Style,
                Background = SessionColorBrush.Create(color),
                VerticalAlignment = VerticalAlignment.Center
            };
            var hexText = new TextBlock
            {
                Text = color.Value.ToUpperInvariant(),
                Style = Application.Current?.Resources["FkColorHexText"] as Style,
                VerticalAlignment = VerticalAlignment.Center
            };
            preview.Children.Add(swatch);
            preview.Children.Add(hexText);
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
        var box = new TextBox
        {
            Width = 60,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            Language = "en-US",
            FlowDirection = FlowDirection.LeftToRight,
            TextReadingOrder = TextReadingOrder.UseFlowDirection,
            TextAlignment = TextAlignment.Center,
            InputScope = scope,
            Padding = new Thickness(6, 4, 6, 4),
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(box, name);
        return box;
    }

    private static StackPanel DurationFields(TextBox minutes, TextBox seconds)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(minutes);
        var minutesLabel = Presentation.Text("min", 12, true);
        minutesLabel.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(minutesLabel);
        panel.Children.Add(seconds);
        var secondsLabel = Presentation.Text("sec", 12, true);
        secondsLabel.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(secondsLabel);
        return panel;
    }

    private static StackPanel Section(string title, UIElement content)
    {
        var section = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        var heading = Presentation.DimText(title);
        if (Application.Current?.Resources["FkSectionText"] is Style style) heading.Style = style;
        section.Children.Add(heading);
        section.Children.Add(Presentation.Card(content, 0));
        return section;
    }

    private static Grid Row(string label, string? description, FrameworkElement control, bool last = false)
    {
        var grid = new Grid
        {
            MinHeight = 52,
            ColumnSpacing = 24,
            Padding = new Thickness(16, 14, 16, 14),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!last)
        {
            grid.Style = Application.Current?.Resources["FkSettingRow"] as Style;
            grid.BorderThickness = new Thickness(0, 0, 0, 1);
        }
        var copy = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var labelText = Presentation.Text(label, 13);
        labelText.TextWrapping = TextWrapping.Wrap;
        copy.Children.Add(labelText);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var descriptionText = Presentation.DimText(description);
            descriptionText.TextWrapping = TextWrapping.Wrap;
            copy.Children.Add(descriptionText);
        }
        grid.Children.Add(copy);
        Grid.SetColumn(copy, 0);

        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        return grid;
    }
}
