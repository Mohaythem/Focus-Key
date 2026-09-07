using System.Globalization;

namespace FocusKey.Foundation.Settings;

/// <summary>UI-thread settings workflow. Persistence succeeds before runtime refresh is attempted.</summary>
public sealed class SettingsPageController(SettingsService settings, Func<Task> refreshRuntime, Action<Exception> report)
{
    public ApplicationSettings? Saved { get; private set; }
    public bool IsBusy { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public event Action? Changed;

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        Saved = null;
        Message = "Loading settings…";
        Changed?.Invoke();
        try { Saved = await settings.LoadAsync(); Message = "Values loaded from this device."; }
        catch (Exception exception) { Message = "Could not load settings. Reload to retry."; report(exception); }
        finally { IsBusy = false; Changed?.Invoke(); }
    }

    public async Task SaveAsync(Func<ApplicationSettings> readDraft)
    {
        if (IsBusy || Saved is null) return;
        ApplicationSettings draft;
        try { draft = readDraft(); draft.Validate(); }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or FormatException)
        {
            Message = "Check your input. Durations must be positive whole seconds; minutes must be a whole number and seconds 0–59.";
            Changed?.Invoke();
            return;
        }
        IsBusy = true;
        Message = "Saving…";
        Changed?.Invoke();
        try
        {
            await settings.SaveAsync(draft);
            Saved = draft;
        }
        catch (Exception exception)
        {
            Message = "Could not save settings. Your edits are retained. Retry Save or reload the saved values.";
            report(exception);
            IsBusy = false;
            Changed?.Invoke();
            return;
        }
        try { await refreshRuntime(); Message = "Settings saved and applied. Durations affect future sessions only."; }
        catch (Exception exception)
        {
            Message = "Settings saved, but runtime refresh failed. Save again to retry applying them.";
            report(exception);
        }
        finally { IsBusy = false; Changed?.Invoke(); }
    }

    public static TimeSpan Duration(string minutes, string seconds)
    {
        if (!long.TryParse(minutes, NumberStyles.None, CultureInfo.InvariantCulture, out long m) ||
            !int.TryParse(seconds, NumberStyles.None, CultureInfo.InvariantCulture, out int s) || s > 59)
            throw new ArgumentException("Enter whole minutes and seconds between 0 and 59.");
        return TimeSpan.FromTicks(checked(checked(m * 60 + s) * TimeSpan.TicksPerSecond));
    }
}
