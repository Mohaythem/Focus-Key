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
            SELECT work_duration_seconds, break_duration_seconds, appearance, work_color, break_color, session_sounds_enabled, activity_collapsed, global_shortcut, main_window_shortcut, overlay_position_x, overlay_position_y, time_format, appearance_expanded, shortcuts_expanded, advanced_expanded, start_sound_enabled, completion_sound_enabled, ui_scale_percent
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
            var soundsEnabled = reader.GetInt64(5) != 0;
            var activityCollapsed = reader.GetInt64(6) != 0;
            var globalShortcut = GlobalShortcut.TryParse(reader.GetString(7), out var parsedShortcut)
                ? parsedShortcut
                : GlobalShortcut.Default;
            var mainWindowShortcut = GlobalShortcut.TryParse(reader.GetString(8), out var parsedMainWindowShortcut)
                ? parsedMainWindowShortcut
                : GlobalShortcut.DefaultMainWindow;
            int? overlayX = reader.IsDBNull(9) ? null : reader.GetInt32(9);
            int? overlayY = reader.IsDBNull(10) ? null : reader.GetInt32(10);
            var timeFormat = reader.IsDBNull(11) ? TimeFormat.TwentyFourHour : (TimeFormatText.TryParse(reader.GetString(11), out var tf) ? tf : TimeFormat.TwentyFourHour);
            bool appearanceExpanded = !reader.IsDBNull(12) && reader.GetInt64(12) != 0;
            bool shortcutsExpanded = !reader.IsDBNull(13) && reader.GetInt64(13) != 0;
            bool advancedExpanded = !reader.IsDBNull(14) && reader.GetInt64(14) != 0;
            bool startSoundEnabled = reader.IsDBNull(15) || reader.GetInt64(15) != 0;
            bool completionSoundEnabled = reader.IsDBNull(16) || reader.GetInt64(16) != 0;
            int uiScalePercent = reader.IsDBNull(17) ? 100 : reader.GetInt32(17);
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
                SessionSoundsEnabled = soundsEnabled,
                StartSoundEnabled = startSoundEnabled,
                CompletionSoundEnabled = completionSoundEnabled,
                ActivityCollapsed = activityCollapsed,
                GlobalShortcut = globalShortcut,
                MainWindowShortcut = mainWindowShortcut,
                OverlayPositionX = overlayX,
                OverlayPositionY = overlayY,
                TimeFormat = timeFormat,
                AppearanceExpanded = appearanceExpanded,
                ShortcutsExpanded = shortcutsExpanded,
                AdvancedExpanded = advancedExpanded,
                UiScalePercent = uiScalePercent,
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
                break_color = $breakColor,
                session_sounds_enabled = $sounds,
                start_sound_enabled = $startSound,
                completion_sound_enabled = $completionSound,
                activity_collapsed = $activityCollapsed,
                global_shortcut = $globalShortcut,
                main_window_shortcut = $mainWindowShortcut,
                overlay_position_x = $overlayX,
                overlay_position_y = $overlayY,
                time_format = $timeFormat,
                appearance_expanded = $appearanceExpanded,
                shortcuts_expanded = $shortcutsExpanded,
                advanced_expanded = $advancedExpanded,
                ui_scale_percent = $uiScalePercent
            WHERE singleton = 1;
            """;
        var light = settings.LightTheme ?? ThemeConfiguration.DefaultLight;
        var dark = settings.DarkTheme ?? ThemeConfiguration.DefaultDark;
        command.Parameters.AddWithValue("$work", checked((long)settings.WorkDuration.TotalSeconds));
        command.Parameters.AddWithValue("$break", checked((long)settings.BreakDuration.TotalSeconds));
        command.Parameters.AddWithValue("$appearance", AppearanceText.Format(settings.Appearance));
        command.Parameters.AddWithValue("$workColor", settings.WorkColor.Value);
        command.Parameters.AddWithValue("$breakColor", settings.BreakColor.Value);
        command.Parameters.AddWithValue("$sounds", settings.SessionSoundsEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$startSound", settings.StartSoundEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$completionSound", settings.CompletionSoundEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$activityCollapsed", settings.ActivityCollapsed ? 1 : 0);
        command.Parameters.AddWithValue("$globalShortcut", (settings.GlobalShortcut ?? GlobalShortcut.Default).ToString());
        command.Parameters.AddWithValue("$mainWindowShortcut", (settings.MainWindowShortcut ?? GlobalShortcut.DefaultMainWindow).ToString());
        command.Parameters.AddWithValue("$overlayX", settings.OverlayPositionX.HasValue ? (object)settings.OverlayPositionX.Value : DBNull.Value);
        command.Parameters.AddWithValue("$overlayY", settings.OverlayPositionY.HasValue ? (object)settings.OverlayPositionY.Value : DBNull.Value);
        command.Parameters.AddWithValue("$timeFormat", TimeFormatText.Format(settings.TimeFormat));
        command.Parameters.AddWithValue("$appearanceExpanded", settings.AppearanceExpanded ? 1 : 0);
        command.Parameters.AddWithValue("$shortcutsExpanded", settings.ShortcutsExpanded ? 1 : 0);
        command.Parameters.AddWithValue("$advancedExpanded", settings.AdvancedExpanded ? 1 : 0);
        command.Parameters.AddWithValue("$uiScalePercent", settings.UiScalePercent);
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



    private static HexColor ParsePersistedColor(string value, string column)
    {
        if (!HexColor.TryParse(value, out HexColor color) || color.Value != value)
            throw new InvalidDataException($"Persisted {column} '{value}' is not canonical #RRGGBB.");
        return color;
    }
}
