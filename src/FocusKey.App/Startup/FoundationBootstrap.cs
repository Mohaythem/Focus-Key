using System.Reflection;
using FocusKey.Foundation;
using FocusKey.Foundation.Data;
using FocusKey.Foundation.Logging;
using FocusKey.Foundation.Sessions;
using FocusKey.Foundation.Today;
using FocusKey.Foundation.Reports;
using FocusKey.Foundation.Settings;

namespace FocusKey.Startup;

/// <summary>
/// Explicit composition of the Phase 0 foundation: paths, then logging, then the database.
/// Wired by hand on purpose — there is nothing here that a container would make clearer.
/// </summary>
internal static class FoundationBootstrap
{
    internal static async Task<StartupContext> RunAsync(CancellationToken cancellationToken = default)
    {
        AppPaths paths = AppPaths.Resolve();
        paths.EnsureCreated();

        var logger = new FileAppLogger(paths.LogFile);

        try
        {
            logger.Info($"Focus Key {Version} starting (process {Environment.ProcessId}).");
            logger.Info($"Application data root: {paths.RootDirectory}");

            if (!string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable(AppPaths.DataRootEnvironmentVariable)))
            {
                logger.Warning($"Data root came from {AppPaths.DataRootEnvironmentVariable}.");
            }

            logger.Info($"Log file: {paths.LogFile}");

            var connections = new SqliteConnectionFactory(paths.DatabaseFile);
            DatabaseInitializationResult database = new DatabaseBootstrapper(connections, logger).Initialize();
            var repository = new SqliteSessionRepository(connections);
            var sessions = new SessionCoordinator(repository);
            SessionRecoveryResult recovery = await sessions.InitializeAsync(cancellationToken).ConfigureAwait(false);
            logger.Info($"Session startup recovery: {recovery.Kind}.");
            var settings = new SettingsService(new SqliteSettingsRepository(connections));
            await settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            logger.Info("Application settings loaded and validated.");

            logger.Info("Foundation initialization complete.");

            return new StartupContext
            {
                Paths = paths,
                Logger = logger,
                Database = database,
                Sessions = sessions,
                Today = new TodayService(repository),
                Reports = new ReportsService(repository),
                Settings = settings,
            };
        }
        catch (Exception exception)
        {
            logger.Error("Foundation initialization failed.", exception);
            logger.Dispose();
            throw;
        }
    }

    internal static string Version =>
        typeof(FoundationBootstrap).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(FoundationBootstrap).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
