using FocusKey.Startup;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace FocusKey;

/// <summary>
/// Phase 0 placeholder window. It exists to prove the native application launches and that
/// foundation initialization actually happened. No product UI belongs here yet.
/// </summary>
public sealed partial class MainWindow : Window
{
    internal event Action? ExitRequested;

    private void OnExitClick(object sender, RoutedEventArgs args) => ExitRequested?.Invoke();

    internal MainWindow(StartupContext startup)
    {
        ArgumentNullException.ThrowIfNull(startup);

        InitializeComponent();

        AppWindow.Resize(new SizeInt32(660, 400));

        FoundationDetails.Text = string.Join(
            Environment.NewLine,
            $"version         {FoundationBootstrap.Version}",
            $"data root       {startup.Paths.RootDirectory}",
            $"database        {startup.Database.DatabaseFile}",
            $"schema version  {startup.Database.SchemaVersionAfter}",
            $"log file        {startup.Paths.LogFile}");

        startup.Logger.Info("Placeholder window displayed.");
    }
}
