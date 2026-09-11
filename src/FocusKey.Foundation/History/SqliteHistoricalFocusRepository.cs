using System.Globalization;
using FocusKey.Foundation.Data;
using Microsoft.Data.Sqlite;

namespace FocusKey.Foundation.History;

public sealed class SqliteHistoricalFocusRepository(SqliteConnectionFactory connections) : IHistoricalFocusRepository
{
    private readonly SqliteConnectionFactory _connections = connections ?? throw new ArgumentNullException(nameof(connections));

    public async Task<IReadOnlyList<HistoricalFocusRecord>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, date, project, duration_seconds, source_hours, imported_at_utc
            FROM historical_focus
            ORDER BY date ASC, project ASC;
            """;

        var list = new List<HistoricalFocusRecord>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadRecord(reader));
        }

        return list.AsReadOnly();
    }

    public async Task<IReadOnlyList<HistoricalFocusRecord>> GetBetweenAsync(DateOnly start, DateOnly end, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, date, project, duration_seconds, source_hours, imported_at_utc
            FROM historical_focus
            WHERE date >= $start AND date < $end
            ORDER BY date ASC, project ASC;
            """;
        command.Parameters.AddWithValue("$start", start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$end", end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var list = new List<HistoricalFocusRecord>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            list.Add(ReadRecord(reader));
        }

        return list.AsReadOnly();
    }

    public async Task<HistoricalFocusRecord?> GetAsync(DateOnly date, string project, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id, date, project, duration_seconds, source_hours, imported_at_utc
            FROM historical_focus
            WHERE date = $date AND project = $project;
            """;
        command.Parameters.AddWithValue("$date", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$project", project);

        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return ReadRecord(reader);
        }

        return null;
    }

    public async Task<CsvImportResult> ImportAsync(
        IEnumerable<HistoricalFocusEntry> entries,
        int totalRowsFound,
        int invalidRows,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        await using SqliteConnection connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);

        int newRecords = 0;
        int updatedRecords = 0;
        int duplicateRecords = 0;
        double totalHours = 0.0;
        string nowUtc = UtcTimestamp.Format(DateTimeOffset.UtcNow);

        try
        {
            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string dateStr = entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                long durationSeconds = (long)entry.Duration.TotalSeconds;
                double hours = entry.Hours;
                totalHours += entry.Duration.TotalHours;

                // Check existing record
                await using var checkCmd = connection.CreateCommand();
                checkCmd.Transaction = transaction;
                checkCmd.CommandText =
                    """
                    SELECT duration_seconds, source_hours FROM historical_focus
                    WHERE date = $date AND project = $project;
                    """;
                checkCmd.Parameters.AddWithValue("$date", dateStr);
                checkCmd.Parameters.AddWithValue("$project", entry.Project);

                await using var reader = await checkCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    await reader.CloseAsync().ConfigureAwait(false);
                    // New record
                    await using var insertCmd = connection.CreateCommand();
                    insertCmd.Transaction = transaction;
                    insertCmd.CommandText =
                        """
                        INSERT INTO historical_focus (date, project, duration_seconds, source_hours, imported_at_utc)
                        VALUES ($date, $project, $duration, $hours, $now);
                        """;
                    insertCmd.Parameters.AddWithValue("$date", dateStr);
                    insertCmd.Parameters.AddWithValue("$project", entry.Project);
                    insertCmd.Parameters.AddWithValue("$duration", durationSeconds);
                    insertCmd.Parameters.AddWithValue("$hours", hours);
                    insertCmd.Parameters.AddWithValue("$now", nowUtc);
                    await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    newRecords++;
                }
                else
                {
                    long existingSeconds = reader.GetInt64(0);
                    await reader.CloseAsync().ConfigureAwait(false);

                    if (existingSeconds == durationSeconds || Math.Abs(existingSeconds - durationSeconds) <= 1)
                    {
                        // Duplicate identical record — no change needed
                        duplicateRecords++;
                    }
                    else
                    {
                        // Updated record with new duration value
                        await using var updateCmd = connection.CreateCommand();
                        updateCmd.Transaction = transaction;
                        updateCmd.CommandText =
                            """
                            UPDATE historical_focus
                            SET duration_seconds = $duration, source_hours = $hours, imported_at_utc = $now
                            WHERE date = $date AND project = $project;
                            """;
                        updateCmd.Parameters.AddWithValue("$duration", durationSeconds);
                        updateCmd.Parameters.AddWithValue("$hours", hours);
                        updateCmd.Parameters.AddWithValue("$now", nowUtc);
                        updateCmd.Parameters.AddWithValue("$date", dateStr);
                        updateCmd.Parameters.AddWithValue("$project", entry.Project);
                        await updateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        updatedRecords++;
                    }
                }
            }

            transaction.Commit();
            return new CsvImportResult(
                Success: true,
                RowsFound: totalRowsFound,
                NewRecords: newRecords,
                UpdatedRecords: updatedRecords,
                DuplicateRecords: duplicateRecords,
                InvalidRows: invalidRows,
                TotalImportedHours: Math.Round(totalHours, 2));
        }
        catch (Exception exception)
        {
            try { transaction.Rollback(); } catch { }
            return new CsvImportResult(
                Success: false,
                RowsFound: totalRowsFound,
                NewRecords: 0,
                UpdatedRecords: 0,
                DuplicateRecords: 0,
                InvalidRows: invalidRows,
                TotalImportedHours: 0,
                ErrorMessage: exception.Message);
        }
    }

    private static HistoricalFocusRecord ReadRecord(SqliteDataReader reader)
    {
        long id = reader.GetInt64(0);
        string dateStr = reader.GetString(1);
        DateOnly date = DateOnly.ParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        string project = reader.GetString(2);
        TimeSpan duration = TimeSpan.FromSeconds(reader.GetInt64(3));
        double sourceHours = reader.GetDouble(4);
        DateTimeOffset importedAt = UtcTimestamp.Parse(reader.GetString(5));

        return new HistoricalFocusRecord(id, date, project, duration, sourceHours, importedAt);
    }
}
