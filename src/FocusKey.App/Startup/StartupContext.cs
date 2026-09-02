using FocusKey.Foundation;
using FocusKey.Foundation.Data;
using FocusKey.Foundation.Logging;

namespace FocusKey.Startup;

/// <summary>Foundation services created once at startup and shared for the process lifetime.</summary>
internal sealed class StartupContext : IDisposable
{
    public required AppPaths Paths { get; init; }

    public required FileAppLogger Logger { get; init; }

    public required DatabaseInitializationResult Database { get; init; }

    public void Dispose() => Logger.Dispose();
}
