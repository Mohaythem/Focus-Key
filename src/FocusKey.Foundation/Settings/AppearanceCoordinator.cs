namespace FocusKey.Foundation.Settings;

/// <summary>
/// Owns the process-wide runtime appearance value while keeping the persisted settings record as
/// its source of truth. Refresh is explicit and updates are serialized; no database polling occurs.
/// </summary>
public sealed class AppearanceCoordinator(SettingsService settings)
{
    private readonly SettingsService _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _state = new();
    private Appearance? _current;

    public event Action<Appearance>? Changed;

    public Appearance Current
    {
        get
        {
            lock (_state)
                return _current ?? throw new InvalidOperationException("Initialize appearance before reading it.");
        }
    }

    public Task<Appearance> InitializeAsync(CancellationToken cancellationToken = default) =>
        RefreshAsync(cancellationToken);

    public async Task<Appearance> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ApplicationSettings current = await _settings.LoadAsync(cancellationToken).ConfigureAwait(false);
            return PublishIfChanged(current.Appearance);
        }
        finally { _operations.Release(); }
    }

    public async Task<Appearance> UpdateAsync(
        Appearance appearance,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(appearance))
            throw new ArgumentOutOfRangeException(nameof(appearance), appearance, "Unsupported appearance.");
        await _operations.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ApplicationSettings updated = await _settings.UpdateAppearanceAsync(appearance, cancellationToken)
                .ConfigureAwait(false);
            return PublishIfChanged(updated.Appearance);
        }
        finally { _operations.Release(); }
    }

    private Appearance PublishIfChanged(Appearance appearance)
    {
        bool changed;
        lock (_state)
        {
            changed = _current != appearance;
            _current = appearance;
        }
        if (changed) Changed?.Invoke(appearance);
        return appearance;
    }
}
