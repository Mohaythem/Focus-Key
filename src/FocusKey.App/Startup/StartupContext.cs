using FocusKey.Foundation;
using FocusKey.Foundation.Data;
using FocusKey.Foundation.Logging;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Today;
using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Settings;

namespace FocusKey.Startup;

/// <summary>Foundation services created once at startup and shared for the process lifetime.</summary>
internal sealed class StartupContext : IDisposable
{
    public required AppPaths Paths { get; init; }

    public required FileAppLogger Logger { get; init; }

    public required DatabaseInitializationResult Database { get; init; }

    public required SessionCoordinator Sessions { get; init; }
    public required TodayService Today { get; init; }
    public required ReportsService Reports { get; init; }
    public required SettingsService Settings { get; init; }

    public void Dispose() => Logger.Dispose();
}
