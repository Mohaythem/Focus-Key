using System.Globalization;
using FocusKey.Foundation.Settings;
using FocusKey.Shell;
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
    private readonly StackPanel _fields = new() { Spacing = 16 };
    private readonly ContentControl _editor = new();
    private readonly TextBox _workMinutes = Number("Work minutes");
    private readonly TextBox _workSeconds = Number("Work seconds");
    private readonly TextBox _breakMinutes = Number("Break minutes");
    private readonly TextBox _breakSeconds = Number("Break seconds");
    private readonly ComboBox _appearance = new() { ItemsSource = Enum.GetNames<Appearance>(), MinWidth = 160, FontSize = 12 };
    private static readonly string[] ContrastOptions = ["Standard", "Higher Contrast"];
    private readonly ComboBox _contrast = new() { ItemsSource = ContrastOptions, MinWidth = 160, FontSize = 12 };
    private static readonly string[] TimeFormatOptions = ["24-hour (09:05)", "12-hour (9:05 AM)"];
    private readonly ComboBox _timeFormat = new() { ItemsSource = TimeFormatOptions, MinWidth = 160, FontSize = 12 };
    private static readonly string[] UiScaleOptions = ["80%", "90%", "100%", "110%", "125%", "150%"];
    private readonly ComboBox _uiScale = new() { ItemsSource = UiScaleOptions, MinWidth = 160, FontSize = 12 };
    private readonly Button _resetOverlayPositionButton = new() { Content = "Reset position", FontSize = 12, Padding = new Thickness(12, 6, 12, 6) };
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
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _colorDebounceTimer;
    private readonly Dictionary<SettingsField, Func<Task>> _pendingColorActions = new();
    private Task _colorDrainTask = Task.CompletedTask;
    private bool _colorDrainRunning;
    private bool _applying;
    private bool _workDirty, _breakDirty;
    private readonly ToggleSwitch _sessionSounds = new() { OnContent = "On", OffContent = "Off", MinWidth = 0 };
    private readonly ToggleSwitch _startSound = new() { OnContent = "On", OffContent = "Off", MinWidth = 0 };
    private readonly ToggleSwitch _completionSound = new() { OnContent = "On", OffContent = "Off", MinWidth = 0 };
    private readonly Button _startSoundPreviewButton = new() { Content = "Preview", FontSize = 12, Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
    private readonly Button _completionSoundPreviewButton = new() { Content = "Preview", FontSize = 12, Padding = new Thickness(10, 5, 10, 5), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
    private readonly ToggleSwitch _startWithWindows = new() { OnContent = "On", OffContent = "Off", MinWidth = 0 };
    private readonly Button _importHistoryButton = new() { Content = "Import history…", FontSize = 12, Padding = new Thickness(12, 6, 12, 6) };
    private readonly Button _exportHistoryButton = new() { Content = "Export history…", FontSize = 12, Padding = new Thickness(12, 6, 12, 6) };
    private readonly FocusKey.Foundation.Shell.IWindowsStartupService _windowsStartup;
    private readonly FocusKey.Foundation.History.HistoricalFocusService _historyService;
    private readonly Func<Task> _refreshReports;
    private readonly Func<IntPtr> _getWindowHandle;
    private readonly Action<Exception> _report;
    private readonly Action? _previewStart;
    private readonly Action? _previewCompletion;
    internal delegate bool TryUpdateShortcutHandler(GlobalShortcut newShortcut, out string? error);
    private readonly TryUpdateShortcutHandler? _tryUpdateShortcut;
    private readonly TryUpdateShortcutHandler? _tryUpdateMainWindowShortcut;
    private readonly Action<GlobalShortcut>? _onShortcutChanged;
    private readonly Action<GlobalShortcut>? _onMainWindowShortcutChanged;
    private readonly Action<int>? _onUiScaleChanged;
    internal event Action<int>? UiScaleChanged;
    private readonly Button _shortcutButton = new();
    private readonly TextBlock _shortcutText = new();
    private readonly Button _resetShortcutButton = new();
    private readonly TextBlock _shortcutError = new() { Visibility = Visibility.Collapsed };
    private GlobalShortcut _currentShortcut = GlobalShortcut.Default;
    private bool _isListeningForShortcut;

    private readonly Button _mainWindowShortcutButton = new();
    private readonly TextBlock _mainWindowShortcutText = new();
    private readonly Button _resetMainWindowShortcutButton = new();
    private readonly TextBlock _mainWindowShortcutError = new() { Visibility = Visibility.Collapsed };
    private GlobalShortcut _currentMainWindowShortcut = GlobalShortcut.DefaultMainWindow;
    private bool _isListeningForMainWindowShortcut;
    private Action? _refreshWorkSwatches;
    private Action? _refreshBreakSwatches;
    private Action<bool>? _setAppearanceExpanded;
    private Action<bool>? _setShortcutsExpanded;
    private Action<bool>? _setAdvancedExpanded;

    private double _scaleFactor = 1.0;
    private int _uiScalePercent = UiScaleLevels.DefaultPercent;
    private readonly List<Action<double>> _scaleUpdaters = new();
    private void RegisterScaleAction(Action<double> action) => _scaleUpdaters.Add(action);

    internal SettingsView(
        SettingsService settings,
        FocusKey.Foundation.History.HistoricalFocusService history,
        FocusKey.Foundation.Shell.IWindowsStartupService windowsStartup,
        Func<Task> refresh,
        Func<Task> refreshReports,
        Func<IntPtr> getWindowHandle,
        Action<Exception> report,
        TryUpdateShortcutHandler? tryUpdateShortcut = null,
        TryUpdateShortcutHandler? tryUpdateMainWindowShortcut = null,
        Action<GlobalShortcut>? onShortcutChanged = null,
        Action<GlobalShortcut>? onMainWindowShortcutChanged = null,
        Action? previewStart = null,
        Action? previewCompletion = null,
        Action<int>? onUiScaleChanged = null)
    {
        _windowsStartup = windowsStartup ?? throw new ArgumentNullException(nameof(windowsStartup));
        _historyService = history ?? throw new ArgumentNullException(nameof(history));
        _refreshReports = refreshReports ?? throw new ArgumentNullException(nameof(refreshReports));
        _getWindowHandle = getWindowHandle ?? throw new ArgumentNullException(nameof(getWindowHandle));
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _tryUpdateShortcut = tryUpdateShortcut;
        _tryUpdateMainWindowShortcut = tryUpdateMainWindowShortcut;
        _onShortcutChanged = onShortcutChanged;
        _onMainWindowShortcutChanged = onMainWindowShortcutChanged;
        _previewStart = previewStart;
        _previewCompletion = previewCompletion;
        _onUiScaleChanged = onUiScaleChanged;
        _controller = new(settings, refresh, report);
        _colorDebounceTimer = DispatcherQueue.CreateTimer();
        _colorDebounceTimer.Interval = TimeSpan.FromMilliseconds(250);
        _colorDebounceTimer.Tick += (_, _) => StartColorDrain();
        Unloaded += async (_, _) => await FlushPendingColorSaveAsync();

        UpdateUiScaleSelection(UiScaleLevels.DefaultPercent);

        AutomationProperties.SetName(_appearance, "Color scheme");
        AutomationProperties.SetName(_contrast, "Contrast");
        AutomationProperties.SetName(_lightPreset, "Light theme preset");
        AutomationProperties.SetName(_darkPreset, "Dark theme preset");
        AutomationProperties.SetName(_timeFormat, "Clock format");
        AutomationProperties.SetName(_uiScale, "UI scale");
        AutomationProperties.SetAutomationId(_uiScale, "UiScaleComboBox");
        ToolTipService.SetToolTip(_uiScale, "Adjust application user interface scale.");
        AutomationProperties.SetName(_resetOverlayPositionButton, "Reset overlay window position");
        AutomationProperties.SetName(_sessionSounds, "Session sounds");
        AutomationProperties.SetName(_startSound, "Start sound");
        AutomationProperties.SetName(_completionSound, "Completion sound");
        AutomationProperties.SetName(_startSoundPreviewButton, "Preview start sound");
        AutomationProperties.SetName(_completionSoundPreviewButton, "Preview completion sound");
        ToolTipService.SetToolTip(_startSoundPreviewButton, "Preview start sound");
        ToolTipService.SetToolTip(_completionSoundPreviewButton, "Preview completion sound");
        AutomationProperties.SetName(_startWithWindows, "Start with Windows");
        AutomationProperties.SetName(_importHistoryButton, "Import history");
        AutomationProperties.SetName(_exportHistoryButton, "Export history");
        AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(_status, "SettingsStatus");
        if (Application.Current?.Resources["FkMutedText"] is Style statusStyle) _status.Style = statusStyle;
        _status.FontSize = 11;

        HorizontalAlignment = HorizontalAlignment.Stretch;

        RegisterScaleAction(factor =>
        {
            _workMinutes.Width = Math.Round(60 * factor);
            _workMinutes.Height = Math.Round(32 * factor);
            _workMinutes.FontSize = Math.Round(13 * factor);
            _workMinutes.Padding = new Thickness(Math.Round(6 * factor), Math.Round(4 * factor), Math.Round(6 * factor), Math.Round(4 * factor));

            _workSeconds.Width = Math.Round(60 * factor);
            _workSeconds.Height = Math.Round(32 * factor);
            _workSeconds.FontSize = Math.Round(13 * factor);
            _workSeconds.Padding = new Thickness(Math.Round(6 * factor), Math.Round(4 * factor), Math.Round(6 * factor), Math.Round(4 * factor));

            _breakMinutes.Width = Math.Round(60 * factor);
            _breakMinutes.Height = Math.Round(32 * factor);
            _breakMinutes.FontSize = Math.Round(13 * factor);
            _breakMinutes.Padding = new Thickness(Math.Round(6 * factor), Math.Round(4 * factor), Math.Round(6 * factor), Math.Round(4 * factor));

            _breakSeconds.Width = Math.Round(60 * factor);
            _breakSeconds.Height = Math.Round(32 * factor);
            _breakSeconds.FontSize = Math.Round(13 * factor);
            _breakSeconds.Padding = new Thickness(Math.Round(6 * factor), Math.Round(4 * factor), Math.Round(6 * factor), Math.Round(4 * factor));

            _appearance.MinWidth = Math.Round(160 * factor);
            _appearance.Height = Math.Round(32 * factor);
            _appearance.FontSize = Math.Round(12 * factor);

            _contrast.MinWidth = Math.Round(160 * factor);
            _contrast.Height = Math.Round(32 * factor);
            _contrast.FontSize = Math.Round(12 * factor);

            _timeFormat.MinWidth = Math.Round(160 * factor);
            _timeFormat.Height = Math.Round(32 * factor);
            _timeFormat.FontSize = Math.Round(12 * factor);

            _uiScale.MinWidth = Math.Round(160 * factor);
            _uiScale.Height = Math.Round(32 * factor);
            _uiScale.FontSize = Math.Round(12 * factor);

            _lightPreset.MinWidth = Math.Round(200 * factor);
            _lightPreset.Height = Math.Round(32 * factor);
            _lightPreset.FontSize = Math.Round(12 * factor);

            _darkPreset.MinWidth = Math.Round(200 * factor);
            _darkPreset.Height = Math.Round(32 * factor);
            _darkPreset.FontSize = Math.Round(12 * factor);

            _sessionSounds.FontSize = Math.Round(12 * factor);
            _startSound.FontSize = Math.Round(12 * factor);
            _completionSound.FontSize = Math.Round(12 * factor);
            _startWithWindows.FontSize = Math.Round(12 * factor);

            _startSoundPreviewButton.FontSize = Math.Round(12 * factor);
            _startSoundPreviewButton.Height = Math.Round(32 * factor);
            _startSoundPreviewButton.Padding = new Thickness(Math.Round(10 * factor), Math.Round(5 * factor), Math.Round(10 * factor), Math.Round(5 * factor));
            _startSoundPreviewButton.CornerRadius = new CornerRadius(Math.Round(4 * factor));

            _completionSoundPreviewButton.FontSize = Math.Round(12 * factor);
            _completionSoundPreviewButton.Height = Math.Round(32 * factor);
            _completionSoundPreviewButton.Padding = new Thickness(Math.Round(10 * factor), Math.Round(5 * factor), Math.Round(10 * factor), Math.Round(5 * factor));
            _completionSoundPreviewButton.CornerRadius = new CornerRadius(Math.Round(4 * factor));

            _resetOverlayPositionButton.FontSize = Math.Round(12 * factor);
            _resetOverlayPositionButton.Height = Math.Round(32 * factor);
            _resetOverlayPositionButton.Padding = new Thickness(Math.Round(12 * factor), Math.Round(6 * factor), Math.Round(12 * factor), Math.Round(6 * factor));

            _importHistoryButton.FontSize = Math.Round(12 * factor);
            _importHistoryButton.Height = Math.Round(32 * factor);
            _importHistoryButton.Padding = new Thickness(Math.Round(12 * factor), Math.Round(6 * factor), Math.Round(12 * factor), Math.Round(6 * factor));

            _exportHistoryButton.FontSize = Math.Round(12 * factor);
            _exportHistoryButton.Height = Math.Round(32 * factor);
            _exportHistoryButton.Padding = new Thickness(Math.Round(12 * factor), Math.Round(6 * factor), Math.Round(12 * factor), Math.Round(6 * factor));

            _reload.FontSize = Math.Round(12 * factor);
            _reload.Height = Math.Round(32 * factor);
            _reload.Padding = new Thickness(Math.Round(12 * factor), Math.Round(6 * factor), Math.Round(12 * factor), Math.Round(6 * factor));

            _status.FontSize = Math.Round(11 * factor);
        });

        // 1. SESSION (Permanent Top-Level Card)
        var sessionLayout = new StackPanel { Spacing = 12 };
        RegisterScaleAction(factor => sessionLayout.Spacing = Math.Round(12 * factor));

        // Sub-card A: DURATIONS
        var durationsPanel = new StackPanel { Spacing = 0 };
        durationsPanel.Children.Add(Row("Work duration", "Length of work focus sessions.", DurationFields(_workMinutes, _workSeconds), false));
        durationsPanel.Children.Add(Row("Break duration", "Length of break rest sessions.", DurationFields(_breakMinutes, _breakSeconds), true));
        sessionLayout.Children.Add(SubCard("DURATIONS", durationsPanel));

        // Sub-card B: SESSION SOUNDS (Master + Per-Sound Model)
        var soundsPanel = new StackPanel { Spacing = 0 };
        soundsPanel.Children.Add(Row("Session sounds", "Play audio cues for session start and completion.", _sessionSounds, false));
        var startSoundControl = SoundActionFields(_startSoundPreviewButton, _startSound);
        soundsPanel.Children.Add(Row("Start sound", "Played when a Work or Break session starts.", startSoundControl, false));
        var completionSoundControl = SoundActionFields(_completionSoundPreviewButton, _completionSound);
        soundsPanel.Children.Add(Row("Completion sound", "Played when a session completes.", completionSoundControl, true));
        sessionLayout.Children.Add(SubCard("SESSION SOUNDS", soundsPanel));

        _fields.Children.Add(BuildSessionSection(sessionLayout));

        // 2. APPEARANCE (Collapsible Top-Level Card)
        var appearanceLayout = new StackPanel { Spacing = 12 };
        RegisterScaleAction(factor => appearanceLayout.Spacing = Math.Round(12 * factor));

        // Sub-card A: SYSTEM APPEARANCE
        var systemAppearance = new StackPanel { Spacing = 0 };
        systemAppearance.Children.Add(Row("Color scheme", "Choose system default, light, or dark theme.", _appearance, false));
        systemAppearance.Children.Add(Row("Contrast", "Enhance text legibility and border definition.", _contrast, true));
        appearanceLayout.Children.Add(SubCard("SYSTEM APPEARANCE", systemAppearance));

        // Sub-card B: LIGHT THEME
        var lightTheme = new StackPanel { Spacing = 0 };
        ConfigureColor(_lightBgButton, _lightBgColor, "Light background");
        ConfigureColor(_lightFgButton, _lightFgColor, "Light foreground");
        ConfigureColor(_lightAccentButton, _lightAccentColor, "Light accent");
        lightTheme.Children.Add(Row("Preset", "Curated light theme palette.", _lightPreset, false));
        lightTheme.Children.Add(Row("Background", "Light page and window background.", _lightBgButton, false));
        lightTheme.Children.Add(Row("Foreground", "Light primary text and icons.", _lightFgButton, false));
        lightTheme.Children.Add(Row("Accent", "Light interactive accent and highlights.", _lightAccentButton, true));
        appearanceLayout.Children.Add(SubCard("LIGHT THEME", lightTheme));

        // Sub-card C: DARK THEME
        var darkTheme = new StackPanel { Spacing = 0 };
        ConfigureColor(_darkBgButton, _darkBgColor, "Dark background");
        ConfigureColor(_darkFgButton, _darkFgColor, "Dark foreground");
        ConfigureColor(_darkAccentButton, _darkAccentColor, "Dark accent");
        darkTheme.Children.Add(Row("Preset", "Curated dark theme palette.", _darkPreset, false));
        darkTheme.Children.Add(Row("Background", "Dark page and window background.", _darkBgButton, false));
        darkTheme.Children.Add(Row("Foreground", "Dark primary text and icons.", _darkFgButton, false));
        darkTheme.Children.Add(Row("Accent", "Dark interactive accent and highlights.", _darkAccentButton, true));
        appearanceLayout.Children.Add(SubCard("DARK THEME", darkTheme));

        // Sub-card D: SESSION COLORS
        var sessionColors = new StackPanel { Spacing = 0 };
        ConfigureColor(_workButton, _workColor, "Work color");
        ConfigureColor(_breakButton, _breakColor, "Break color");
        var workSelector = BuildColorSelector(_workButton, _workColor, WorkColorPresets, true);
        var breakSelector = BuildColorSelector(_breakButton, _breakColor, BreakColorPresets, false);
        sessionColors.Children.Add(Row("Work color", "Used for work session indicators and timer.", workSelector, false));
        sessionColors.Children.Add(Row("Break color", "Used for break session indicators and timer.", breakSelector, true));
        appearanceLayout.Children.Add(SubCard("SESSION COLORS", sessionColors));

        // Sub-card E: DISPLAY
        var display = new StackPanel { Spacing = 0 };
        display.Children.Add(Row("Clock format", "Display time in 24-hour (09:05) or 12-hour (9:05 AM) format.", _timeFormat, false));
        display.Children.Add(Row("UI scale", "Adjust application user interface scale.", _uiScale, true));
        appearanceLayout.Children.Add(SubCard("DISPLAY", display));

        _fields.Children.Add(BuildCollapsibleSection(
            "APPEARANCE",
            appearanceLayout,
            _controller.Saved?.AppearanceExpanded ?? false,
            async expanded =>
            {
                await FlushPendingColorSaveAsync();
                await _controller.UpdateAppearanceExpandedAsync(expanded);
            },
            out _setAppearanceExpanded));

        // 3. SHORTCUTS (Collapsible Top-Level Card)
        var shortcutsLayout = new StackPanel { Spacing = 12 };
        RegisterScaleAction(factor => shortcutsLayout.Spacing = Math.Round(12 * factor));

        // Sub-card A: QUICK OVERLAY
        var overlayShortcutPanel = new StackPanel { Spacing = 0 };
        var overlayShortcutControl = BuildShortcutControl();
        overlayShortcutPanel.Children.Add(Row("Quick Overlay", "Global shortcut to toggle the quick overlay.", overlayShortcutControl, true));
        shortcutsLayout.Children.Add(SubCard("QUICK OVERLAY", overlayShortcutPanel));

        // Sub-card B: OPEN FOCUS KEY
        var mainWindowShortcutPanel = new StackPanel { Spacing = 0 };
        var mainWindowShortcutControl = BuildMainWindowShortcutControl();
        mainWindowShortcutPanel.Children.Add(Row("Open Focus Key", "Global shortcut to open and focus the main window.", mainWindowShortcutControl, true));
        shortcutsLayout.Children.Add(SubCard("OPEN FOCUS KEY", mainWindowShortcutPanel));

        _fields.Children.Add(BuildCollapsibleSection(
            "SHORTCUTS",
            shortcutsLayout,
            _controller.Saved?.ShortcutsExpanded ?? false,
            async expanded =>
            {
                await FlushPendingColorSaveAsync();
                await _controller.UpdateShortcutsExpandedAsync(expanded);
            },
            out _setShortcutsExpanded));

        // 4. ADVANCED (Collapsible Top-Level Card)
        var advancedLayout = new StackPanel { Spacing = 12 };
        RegisterScaleAction(factor => advancedLayout.Spacing = Math.Round(12 * factor));

        // Sub-card A: STARTUP
        var startupPanel = new StackPanel { Spacing = 0 };
        startupPanel.Children.Add(Row("Start with Windows", "Launch Focus Key automatically when you sign in.", _startWithWindows, true));
        advancedLayout.Children.Add(SubCard("STARTUP", startupPanel));

        // Sub-card B: QUICK OVERLAY POSITION
        var overlayPosPanel = new StackPanel { Spacing = 0 };
        overlayPosPanel.Children.Add(Row("Reset position", "Reset overlay window position to the center of your screen.", _resetOverlayPositionButton, true));
        advancedLayout.Children.Add(SubCard("QUICK OVERLAY", overlayPosPanel));

        // Sub-card C: DATA
        var dataPanel = new StackPanel { Spacing = 0 };
        dataPanel.Children.Add(Row("Import history", "Import focus history from a tab-delimited CSV file.", _importHistoryButton, false));
        dataPanel.Children.Add(Row("Export history", "Export all focus history to a tab-delimited CSV file.", _exportHistoryButton, true));
        advancedLayout.Children.Add(SubCard("DATA", dataPanel));

        _fields.Children.Add(BuildCollapsibleSection(
            "ADVANCED",
            advancedLayout,
            _controller.Saved?.AdvancedExpanded ?? false,
            async expanded =>
            {
                await FlushPendingColorSaveAsync();
                await _controller.UpdateAdvancedExpandedAsync(expanded);
            },
            out _setAdvancedExpanded));

        _editor.Content = _fields;
        _editor.HorizontalContentAlignment = HorizontalAlignment.Stretch;

        var footer = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(4, 20, 4, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        footer.Children.Add(_reload);
        footer.Children.Add(_status);

        // Settings Page Heading (Matching Today and Reports visual hierarchy)
        var title = new TextBlock
        {
            Text = "Settings",
            Style = Application.Current?.Resources["FkPageTitleText"] as Style,
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetHeadingLevel(title, AutomationHeadingLevel.Level1);

        var topHeader = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 4)
        };
        topHeader.Children.Add(title);

        var panel = new StackPanel { Spacing = 16, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(topHeader);
        panel.Children.Add(_editor);
        panel.Children.Add(footer);
        Content = panel;

        RegisterScaleAction(factor =>
        {
            _fields.Spacing = Math.Round(16 * factor);
            footer.Spacing = Math.Round(8 * factor);
            footer.Margin = new Thickness(Math.Round(4 * factor), Math.Round(20 * factor), Math.Round(4 * factor), Math.Round(16 * factor));
            title.FontSize = Math.Round(28 * factor);
            topHeader.Margin = new Thickness(0, 0, 0, Math.Round(4 * factor));
            panel.Spacing = Math.Round(16 * factor);
        });

        _reload.Click += async (_, _) =>
        {
            await FlushPendingColorSaveAsync();
            await OpenAsync();
        };
        _appearance.SelectionChanged += async (_, _) =>
        {
            if (!_applying)
            {
                await FlushPendingColorSaveAsync();
                await _controller.UpdateAppearanceAsync((Appearance)_appearance.SelectedIndex);
            }
        };
        _contrast.SelectionChanged += async (_, _) =>
        {
            if (!_applying && _contrast.SelectedIndex >= 0)
            {
                await FlushPendingColorSaveAsync();
                await _controller.UpdateContrastAsync((Contrast)_contrast.SelectedIndex);
            }
        };
        _timeFormat.SelectionChanged += async (_, _) =>
        {
            if (!_applying && _timeFormat.SelectedIndex >= 0)
            {
                await FlushPendingColorSaveAsync();
                await _controller.UpdateTimeFormatAsync((TimeFormat)_timeFormat.SelectedIndex);
            }
        };
        _uiScale.SelectionChanged += async (_, _) =>
        {
            if (_applying) return;
            if (_uiScale.SelectedIndex < 0) return;

            int percent = UiScaleLevels.DefaultPercent;
            if (_uiScale.SelectedItem is string text && text.EndsWith('%') && int.TryParse(text.TrimEnd('%'), out int parsed))
            {
                percent = parsed;
            }
            else if (_uiScale.SelectedIndex >= 0 && _uiScale.SelectedIndex < UiScaleOptions.Length &&
                     int.TryParse(UiScaleOptions[_uiScale.SelectedIndex].TrimEnd('%'), out int optParsed))
            {
                percent = optParsed;
            }

            if (!UiScaleLevels.IsValid(percent))
                percent = UiScaleLevels.DefaultPercent;

            await FlushPendingColorSaveAsync();
            _onUiScaleChanged?.Invoke(percent);
            UiScaleChanged?.Invoke(percent);
            await _controller.UpdateUiScaleAsync(percent);
        };
        _resetOverlayPositionButton.Click += async (_, _) =>
        {
            if (_applying) return;
            await FlushPendingColorSaveAsync();
            await _controller.ResetOverlayPositionAsync();
        };
        _workColor.ColorChanged += (_, _) =>
        {
            if (!_applying)
            {
                var color = ColorValue(_workColor);
                DebounceColorSave(SettingsField.WorkColor, () => _controller.UpdateWorkColorAsync(color));
            }
        };
        _breakColor.ColorChanged += (_, _) =>
        {
            if (!_applying)
            {
                var color = ColorValue(_breakColor);
                DebounceColorSave(SettingsField.BreakColor, () => _controller.UpdateBreakColorAsync(color));
            }
        };

        _lightPreset.SelectionChanged += async (_, _) =>
        {
            if (_applying) return;
            int idx = _lightPreset.SelectedIndex;
            string? presetId = idx >= 0 && idx < ThemePresets.LightPresets.Count
                ? ThemePresets.LightPresets[idx].Id
                : null;
            await FlushPendingColorSaveAsync();
            if (presetId is not null) await _controller.UpdateLightPresetAsync(presetId);
        };
        _lightBgColor.ColorChanged += (_, _) =>
        {
            if (!_applying)
            {
                var color = ColorValue(_lightBgColor);
                DebounceColorSave(SettingsField.LightBackground, () => _controller.UpdateLightColorAsync(true, false, color));
            }
        };
        _lightFgColor.ColorChanged += (_, _) =>
        {
            if (!_applying)
            {
                var color = ColorValue(_lightFgColor);
                DebounceColorSave(SettingsField.LightForeground, () => _controller.UpdateLightColorAsync(false, true, color));
            }
        };
        _lightAccentColor.ColorChanged += (_, _) =>
        {
            if (!_applying)
            {
                var color = ColorValue(_lightAccentColor);
                DebounceColorSave(SettingsField.LightAccent, () => _controller.UpdateLightColorAsync(false, false, color));
            }
        };

        _darkPreset.SelectionChanged += async (_, _) =>
        {
            if (_applying) return;
            int idx = _darkPreset.SelectedIndex;
            string? presetId = idx >= 0 && idx < ThemePresets.DarkPresets.Count
                ? ThemePresets.DarkPresets[idx].Id
                : null;
            await FlushPendingColorSaveAsync();
            if (presetId is not null) await _controller.UpdateDarkPresetAsync(presetId);
        };
        _darkBgColor.ColorChanged += (_, _) =>
        {
            if (!_applying)
            {
                var color = ColorValue(_darkBgColor);
                DebounceColorSave(SettingsField.DarkBackground, () => _controller.UpdateDarkColorAsync(true, false, color));
            }
        };
        _darkFgColor.ColorChanged += (_, _) =>
        {
            if (!_applying)
            {
                var color = ColorValue(_darkFgColor);
                DebounceColorSave(SettingsField.DarkForeground, () => _controller.UpdateDarkColorAsync(false, true, color));
            }
        };
        _darkAccentColor.ColorChanged += (_, _) =>
        {
            if (!_applying)
            {
                var color = ColorValue(_darkAccentColor);
                DebounceColorSave(SettingsField.DarkAccent, () => _controller.UpdateDarkColorAsync(false, false, color));
            }
        };

        _sessionSounds.Toggled += async (_, _) =>
        {
            if (!_applying)
            {
                await FlushPendingColorSaveAsync();
                UpdateSoundControlsInteractiveState(_sessionSounds.IsOn);
                await _controller.UpdateSessionSoundsAsync(_sessionSounds.IsOn);
            }
        };

        _startSound.Toggled += async (_, _) =>
        {
            if (!_applying)
            {
                await FlushPendingColorSaveAsync();
                await _controller.UpdateStartSoundAsync(_startSound.IsOn);
            }
        };

        _completionSound.Toggled += async (_, _) =>
        {
            if (!_applying)
            {
                await FlushPendingColorSaveAsync();
                await _controller.UpdateCompletionSoundAsync(_completionSound.IsOn);
            }
        };

        _startSoundPreviewButton.Click += (_, _) => _previewStart?.Invoke();
        _completionSoundPreviewButton.Click += (_, _) => _previewCompletion?.Invoke();

        _startWithWindows.Toggled += (_, _) =>
        {
            if (!_applying)
            {
                try
                {
                    _windowsStartup.SetEnabled(_startWithWindows.IsOn);
                }
                catch (Exception exception)
                {
                    _report(exception);
                    RefreshStartupToggle();
                }
            }
        };

        _importHistoryButton.Click += OnImportHistoryClicked;
        _exportHistoryButton.Click += OnExportHistoryClicked;

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
        ActualThemeChanged += (_, _) => { RefreshVisuals(); Render(); };
        RefreshVisuals();
        Render();
        RefreshStartupToggle();
        ApplyUiScale(UiScaleLevels.DefaultPercent);
    }

    internal async Task OpenAsync()
    {
        await FlushPendingColorSaveAsync();
        await _controller.LoadAsync();
        RefreshStartupToggle();
    }

    private void UpdateSoundControlsInteractiveState(bool masterOn)
    {
        _startSound.IsEnabled = masterOn;
        _completionSound.IsEnabled = masterOn;
        _startSound.Opacity = masterOn ? 1.0 : 0.6;
        _completionSound.Opacity = masterOn ? 1.0 : 0.6;
        _startSoundPreviewButton.IsEnabled = true;
        _completionSoundPreviewButton.IsEnabled = true;
    }

    private void RefreshStartupToggle()
    {
        _applying = true;
        try
        {
            _startWithWindows.IsOn = _windowsStartup.IsEnabled();
        }
        catch (Exception exception)
        {
            _report(exception);
        }
        finally
        {
            _applying = false;
        }
    }

    private void DebounceColorSave(SettingsField field, Func<Task> saveAction)
    {
        _pendingColorActions[field] = saveAction;
        _colorDebounceTimer.Stop();
        _colorDebounceTimer.Start();
    }

    private async Task FlushPendingColorSaveAsync()
    {
        _colorDebounceTimer.Stop();
        StartColorDrain();
        await _colorDrainTask;
    }

    private void StartColorDrain()
    {
        if (_colorDrainRunning || _pendingColorActions.Count == 0) return;
        _colorDrainRunning = true;
        _colorDrainTask = DrainColorQueueAsync();
    }

    private async Task DrainColorQueueAsync()
    {
        try
        {
            // Keep the queue alive until every action accepted before shutdown has run. A
            // ColorChanged event can arrive while an earlier persistence action is awaiting.
            while (_pendingColorActions.Count > 0)
            {
                var actions = _pendingColorActions.Values.ToArray();
                _pendingColorActions.Clear();
                foreach (var action in actions) await action();
            }
        }
        finally { _colorDrainRunning = false; }
    }

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
        await FlushPendingColorSaveAsync();
        await CommitPendingDurationsAsync();
        await _controller.DrainAsync();
    }

    private Task CommitPendingDurationsAsync() => Task.WhenAll(
        CommitDurationAsync(true),
        CommitDurationAsync(false));

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
                case SettingsField.Contrast: _contrast.SelectedIndex = (int)saved.Contrast; break;
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
                case SettingsField.SessionSounds:
                    _sessionSounds.IsOn = saved.SessionSoundsEnabled;
                    UpdateSoundControlsInteractiveState(saved.SessionSoundsEnabled);
                    break;
                case SettingsField.StartSound:
                    _startSound.IsOn = saved.StartSoundEnabled;
                    break;
                case SettingsField.CompletionSound:
                    _completionSound.IsOn = saved.CompletionSoundEnabled;
                    break;
                case SettingsField.GlobalShortcut:
                    UpdateShortcutVisuals(saved.GlobalShortcut ?? GlobalShortcut.Default);
                    break;
                case SettingsField.MainWindowShortcut:
                    UpdateMainWindowShortcutVisuals(saved.MainWindowShortcut ?? GlobalShortcut.DefaultMainWindow);
                    break;
                case SettingsField.TimeFormat:
                    _timeFormat.SelectedIndex = (int)saved.TimeFormat;
                    break;
                case SettingsField.UiScale:
                    UpdateUiScaleSelection(saved.UiScalePercent);
                    break;
                case SettingsField.OverlayPosition:
                    break;
                case SettingsField.AppearanceExpanded:
                    _setAppearanceExpanded?.Invoke(saved.AppearanceExpanded);
                    break;
                case SettingsField.ShortcutsExpanded:
                    _setShortcutsExpanded?.Invoke(saved.ShortcutsExpanded);
                    break;
                case SettingsField.AdvancedExpanded:
                    _setAdvancedExpanded?.Invoke(saved.AdvancedExpanded);
                    break;
            }
        }
        finally { _applying = false; }
    }

    public void ApplyUiScale(int percent)
    {
        _applying = true;
        try
        {
            int clamped = UiScaleLevels.IsValid(percent)
                ? percent
                : (percent < UiScaleLevels.MinPercent ? UiScaleLevels.MinPercent : (percent > UiScaleLevels.MaxPercent ? UiScaleLevels.MaxPercent : UiScaleLevels.DefaultPercent));
            _uiScalePercent = clamped;
            double factor = UiScaleLevels.ToFactor(clamped);
            _scaleFactor = factor;

            UpdateUiScaleSelection(clamped);

            for (int i = 0; i < _scaleUpdaters.Count; i++)
            {
                _scaleUpdaters[i](factor);
            }
        }
        finally
        {
            _applying = false;
        }
    }

    internal Task UpdateUiScaleAsync(int percent, CancellationToken cancellationToken = default) =>
        _controller.UpdateUiScaleAsync(percent, cancellationToken);

    public void ApplySettings(ApplicationSettings settings)
    {
        if (settings is null) return;
        ApplyUiScale(settings.UiScalePercent);
    }

    private void UpdateUiScaleSelection(int percent)
    {
        int clamped = UiScaleLevels.IsValid(percent)
            ? percent
            : (percent < UiScaleLevels.MinPercent ? UiScaleLevels.MinPercent : (percent > UiScaleLevels.MaxPercent ? UiScaleLevels.MaxPercent : UiScaleLevels.DefaultPercent));
        string target = $"{clamped}%";
        int idx = Array.IndexOf(UiScaleOptions, target);
        if (idx >= 0)
        {
            if (_uiScale.SelectedIndex != idx)
            {
                _uiScale.SelectedIndex = idx;
            }
            if (!Equals(_uiScale.SelectedItem, UiScaleOptions[idx]))
            {
                _uiScale.SelectedItem = UiScaleOptions[idx];
            }
        }
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

    private void ConfigureColor(Button button, ColorPicker picker, string name)
    {
        var flyout = new Flyout { Content = picker };
        button.Flyout = flyout;
        button.Style = Application.Current?.Resources["FkColorButton"] as Style;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.HorizontalAlignment = HorizontalAlignment.Right;

        Border? swatch = null;
        TextBlock? hexText = null;
        StackPanel? preview = null;

        void Preview()
        {
            var color = ColorValue(picker);
            double factor = _scaleFactor;
            preview = new StackPanel { Orientation = Orientation.Horizontal, Spacing = Math.Round(8 * factor), VerticalAlignment = VerticalAlignment.Center };
            swatch = new Border
            {
                Style = Application.Current?.Resources["FkSwatchBorder"] as Style,
                Background = SessionColorBrush.Create(color),
                VerticalAlignment = VerticalAlignment.Center,
                Width = Math.Round(20 * factor),
                Height = Math.Round(20 * factor),
                CornerRadius = new CornerRadius(Math.Round(4 * factor))
            };
            hexText = new TextBlock
            {
                Text = color.Value.ToUpperInvariant(),
                Style = Application.Current?.Resources["FkColorHexText"] as Style,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = Math.Round(12 * factor)
            };
            preview.Children.Add(swatch);
            preview.Children.Add(hexText);
            button.Content = preview;
            AutomationProperties.SetName(button, $"{name}, {color.Value}, choose color");
        }
        picker.ColorChanged += (_, _) => Preview();
        Preview();

        RegisterScaleAction(factor =>
        {
            picker.Width = Math.Round(280 * factor);
            button.Padding = new Thickness(Math.Round(12 * factor), Math.Round(6 * factor), Math.Round(12 * factor), Math.Round(6 * factor));
            button.Height = Math.Round(32 * factor);
            if (preview is not null) preview.Spacing = Math.Round(8 * factor);
            if (swatch is not null)
            {
                swatch.Width = Math.Round(20 * factor);
                swatch.Height = Math.Round(20 * factor);
                swatch.CornerRadius = new CornerRadius(Math.Round(4 * factor));
            }
            if (hexText is not null) hexText.FontSize = Math.Round(12 * factor);
        });
    }

    private static readonly (string Name, HexColor Color)[] WorkColorPresets =
    [
        ("Focus Teal", HexColor.Parse("#2F8F83")),
        ("Deep Teal", HexColor.Parse("#24756D")),
        ("Fresh Teal", HexColor.Parse("#3A9D8F")),
        ("Steel Cyan", HexColor.Parse("#3D8391")),
        ("Focus Blue", HexColor.Parse("#3B78B4"))
    ];

    private static readonly (string Name, HexColor Color)[] BreakColorPresets =
    [
        ("Calm Violet", HexColor.Parse("#7667B8")),
        ("Indigo", HexColor.Parse("#5967A8")),
        ("Soft Purple", HexColor.Parse("#8067A8")),
        ("Plum", HexColor.Parse("#8A5F8F")),
        ("Slate Violet", HexColor.Parse("#686784"))
    ];

    private FrameworkElement BuildColorSelector(Button customButton, ColorPicker picker, (string Name, HexColor Color)[] presets, bool isWork)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var swatches = new List<Border>();
        var presetButtons = new List<Button>();

        void UpdateSwatches(HexColor current)
        {
            for (int i = 0; i < presets.Length; i++)
            {
                bool isSelected = string.Equals(presets[i].Color.Value, current.Value, StringComparison.OrdinalIgnoreCase);
                swatches[i].BorderThickness = new Thickness(isSelected ? 2 : 1);
                swatches[i].BorderBrush = isSelected
                    ? Presentation.ThemeBrush("FkForeground", this)
                    : Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this);
            }
        }

        for (int i = 0; i < presets.Length; i++)
        {
            var preset = presets[i];
            var swatch = new Border
            {
                Width = 20,
                Height = 20,
                CornerRadius = new CornerRadius(4),
                Background = SessionColorBrush.Create(preset.Color),
                BorderThickness = new Thickness(1),
                BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this),
                VerticalAlignment = VerticalAlignment.Center
            };
            swatches.Add(swatch);

            var btn = new Button
            {
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(4),
                Content = swatch,
                VerticalAlignment = VerticalAlignment.Center,
                MinHeight = 20,
                MinWidth = 20
            };
            presetButtons.Add(btn);
            ToolTipService.SetToolTip(btn, $"{preset.Name} ({preset.Color.Value})");
            AutomationProperties.SetName(btn, $"{preset.Name}, {preset.Color.Value}");

            btn.Click += async (_, _) =>
            {
                if (_applying) return;
                await FlushPendingColorSaveAsync();
                // Presets are an explicit immediate commit. Suppress the picker callback
                // while changing its preview so it cannot enqueue a duplicate delayed save.
                _applying = true;
                try { picker.Color = SessionColorBrush.Create(preset.Color).Color; }
                finally { _applying = false; }
                if (isWork) await _controller.UpdateWorkColorAsync(preset.Color);
                else await _controller.UpdateBreakColorAsync(preset.Color);
                UpdateSwatches(preset.Color);
            };
            panel.Children.Add(btn);
        }

        picker.ColorChanged += (_, _) =>
        {
            UpdateSwatches(ColorValue(picker));
        };

        if (isWork) _refreshWorkSwatches = () => UpdateSwatches(ColorValue(picker));
        else _refreshBreakSwatches = () => UpdateSwatches(ColorValue(picker));

        customButton.Margin = new Thickness(6, 0, 0, 0);
        panel.Children.Add(customButton);
        UpdateSwatches(ColorValue(picker));

        RegisterScaleAction(factor =>
        {
            panel.Spacing = Math.Round(6 * factor);
            customButton.Margin = new Thickness(Math.Round(6 * factor), 0, 0, 0);
            for (int i = 0; i < swatches.Count; i++)
            {
                swatches[i].Width = Math.Round(20 * factor);
                swatches[i].Height = Math.Round(20 * factor);
                swatches[i].CornerRadius = new CornerRadius(Math.Round(4 * factor));
            }
            for (int i = 0; i < presetButtons.Count; i++)
            {
                presetButtons[i].MinWidth = Math.Round(20 * factor);
                presetButtons[i].MinHeight = Math.Round(20 * factor);
            }
        });

        return panel;
    }

    internal void RefreshVisuals(Contrast? contrast = null)
    {
        _refreshWorkSwatches?.Invoke();
        _refreshBreakSwatches?.Invoke();
        _shortcutText.Foreground = Presentation.ThemeBrush("FkSecondary", this);
        _resetShortcutButton.Background = Presentation.ThemeBrush("FkSurface2", this);
        _resetShortcutButton.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this);
        _resetShortcutButton.Foreground = Presentation.ThemeBrush("FkSecondary", this);
        _mainWindowShortcutText.Foreground = Presentation.ThemeBrush("FkSecondary", this);
        _resetMainWindowShortcutButton.Background = Presentation.ThemeBrush("FkSurface2", this);
        _resetMainWindowShortcutButton.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this);
        _resetMainWindowShortcutButton.Foreground = Presentation.ThemeBrush("FkSecondary", this);
        _startSoundPreviewButton.Background = Presentation.ThemeBrush("FkSurface2", this);
        _startSoundPreviewButton.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this);
        _startSoundPreviewButton.Foreground = Presentation.ThemeBrush("FkSecondary", this);
        _completionSoundPreviewButton.Background = Presentation.ThemeBrush("FkSurface2", this);
        _completionSoundPreviewButton.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this);
        _completionSoundPreviewButton.Foreground = Presentation.ThemeBrush("FkSecondary", this);
        Render();
    }

    private FrameworkElement BuildShortcutControl()
    {
        var root = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

        _shortcutButton.Style = Application.Current?.Resources["FkColorButton"] as Style;
        _shortcutButton.MinWidth = 120;
        _shortcutButton.Padding = new Thickness(12, 6, 12, 6);
        _shortcutButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _shortcutButton.VerticalAlignment = VerticalAlignment.Center;

        _shortcutText.Text = _currentShortcut.ToString();
        _shortcutText.FontFamily = new FontFamily("Consolas");
        _shortcutText.FontSize = 12;
        _shortcutText.Foreground = Presentation.ThemeBrush("FkSecondary", this);
        _shortcutText.HorizontalAlignment = HorizontalAlignment.Center;
        _shortcutButton.Content = _shortcutText;

        AutomationProperties.SetName(_shortcutButton, $"Global shortcut, {_currentShortcut}, click to change");

        _resetShortcutButton.Content = "Reset";
        _resetShortcutButton.FontSize = 12;
        _resetShortcutButton.Padding = new Thickness(10, 6, 10, 6);
        _resetShortcutButton.CornerRadius = new CornerRadius(4);
        _resetShortcutButton.Background = Presentation.ThemeBrush("FkSurface2", this);
        _resetShortcutButton.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this);
        _resetShortcutButton.BorderThickness = new Thickness(1);
        _resetShortcutButton.Foreground = Presentation.ThemeBrush("FkSecondary", this);
        _resetShortcutButton.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(_resetShortcutButton, "Reset to Shift + F3");
        AutomationProperties.SetName(_resetShortcutButton, "Reset shortcut to Shift + F3");

        row.Children.Add(_shortcutButton);
        row.Children.Add(_resetShortcutButton);
        root.Children.Add(row);

        _shortcutError.Style = Application.Current?.Resources["FkMutedText"] as Style;
        _shortcutError.Foreground = Presentation.ThemeBrush("FkStatusStopped", this);
        _shortcutError.FontSize = 11;
        _shortcutError.TextWrapping = TextWrapping.Wrap;
        _shortcutError.HorizontalAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetLiveSetting(_shortcutError, AutomationLiveSetting.Polite);
        root.Children.Add(_shortcutError);

        _shortcutButton.Click += (_, _) =>
        {
            if (_isListeningForShortcut)
            {
                CancelShortcutListening();
            }
            else
            {
                StartShortcutListening();
            }
        };

        _shortcutButton.PreviewKeyDown += OnShortcutPreviewKeyDown;
        _shortcutButton.LostFocus += (_, _) =>
        {
            if (_isListeningForShortcut) CancelShortcutListening();
        };

        _resetShortcutButton.Click += async (_, _) =>
        {
            if (_applying) return;
            CancelShortcutListening();
            await ApplyNewShortcutAsync(GlobalShortcut.Default);
        };

        RegisterScaleAction(factor =>
        {
            root.Spacing = Math.Round(6 * factor);
            row.Spacing = Math.Round(8 * factor);
            _shortcutButton.MinWidth = Math.Round(120 * factor);
            _shortcutButton.Height = Math.Round(32 * factor);
            _shortcutButton.Padding = new Thickness(Math.Round(12 * factor), Math.Round(6 * factor), Math.Round(12 * factor), Math.Round(6 * factor));
            _shortcutText.FontSize = Math.Round(12 * factor);
            _resetShortcutButton.FontSize = Math.Round(12 * factor);
            _resetShortcutButton.Height = Math.Round(32 * factor);
            _resetShortcutButton.Padding = new Thickness(Math.Round(10 * factor), Math.Round(6 * factor), Math.Round(10 * factor), Math.Round(6 * factor));
            _resetShortcutButton.CornerRadius = new CornerRadius(Math.Round(4 * factor));
            _shortcutError.FontSize = Math.Round(11 * factor);
        });

        return root;
    }

    private void StartShortcutListening()
    {
        _isListeningForShortcut = true;
        _shortcutError.Visibility = Visibility.Collapsed;
        _shortcutText.Text = "[ Press combination ]";
        _shortcutText.Foreground = Presentation.ThemeBrush("FkAccent", this);
        AutomationProperties.SetName(_shortcutButton, "Listening for shortcut. Press key combination or Escape to cancel.");
    }

    private void CancelShortcutListening()
    {
        _isListeningForShortcut = false;
        UpdateShortcutVisuals(_currentShortcut);
    }

    private void UpdateShortcutVisuals(GlobalShortcut shortcut)
    {
        _currentShortcut = shortcut;
        _shortcutText.Text = shortcut.ToString();
        _shortcutText.Foreground = Presentation.ThemeBrush("FkSecondary", this);
        AutomationProperties.SetName(_shortcutButton, $"Global shortcut, {shortcut}, click to change");
    }

    private async void OnShortcutPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!_isListeningForShortcut) return;
        args.Handled = true;

        uint vk = (uint)(args.Key != VirtualKey.None ? args.Key : args.OriginalKey);

        if (vk == 0x1B) // VK_ESCAPE
        {
            CancelShortcutListening();
            return;
        }

        ShortcutModifiers mods = ShortcutModifiers.None;
        if ((NativeMethods.GetKeyState(0x10) & 0x8000) != 0) mods |= ShortcutModifiers.Shift;
        if ((NativeMethods.GetKeyState(0x11) & 0x8000) != 0) mods |= ShortcutModifiers.Control;
        if ((NativeMethods.GetKeyState(0x12) & 0x8000) != 0) mods |= ShortcutModifiers.Alt;
        if ((NativeMethods.GetKeyState(0x5B) & 0x8000) != 0 || (NativeMethods.GetKeyState(0x5C) & 0x8000) != 0) mods |= ShortcutModifiers.Windows;

        if (vk is 0x10 or 0xA0 or 0xA1) mods |= ShortcutModifiers.Shift;
        else if (vk is 0x11 or 0xA2 or 0xA3) mods |= ShortcutModifiers.Control;
        else if (vk is 0x12 or 0xA4 or 0xA5) mods |= ShortcutModifiers.Alt;
        else if (vk is 0x5B or 0x5C) mods |= ShortcutModifiers.Windows;

        bool isModifierOnly = vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;
        if (isModifierOnly)
        {
            var parts = new List<string>();
            if (mods.HasFlag(ShortcutModifiers.Windows)) parts.Add("Win");
            if (mods.HasFlag(ShortcutModifiers.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ShortcutModifiers.Alt)) parts.Add("Alt");
            if (mods.HasFlag(ShortcutModifiers.Shift)) parts.Add("Shift");
            _shortcutText.Text = parts.Count > 0 ? $"{string.Join(" + ", parts)} + …" : "[ Press combination ]";
            return;
        }

        var candidate = new GlobalShortcut(mods, vk);
        if (!candidate.IsValid(out string? valError))
        {
            _shortcutError.Text = valError ?? "Invalid shortcut combination.";
            _shortcutError.Visibility = Visibility.Visible;
            return;
        }

        CancelShortcutListening();
        await ApplyNewShortcutAsync(candidate);
    }

    private async Task ApplyNewShortcutAsync(GlobalShortcut candidate)
    {
        if (candidate == _currentMainWindowShortcut)
        {
            _shortcutError.Text = "Quick Overlay shortcut cannot be identical to Open Focus Key shortcut.";
            _shortcutError.Visibility = Visibility.Visible;
            return;
        }

        _applying = true;
        try
        {
            string? regError = null;
            bool registered = _tryUpdateShortcut == null || _tryUpdateShortcut(candidate, out regError);
            if (registered)
            {
                UpdateShortcutVisuals(candidate);
                _shortcutError.Text = string.Empty;
                _shortcutError.Visibility = Visibility.Collapsed;
                await _controller.UpdateGlobalShortcutAsync(candidate);
                _onShortcutChanged?.Invoke(candidate);
            }
            else
            {
                UpdateShortcutVisuals(_currentShortcut);
                _shortcutError.Text = regError ?? "Could not register shortcut.";
                _shortcutError.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _applying = false;
        }
    }

    private FrameworkElement BuildMainWindowShortcutControl()
    {
        var root = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

        _mainWindowShortcutButton.Style = Application.Current?.Resources["FkColorButton"] as Style;
        _mainWindowShortcutButton.MinWidth = 120;
        _mainWindowShortcutButton.Padding = new Thickness(12, 6, 12, 6);
        _mainWindowShortcutButton.HorizontalContentAlignment = HorizontalAlignment.Center;
        _mainWindowShortcutButton.VerticalAlignment = VerticalAlignment.Center;

        _mainWindowShortcutText.Text = _currentMainWindowShortcut.ToString();
        _mainWindowShortcutText.FontFamily = new FontFamily("Consolas");
        _mainWindowShortcutText.FontSize = 12;
        _mainWindowShortcutText.Foreground = Presentation.ThemeBrush("FkSecondary", this);
        _mainWindowShortcutText.HorizontalAlignment = HorizontalAlignment.Center;
        _mainWindowShortcutButton.Content = _mainWindowShortcutText;

        AutomationProperties.SetName(_mainWindowShortcutButton, $"Open Focus Key shortcut, {_currentMainWindowShortcut}, click to change");

        _resetMainWindowShortcutButton.Content = "Reset";
        _resetMainWindowShortcutButton.FontSize = 12;
        _resetMainWindowShortcutButton.Padding = new Thickness(10, 6, 10, 6);
        _resetMainWindowShortcutButton.CornerRadius = new CornerRadius(4);
        _resetMainWindowShortcutButton.Background = Presentation.ThemeBrush("FkSurface2", this);
        _resetMainWindowShortcutButton.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this);
        _resetMainWindowShortcutButton.BorderThickness = new Thickness(1);
        _resetMainWindowShortcutButton.Foreground = Presentation.ThemeBrush("FkSecondary", this);
        _resetMainWindowShortcutButton.VerticalAlignment = VerticalAlignment.Center;
        ToolTipService.SetToolTip(_resetMainWindowShortcutButton, "Reset to Shift + F4");
        AutomationProperties.SetName(_resetMainWindowShortcutButton, "Reset shortcut to Shift + F4");

        row.Children.Add(_mainWindowShortcutButton);
        row.Children.Add(_resetMainWindowShortcutButton);
        root.Children.Add(row);

        _mainWindowShortcutError.Style = Application.Current?.Resources["FkMutedText"] as Style;
        _mainWindowShortcutError.Foreground = Presentation.ThemeBrush("FkStatusStopped", this);
        _mainWindowShortcutError.FontSize = 11;
        _mainWindowShortcutError.TextWrapping = TextWrapping.Wrap;
        _mainWindowShortcutError.HorizontalAlignment = HorizontalAlignment.Right;
        AutomationProperties.SetLiveSetting(_mainWindowShortcutError, AutomationLiveSetting.Polite);
        root.Children.Add(_mainWindowShortcutError);

        _mainWindowShortcutButton.Click += (_, _) =>
        {
            if (_isListeningForMainWindowShortcut)
            {
                CancelMainWindowShortcutListening();
            }
            else
            {
                StartMainWindowShortcutListening();
            }
        };

        _mainWindowShortcutButton.PreviewKeyDown += OnMainWindowShortcutPreviewKeyDown;
        _mainWindowShortcutButton.LostFocus += (_, _) =>
        {
            if (_isListeningForMainWindowShortcut) CancelMainWindowShortcutListening();
        };

        _resetMainWindowShortcutButton.Click += async (_, _) =>
        {
            if (_applying) return;
            CancelMainWindowShortcutListening();
            await ApplyNewMainWindowShortcutAsync(GlobalShortcut.DefaultMainWindow);
        };

        RegisterScaleAction(factor =>
        {
            root.Spacing = Math.Round(6 * factor);
            row.Spacing = Math.Round(8 * factor);
            _mainWindowShortcutButton.MinWidth = Math.Round(120 * factor);
            _mainWindowShortcutButton.Height = Math.Round(32 * factor);
            _mainWindowShortcutButton.Padding = new Thickness(Math.Round(12 * factor), Math.Round(6 * factor), Math.Round(12 * factor), Math.Round(6 * factor));
            _mainWindowShortcutText.FontSize = Math.Round(12 * factor);
            _resetMainWindowShortcutButton.FontSize = Math.Round(12 * factor);
            _resetMainWindowShortcutButton.Height = Math.Round(32 * factor);
            _resetMainWindowShortcutButton.Padding = new Thickness(Math.Round(10 * factor), Math.Round(6 * factor), Math.Round(10 * factor), Math.Round(6 * factor));
            _resetMainWindowShortcutButton.CornerRadius = new CornerRadius(Math.Round(4 * factor));
            _mainWindowShortcutError.FontSize = Math.Round(11 * factor);
        });

        return root;
    }

    private void StartMainWindowShortcutListening()
    {
        _isListeningForMainWindowShortcut = true;
        _mainWindowShortcutError.Visibility = Visibility.Collapsed;
        _mainWindowShortcutText.Text = "[ Press combination ]";
        _mainWindowShortcutText.Foreground = Presentation.ThemeBrush("FkAccent", this);
        AutomationProperties.SetName(_mainWindowShortcutButton, "Listening for shortcut. Press key combination or Escape to cancel.");
    }

    private void CancelMainWindowShortcutListening()
    {
        _isListeningForMainWindowShortcut = false;
        UpdateMainWindowShortcutVisuals(_currentMainWindowShortcut);
    }

    private void UpdateMainWindowShortcutVisuals(GlobalShortcut shortcut)
    {
        _currentMainWindowShortcut = shortcut;
        _mainWindowShortcutText.Text = shortcut.ToString();
        _mainWindowShortcutText.Foreground = Presentation.ThemeBrush("FkSecondary", this);
        AutomationProperties.SetName(_mainWindowShortcutButton, $"Open Focus Key shortcut, {shortcut}, click to change");
    }

    private async void OnMainWindowShortcutPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!_isListeningForMainWindowShortcut) return;
        args.Handled = true;

        uint vk = (uint)(args.Key != VirtualKey.None ? args.Key : args.OriginalKey);

        if (vk == 0x1B) // VK_ESCAPE
        {
            CancelMainWindowShortcutListening();
            return;
        }

        ShortcutModifiers mods = ShortcutModifiers.None;
        if ((NativeMethods.GetKeyState(0x10) & 0x8000) != 0) mods |= ShortcutModifiers.Shift;
        if ((NativeMethods.GetKeyState(0x11) & 0x8000) != 0) mods |= ShortcutModifiers.Control;
        if ((NativeMethods.GetKeyState(0x12) & 0x8000) != 0) mods |= ShortcutModifiers.Alt;
        if ((NativeMethods.GetKeyState(0x5B) & 0x8000) != 0 || (NativeMethods.GetKeyState(0x5C) & 0x8000) != 0) mods |= ShortcutModifiers.Windows;

        if (vk is 0x10 or 0xA0 or 0xA1) mods |= ShortcutModifiers.Shift;
        else if (vk is 0x11 or 0xA2 or 0xA3) mods |= ShortcutModifiers.Control;
        else if (vk is 0x12 or 0xA4 or 0xA5) mods |= ShortcutModifiers.Alt;
        else if (vk is 0x5B or 0x5C) mods |= ShortcutModifiers.Windows;

        bool isModifierOnly = vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;
        if (isModifierOnly)
        {
            var parts = new List<string>();
            if (mods.HasFlag(ShortcutModifiers.Windows)) parts.Add("Win");
            if (mods.HasFlag(ShortcutModifiers.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ShortcutModifiers.Alt)) parts.Add("Alt");
            if (mods.HasFlag(ShortcutModifiers.Shift)) parts.Add("Shift");
            _mainWindowShortcutText.Text = parts.Count > 0 ? $"{string.Join(" + ", parts)} + …" : "[ Press combination ]";
            return;
        }

        var candidate = new GlobalShortcut(mods, vk);
        if (!candidate.IsValid(out string? valError))
        {
            _mainWindowShortcutError.Text = valError ?? "Invalid shortcut combination.";
            _mainWindowShortcutError.Visibility = Visibility.Visible;
            return;
        }

        CancelMainWindowShortcutListening();
        await ApplyNewMainWindowShortcutAsync(candidate);
    }

    private async Task ApplyNewMainWindowShortcutAsync(GlobalShortcut candidate)
    {
        if (candidate == _currentShortcut)
        {
            _mainWindowShortcutError.Text = "Open Focus Key shortcut cannot be identical to Quick Overlay shortcut.";
            _mainWindowShortcutError.Visibility = Visibility.Visible;
            return;
        }

        _applying = true;
        try
        {
            string? regError = null;
            bool registered = _tryUpdateMainWindowShortcut == null || _tryUpdateMainWindowShortcut(candidate, out regError);
            if (registered)
            {
                UpdateMainWindowShortcutVisuals(candidate);
                _mainWindowShortcutError.Text = string.Empty;
                _mainWindowShortcutError.Visibility = Visibility.Collapsed;
                await _controller.UpdateMainWindowShortcutAsync(candidate);
                _onMainWindowShortcutChanged?.Invoke(candidate);
            }
            else
            {
                UpdateMainWindowShortcutVisuals(_currentMainWindowShortcut);
                _mainWindowShortcutError.Text = regError ?? "Could not register shortcut.";
                _mainWindowShortcutError.Visibility = Visibility.Visible;
            }
        }
        finally
        {
            _applying = false;
        }
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

    private StackPanel DurationFields(TextBox minutes, TextBox seconds)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        panel.Children.Add(minutes);
        var minutesLabel = Presentation.Text("min", 12, true);
        minutesLabel.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(minutesLabel);
        panel.Children.Add(seconds);
        var secondsLabel = Presentation.Text("sec", 12, true);
        secondsLabel.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(secondsLabel);

        RegisterScaleAction(factor =>
        {
            panel.Spacing = Math.Round(8 * factor);
            minutesLabel.FontSize = Math.Round(12 * factor);
            secondsLabel.FontSize = Math.Round(12 * factor);
        });

        return panel;
    }

    private StackPanel SoundActionFields(Button previewButton, ToggleSwitch toggle)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        panel.Children.Add(previewButton);
        panel.Children.Add(toggle);

        RegisterScaleAction(factor =>
        {
            panel.Spacing = Math.Round(8 * factor);
        });

        return panel;
    }

    private Border BuildSessionSection(UIElement content)
    {
        var card = new Border
        {
            Style = Application.Current?.Resources["FkCard"] as Style,
            Padding = new Thickness(0)
        };

        var panel = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var heading = Presentation.BodyStrong("SESSION", 13);
        heading.Margin = new Thickness(16, 14, 16, 14);
        panel.Children.Add(heading);

        var divider = new Border
        {
            Style = Application.Current?.Resources["FkCardDivider"] as Style,
            Height = 1,
            Background = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this),
            Opacity = 0.6,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Children.Add(divider);

        var contentContainer = new Border
        {
            Padding = new Thickness(14, 14, 14, 14),
            Child = content
        };
        panel.Children.Add(contentContainer);

        RegisterScaleAction(factor =>
        {
            heading.FontSize = Math.Round(13 * factor);
            heading.Margin = new Thickness(Math.Round(16 * factor), Math.Round(14 * factor), Math.Round(16 * factor), Math.Round(14 * factor));
            contentContainer.Padding = new Thickness(Math.Round(14 * factor));
        });

        card.Child = panel;
        return card;
    }

    private Border SubCard(string? title, UIElement content)
    {
        var border = new Border
        {
            Style = Application.Current?.Resources["FkCardSubtle"] as Style,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(0)
        };

        if (string.IsNullOrWhiteSpace(title))
        {
            border.Child = content;
            RegisterScaleAction(factor => border.CornerRadius = new CornerRadius(Math.Round(6 * factor)));
            return border;
        }

        var panel = new StackPanel { Spacing = 0 };
        var heading = Presentation.DimText(title, 11);
        if (Application.Current?.Resources["FkSectionText"] is Style style)
        {
            heading.Style = style;
        }
        heading.Margin = new Thickness(16, 12, 16, 10);
        panel.Children.Add(heading);

        var divider = new Border
        {
            Style = Application.Current?.Resources["FkSubCardDivider"] as Style,
            Height = 1,
            Background = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this),
            Opacity = 0.4,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        panel.Children.Add(divider);
        panel.Children.Add(content);

        RegisterScaleAction(factor =>
        {
            border.CornerRadius = new CornerRadius(Math.Round(6 * factor));
            heading.FontSize = Math.Round(11 * factor);
            heading.Margin = new Thickness(Math.Round(16 * factor), Math.Round(12 * factor), Math.Round(16 * factor), Math.Round(10 * factor));
        });

        border.Child = panel;
        return border;
    }

    private FrameworkElement BuildCollapsibleSection(
        string title,
        UIElement content,
        bool isExpanded,
        Func<bool, Task> onToggleExpanded,
        out Action<bool> setExpandedVisual)
    {
        var card = new Border
        {
            Style = Application.Current?.Resources["FkCard"] as Style,
            Padding = new Thickness(0)
        };

        var rootPanel = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };

        var chevron = new FontIcon
        {
            Glyph = isExpanded ? "\uE70E" : "\uE76C",
            FontSize = 11,
            Foreground = Presentation.ThemeBrush("FkSecondary", this),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var heading = Presentation.BodyStrong(title, 13);
        heading.VerticalAlignment = VerticalAlignment.Center;
        heading.HorizontalAlignment = HorizontalAlignment.Left;

        var headerGrid = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center
        };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Grid.SetColumn(heading, 0);
        Grid.SetColumn(chevron, 1);
        headerGrid.Children.Add(heading);
        headerGrid.Children.Add(chevron);

        var headerButton = new Button
        {
            Content = headerGrid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14, 16, 14),
            MinHeight = 48
        };

        var divider = new Border
        {
            Style = Application.Current?.Resources["FkCardDivider"] as Style,
            Height = 1,
            Background = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", this),
            Opacity = 0.6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed
        };

        var contentContainer = new Border
        {
            Padding = new Thickness(14, 14, 14, 14),
            Child = content,
            Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed
        };

        bool currentExpanded = isExpanded;

        void ApplyVisual(bool expanded)
        {
            currentExpanded = expanded;
            chevron.Glyph = expanded ? "\uE70E" : "\uE76C";
            divider.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            contentContainer.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(headerButton, $"{title}, {(expanded ? "expanded" : "collapsed")}");
        }

        ApplyVisual(isExpanded);
        setExpandedVisual = ApplyVisual;

        headerButton.Click += async (_, _) =>
        {
            if (_applying) return;
            bool next = !currentExpanded;
            ApplyVisual(next);
            await onToggleExpanded(next);
        };

        rootPanel.Children.Add(headerButton);
        rootPanel.Children.Add(divider);
        rootPanel.Children.Add(contentContainer);

        RegisterScaleAction(factor =>
        {
            chevron.FontSize = Math.Round(11 * factor);
            heading.FontSize = Math.Round(13 * factor);
            headerButton.CornerRadius = new CornerRadius(Math.Round(8 * factor));
            headerButton.Padding = new Thickness(Math.Round(16 * factor), Math.Round(14 * factor), Math.Round(16 * factor), Math.Round(14 * factor));
            headerButton.MinHeight = Math.Round(48 * factor);
            contentContainer.Padding = new Thickness(Math.Round(14 * factor));
        });

        card.Child = rootPanel;
        return card;
    }

    private Grid Row(string label, string? description, FrameworkElement control, bool last = false)
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
        TextBlock? descriptionText = null;
        if (!string.IsNullOrWhiteSpace(description))
        {
            descriptionText = Presentation.DimText(description);
            descriptionText.TextWrapping = TextWrapping.Wrap;
            copy.Children.Add(descriptionText);
        }
        grid.Children.Add(copy);
        Grid.SetColumn(copy, 0);

        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        RegisterScaleAction(factor =>
        {
            grid.MinHeight = Math.Round(52 * factor);
            grid.ColumnSpacing = Math.Round(24 * factor);
            grid.Padding = new Thickness(Math.Round(16 * factor), Math.Round(14 * factor), Math.Round(16 * factor), Math.Round(14 * factor));
            copy.Spacing = Math.Round(3 * factor);
            labelText.FontSize = Math.Round(13 * factor);
            if (descriptionText is not null)
            {
                descriptionText.FontSize = Math.Round(12 * factor);
            }
        });

        return grid;
    }

    private async void OnImportHistoryClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker();
            var hwnd = _getWindowHandle();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.ViewMode = Windows.Storage.Pickers.PickerViewMode.List;
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add(".txt");

            var file = await picker.PickSingleFileAsync();
            if (file is null) return;

            string content = await Windows.Storage.FileIO.ReadTextAsync(file);
            var result = await _historyService.ImportWebsiteCsvAsync(content);

            var dialog = new ContentDialog
            {
                Title = result.Success ? "Import Complete" : "Import Failed",
                Content = result.Success
                    ? $"Rows found: {result.RowsFound}\n" +
                      $"New records: {result.NewRecords}\n" +
                      $"Updated records: {result.UpdatedRecords}\n" +
                      $"Duplicate / no-change: {result.DuplicateRecords}\n" +
                      $"Invalid rows: {result.InvalidRows}\n" +
                      $"Total imported focus time: {result.TotalImportedHours:0.##} hours"
                    : $"Import failed:\n{result.ErrorMessage}",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();

            if (result.Success && (result.NewRecords > 0 || result.UpdatedRecords > 0))
            {
                await _refreshReports();
            }
        }
        catch (Exception exception)
        {
            _report(exception);
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Import Error",
                    Content = $"An error occurred during import: {exception.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch { }
        }
    }

    private async void OnExportHistoryClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            var hwnd = _getWindowHandle();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
            picker.FileTypeChoices.Add("CSV File (Tab-delimited)", [".csv"]);
            picker.SuggestedFileName = $"FocusKey-report-{DateTime.Now:yyyyMMdd}.csv";

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;

            string csvContent = await _historyService.ExportWebsiteCsvAsync();
            await Windows.Storage.FileIO.WriteTextAsync(file, csvContent);

            var dialog = new ContentDialog
            {
                Title = "Export Complete",
                Content = $"Successfully exported focus history to:\n{file.Name}",
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }
        catch (Exception exception)
        {
            _report(exception);
            try
            {
                var dialog = new ContentDialog
                {
                    Title = "Export Error",
                    Content = $"An error occurred during export: {exception.Message}",
                    CloseButtonText = "OK",
                    XamlRoot = this.XamlRoot
                };
                await dialog.ShowAsync();
            }
            catch { }
        }
    }
}
