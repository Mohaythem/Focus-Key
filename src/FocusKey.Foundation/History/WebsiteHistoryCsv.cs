using System.Globalization;
using System.Text;

namespace FocusKey.Foundation.History;

public sealed record CsvParseResult(
    bool Success,
    IReadOnlyList<HistoricalFocusEntry> ValidEntries,
    int TotalRowsFound,
    int InvalidRows,
    string? ErrorMessage = null);

public sealed record DailyFocusExportRecord(DateOnly Date, string Project, double Hours);

/// <summary>
/// Handles import parsing and export serialization for the authoritative website CSV format.
/// The website CSV format is TAB-delimited with headers: date, project, hours.
/// </summary>
public static class WebsiteHistoryCsv
{
    public const string Header = "date\tproject\thours";

    /// <summary>
    /// Parses historical focus data from the website CSV format.
    /// Performs complete validation of headers, delimiters, date format (yyyyMMdd),
    /// decimal hours, and duplicate consolidation within the file.
    /// </summary>
    public static CsvParseResult Parse(string csvContent)
    {
        ArgumentNullException.ThrowIfNull(csvContent);

        // Strip UTF-8 BOM if present
        if (csvContent.StartsWith('\uFEFF'))
        {
            csvContent = csvContent[1..];
        }

        string[] lines = csvContent.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
        int lineIndex = 0;

        // Skip leading blank lines
        while (lineIndex < lines.Length && string.IsNullOrWhiteSpace(lines[lineIndex]))
        {
            lineIndex++;
        }

        if (lineIndex >= lines.Length)
        {
            return new CsvParseResult(false, [], 0, 0, "The CSV file is empty.");
        }

        // Validate header
        string headerLine = lines[lineIndex].TrimEnd();
        lineIndex++;

        string[] headerCols = headerLine.Split('\t');
        if (headerCols.Length != 3 ||
            !headerCols[0].Trim().Equals("date", StringComparison.OrdinalIgnoreCase) ||
            !headerCols[1].Trim().Equals("project", StringComparison.OrdinalIgnoreCase) ||
            !headerCols[2].Trim().Equals("hours", StringComparison.OrdinalIgnoreCase))
        {
            return new CsvParseResult(false, [], 0, 0,
                "Invalid CSV header. Expected tab-delimited columns: 'date\tproject\thours'.");
        }

        int totalRowsFound = 0;
        int invalidRows = 0;
        // Group and consolidate duplicate rows within the same file by (date, project)
        var entriesByDayProject = new Dictionary<(DateOnly Date, string Project), double>();

        for (; lineIndex < lines.Length; lineIndex++)
        {
            string rawLine = lines[lineIndex];
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue; // Ignore blank lines
            }

            totalRowsFound++;
            string[] cols = rawLine.Split('\t');
            if (cols.Length != 3)
            {
                invalidRows++;
                continue;
            }

            // 1. Date: yyyyMMdd
            string dateStr = cols[0].Trim();
            if (dateStr.Length != 8 ||
                !DateTime.TryParseExact(dateStr, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
            {
                invalidRows++;
                continue;
            }
            DateOnly date = DateOnly.FromDateTime(dt);

            // 2. Project: clean quotes and whitespace
            string project = cols[1].Trim();
            if (project.StartsWith('"') && project.EndsWith('"') && project.Length >= 2)
            {
                project = project[1..^1].Trim();
            }

            // 3. Hours: decimal hours using invariant .
            string hoursStr = cols[2].Trim();
            if (!double.TryParse(hoursStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double hours) ||
                double.IsNaN(hours) || double.IsInfinity(hours) || hours <= 0 || hours > 24.0)
            {
                invalidRows++;
                continue;
            }

            hours = Math.Round(hours, 2);
            var key = (date, project);
            if (entriesByDayProject.TryGetValue(key, out double existingHours))
            {
                // Consolidate intra-file duplicate rows by summing hours up to 24h
                entriesByDayProject[key] = Math.Min(24.0, Math.Round(existingHours + hours, 2));
            }
            else
            {
                entriesByDayProject[key] = hours;
            }
        }

        var validList = entriesByDayProject
            .Select(kvp => new HistoricalFocusEntry(kvp.Key.Date, kvp.Key.Project, kvp.Value))
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Project)
            .ToList();

        return new CsvParseResult(true, validList.AsReadOnly(), totalRowsFound, invalidRows);
    }

    /// <summary>
    /// Serializes historical and native focus records into the website CSV format.
    /// Uses TAB delimiter, yyyyMMdd date format, quoted project name or empty quotes (""),
    /// and invariant decimal hours.
    /// </summary>
    public static string Export(IEnumerable<DailyFocusExportRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var sb = new StringBuilder();
        sb.AppendLine(Header);

        foreach (var record in records.OrderBy(r => r.Date).ThenBy(r => r.Project))
        {
            if (record.Hours <= 0) continue;

            string dateStr = record.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string projectStr = string.IsNullOrEmpty(record.Project) ? "\"\"" : $"\"{record.Project}\"";
            string hoursStr = record.Hours.ToString("0.##", CultureInfo.InvariantCulture);

            sb.Append(dateStr).Append('\t').Append(projectStr).Append('\t').AppendLine(hoursStr);
        }

        return sb.ToString();
    }
}
