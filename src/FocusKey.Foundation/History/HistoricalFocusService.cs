using System.Globalization;
using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.History;

public sealed class HistoricalFocusService(
    IHistoricalFocusRepository historyRepository,
    ISessionRepository sessionRepository,
    Func<TimeZoneInfo>? localTimeZone = null)
{
    private readonly IHistoricalFocusRepository _history = historyRepository ?? throw new ArgumentNullException(nameof(historyRepository));
    private readonly ISessionRepository _sessions = sessionRepository ?? throw new ArgumentNullException(nameof(sessionRepository));
    private readonly Func<TimeZoneInfo> _zone = localTimeZone ?? (() => TimeZoneInfo.Local);

    public event Action? HistoryChanged;

    public async Task<CsvImportResult> ImportWebsiteCsvAsync(string csvContent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(csvContent);

        var parseResult = WebsiteHistoryCsv.Parse(csvContent);
        if (!parseResult.Success)
        {
            return new CsvImportResult(
                Success: false,
                RowsFound: parseResult.TotalRowsFound,
                NewRecords: 0,
                UpdatedRecords: 0,
                DuplicateRecords: 0,
                InvalidRows: parseResult.InvalidRows,
                TotalImportedHours: 0,
                ErrorMessage: parseResult.ErrorMessage);
        }

        var result = await _history.ImportAsync(
            parseResult.ValidEntries,
            parseResult.TotalRowsFound,
            parseResult.InvalidRows,
            parseResult.DuplicateRows,
            cancellationToken).ConfigureAwait(false);

        if (result.Success && (result.NewRecords > 0 || result.UpdatedRecords > 0))
        {
            HistoryChanged?.Invoke();
        }

        return result;
    }

    public async Task<string> ExportWebsiteCsvAsync(CancellationToken cancellationToken = default)
    {
        TimeZoneInfo zone = _zone();

        // 1. Fetch all native sessions
        var sessions = await _sessions.GetStartedBetweenAsync(
            DateTimeOffset.MinValue,
            DateTimeOffset.MaxValue,
            cancellationToken).ConfigureAwait(false);

        // Group native completed work sessions by local calendar date
        var nativeFocusByDate = sessions
            .Where(s => s.Type == SessionType.Work && s.Status == SessionStatus.Completed)
            .GroupBy(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.StartedAt, zone).DateTime))
            .ToDictionary(
                g => g.Key,
                g => g.Aggregate(TimeSpan.Zero, (acc, s) => acc + (s.ActualDuration ?? TimeSpan.Zero)));

        // 2. Fetch all historical focus records
        var historical = await _history.GetAllAsync(cancellationToken).ConfigureAwait(false);

        // 3. Merge: key is (Date, Project)
        var exportDict = new Dictionary<(DateOnly Date, string Project), TimeSpan>();

        // Add imported records first
        foreach (var rec in historical)
        {
            var key = (rec.Date, rec.Project);
            exportDict[key] = rec.Duration;
        }

        // Add native focus records (native sessions have empty project "")
        foreach (var kvp in nativeFocusByDate)
        {
            var key = (kvp.Key, "");
            TimeSpan nativeDuration = kvp.Value;

            if (exportDict.TryGetValue(key, out TimeSpan existingDuration))
            {
                // Both imported history and native sessions exist for this day with empty project
                exportDict[key] = existingDuration + nativeDuration;
            }
            else
            {
                exportDict[key] = nativeDuration;
            }
        }

        var exportList = exportDict
            .Select(kvp => new DailyFocusExportRecord(kvp.Key.Date, kvp.Key.Project, kvp.Value))
            .ToList();

        return WebsiteHistoryCsv.Export(exportList);
    }
}
