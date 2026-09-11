using System.Globalization;
using System.Text;

namespace FocusKey.Foundation.History;

public enum CsvHistorySchema
{
    Hours,
    Minutes
}

public sealed record CsvParseResult(
    bool Success,
    IReadOnlyList<HistoricalFocusEntry> ValidEntries,
    int TotalRowsFound,
    int InvalidRows,
    string? ErrorMessage = null,
    CsvHistorySchema? DetectedSchema = null);

public sealed record DailyFocusExportRecord(DateOnly Date, string Project, TimeSpan Duration)
{
    public DailyFocusExportRecord(DateOnly date, string project, double hours)
        : this(date, project, TimeSpan.FromSeconds((long)Math.Round(hours * 3600.0)))
    {
    }

    public long TotalMinutes => (long)Math.Round(Duration.TotalSeconds / 60.0);
    public double Hours => Math.Round(Duration.TotalHours, 2);
}

/// <summary>
/// Handles import parsing and export serialization for the authoritative website CSV formats.
/// Supports both Schema A (hours: "date\tproject\thours") and Schema B (minutes: "date\tproject\tminutes").
/// Canonical export schema is whole integer minutes ("date\tproject\tminutes").
/// </summary>
public static class WebsiteHistoryCsv
{
    public const string Header = "date\tproject\tminutes";
    public const string HoursHeader = "date\tproject\thours";
    public const string MinutesHeader = "date\tproject\tminutes";

    /// <summary>
    /// Parses historical focus data from either website CSV format (hours or minutes).
    /// Performs complete validation of headers, delimiters, date format (yyyyMMdd),
    /// numeric values, and duplicate consolidation within the file.
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
            !headerCols[1].Trim().Equals("project", StringComparison.OrdinalIgnoreCase))
        {
            return new CsvParseResult(false, [], 0, 0,
                "Invalid CSV header. Expected tab-delimited columns: 'date\tproject\thours' or 'date\tproject\tminutes'.");
        }

        CsvHistorySchema schema;
        string unitCol = headerCols[2].Trim();
        if (unitCol.Equals("hours", StringComparison.OrdinalIgnoreCase))
        {
            schema = CsvHistorySchema.Hours;
        }
        else if (unitCol.Equals("minutes", StringComparison.OrdinalIgnoreCase))
        {
            schema = CsvHistorySchema.Minutes;
        }
        else
        {
            return new CsvParseResult(false, [], 0, 0,
                "Invalid CSV header. Expected tab-delimited columns: 'date\tproject\thours' or 'date\tproject\tminutes'.");
        }

        int totalRowsFound = 0;
        int invalidRows = 0;
        // Group and consolidate duplicate rows within the same file by (date, project)
        var durationByDayProject = new Dictionary<(DateOnly Date, string Project), TimeSpan>();

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

            // 3. Duration: hours or minutes
            string valStr = cols[2].Trim();
            TimeSpan duration;
            if (schema == CsvHistorySchema.Minutes)
            {
                if (!double.TryParse(valStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double minutes) ||
                    double.IsNaN(minutes) || double.IsInfinity(minutes) || minutes <= 0 || minutes > 1440.0)
                {
                    invalidRows++;
                    continue;
                }
                long seconds = (long)Math.Round(minutes * 60.0);
                duration = TimeSpan.FromSeconds(seconds);
            }
            else
            {
                if (!double.TryParse(valStr, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double hours) ||
                    double.IsNaN(hours) || double.IsInfinity(hours) || hours <= 0 || hours > 24.0)
                {
                    invalidRows++;
                    continue;
                }
                long seconds = (long)Math.Round(hours * 3600.0);
                duration = TimeSpan.FromSeconds(seconds);
            }

            var key = (date, project);
            if (durationByDayProject.TryGetValue(key, out TimeSpan existingDuration))
            {
                // Consolidate intra-file duplicate rows by summing duration up to 24h
                long sumSeconds = Math.Min(86400, (long)existingDuration.TotalSeconds + (long)duration.TotalSeconds);
                durationByDayProject[key] = TimeSpan.FromSeconds(sumSeconds);
            }
            else
            {
                durationByDayProject[key] = duration;
            }
        }

        var validList = durationByDayProject
            .Select(kvp => new HistoricalFocusEntry(
                kvp.Key.Date,
                kvp.Key.Project,
                kvp.Value,
                Math.Round(kvp.Value.TotalHours, 2)))
            .OrderBy(e => e.Date)
            .ThenBy(e => e.Project)
            .ToList();

        return new CsvParseResult(true, validList.AsReadOnly(), totalRowsFound, invalidRows, null, schema);
    }

    /// <summary>
    /// Serializes historical and native focus records into the website CSV format.
    /// Uses TAB delimiter, yyyyMMdd date format, quoted project name or empty quotes (""),
    /// and whole integer minutes (the canonical website export format).
    /// </summary>
    public static string Export(IEnumerable<DailyFocusExportRecord> records)
    {
        ArgumentNullException.ThrowIfNull(records);

        var sb = new StringBuilder();
        sb.AppendLine(MinutesHeader);

        foreach (var record in records.OrderBy(r => r.Date).ThenBy(r => r.Project))
        {
            long minutes = record.TotalMinutes;
            if (minutes <= 0) continue;

            string dateStr = record.Date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            string projectStr = string.IsNullOrEmpty(record.Project) ? "\"\"" : $"\"{record.Project}\"";

            sb.Append(dateStr).Append('\t').Append(projectStr).Append('\t').AppendLine(minutes.ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }
}
