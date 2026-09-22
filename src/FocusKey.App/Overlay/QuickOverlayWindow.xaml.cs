using System.Globalization;
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
    private bool _visible;
    private bool _closing;
    private long _shownTimestamp;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _displayTimer;
    private readonly Action<string>? _trace;
    private readonly Func<Task<ApplicationSettings>>? _getSettings;
    private readonly Func<int?, int?, Task>? _savePosition;
    private SessionColors _colors = SessionColors.From(ApplicationSettings.Default);
    private ThemePalette? _palette;
    private Appearance _appearance = Appearance.System;
    private Contrast _contrast = Contrast.Standard;
    private int? _persistedPositionX;
    private int? _persistedPositionY;
    private bool _positionLoaded;
    private bool _isDragging;
    private NativeMethods.POINT _dragStartCursorPos;
    private PointInt32 _dragStartWindowPos;

    internal void ApplyColors(SessionColors colors) { _colors = colors; Render(_state); }
    internal void ApplyPosition(int? x, int? y)
    {
        _persistedPositionX = x;
        _persistedPositionY = y;
        _positionLoaded = true;
    }
    internal void ApplyShortcut(GlobalShortcut shortcut)
    {
        if (OverlayShortcutHint is not null)
        {
            OverlayShortcutHint.Text = shortcut.ToString();
        }
    }

    public QuickOverlayWindow(
        Func<Task<ApplicationSettings>>? getSettings = null,
        Func<int?, int?, Task>? savePosition = null,
        Action<string>? trace = null)
    {
        _getSettings = getSettings;
        _savePosition = savePosition;
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
        Surface.Loaded += (_, _) => { PositionWindow(recenter: true); FocusSelection(); };
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
            SaveCurrentPosition();
            DismissRequested?.Invoke();
        };
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPositionChange && _visible)
            {
                // Drag completed or window moved
            }
        };
        Activated += (_, args) =>
        {
            if (args.WindowActivationState == WindowActivationState.Deactivated)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                double elapsedMs = (now - _shownTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                if (_visible && elapsedMs > 250)
                {
                    SaveCurrentPosition();
                    DismissRequested?.Invoke();
                }
            }
        };
    }

    public event Action<SessionType>? SelectionRequested;
    public event Action? StartRequested;
    public event Action? PauseRequested;
    public event Action? StartNewRequested;
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
        bool wasActive = _state.Active is not null;
        bool hasActive = state.Active is not null;
        bool isPaused = state.Active is { Status: SessionStatus.Paused };
        bool changedMode = wasActive != hasActive;
        if (_state.Active?.Id != state.Active?.Id || _state.IsBusy != state.IsBusy || _state.CanStart != state.CanStart || _state.Active?.Status != state.Active?.Status)
            _trace?.Invoke(state.Active is { Status: SessionStatus.Running } observed
                ? $"Quick overlay state: {observed.Type} timer; id={observed.Id}; plannedEnd={observed.PlannedEndAt:O}; busy={state.IsBusy}."
                : state.Active is { Status: SessionStatus.Paused } paused
                ? $"Quick overlay state: {paused.Type} paused; id={paused.Id}; remaining={paused.PlannedDuration - paused.AccumulatedActiveDuration}; busy={state.IsBusy}."
                : $"Quick overlay state: selection; ready={state.CanStart}; busy={state.IsBusy}.");
        _state = state;

        bool isHighContrast = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        bool isDark = IsDarkTheme();

        // Header configuration
        if (hasActive && state.Active is { } currentSession)
        {
            var sessionColor = currentSession.Type == SessionType.Work ? _colors.Work : _colors.Break;
            ActiveBadgeDot.Visibility = Visibility.Visible;
            ActiveBadgeDot.Fill = SessionColorBrush.Create(sessionColor);
            HeaderTitle.Text = currentSession.Type == SessionType.Work
                ? (isPaused ? "WORK SESSION (PAUSED)" : "WORK SESSION")
                : (isPaused ? "BREAK SESSION (PAUSED)" : "BREAK SESSION");
            HeaderTitle.Foreground = Presentation.ThemeBrush("FkForeground", Surface);
            ShortcutHintContainer.Visibility = Visibility.Collapsed;
        }
        else
        {
            ActiveBadgeDot.Visibility = Visibility.Collapsed;
            HeaderTitle.Text = "FOCUS KEY";
            HeaderTitle.Foreground = Presentation.ThemeBrush("FkSecondary", Surface);
            ShortcutHintContainer.Visibility = Visibility.Visible;
        }

        // View Visibility
        IdleContent.Visibility = hasActive ? Visibility.Collapsed : Visibility.Visible;
        ActiveCard.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;

        // Active Controls
        PauseButton.IsEnabled = hasActive && !state.IsBusy;
        PauseButton.Content = state.IsBusy ? "Please wait…" : isPaused ? "Continue" : "Pause";
        AutomationProperties.SetName(PauseButton, isPaused ? "Continue session" : "Pause session");

        StopButton.IsEnabled = false;
        StopButton.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(StopButton, "Stop session");

        StartNewButton.Visibility = isPaused ? Visibility.Visible : Visibility.Collapsed;
        StartNewButton.IsEnabled = isPaused && !state.IsBusy;
        StartNewButton.Content = "Start New";
        AutomationProperties.SetName(StartNewButton, "Start new session");

        if (state.Active is { } session)
        {
            var sessionColor = session.Type == SessionType.Work ? _colors.Work : _colors.Break;

            if (isHighContrast)
            {
                Surface.Background = Presentation.ThemeBrush("FkOverlay", Surface);
                Surface.BorderBrush = Presentation.ThemeBrush("FkBorder", Surface);
                ProgressBar.Background = Presentation.ThemeBrush("FkAccent", Surface);
                ProgressTrack.Background = Presentation.ThemeBrush("FkBorder", Surface);
                ActiveRemaining.Foreground = Presentation.ThemeBrush("FkForeground", Surface);
                PauseButton.Background = Presentation.ThemeBrush("FkSurface2", Surface);
                PauseButton.BorderBrush = Presentation.ThemeBrush("FkBorder", Surface);
                PauseButton.Foreground = Presentation.ThemeBrush("FkForeground", Surface);
                StopButton.Background = Presentation.ThemeBrush("FkSurface2", Surface);
                StopButton.BorderBrush = Presentation.ThemeBrush("FkBorder", Surface);
                StopButton.Foreground = Presentation.ThemeBrush("FkForeground", Surface);
                StartNewButton.Background = Presentation.ThemeBrush("FkSurface2", Surface);
                StartNewButton.BorderBrush = Presentation.ThemeBrush("FkBorder", Surface);
                StartNewButton.Foreground = Presentation.ThemeBrush("FkForeground", Surface);
                if (Surface.BorderBrush is SolidColorBrush hcBorder)
                    ApplyDwmBorder(ToColorRef(hcBorder.Color));
            }
            else if (isDark)
            {
                Surface.Background = Presentation.ThemeBrush("FkOverlay", Surface);
                Surface.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", Surface);
                ProgressBar.Background = SessionColorBrush.Create(sessionColor);
                ProgressTrack.Background = SessionColorBrush.CreateAlpha(HexColor.Parse("#FFFFFF"), 0.12);
                ActiveRemaining.Foreground = Presentation.ThemeBrush("FkForeground", Surface);

                if (isPaused)
                {
                    PauseButton.Background = SessionColorBrush.Create(sessionColor);
                    PauseButton.BorderBrush = SessionColorBrush.Create(sessionColor);
                    PauseButton.Foreground = SessionColorBrush.Create(SessionColors.Foreground(sessionColor));

                    StartNewButton.Background = Presentation.ThemeBrush("FkSurface2", Surface);
                    StartNewButton.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", Surface);
                    StartNewButton.Foreground = Presentation.ThemeBrush("FkForeground", Surface);
                }
                else
                {
                    PauseButton.Background = SessionColorBrush.Create(sessionColor);
                    PauseButton.BorderBrush = SessionColorBrush.Create(sessionColor);
                    PauseButton.Foreground = SessionColorBrush.Create(SessionColors.Foreground(sessionColor));
                }
                ApplyDwmBorder(ToColorRef(sessionColor));
            }
            else
            {
                Surface.Background = Presentation.ThemeBrush("FkOverlay", Surface);
                Surface.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", Surface);
                ProgressBar.Background = SessionColorBrush.Create(sessionColor);
                ProgressTrack.Background = Presentation.ThemeBrush("FkBorder", Surface);
                ActiveRemaining.Foreground = Presentation.ThemeBrush("FkForeground", Surface);

                if (isPaused)
                {
                    PauseButton.Background = SessionColorBrush.Create(sessionColor);
                    PauseButton.BorderBrush = SessionColorBrush.Create(sessionColor);
                    PauseButton.Foreground = SessionColorBrush.Create(SessionColors.Foreground(sessionColor));

                    StartNewButton.Background = Presentation.ThemeBrush("FkSurface2", Surface);
                    StartNewButton.BorderBrush = Presentation.ThemeBrush("CardStrokeColorDefaultBrush", Surface);
                    StartNewButton.Foreground = Presentation.ThemeBrush("FkForeground", Surface);
                }
                else
                {
                    PauseButton.Background = SessionColorBrush.Create(sessionColor);
                    PauseButton.BorderBrush = SessionColorBrush.Create(sessionColor);
                    PauseButton.Foreground = SessionColorBrush.Create(SessionColors.Foreground(sessionColor));
                }
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
            PauseButton.ClearValue(Control.BackgroundProperty);
            PauseButton.ClearValue(Control.BorderBrushProperty);
            PauseButton.ClearValue(Control.ForegroundProperty);
            StopButton.ClearValue(Control.BackgroundProperty);
            StopButton.ClearValue(Control.BorderBrushProperty);
            StopButton.ClearValue(Control.ForegroundProperty);
            StartNewButton.ClearValue(Control.BackgroundProperty);
            StartNewButton.ClearValue(Control.BorderBrushProperty);
            StartNewButton.ClearValue(Control.ForegroundProperty);
            ProgressTrack.ClearValue(Border.BackgroundProperty);
        }

        RenderCountdown();

        WorkDuration.Text = QuickOverlayDurationFormatter.Format(state.Durations?.Work);
        BreakDuration.Text = QuickOverlayDurationFormatter.Format(state.Durations?.Break);
        bool isWorkSelected = state.Selected == SessionType.Work;
        AutomationProperties.SetItemStatus(WorkCard, isWorkSelected ? "Selected" : "Not Selected");
        AutomationProperties.SetItemStatus(BreakCard, !isWorkSelected ? "Selected" : "Not Selected");
        AutomationProperties.SetName(WorkCard, isWorkSelected ? $"Work session, {WorkDuration.Text}, Selected" : $"Work session, {WorkDuration.Text}, Not Selected");
        AutomationProperties.SetName(BreakCard, !isWorkSelected ? $"Break session, {BreakDuration.Text}, Selected" : $"Break session, {BreakDuration.Text}, Not Selected");

        PaintCard(WorkCard, WorkLabel, WorkDuration, WorkDot, isWorkSelected, _colors.Work, isDark);
        PaintCard(BreakCard, BreakLabel, BreakDuration, BreakDot, !isWorkSelected, _colors.Break, isDark);

        StartButton.IsEnabled = state.CanStart && !state.IsBusy;
        StartButton.Content = state.IsBusy ? "Please wait…" : "Start";
        string chosenDuration = isWorkSelected ? WorkDuration.Text : BreakDuration.Text;
        AutomationProperties.SetName(StartButton, $"Start {state.Selected} session, {chosenDuration}");
        if (StartKeyHint is not null) StartKeyHint.Text = "Start";

        var startColor = isWorkSelected ? _colors.Work : _colors.Break;
        StartButton.Background = SessionColorBrush.Create(startColor);
        StartButton.Foreground = SessionColorBrush.Create(SessionColors.Foreground(startColor));
        StartButton.BorderThickness = new Thickness(0);
        FeedbackText.Text = state.Feedback ?? string.Empty;
        FeedbackText.Visibility = state.Feedback is null ? Visibility.Collapsed : Visibility.Visible;

        if (_visible) PositionWindow(recenter: false);
        if (_visible && changedMode)
        {
            FocusSelection();
            AnimateMode(hasActive ? ActiveCard : IdleContent);
        }
    }

    private static void AnimateMode(UIElement element)
    {
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
        bool isPaused = _state.Active is { Status: SessionStatus.Paused };
        string statusText = isPaused ? "Paused" : "Running";
        AutomationProperties.SetName(ActiveRemaining, $"{_state.Active?.Type} session, {ActiveRemaining.Text} remaining, {statusText}");
        if (_state.Active is { } activeSession)
        {
            double totalSecs = activeSession.PlannedDuration.TotalSeconds;
            double remSecs = Math.Max(0, remaining.TotalSeconds);
            double pct = totalSecs > 0 ? Math.Clamp((totalSecs - remSecs) / totalSecs, 0, 1) : 0;
            double trackWidth = ProgressTrack.ActualWidth > 0 ? ProgressTrack.ActualWidth : 432;
            ProgressBar.Width = Math.Max(3, trackWidth * pct);
        }
        if (_visible && _state.Active is not null && remaining > TimeSpan.Zero) _displayTimer.Start();
        else _displayTimer.Stop();
    }

    public async void ShowAndFocus()
    {
        if (_closing) return;
        _shownTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

        if (!_positionLoaded && _getSettings is not null)
        {
            try
            {
                var settings = await _getSettings();
                _persistedPositionX = settings.OverlayPositionX;
                _persistedPositionY = settings.OverlayPositionY;
                _positionLoaded = true;
            }
            catch (Exception ex)
            {
                _trace?.Invoke($"Could not load overlay position settings: {ex.Message}");
            }
        }

        _visible = true;
        PositionWindow(recenter: !_positionLoaded || !_persistedPositionX.HasValue || !_persistedPositionY.HasValue);
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
        SaveCurrentPosition();
        if (!_closing) AppWindow.Hide();
    }

    public void Dispose()
    {
        if (_closing) return;
        _visible = false;
        _closing = true;
        _displayTimer.Stop();
        SaveCurrentPosition();
        Close();
    }

    private void FocusSelection() =>
        (_state.Active is not null ? PauseButton : _state.Selected == SessionType.Work ? WorkCard : BreakCard).Focus(FocusState.Keyboard);

    private void PositionWindow(bool recenter)
    {
        const double widthDip = 480.0;
        Surface.Width = widthDip;
        Surface.Height = double.NaN;
        Surface.Measure(new Windows.Foundation.Size(widthDip, double.PositiveInfinity));

        double heightDip;
        if (_state.Active is not null)
        {
            heightDip = Math.Max(220, Math.Ceiling(Surface.DesiredSize.Height));
        }
        else
        {
            heightDip = Math.Max(280, Math.Ceiling(Surface.DesiredSize.Height));
            if (_state.Feedback is not null)
            {
                heightDip = Math.Max(310, heightDip);
            }
        }

        Surface.Width = widthDip;
        Surface.Height = heightDip;
        IntPtr hwnd = WindowNative.GetWindowHandle(this);
        double scale = NativeMethods.GetDpiForWindow(hwnd) / 96.0;
        if (scale <= 0) scale = 1.0;
        int widthPx = (int)Math.Ceiling(widthDip * scale);
        int heightPx = (int)Math.Ceiling(heightDip * scale);

        if (recenter || !_persistedPositionX.HasValue || !_persistedPositionY.HasValue)
        {
            var fgWorkArea = NativeMethods.GetForegroundWorkArea();
            var center = OverlayPositionHelper.CalculateInitialCenter(widthPx, heightPx, fgWorkArea);
            AppWindow.MoveAndResize(new RectInt32(center.X, center.Y, widthPx, heightPx));
        }
        else
        {
            var workAreas = NativeMethods.GetAllWorkAreas();
            int currentX = AppWindow.Position.X != 0 ? AppWindow.Position.X : _persistedPositionX.Value;
            int currentY = AppWindow.Position.Y != 0 ? AppWindow.Position.Y : _persistedPositionY.Value;
            var clamped = OverlayPositionHelper.ClampToWorkAreas(currentX, currentY, widthPx, heightPx, workAreas);
            AppWindow.MoveAndResize(new RectInt32(clamped.X, clamped.Y, widthPx, heightPx));
        }
    }

    private void SaveCurrentPosition()
    {
        if (!_visible && !_closing) return;
        try
        {
            int posX = AppWindow.Position.X;
            int posY = AppWindow.Position.Y;
            if (posX != 0 || posY != 0)
            {
                _persistedPositionX = posX;
                _persistedPositionY = posY;
                _savePosition?.Invoke(posX, posY);
            }
        }
        catch (Exception ex)
        {
            _trace?.Invoke($"Could not save overlay position: {ex.Message}");
        }
    }

    private void OnHeaderPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pt = e.GetCurrentPoint(HeaderGrid);
        if (pt.Properties.IsLeftButtonPressed)
        {
            _isDragging = true;
            HeaderGrid.CapturePointer(e.Pointer);
            NativeMethods.GetCursorPos(out _dragStartCursorPos);
            _dragStartWindowPos = AppWindow.Position;
            e.Handled = true;
        }
    }

    private void OnHeaderPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            if (NativeMethods.GetCursorPos(out var currentCursorPos))
            {
                int deltaX = currentCursorPos.X - _dragStartCursorPos.X;
                int deltaY = currentCursorPos.Y - _dragStartCursorPos.Y;
                int newX = _dragStartWindowPos.X + deltaX;
                int newY = _dragStartWindowPos.Y + deltaY;
                AppWindow.Move(new PointInt32(newX, newY));
            }
            e.Handled = true;
        }
    }

    private void OnHeaderPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            HeaderGrid.ReleasePointerCapture(e.Pointer);
            SaveCurrentPosition();
            e.Handled = true;
        }
    }

    private void OnHeaderPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            SaveCurrentPosition();
        }
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs args)
    {
        SaveCurrentPosition();
        DismissRequested?.Invoke();
    }

    private void OnWorkClicked(object sender, RoutedEventArgs args) => SelectionRequested?.Invoke(SessionType.Work);
    private void OnBreakClicked(object sender, RoutedEventArgs args) => SelectionRequested?.Invoke(SessionType.Break);
    private void OnStartClicked(object sender, RoutedEventArgs args) => StartRequested?.Invoke();
    private void OnPauseClicked(object sender, RoutedEventArgs args)
    {
        if (_state.Active is { Status: SessionStatus.Paused }) StartRequested?.Invoke();
        else PauseRequested?.Invoke();
    }
    private void OnStartNewClicked(object sender, RoutedEventArgs args) => StartNewRequested?.Invoke();
    private void OnStopClicked(object sender, RoutedEventArgs args) => StopRequested?.Invoke();

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs args)
    {
        switch (args.Key)
        {
            case VirtualKey.Escape:
                args.Handled = true;
                SaveCurrentPosition();
                DismissRequested?.Invoke();
                break;
            case VirtualKey.Left:
                args.Handled = true;
                if (_state.Active is { Status: SessionStatus.Paused })
                {
                    PauseButton.Focus(FocusState.Keyboard);
                }
                else if (_state.Active is null)
                {
                    SelectionRequested?.Invoke(SessionType.Work);
                    FocusSelection();
                }
                break;
            case VirtualKey.Right:
                args.Handled = true;
                if (_state.Active is { Status: SessionStatus.Paused })
                {
                    StartNewButton.Focus(FocusState.Keyboard);
                }
                else if (_state.Active is null)
                {
                    SelectionRequested?.Invoke(SessionType.Break);
                    FocusSelection();
                }
                break;
            case VirtualKey.Enter:
            case VirtualKey.Space:
                if (args.KeyStatus.WasKeyDown) break;
                args.Handled = true;
                if (_state.Active is { Status: SessionStatus.Running })
                {
                    PauseRequested?.Invoke();
                }
                else if (_state.Active is { Status: SessionStatus.Paused })
                {
                    var focused = FocusManager.GetFocusedElement(Surface.XamlRoot);
                    if (ReferenceEquals(focused, StartNewButton))
                    {
                        StartNewRequested?.Invoke();
                    }
                    else
                    {
                        StartRequested?.Invoke();
                    }
                }
                else if (_state.Active is null)
                {
                    StartRequested?.Invoke();
                }
                break;
        }
    }

    private void OnCloseClicked(object sender, PointerRoutedEventArgs args)
    {
        args.Handled = true;
        SaveCurrentPosition();
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

    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr window, uint attribute, ref uint value, int size);
}

