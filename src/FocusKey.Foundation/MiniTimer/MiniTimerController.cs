using System.Globalization;
using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.MiniTimer;

/// <summary>UI-thread read-only projection. Display ticks never read or write persistence.</summary>
public sealed class MiniTimerController(Func<CancellationToken, Task<SessionSnapshot?>> read,
    Action<Exception> report, TimeProvider? timeProvider = null) : IDisposable
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private int _generation;
    private bool _disposed;
    public bool IsVisible { get; private set; }
    public SessionSnapshot? Session { get; private set; }
    public string? Error { get; private set; }
    public bool IsLoading { get; private set; }
    public event Action? Changed;
    public TimeSpan Remaining => RemainingAt(Session, _time.GetUtcNow());
    public static TimeSpan RemainingAt(SessionSnapshot? session, DateTimeOffset now) => session is not null
        ? (session.IsPaused ? session.Remaining : TimeSpan.FromTicks(Math.Max(0, (session.PlannedEndAt - now).Ticks))) : TimeSpan.Zero;
    public string Text => Error ?? (IsLoading ? "Loading…" : Session is { } session
        ? $"{session.Type} · {Format(Remaining)}" : "No active session");
    public static string Format(TimeSpan remaining)
    {
        long seconds = Math.Max(0, (long)Math.Ceiling(remaining.TotalSeconds));
        return string.Create(CultureInfo.InvariantCulture, $"{seconds / 60:00}:{seconds % 60:00}");
    }
    public async Task OpenAsync()
    {
        if (_disposed) return;
        IsVisible = true;
        await RefreshAsync();
    }
    public async Task RefreshAsync()
    {
        if (_disposed || !IsVisible) return;
        int generation = ++_generation;
        IsLoading = true;
        Changed?.Invoke();
        try
        {
            var session = await read(CancellationToken.None);
            if (_disposed || generation != _generation) return;
            Session = session;
            Error = null;
        }
        catch (Exception exception)
        {
            if (!_disposed && generation == _generation)
            {
                Session = null;
                Error = "Unavailable — reopen to retry";
                report(exception);
            }
        }
        finally
        {
            if (!_disposed && generation == _generation) { IsLoading = false; Changed?.Invoke(); }
        }
    }
    public void Hide()
    {
        IsVisible = false;
        ++_generation;
        IsLoading = false;
        Session = null;
        Changed?.Invoke();
    }
    public void Dispose() { if (_disposed) return; _disposed = true; Hide(); Changed = null; }
}
