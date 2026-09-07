namespace FocusKey.Foundation.Sessions;

/// <summary>
/// Supplies the durations to use when a new session is created. Implementations may read durable
/// configuration; callers receive one complete snapshot for each operation.
/// </summary>
public interface ISessionDurationProvider
{
    Task<SessionDurations> GetDurationsAsync(CancellationToken cancellationToken = default);
}

internal sealed class FixedSessionDurationProvider(SessionDurations durations) : ISessionDurationProvider
{
    private readonly SessionDurations _durations = durations ?? throw new ArgumentNullException(nameof(durations));

    public Task<SessionDurations> GetDurationsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_durations);
    }
}
