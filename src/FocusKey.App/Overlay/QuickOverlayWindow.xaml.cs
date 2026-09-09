using System.Runtime.InteropServices;
using FocusKey.Foundation.Overlay;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Settings;
using FocusKey.Foundation.MiniTimer;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace FocusKey.Overlay;

/// <summary>A reusable native utility surface; all session behavior belongs to its controller.</summary>
public sealed partial class QuickOverlayWindow : Window, IQuickOverlayView
{
    private QuickOverlayState _state = new(SessionType.Work, true, false, null);
    private DisplayArea? _display;
    private bool _visible;
    private bool _closing;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _displayTimer;
    private readonly Action<string>? _trace;
    private readonly Dictionary<(Button, string), SolidColorBrush> _stateBrushes = new();
    private SessionColors _colors = SessionColors.From(ApplicationSettings.Default);
    internal void ApplyColors(SessionColors colors) { _colors = colors; Render(_state); }

    public QuickOverlayWindow(Action<string>? trace = null)
    {
        _trace = trace;
        InitializeComponent();
        _displayTimer = DispatcherQueue.CreateTimer();
        _displayTimer.Interval = TimeSpan.FromSeconds(1);
        _displayTimer.Tick += (_, _) => RenderCountdown();
        var presenter = OverlappedPresenter.Create();
        presenter.IsResizable = false;
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        presenter.IsAlwaysOnTop = true;
        presenter.SetBorderAndTitleBar(false, false);
        AppWindow.SetPresenter(presenter);
        AppWindow.IsShownInSwitchers = false;
        Surface.PreviewKeyDown += OnPreviewKeyDown;
        Surface.Loaded += (_, _) => { ResizeAndCenter(); FocusSelection(); };
        AppWindow.Closing += (_, args) =>
        {
            if (_closing) return;
            args.Cancel = true;
            DismissRequested?.Invoke();
        };
        Activated += (_, args) =>
        {
            if (_visible && args.WindowActivationState == WindowActivationState.Deactivated)
                DismissRequested?.Invoke();
        };
    }

    public event Action<SessionType>? SelectionRequested;
    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? DismissRequested;

    internal void ApplyAppearance(Appearance appearance)
    {
        WindowAppearance.Apply(Surface, AppWindow, appearance);
        uint borderColor = appearance switch
        {
            Appearance.System => 0xFFFFFFFF,
            Appearance.Light => 0x00DEDEDE,
            Appearance.Dark => 0x0024241E,
            _ => throw new ArgumentOutOfRangeException(nameof(appearance), appearance, "Unsupported appearance."),
        };
        DwmSetWindowAttribute(WindowNative.GetWindowHandle(this), 34, ref borderColor, sizeof(uint));
        Render(_state);
    }

    public void Render(QuickOverlayState state)
    {
        bool changedMode = (_state.Active is null) != (state.Active is null);
        if (_state.Active?.Id != state.Active?.Id || _state.IsBusy != state.IsBusy || _state.CanStart != state.CanStart)
            _trace?.Invoke(state.Active is { } observed
                ? $"Quick overlay state: {observed.Type} timer; id={observed.Id}; plannedEnd={observed.PlannedEndAt:O}; busy={state.IsBusy}."
                : $"Quick overlay state: selection; ready={state.CanStart}; busy={state.IsBusy}.");
        _state = state;
        bool active = state.Active is not null;
        SelectionCards.Visibility = StartButton.Visibility = SelectionHint.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        ActiveCard.Visibility = StopButton.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        ActionHint.Text = active ? "Stop" : "Start";
        StopButton.IsEnabled = active && !state.IsBusy;
        StopButton.Content = state.IsBusy ? "Please wait…" : "Stop";
        if (state.Active is { } session)
        {
            var color = session.Type == SessionType.Work ? _colors.Work : _colors.Break;
            ActiveCard.Background = SessionColorBrush.Create(color);
            ActiveType.Foreground = ActiveRemaining.Foreground = SessionColorBrush.Create(SessionColors.Foreground(color));
            ActiveType.Text = session.Type.ToString();
            PaintButton(StopButton, color);
        }
        RenderCountdown();
        WorkDuration.Text = QuickOverlayDurationFormatter.Format(state.Durations?.Work);
        BreakDuration.Text = QuickOverlayDurationFormatter.Format(state.Durations?.Break);
        AutomationProperties.SetName(WorkCard, $"Work, {WorkDuration.Text}");
        AutomationProperties.SetName(BreakCard, $"Break, {BreakDuration.Text}");
        PaintCard(WorkCard, WorkLabel, WorkDuration, state.Selected == SessionType.Work, _colors.Work);
        PaintCard(BreakCard, BreakLabel, BreakDuration, state.Selected == SessionType.Break, _colors.Break);
        // Keep focusable cards available for navigation while their controller ignores selection
        // during loading/saving or an existing session. Start is explicitly disabled.
        StartButton.IsEnabled = state.CanStart && !state.IsBusy;
        StartButton.Content = state.IsBusy ? "Please wait…" : "Start";
        PaintButton(StartButton, state.Selected == SessionType.Work ? _colors.Work : _colors.Break);
        FeedbackText.Text = state.Feedback ?? string.Empty;
        FeedbackText.Visibility = state.Feedback is null ? Visibility.Collapsed : Visibility.Visible;
        if (_visible) ResizeAndCenter();
        if (_visible && changedMode) FocusSelection();
    }

    private void RenderCountdown()
    {
        if (_closing) return;
        TimeSpan remaining = MiniTimerController.RemainingAt(_state.Active, TimeProvider.System.GetUtcNow());
        ActiveRemaining.Text = MiniTimerController.Format(remaining);
        AutomationProperties.SetName(ActiveRemaining, $"{_state.Active?.Type}, {ActiveRemaining.Text} remaining");
        if (_visible && _state.Active is not null && remaining > TimeSpan.Zero) _displayTimer.Start();
        else _displayTimer.Stop();
    }

    public void ShowAndFocus()
    {
        if (_closing) return;
        if (!_visible)
        {
            IntPtr foreground = GetForegroundWindow();
            _display = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(foreground), DisplayAreaFallback.Primary);
            // Move to the foreground monitor first so subsequent DPI queries belong to that monitor.
            RectInt32 area = _display.WorkArea;
            AppWindow.Move(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2));
        }
        _visible = true;
        ResizeAndCenter();
        AppWindow.Show();
        Activate();
        FocusSelection();
        RenderCountdown();
    }

    public void Hide()
    {
        _visible = false;
        _displayTimer.Stop();
        if (!_closing) AppWindow.Hide();
    }

    public void Dispose()
    {
        if (_closing) return;
        _visible = false;
        _closing = true;
        _displayTimer.Stop();
        Close();
    }

    private void FocusSelection() =>
        (_state.Active is not null ? StopButton : _state.Selected == SessionType.Work ? WorkCard : BreakCard).Focus(FocusState.Keyboard);

    private void ResizeAndCenter()
    {
        _display ??= DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
        // Keep the reference surface stable. Feedback gets its own two-line allowance rather
        // than measuring a root still constrained by the previous native window dimensions.
        double heightDip = _state.Feedback is null ? 280 : 340;
        Surface.Height = heightDip;
        double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1;
        int width = (int)Math.Ceiling(420 * scale);
        int height = (int)Math.Ceiling(heightDip * scale);
        RectInt32 area = _display.WorkArea;
        AppWindow.MoveAndResize(new RectInt32(area.X + (area.Width - width) / 2,
            area.Y + (area.Height - height) / 2, width, height));
    }

    private void OnWorkClicked(object sender, RoutedEventArgs args) => SelectionRequested?.Invoke(SessionType.Work);
    private void OnBreakClicked(object sender, RoutedEventArgs args) => SelectionRequested?.Invoke(SessionType.Break);
    private void OnStartClicked(object sender, RoutedEventArgs args) => StartRequested?.Invoke();
    private void OnStopClicked(object sender, RoutedEventArgs args) => StopRequested?.Invoke();

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Escape:
                args.Handled = true;
                DismissRequested?.Invoke();
                break;
            case VirtualKey.Left:
            case VirtualKey.Right:
                args.Handled = true;
                if (_state.Active is not null) break;
                SelectionRequested?.Invoke(args.Key == VirtualKey.Left ? SessionType.Work : SessionType.Break);
                FocusSelection();
                break;
            case VirtualKey.Enter:
                // Handle once at the preview stage, avoiding an additional native Button click.
                args.Handled = true;
                if (args.KeyStatus.WasKeyDown) break;
                if (_state.Active is not null) StopRequested?.Invoke();
                else StartRequested?.Invoke();
                break;
        }
        // Space and Tab retain the native Button keyboard/focus behavior.
    }

    private void PaintCard(Button card, TextBlock label, TextBlock duration, bool selected, HexColor color)
    {
        PaintButton(card, color);
        card.BorderThickness = new Thickness(selected ? 3 : 1);
        label.Foreground = duration.Foreground = card.Foreground;
    }

    private void PaintButton(Button button, HexColor color)
    {
        button.Background = SessionColorBrush.Create(color);
        button.Foreground = SessionColorBrush.Create(SessionColors.Foreground(color));
        button.BorderBrush = button.Foreground;
        foreach (string state in new[] { "PointerOver", "Pressed", "Disabled" })
        {
            UpdateResource(button, $"ButtonBackground{state}", (SolidColorBrush)button.Background);
            UpdateResource(button, $"ButtonForeground{state}", (SolidColorBrush)button.Foreground);
            UpdateResource(button, $"ButtonBorderBrush{state}", (SolidColorBrush)button.BorderBrush);
        }
    }

    private void UpdateResource(Button button, string key, SolidColorBrush value)
    {
        // Keep resource identity so already-materialized native visual states also update.
        if (_stateBrushes.TryGetValue((button, key), out var existing))
            existing.Color = value.Color;
        else
        {
            var owned = new SolidColorBrush(value.Color);
            _stateBrushes.Add((button, key), owned);
            button.Resources[key] = owned;
        }
    }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref uint value, int size);
}
