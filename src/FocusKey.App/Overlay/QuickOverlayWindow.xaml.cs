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
using Microsoft.UI.Xaml.Hosting;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;
using FocusKey.Shell;

namespace FocusKey.Overlay;

/// <summary>A reusable native utility surface; all session behavior belongs to its controller.</summary>
public sealed partial class QuickOverlayWindow : Window, IQuickOverlayView
{
    private QuickOverlayState _state = new(SessionType.Work, true, false, null);
    private DisplayArea? _display;
    private bool _visible;
    private bool _closing;
    private long _shownTimestamp;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _displayTimer;
    private readonly Action<string>? _trace;
    private SessionColors _colors = SessionColors.From(ApplicationSettings.Default);
    private ThemePalette? _palette;
    private Appearance _appearance = Appearance.System;
    private Contrast _contrast = Contrast.Standard;
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
        var hIcon = NativeMethods.LoadIcon(NativeMethods.GetModuleHandle(null), (IntPtr)NativeMethods.IDI_APPLICATION);
        AppWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(hIcon));
        AppWindow.SetPresenter(presenter);
        try { AppWindow.IsShownInSwitchers = false; }
        catch { }
        Surface.PreviewKeyDown += OnPreviewKeyDown;
        Surface.Loaded += (_, _) => { ResizeAndCenter(); FocusSelection(); };
        Surface.ActualThemeChanged += (_, _) =>
        {
            if (_appearance == Appearance.System && _state.Active is null) ApplySystemSurfaceTheme();
            Render(_state);
        };
        WorkCard.GotFocus += (_, _) => { if (_state.Active is null && _state.Selected != SessionType.Work) SelectionRequested?.Invoke(SessionType.Work); };
        BreakCard.GotFocus += (_, _) => { if (_state.Active is null && _state.Selected != SessionType.Break) SelectionRequested?.Invoke(SessionType.Break); };
        AppWindow.Closing += (_, args) =>
        {
            if (_closing) return;
            args.Cancel = true;
            DismissRequested?.Invoke();
        };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                double elapsedMs = (now - _shownTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (_visible && elapsedMs > 250)
                {
                    DismissRequested?.Invoke();
                }
            }
        };
    }

    public event Action<SessionType>? SelectionRequested;
    public event Action? StartRequested;
    public event Action? StopRequested;
    public event Action? DismissRequested;

    internal void ApplyAppearance(Appearance appearance, ThemePalette? palette = null, Contrast contrast = Contrast.Standard)
    {
        _appearance = appearance;
        _palette = palette;
        _contrast = contrast;
        WindowAppearance.Apply(Surface, AppWindow, appearance);
        if (_state.Active is null)
        {
            if (appearance == Appearance.System)
            {
                ApplySystemSurfaceTheme();
            }
            else if (palette is not null)
            {
                Surface.Background = SessionColorBrush.Create(palette.Surface);
                Surface.BorderBrush = SessionColorBrush.Create(palette.Border);
                ApplyDwmBorder(ToColorRef(palette.Border));
            }
            else
            {
                uint borderColor = appearance switch
                {
                    Appearance.System => 0xFFFFFFFF,
                    Appearance.Light => 0x00DEDEDE,
                    Appearance.Dark => 0x002E2E2E,
                    _ => throw new ArgumentOutOfRangeException(nameof(appearance), appearance, "Unsupported appearance."),
                };
                ApplyDwmBorder(borderColor);
            }
        }

        var targetTheme = WindowAppearance.ToElementTheme(appearance);
        Surface.RequestedTheme = targetTheme;
        Render(_state);
    }

    private bool IsDarkTheme()
    {
        if (_appearance == Appearance.Dark) return true;
        if (_appearance == Appearance.Light) return false;
        if (Surface.ActualTheme == ElementTheme.Dark) return true;
        if (Surface.ActualTheme == ElementTheme.Light) return false;
        var bg = new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background);
        double luminance = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        return luminance < 0.5;
    }

    private void ApplySystemSurfaceTheme()
    {
        Surface.Background = Presentation.ThemeBrush("FkOverlay", Surface);
        Surface.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", Surface);
        if (Surface.BorderBrush is SolidColorBrush border)
            ApplyDwmBorder(ToColorRef(border.Color));
    }

    private void ApplyDwmBorder(uint color)
    {
        DwmSetWindowAttribute(WindowNative.GetWindowHandle(this), 34, ref color, sizeof(uint));
    }

    private static uint ToColorRef(Windows.UI.Color color) =>
        (uint)(color.B << 16 | color.G << 8 | color.R);

    private static uint ToColorRef(HexColor hex) =>
        (uint)(Convert.ToByte(hex.Value.Substring(5, 2), 16) << 16 |
               Convert.ToByte(hex.Value.Substring(3, 2), 16) << 8 |
               Convert.ToByte(hex.Value.Substring(1, 2), 16));

    public void Render(QuickOverlayState state)
    {
        bool changedMode = (_state.Active is null) != (state.Active is null);
        if (_state.Active?.Id != state.Active?.Id || _state.IsBusy != state.IsBusy || _state.CanStart != state.CanStart)
            _trace?.Invoke(state.Active is { } observed
                ? $"Quick overlay state: {observed.Type} timer; id={observed.Id}; plannedEnd={observed.PlannedEndAt:O}; busy={state.IsBusy}."
                : $"Quick overlay state: selection; ready={state.CanStart}; busy={state.IsBusy}.");
        _state = state;
        bool active = state.Active is not null;
        Surface.Padding = active ? new Thickness(24, 24, 24, 20) : new Thickness(24);
        IdleHeader.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        SelectionCards.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        StartButton.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        KeyboardHints.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        ActiveCard.Visibility = active ? Visibility.Visible : Visibility.Collapsed;

        StopButton.IsEnabled = active && !state.IsBusy;
        StopButton.Content = state.IsBusy ? "Please wait…" : "Stop Session";

        bool isHighContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        bool isDark = IsDarkTheme();
        if (state.Active is { } session)
        {
            var sessionColor = session.Type == SessionType.Work ? _colors.Work : _colors.Break;
            ActiveType.Text = session.Type == SessionType.Work ? "WORK SESSION" : "BREAK SESSION";

            if (isHighContrast)
            {
                Surface.Background = Presentation.ThemeBrush("FkOverlay", Surface);
                Surface.BorderBrush = Presentation.ThemeBrush("FkBorder", Surface);
                ProgressBar.Background = Presentation.ThemeBrush("FkAccent", Surface);
                ProgressTrack.Background = Presentation.ThemeBrush("FkBorder", Surface);
                ActiveType.Foreground = Presentation.ThemeBrush("FkForeground", Surface);
                ActiveRemaining.Foreground = Presentation.ThemeBrush("FkForeground", Surface);
                StopButton.Background = Presentation.ThemeBrush("FkSurface2", Surface);
                StopButton.BorderBrush = Presentation.ThemeBrush("FkBorder", Surface);
                StopButton.Foreground = Presentation.ThemeBrush("FkForeground", Surface);
                if (Surface.BorderBrush is SolidColorBrush hcBorder)
                    ApplyDwmBorder(ToColorRef(hcBorder.Color));
            }
            else if (isDark)
            {
                var shaded = SessionColorBrush.CreateShaded(sessionColor, -18);
                Surface.Background = shaded;
                Surface.BorderBrush = SessionColorBrush.Create(sessionColor);
                ProgressBar.Background = SessionColorBrush.Create(sessionColor);
                ProgressTrack.Background = SessionColorBrush.CreateAlpha(HexColor.Parse("#FFFFFF"), 0.18);
                Windows.UI.Color activeSurfaceColor = shaded.Color;
                var activeTextColor = SessionColors.Foreground(ToHexColor(activeSurfaceColor));
                ActiveType.Foreground = SessionColorBrush.Create(activeTextColor);
                ActiveRemaining.Foreground = SessionColorBrush.Create(activeTextColor);
                StopButton.Background = Presentation.ThemeBrush("FkSurface2", Surface);
                StopButton.BorderBrush = SessionColorBrush.CreateAlpha(sessionColor, 0.40);
                StopButton.Foreground = Presentation.ThemeBrush("FkSecondary", Surface);
                ApplyDwmBorder(ToColorRef(sessionColor));
            }
            else
            {
                Surface.Background = SessionColorBrush.Create(sessionColor);
                Surface.BorderBrush = SessionColorBrush.Create(sessionColor);
                var activeTextColor = SessionColors.Foreground(sessionColor);
                ActiveType.Foreground = SessionColorBrush.Create(activeTextColor);
                ActiveRemaining.Foreground = SessionColorBrush.Create(activeTextColor);
                ProgressBar.Background = SessionColorBrush.Create(activeTextColor);
                ProgressTrack.Background = SessionColorBrush.CreateAlpha(activeTextColor, 0.25);
                StopButton.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 255, 255, 255));
                StopButton.BorderBrush = SessionColorBrush.CreateAlpha(activeTextColor, 0.35);
                StopButton.Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(255, 20, 20, 20));
                ApplyDwmBorder(ToColorRef(sessionColor));
            }
        }
        else
        {
            if (_appearance == Appearance.System)
            {
                ApplySystemSurfaceTheme();
            }
            else if (_palette is not null)
            {
                Surface.Background = SessionColorBrush.Create(_palette.Surface);
                Surface.BorderBrush = SessionColorBrush.Create(_palette.Border);
                ApplyDwmBorder(ToColorRef(_palette.Border));
            }
            else
            {
                Surface.ClearValue(Control.BackgroundProperty);
                Surface.ClearValue(Control.BorderBrushProperty);
            }
            StopButton.ClearValue(Control.BackgroundProperty);
            StopButton.ClearValue(Control.BorderBrushProperty);
            StopButton.ClearValue(Control.ForegroundProperty);
            ProgressTrack.ClearValue(Border.BackgroundProperty);
        }

        RenderCountdown();
        WorkDuration.Text = QuickOverlayDurationFormatter.Format(state.Durations?.Work);
        BreakDuration.Text = QuickOverlayDurationFormatter.Format(state.Durations?.Break);
        AutomationProperties.SetName(WorkCard, $"Work, {WorkDuration.Text}");
        AutomationProperties.SetName(BreakCard, $"Break, {BreakDuration.Text}");
        PaintCard(WorkCard, WorkLabel, WorkDuration, WorkDot, state.Selected == SessionType.Work, _colors.Work, isDark);
        PaintCard(BreakCard, BreakLabel, BreakDuration, BreakDot, state.Selected == SessionType.Break, _colors.Break, isDark);
        // Keep focusable cards available for navigation while their controller ignores selection
        // during loading/saving or an existing session. Start is explicitly disabled.
        StartButton.IsEnabled = state.CanStart && !state.IsBusy;
        StartButton.Content = state.IsBusy ? "Please wait…" : "Start";
        var startColor = state.Selected == SessionType.Work ? _colors.Work : _colors.Break;
        StartButton.Background = SessionColorBrush.Create(startColor);
        StartButton.Foreground = SessionColorBrush.Create(SessionColors.Foreground(startColor));
        StartButton.BorderThickness = new Thickness(0);
        FeedbackText.Text = state.Feedback ?? string.Empty;
        FeedbackText.Visibility = state.Feedback is null ? Visibility.Collapsed : Visibility.Visible;
        if (_visible) ResizeAndCenter();
        if (_visible && changedMode)
        {
            FocusSelection();
            AnimateMode(state.Active is null ? SelectionCards : ActiveCard);
        }
    }

    private static void AnimateMode(UIElement element)
    {
        // A short compositor fade never delays focus, input or lifecycle transitions.
        if (!new Windows.UI.ViewManagement.UISettings().AnimationsEnabled) return;
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, 0.65f);
        animation.InsertKeyFrame(1, 1);
        animation.Duration = TimeSpan.FromMilliseconds(120);
        visual.StartAnimation("Opacity", animation);
    }

    private void RenderCountdown()
    {
        if (_closing) return;
        TimeSpan remaining = MiniTimerController.RemainingAt(_state.Active, TimeProvider.System.GetUtcNow());
        ActiveRemaining.Text = MiniTimerController.Format(remaining);
        AutomationProperties.SetName(ActiveRemaining, $"{_state.Active?.Type}, {ActiveRemaining.Text} remaining");
        if (_state.Active is { } activeSession)
        {
            double totalSecs = activeSession.PlannedDuration.TotalSeconds;
            double remSecs = Math.Max(0, remaining.TotalSeconds);
            double pct = totalSecs > 0 ? Math.Clamp((totalSecs - remSecs) / totalSecs, 0, 1) : 0;
            double trackWidth = ProgressTrack.ActualWidth > 0 ? ProgressTrack.ActualWidth : 372;
            ProgressBar.Width = Math.Max(2, trackWidth * pct);
        }
        if (_visible && _state.Active is not null && remaining > TimeSpan.Zero) _displayTimer.Start();
        else _displayTimer.Stop();
    }

    public void ShowAndFocus()
    {
        if (_closing) return;
        _shownTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!_visible)
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            _display = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(foreground), DisplayAreaFallback.Primary);
            // Move to the foreground monitor first so subsequent DPI queries belong to that monitor.
            RectInt32 area = _display.WorkArea;
            AppWindow.Move(new PointInt32(area.X + area.Width / 2, area.Y + area.Height / 2));
        }
        _visible = true;
        ResizeAndCenter();
        AppWindow.Show();
        Activate();

        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        NativeMethods.ForceForeground(hwnd);

        FocusSelection();
        RenderCountdown();

        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            if (_visible) FocusSelection();
        });
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
        // Keep the reference surface stable. Feedback and idle measurement allow dynamic sizing
        // so keyboard hints and text scaling never clip or collide with the bottom window border.
        double widthDip = _state.Active is not null ? 379 : 420;
        double heightDip;
        Surface.Width = widthDip;
        Surface.Height = double.NaN;
        Surface.Measure(new Windows.Foundation.Size(widthDip, double.PositiveInfinity));

        if (_state.Active is not null)
        {
            heightDip = Math.Max(198, Math.Ceiling(Surface.DesiredSize.Height));
        }
        else
        {
            // Baseline 286 DIP provides comfortable bottom breathing room; expands dynamically for text scaling or feedback
            heightDip = Math.Max(286, Math.Ceiling(Surface.DesiredSize.Height));
            if (_state.Feedback is not null)
            {
                heightDip = Math.Max(320, heightDip);
            }
        }

        Surface.Width = widthDip;
        Surface.Height = heightDip;
        double scale = NativeMethods.GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        if (scale <= 0) scale = 1;
        int width = (int)Math.Ceiling(widthDip * scale);
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

    private void OnCloseClicked(object sender, PointerRoutedEventArgs args)
    {
        args.Handled = true;
        DismissRequested?.Invoke();
    }

    private static void PaintCard(Button card, TextBlock label, TextBlock duration, Microsoft.UI.Xaml.Shapes.Ellipse? dot, bool selected, HexColor color, bool isDark)
    {
        if (dot is not null)
        {
            dot.Fill = selected
                ? SessionColorBrush.Create(SessionColors.Foreground(color))
                : SessionColorBrush.Create(color);
        }

        if (selected)
        {
            card.Background = SessionColorBrush.Create(color);
            card.BorderBrush = SessionColorBrush.Create(color);
            card.BorderThickness = new Thickness(1);
            card.Opacity = 1.0;

            var textFg = SessionColorBrush.Create(SessionColors.Foreground(color));
            label.Foreground = textFg;
            duration.Foreground = textFg;
        }
        else
        {
            if (isDark)
            {
                card.Background = SessionColorBrush.CreateShaded(color, -20);
                card.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", card);
            }
            else
            {
                card.Background = SessionColorBrush.CreateAlpha(color, 0.10);
                card.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", card);
            }
            card.BorderThickness = new Thickness(1);
            card.Opacity = 1.0;

            var dimFg = Presentation.ThemeBrush("FkSecondary", card);
            label.Foreground = dimFg;
            duration.Foreground = dimFg;
        }
    }

    private static Windows.UI.Color Blend(HexColor foreground, double opacity, Windows.UI.Color background)
    {
        Windows.UI.Color source = SessionColorBrush.Create(foreground).Color;
        byte alpha = (byte)Math.Clamp((int)Math.Round(opacity * 255), 0, 255);
        byte BlendChannel(byte sourceChannel, byte backgroundChannel) =>
            (byte)Math.Clamp((sourceChannel * alpha + backgroundChannel * (255 - alpha) + 127) / 255, 0, 255);
        return Windows.UI.Color.FromArgb(255,
            BlendChannel(source.R, background.R),
            BlendChannel(source.G, background.G),
            BlendChannel(source.B, background.B));
    }

    private static HexColor ToHexColor(Windows.UI.Color color) =>
        HexColor.Parse($"#{color.R:X2}{color.G:X2}{color.B:X2}");
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref uint value, int size);
}

