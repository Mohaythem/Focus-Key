namespace FocusKey.Foundation.Reports;

/// <summary>UI-thread navigation and refresh ordering. Hidden reports never query persistence.</summary>
public sealed class ReportsController(Func<ReportPeriod, DateOnly, CancellationToken, Task<ReportsSnapshot>> read,
    Func<DateOnly> currentDate, Action<Exception> report) : IDisposable
{
    private bool _visible, _disposed;
    private int _generation;
    private bool _followCurrent = true;
    public ReportPeriod Period { get; private set; } = ReportPeriod.Weekly;
    public DateOnly Date { get; private set; } = currentDate();
    public ReportsSnapshot? Snapshot { get; private set; }
    public bool IsRefreshing { get; private set; }
    public string? Error { get; private set; }
    public event Action? Changed;

    public Task OpenAsync() { if (_disposed) return Task.CompletedTask; _visible = true; return RefreshAsync(); }
    public void Hide() { _visible = false; ++_generation; IsRefreshing = false; }
    public Task SelectAsync(ReportPeriod period, DateOnly date)
    {
        if (_disposed) return Task.CompletedTask;
        ReportRange.For(period, date);
        Period = period; Date = date; _followCurrent = false;
        return RefreshAsync();
    }
    public Task CurrentAsync() { _followCurrent = true; return RefreshAsync(); }
    public Task MoveAsync(int direction)
    {
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var range = ReportRange.For(Period, Date);
        DateOnly next;
        try { next = Period == ReportPeriod.Monthly ? range.Start.AddMonths(direction) :
            range.Start.AddDays(direction * (Period == ReportPeriod.Weekly ? 7 : 1)); }
        catch (ArgumentOutOfRangeException) { return Task.CompletedTask; }
        if (next < ReportRange.MinimumDate || next > ReportRange.MaximumDate) return Task.CompletedTask;
        return SelectAsync(Period, next);
    }
    public async Task RefreshAsync()
    {
        if (!_visible || _disposed) return;
        if (_followCurrent) Date = currentDate();
        int generation = ++_generation;
        IsRefreshing = true; Error = null; Snapshot = null;
        Changed?.Invoke();
        try
        {
            var snapshot = await read(Period, Date, CancellationToken.None);
            if (generation == _generation && !_disposed) Snapshot = snapshot;
        }
        catch (Exception exception)
        {
            if (generation == _generation && !_disposed) Error = "Could not load Reports. Try Refresh again.";
            report(exception);
        }
        finally
        {
            if (generation == _generation && !_disposed) { IsRefreshing = false; Changed?.Invoke(); }
        }
    }
    public void Dispose() { Hide(); _disposed = true; Changed = null; }
}
