using FocusKey.Foundation.Data;
using Microsoft.Data.Sqlite;

namespace FocusKey.Foundation.Settings;

public sealed class SqliteSettingsRepository(SqliteConnectionFactory connections) : ISettingsRepository
{
    private readonly SqliteConnectionFactory _connections = connections ?? throw new ArgumentNullException(nameof(connections));

    public async Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT work_duration_seconds, break_duration_seconds, appearance, work_color, break_color
            FROM application_settings WHERE singleton = 1;
            """;
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            throw new InvalidDataException("The authoritative application settings record is missing.");
        try
        {
            var settings = new ApplicationSettings
            {
                WorkDuration = TimeSpan.FromSeconds(reader.GetInt64(0)),
                BreakDuration = TimeSpan.FromSeconds(reader.GetInt64(1)),
                Appearance = AppearanceText.Parse(reader.GetString(2)),
                WorkColor = ParsePersistedColor(reader.GetString(3), "work_color"),
                BreakColor = ParsePersistedColor(reader.GetString(4), "break_color"),
            };
            settings.Validate();
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("More than one authoritative application settings record exists.");
            return settings;
        }
        catch (InvalidDataException) { throw; }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or InvalidCastException)
        {
            throw new InvalidDataException("Persisted application settings are invalid; no defaults were substituted.", exception);
        }
    }

    public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        await using SqliteConnection connection = await _connections.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = connection.BeginTransaction(deferred: false);
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE application_settings SET
                work_duration_seconds = $work,
                break_duration_seconds = $break,
                appearance = $appearance,
                work_color = $workColor,
                break_color = $breakColor
            WHERE singleton = 1;
            """;
        command.Parameters.AddWithValue("$work", checked((long)settings.WorkDuration.TotalSeconds));
        command.Parameters.AddWithValue("$break", checked((long)settings.BreakDuration.TotalSeconds));
        command.Parameters.AddWithValue("$appearance", AppearanceText.Format(settings.Appearance));
        command.Parameters.AddWithValue("$workColor", settings.WorkColor.Value);
        command.Parameters.AddWithValue("$breakColor", settings.BreakColor.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException("The authoritative application settings record is missing.");
        transaction.Commit();
    }

    private static HexColor ParsePersistedColor(string value, string column)
    {
        if (!HexColor.TryParse(value, out HexColor color) || color.Value != value)
            throw new InvalidDataException($"Persisted {column} '{value}' is not canonical #RRGGBB.");
        return color;
    }
}
