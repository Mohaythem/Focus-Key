using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Settings;

/// <summary>Maps the authoritative persisted application settings to session runtime durations.</summary>
public sealed class SettingsSessionDurationProvider(SettingsService settings) : ISessionDurationProvider
{
    private readonly SettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public async Task<SessionDurations> GetDurationsAsync(CancellationToken cancellationToken = default)
    {
        ApplicationSettings current = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
        return new SessionDurations(current.WorkDuration, current.BreakDuration);
    }
}
