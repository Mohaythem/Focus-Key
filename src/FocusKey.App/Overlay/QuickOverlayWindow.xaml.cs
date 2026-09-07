using System.Runtime.InteropServices;
using FocusKey.Foundation.Overlay;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Settings;
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

    public QuickOverlayWindow()
    {
        InitializeComponent();
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
        _state = state;
        WorkDuration.Text = QuickOverlayDurationFormatter.Format(state.Durations?.Work);
        BreakDuration.Text = QuickOverlayDurationFormatter.Format(state.Durations?.Break);
        AutomationProperties.SetName(WorkCard, $"Work, {WorkDuration.Text}");
        AutomationProperties.SetName(BreakCard, $"Break, {BreakDuration.Text}");
        PaintCard(WorkCard, WorkDuration, state.Selected == SessionType.Work, 0x18, 0x37, 0x39);
        PaintCard(BreakCard, BreakDuration, state.Selected == SessionType.Break, 0x43, 0x47, 0x63);
        // Keep focusable cards available for navigation while their controller ignores selection
        // during loading/saving or an existing session. Start is explicitly disabled.
        StartButton.IsEnabled = state.CanStart && !state.IsBusy;
        StartButton.Content = state.IsBusy ? "Please wait…" : "Start";
        StartButton.Background = state.Selected == SessionType.Work ? Brush(0x18, 0x37, 0x39) : Brush(0x43, 0x47, 0x63);
        FeedbackText.Text = state.Feedback ?? string.Empty;
        FeedbackText.Visibility = state.Feedback is null ? Visibility.Collapsed : Visibility.Visible;
        if (_visible) ResizeAndCenter();
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
    }

    public void Hide()
    {
        _visible = false;
        if (!_closing) AppWindow.Hide();
    }

    public void Dispose()
    {
        if (_closing) return;
        _visible = false;
        _closing = true;
        Close();
    }

    private void FocusSelection() =>
        (_state.Selected == SessionType.Work ? WorkCard : BreakCard).Focus(FocusState.Keyboard);

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
                SelectionRequested?.Invoke(args.Key == VirtualKey.Left ? SessionType.Work : SessionType.Break);
                FocusSelection();
                break;
            case VirtualKey.Enter:
                // Handle once at the preview stage, avoiding an additional native Button click.
                args.Handled = true;
                StartRequested?.Invoke();
                break;
        }
        // Space and Tab retain the native Button keyboard/focus behavior.
    }

    private static void PaintCard(Button card, TextBlock duration, bool selected, byte r, byte g, byte b)
    {
        card.Background = selected ? Brush(r, g, b) : Brush((byte)Math.Max(0, r - 20), (byte)Math.Max(0, g - 20), (byte)Math.Max(0, b - 20));
        card.BorderBrush = selected ? Brush(r, g, b) : Brush(0x1e, 0x24, 0x24);
        card.Resources["ButtonBackgroundPointerOver"] = card.Background;
        card.Resources["ButtonBackgroundPressed"] = card.Background;
        card.Resources["ButtonBorderBrushPointerOver"] = card.BorderBrush;
        card.Resources["ButtonBorderBrushPressed"] = card.BorderBrush;
        card.Opacity = selected ? 1 : 0.6;
        duration.Foreground = selected ? Brush(0xf0, 0xf4, 0xf4) : Brush(0x90, 0x9b, 0x9b);
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Windows.UI.Color.FromArgb(255, r, g, b));
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref uint value, int size);
}
