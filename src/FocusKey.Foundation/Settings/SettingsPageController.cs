using System.Globalization;

namespace FocusKey.Foundation.Settings;

public enum SettingsField
{
    WorkDuration, BreakDuration, Appearance, Contrast, WorkColor, BreakColor,
    LightPreset, LightBackground, LightForeground, LightAccent,
    DarkPreset, DarkBackground, DarkForeground, DarkAccent,
    SessionSounds
}

/// <summary>UI-thread auto-save queue. Only committed values are published to runtime.</summary>
public sealed class SettingsPageController(SettingsService settings, Func<Task> refreshRuntime, Action<Exception> report)
{
    private Task _tail = Task.CompletedTask;
    private readonly Dictionary<SettingsField, long> _versions = new();
    private readonly Dictionary<SettingsField, string> _errors = new();
    private int _pending;
    private string? _loadError;
    private bool _runtimeFailed;
    public ApplicationSettings? Saved { get; private set; }
    public bool IsBusy => _pending > 0;
    public bool IsLoading { get; private set; }
    public string Message => _loadError ?? (_errors.Count > 0 ? string.Join(" ", _errors.Values) :
        _runtimeFailed ? "Settings saved, but live apply failed. Reload to retry applying saved values." :
        IsLoading ? "Loading settings…" : IsBusy ? "Saving changes…" :
        "Changes saved automatically. Durations affect future sessions only.");
    public event Action? Changed;
    public event Action<ApplicationSettings>? Loaded;
    public event Action<SettingsField, ApplicationSettings>? Settled;

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        Changed?.Invoke();
        await Enqueue(out var release);
        try
        {
            // Microsoft.Data.Sqlite async methods can execute synchronously; keep disk waits off UI.
            Saved = await Task.Run(() => settings.LoadAsync());
            _loadError = null;
            _errors.Clear();
            await ApplyRuntimeAsync();
            Loaded?.Invoke(Saved);
        }
        catch (Exception exception)
        {
            Saved = null;
            _loadError = "Could not load settings. Reload to retry.";
            report(exception);
        }
        finally { IsLoading = false; release.SetResult(); Changed?.Invoke(); }
    }

    public Task UpdateDurationAsync(bool work, string minutes, string seconds, CancellationToken cancellationToken = default) =>
        ChangeAsync(work ? SettingsField.WorkDuration : SettingsField.BreakDuration,
            () => work ? settings.UpdateWorkDurationAsync(Duration(minutes, seconds)) :
                         settings.UpdateBreakDurationAsync(Duration(minutes, seconds)), cancellationToken);
    public Task UpdateAppearanceAsync(Appearance value, CancellationToken cancellationToken = default) =>
        ChangeAsync(SettingsField.Appearance, () => settings.UpdateAppearanceAsync(value), cancellationToken);
    public Task UpdateContrastAsync(Contrast value, CancellationToken cancellationToken = default) =>
        ChangeAsync(SettingsField.Contrast, () => settings.UpdateContrastAsync(value), cancellationToken);
    public Task UpdateWorkColorAsync(HexColor value, CancellationToken cancellationToken = default) =>
        ChangeAsync(SettingsField.WorkColor, () => settings.UpdateWorkColorAsync(value), cancellationToken);
    public Task UpdateBreakColorAsync(HexColor value, CancellationToken cancellationToken = default) =>
        ChangeAsync(SettingsField.BreakColor, () => settings.UpdateBreakColorAsync(value), cancellationToken);
    public Task UpdateSessionSoundsAsync(bool value, CancellationToken cancellationToken = default) =>
        ChangeAsync(SettingsField.SessionSounds, () => settings.UpdateSessionSoundsEnabledAsync(value), cancellationToken);

    public Task UpdateLightPresetAsync(string presetId, CancellationToken cancellationToken = default)
    {
        var preset = ThemePresets.FindPreset(presetId, false);
        var updated = new ThemeConfiguration
        {
            Preset = preset.Id,
            Background = preset.Palette.Background,
            Foreground = preset.Palette.Foreground,
            Accent = preset.Palette.Accent
        };
        return ChangeAsync(SettingsField.LightPreset, () => settings.UpdateLightThemeAsync(updated), cancellationToken);
    }

    public Task UpdateLightColorAsync(bool isBg, bool isFg, HexColor color, CancellationToken cancellationToken = default)
    {
        var field = isBg ? SettingsField.LightBackground : isFg ? SettingsField.LightForeground : SettingsField.LightAccent;
        return ChangeAsync(field, () =>
        {
            // Build from the latest controller snapshot when the serialized operation runs.
            // This preserves a preceding background/foreground/accent edit queued in the same
            // interaction burst instead of capturing a stale theme before it is persisted.
            var current = Saved?.LightTheme ?? ThemeConfiguration.DefaultLight;
            var bg = isBg ? color : current.Background;
            var fg = isFg ? color : current.Foreground;
            var accent = (!isBg && !isFg) ? color : current.Accent;
            string preset = ThemePresets.DetectPresetId(bg, fg, accent, false);
            var updated = new ThemeConfiguration { Preset = preset, Background = bg, Foreground = fg, Accent = accent };
            return settings.UpdateLightThemeAsync(updated);
        }, cancellationToken);
    }

    public Task UpdateDarkPresetAsync(string presetId, CancellationToken cancellationToken = default)
    {
        var preset = ThemePresets.FindPreset(presetId, true);
        var updated = new ThemeConfiguration
        {
            Preset = preset.Id,
            Background = preset.Palette.Background,
            Foreground = preset.Palette.Foreground,
            Accent = preset.Palette.Accent
        };
        return ChangeAsync(SettingsField.DarkPreset, () => settings.UpdateDarkThemeAsync(updated), cancellationToken);
    }

    public Task UpdateDarkColorAsync(bool isBg, bool isFg, HexColor color, CancellationToken cancellationToken = default)
    {
        var field = isBg ? SettingsField.DarkBackground : isFg ? SettingsField.DarkForeground : SettingsField.DarkAccent;
        return ChangeAsync(field, () =>
        {
            var current = Saved?.DarkTheme ?? ThemeConfiguration.DefaultDark;
            var bg = isBg ? color : current.Background;
            var fg = isFg ? color : current.Foreground;
            var accent = (!isBg && !isFg) ? color : current.Accent;
            string preset = ThemePresets.DetectPresetId(bg, fg, accent, true);
            var updated = new ThemeConfiguration { Preset = preset, Background = bg, Foreground = fg, Accent = accent };
            return settings.UpdateDarkThemeAsync(updated);
        }, cancellationToken);
    }

    private async Task ChangeAsync(SettingsField field, Func<Task<ApplicationSettings>> update, CancellationToken cancellationToken)
    {
        // Once accepted, navigation/exit must drain the write rather than cancel a possible commit.
        cancellationToken.ThrowIfCancellationRequested();
        if (Saved is null || IsLoading) return;
        long version = _versions.GetValueOrDefault(field) + 1;
        _versions[field] = version;
        _pending++;
        Changed?.Invoke();
        await Enqueue(out var release);
        try
        {
            if (_versions[field] != version) return;
            try
            {
                Saved = await Task.Run(update);
                _errors.Remove(field);
                await ApplyRuntimeAsync();
            }
            catch (Exception exception)
            {
                _errors[field] = exception is ArgumentException or OverflowException or FormatException
                    ? $"{field}: invalid value; restored saved value. Use positive whole seconds (seconds 0–59)."
                    : $"{field}: could not save; restored last saved value. Change it again to retry.";
                if (exception is not (ArgumentException or OverflowException or FormatException)) report(exception);
            }
            // Never roll a newer selection back to an older completion.
            if (_versions[field] == version) Settled?.Invoke(field, Saved!);
        }
        finally { _pending--; release.SetResult(); Changed?.Invoke(); }
    }

    private async Task ApplyRuntimeAsync()
    {
        try { await refreshRuntime(); _runtimeFailed = false; }
        catch (Exception exception) { _runtimeFailed = true; report(exception); }
    }

    /// <summary>Wait for accepted updates before graceful application exit.</summary>
    public Task DrainAsync() => _tail;

    private Task Enqueue(out TaskCompletionSource release)
    {
        Task previous = _tail;
        release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _tail = release.Task;
        return previous;
    }

    /// <summary>Normalize decimal digit characters without accepting separators or signs.</summary>
    public static string NormalizeDigits(string text) => string.Concat(text.Select(character =>
    {
        int digit = CharUnicodeInfo.GetDecimalDigitValue(character);
        return digit >= 0 ? (char)('0' + digit) : character;
    }));

    public static TimeSpan Duration(string minutes, string seconds)
    {
        if (!long.TryParse(NormalizeDigits(minutes), NumberStyles.None, CultureInfo.InvariantCulture, out long m) ||
            !int.TryParse(NormalizeDigits(seconds), NumberStyles.None, CultureInfo.InvariantCulture, out int s) || s > 59)
            throw new ArgumentException("Enter whole minutes and seconds between 0 and 59.");
        return TimeSpan.FromTicks(checked(checked(m * 60 + s) * TimeSpan.TicksPerSecond));
    }
}
