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
            var workDuration = TimeSpan.FromSeconds(reader.GetInt64(0));
            var breakDuration = TimeSpan.FromSeconds(reader.GetInt64(1));
            var appearance = AppearanceText.Parse(reader.GetString(2));
            var workColor = ParsePersistedColor(reader.GetString(3), "work_color");
            var breakColor = ParsePersistedColor(reader.GetString(4), "break_color");
            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                throw new InvalidDataException("More than one authoritative application settings record exists.");
            await reader.CloseAsync().ConfigureAwait(false);

            var (lightTheme, darkTheme, contrast) = await LoadThemeSettingsAsync(connection, cancellationToken).ConfigureAwait(false);

            var settings = new ApplicationSettings
            {
                WorkDuration = workDuration,
                BreakDuration = breakDuration,
                Appearance = appearance,
                Contrast = contrast,
                WorkColor = workColor,
                BreakColor = breakColor,
                LightTheme = lightTheme,
                DarkTheme = darkTheme,
            };
            settings.Validate();
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
        await EnsureThemeTableAsync(connection, cancellationToken).ConfigureAwait(false);
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
        var light = settings.LightTheme ?? ThemeConfiguration.DefaultLight;
        var dark = settings.DarkTheme ?? ThemeConfiguration.DefaultDark;
        command.Parameters.AddWithValue("$work", checked((long)settings.WorkDuration.TotalSeconds));
        command.Parameters.AddWithValue("$break", checked((long)settings.BreakDuration.TotalSeconds));
        command.Parameters.AddWithValue("$appearance", AppearanceText.Format(settings.Appearance));
        command.Parameters.AddWithValue("$workColor", settings.WorkColor.Value);
        command.Parameters.AddWithValue("$breakColor", settings.BreakColor.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
            throw new InvalidDataException("The authoritative application settings record is missing.");

        command.CommandText =
            """
            UPDATE theme_settings SET
                light_preset = $lightPreset,
                light_background = $lightBg,
                light_foreground = $lightFg,
                light_accent = $lightAccent,
                dark_preset = $darkPreset,
                dark_background = $darkBg,
                dark_foreground = $darkFg,
                dark_accent = $darkAccent,
                contrast = $contrast
            WHERE singleton = 1;
            """;
        command.Parameters.AddWithValue("$lightPreset", light.Preset);
        command.Parameters.AddWithValue("$lightBg", light.Background.Value);
        command.Parameters.AddWithValue("$lightFg", light.Foreground.Value);
        command.Parameters.AddWithValue("$lightAccent", light.Accent.Value);
        command.Parameters.AddWithValue("$darkPreset", dark.Preset);
        command.Parameters.AddWithValue("$darkBg", dark.Background.Value);
        command.Parameters.AddWithValue("$darkFg", dark.Foreground.Value);
        command.Parameters.AddWithValue("$darkAccent", dark.Accent.Value);
        command.Parameters.AddWithValue("$contrast", ContrastText.Format(settings.Contrast));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        transaction.Commit();
    }

    private static async Task<(ThemeConfiguration light, ThemeConfiguration dark, Contrast contrast)> LoadThemeSettingsAsync(
        SqliteConnection connection, CancellationToken cancellationToken)
    {
        await EnsureThemeTableAsync(connection, cancellationToken).ConfigureAwait(false);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            """
            SELECT light_preset, light_background, light_foreground, light_accent,
                   dark_preset, dark_background, dark_foreground, dark_accent,
                   contrast
            FROM theme_settings WHERE singleton = 1;
            """;
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            return (ThemeConfiguration.DefaultLight, ThemeConfiguration.DefaultDark, Contrast.Standard);

        var light = new ThemeConfiguration
        {
            Preset = reader.GetString(0),
            Background = ParsePersistedColor(reader.GetString(1), "light_background"),
            Foreground = ParsePersistedColor(reader.GetString(2), "light_foreground"),
            Accent = ParsePersistedColor(reader.GetString(3), "light_accent"),
        };
        var dark = new ThemeConfiguration
        {
            Preset = reader.GetString(4),
            Background = ParsePersistedColor(reader.GetString(5), "dark_background"),
            Foreground = ParsePersistedColor(reader.GetString(6), "dark_foreground"),
            Accent = ParsePersistedColor(reader.GetString(7), "dark_accent"),
        };
        var contrast = reader.IsDBNull(8) ? Contrast.Standard : ContrastText.Parse(reader.GetString(8));
        return (light, dark, contrast);
    }

    private static async Task EnsureThemeTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using (var createCmd = connection.CreateCommand())
        {
            createCmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS theme_settings (
                    singleton        INTEGER NOT NULL PRIMARY KEY,
                    light_preset     TEXT    NOT NULL,
                    light_background TEXT    NOT NULL,
                    light_foreground TEXT    NOT NULL,
                    light_accent     TEXT    NOT NULL,
                    dark_preset      TEXT    NOT NULL,
                    dark_background  TEXT    NOT NULL,
                    dark_foreground  TEXT    NOT NULL,
                    dark_accent      TEXT    NOT NULL,
                    contrast         TEXT    NOT NULL DEFAULT 'standard',
                    CHECK (singleton = 1)
                );
                """;
            await createCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            await using var alterCmd = connection.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE theme_settings ADD COLUMN contrast TEXT NOT NULL DEFAULT 'standard';";
            await alterCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SqliteException) { }

        await using (var insertCmd = connection.CreateCommand())
        {
            insertCmd.CommandText =
                """
                INSERT OR IGNORE INTO theme_settings (
                    singleton, light_preset, light_background, light_foreground, light_accent,
                    dark_preset, dark_background, dark_foreground, dark_accent, contrast)
                VALUES (1, 'default', '#F2F5F5', '#0F1414', '#183739',
                           'carbon', '#121212', '#E0E0E0', '#4CC2FF', 'standard');
                """;
            await insertCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        // Migrate uncustomized legacy default dark row to Carbon Studio
        await using (var migrateCmd = connection.CreateCommand())
        {
            migrateCmd.CommandText =
                """
                UPDATE theme_settings
                SET dark_preset = 'carbon',
                    dark_background = '#121212',
                    dark_foreground = '#E0E0E0',
                    dark_accent = '#4CC2FF'
                WHERE singleton = 1
                  AND dark_preset = 'default'
                  AND dark_background = '#0A0D0D'
                  AND dark_foreground = '#F0F4F4'
                  AND dark_accent = '#2D6669';
                """;
            await migrateCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static HexColor ParsePersistedColor(string value, string column)
    {
        if (!HexColor.TryParse(value, out HexColor color) || color.Value != value)
            throw new InvalidDataException($"Persisted {column} '{value}' is not canonical #RRGGBB.");
        return color;
    }
}
