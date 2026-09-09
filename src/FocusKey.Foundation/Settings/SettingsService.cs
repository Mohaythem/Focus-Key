namespace FocusKey.Foundation.Settings;

/// <summary>Application boundary for validated whole-record and individual settings updates.</summary>
public sealed class SettingsService(ISettingsRepository repository)
{
    private readonly ISettingsRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly SemaphoreSlim _updates = new(1, 1);

    public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) =>
        _repository.LoadAsync(cancellationToken);

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        await _updates.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await _repository.SaveAsync(settings, cancellationToken).ConfigureAwait(false); }
        finally { _updates.Release(); }
    }

    public Task<ApplicationSettings> UpdateWorkDurationAsync(TimeSpan value, CancellationToken cancellationToken = default) =>
        UpdateAsync(current => current with { WorkDuration = value }, cancellationToken);
    public Task<ApplicationSettings> UpdateBreakDurationAsync(TimeSpan value, CancellationToken cancellationToken = default) =>
        UpdateAsync(current => current with { BreakDuration = value }, cancellationToken);
    public Task<ApplicationSettings> UpdateAppearanceAsync(Appearance value, CancellationToken cancellationToken = default) =>
        UpdateAsync(current => current with { Appearance = value }, cancellationToken);
    public Task<ApplicationSettings> UpdateWorkColorAsync(HexColor value, CancellationToken cancellationToken = default) =>
        UpdateAsync(current => current with { WorkColor = value }, cancellationToken);
    public Task<ApplicationSettings> UpdateBreakColorAsync(HexColor value, CancellationToken cancellationToken = default) =>
        UpdateAsync(current => current with { BreakColor = value }, cancellationToken);
    public Task<ApplicationSettings> UpdateLightThemeAsync(ThemeConfiguration value, CancellationToken cancellationToken = default) =>
        UpdateAsync(current => current with { LightTheme = value }, cancellationToken);
    public Task<ApplicationSettings> UpdateDarkThemeAsync(ThemeConfiguration value, CancellationToken cancellationToken = default) =>
        UpdateAsync(current => current with { DarkTheme = value }, cancellationToken);

    private async Task<ApplicationSettings> UpdateAsync(
        Func<ApplicationSettings, ApplicationSettings> change, CancellationToken cancellationToken)
    {
        await _updates.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ApplicationSettings updated = change(await _repository.LoadAsync(cancellationToken).ConfigureAwait(false));
            updated.Validate();
            await _repository.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }
        finally { _updates.Release(); }
    }
}
