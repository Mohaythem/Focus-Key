namespace FocusKey.Foundation.History;

public sealed record HistoricalFocusEntry(DateOnly Date, string Project, double Hours);

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
