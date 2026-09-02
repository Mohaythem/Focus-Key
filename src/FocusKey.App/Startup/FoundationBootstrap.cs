using System.Reflection;
using FocusKey.Foundation;
using FocusKey.Foundation.Data;
using FocusKey.Foundation.Logging;

namespace FocusKey.Startup;

/// <summary>
/// Explicit composition of the Phase 0 foundation: paths, then logging, then the database.
/// Wired by hand on purpose — there is nothing here that a container would make clearer.
/// </summary>
internal static class FoundationBootstrap
{
    internal static StartupContext Run()
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

            logger.Info("Foundation initialization complete.");

            return new StartupContext
            {
                Paths = paths,
                Logger = logger,
                Database = database,
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
