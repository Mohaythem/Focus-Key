namespace FocusKey.Foundation.History;

public sealed record HistoricalFocusEntry(DateOnly Date, string Project, TimeSpan Duration, double Hours)
{
    public HistoricalFocusEntry(DateOnly date, string project, double hours)
        : this(date, project, TimeSpan.FromSeconds((long)Math.Round(hours * 3600.0)), hours)
    {
    }

    public static HistoricalFocusEntry FromHours(DateOnly date, string project, double hours) =>
        new(date, project, TimeSpan.FromSeconds((long)Math.Round(hours * 3600.0)), hours);

    public static HistoricalFocusEntry FromMinutes(DateOnly date, string project, double minutes)
    {
        long seconds = (long)Math.Round(minutes * 60.0);
        return new(date, project, TimeSpan.FromSeconds(seconds), Math.Round(minutes / 60.0, 2));
    }
}

public sealed record CsvImportResult(
    bool Success,
    int RowsFound,
    int NewRecords,
    int UpdatedRecords,
    int DuplicateRecords,
    int InvalidRows,
    double TotalImportedHours,
    string? ErrorMessage = null);

public interface IHistoricalFocusRepository
{
    Task<IReadOnlyList<HistoricalFocusRecord>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HistoricalFocusRecord>> GetBetweenAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default);
    Task<HistoricalFocusRecord?> GetAsync(DateOnly date, string project, CancellationToken cancellationToken = default);
    Task<CsvImportResult> ImportAsync(IEnumerable<HistoricalFocusEntry> entries, int totalRowsFound, int invalidRows, CancellationToken cancellationToken = default);
}
