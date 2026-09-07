namespace FocusKey.Foundation.Settings;

/// <summary>Persistence boundary for the single durable application settings record.</summary>
public interface ISettingsRepository
{
    Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
