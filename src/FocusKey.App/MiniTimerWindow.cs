using FocusKey.Foundation.MiniTimer;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using VirtualKey = Windows.System.VirtualKey;

namespace FocusKey;

/// <summary>Reusable utility window; native caption supplies drag, close and keyboard window menu.</summary>
internal sealed class MiniTimerWindow : Window, IDisposable
{
    private readonly MiniTimerController _controller;
    private readonly StackPanel _root = new() { Padding = new Thickness(12), Spacing = 6 };
    private readonly Border _badge = new() { Padding = new Thickness(8, 4, 8, 4), CornerRadius = new CornerRadius(4) };
    private readonly TextBlock _text = new() { FontSize = 20, Language = "en-US",
        FlowDirection = FlowDirection.LeftToRight, TextReadingOrder = TextReadingOrder.UseFlowDirection };
    private readonly DispatcherQueueTimer _timer;
    private readonly OverlappedPresenter _presenter;
    private SessionColors _colors = SessionColors.From(ApplicationSettings.Default);
    private bool _disposed, _positioned;
    internal MiniTimerWindow(MiniTimerController controller, Action openMain)
    {
        _controller = controller;
        Title = "Focus Key Mini Timer";
        _presenter = OverlappedPresenter.Create();
        _presenter.IsResizable = false;
        _presenter.IsMaximizable = false;
        _presenter.IsMinimizable = false;
        AppWindow.SetPresenter(_presenter);
        AppWindow.IsShownInSwitchers = false;
        _badge.Child = _text;
        _root.Children.Add(_badge);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var pin = new ToggleButton { Content = "Keep on top", FontSize = 12 };
        AutomationProperties.SetName(pin, "Keep Mini Timer on top");
        pin.Checked += (_, _) => _presenter.IsAlwaysOnTop = true;
        pin.Unchecked += (_, _) => _presenter.IsAlwaysOnTop = false;
        var open = new Button { Content = "Open Focus Key", FontSize = 12 };
        open.Click += (_, _) => openMain();
        actions.Children.Add(pin);
        actions.Children.Add(open);
        _root.Children.Add(actions);
        Content = _root;
        _root.Loaded += (_, _) => SizeWindow();
        _root.ActualThemeChanged += (_, _) => Paint();
        _root.PreviewKeyDown += (_, args) => { if (args.Key == VirtualKey.Escape) { args.Handled = true; Hide(); } };
        AppWindow.Closing += (_, args) => { if (!_disposed) { args.Cancel = true; Hide(); } };
        _timer = DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) => Render();
        controller.Changed += OnChanged;
        OnChanged();
    }
    internal async Task ShowAsync()
    {
        if (_disposed) return;
        SizeWindow();
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var position = AppWindow.Position;
        int width = AppWindow.Size.Width, height = AppWindow.Size.Height;
        if (!_positioned) position = new(area.X + area.Width - width - 16, area.Y + area.Height - height - 16);
        AppWindow.Move(new PointInt32(Math.Clamp(position.X, area.X, Math.Max(area.X, area.X + area.Width - width)),
            Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - height))));
        _positioned = true;
        Task load = _controller.OpenAsync();
        AppWindow.Show();
        Activate();
        await load;
    }
    private void SizeWindow()
    {
        double scale = _root.XamlRoot?.RasterizationScale ?? 1;
        AppWindow.ResizeClient(new SizeInt32((int)Math.Ceiling(310 * scale), (int)Math.Ceiling(100 * scale)));
    }
    internal void Hide() { if (_disposed) return; _controller.Hide(); _timer.Stop(); AppWindow.Hide(); }
    internal void ApplyAppearance(Appearance appearance) { WindowAppearance.Apply(_root, AppWindow, appearance); Paint(); }
    internal void ApplyColors(SessionColors colors) { _colors = colors; Paint(); }
    private void OnChanged() { Paint(); Render(); }
    private void Render()
    {
        if (_disposed) return;
        _text.Text = _controller.Text;
        _text.FontSize = _controller.Error is null ? 20 : 14;
        AutomationProperties.SetName(_text, _controller.Text);
        if (_controller.IsVisible && !_controller.IsLoading && _controller.Session is not null && _controller.Remaining > TimeSpan.Zero)
            _timer.Start();
        else _timer.Stop();
    }
    private void Paint()
    {
        bool dark = _root.ActualTheme == ElementTheme.Dark;
        _root.Background = new SolidColorBrush(dark ? Microsoft.UI.Colors.Black : Microsoft.UI.Colors.White);
        if (_controller.Session is { } session && _controller.Error is null)
        {
            var color = session.Type == SessionType.Work ? _colors.Work : _colors.Break;
            _badge.Background = SessionColorBrush.Create(color);
            _text.Foreground = SessionColorBrush.Create(SessionColors.Foreground(color));
        }
        else
        {
            _badge.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            _text.Foreground = new SolidColorBrush(dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black);
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _controller.Changed -= OnChanged;
        _controller.Dispose();
        Close();
    }
}
