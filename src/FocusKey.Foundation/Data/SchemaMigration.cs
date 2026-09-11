namespace FocusKey.Foundation.Data;

/// <summary>One ordered, forward-only change to the local database schema.</summary>
/// <param name="Version">Schema version this migration produces. Must be sequential from 1.</param>
/// <param name="Name">Short identifier recorded in <c>schema_migrations</c>.</param>
/// <param name="Sql">Statements applied inside a single transaction.</param>
public sealed record SchemaMigration(int Version, string Name, string Sql);

/// <summary>
/// The complete migration list, applied in order. A migration that has shipped is never edited;
/// new schema work is always a new version.
/// </summary>
public static class SchemaMigrations
{
    /// <summary>Schema version a fully migrated database reports.</summary>
    public static int TargetVersion => All[^1].Version;

    public static IReadOnlyList<SchemaMigration> All { get; } =
    [
        new SchemaMigration(
            Version: 1,
            Name: "schema_metadata",
            Sql: """
                CREATE TABLE IF NOT EXISTS schema_migrations (
                    version        INTEGER NOT NULL PRIMARY KEY,
                    name           TEXT    NOT NULL,
                    applied_at_utc TEXT    NOT NULL
                );
                """),

        new SchemaMigration(
            Version: 2,
            Name: "sessions",
            Sql: """
                CREATE TABLE sessions (
                    id                       TEXT    NOT NULL PRIMARY KEY,
                    type                     TEXT    NOT NULL,
                    status                   TEXT    NOT NULL,
                    started_at_utc           TEXT    NOT NULL,
                    planned_duration_seconds INTEGER NOT NULL,
                    ended_at_utc             TEXT,
                    created_at_utc           TEXT    NOT NULL,

                    CHECK (length(id) = 36),
                    CHECK (type IN ('work', 'break')),
                    CHECK (status IN ('running', 'completed', 'stopped', 'interrupted')),
                    CHECK (planned_duration_seconds > 0),
                    CHECK (length(started_at_utc) = 28),
                    CHECK (length(created_at_utc) = 28),
                    CHECK (ended_at_utc IS NULL OR length(ended_at_utc) = 28),

                    -- Running is exactly the state with no end timestamp.
                    CHECK ((status = 'running') = (ended_at_utc IS NULL)),

                    -- A session cannot end before it started.
                    CHECK (ended_at_utc IS NULL OR ended_at_utc >= started_at_utc)
                ) STRICT;

                -- Only one session may be running at a time. Enforced by storage, not by trust.
                CREATE UNIQUE INDEX ux_sessions_single_running
                    ON sessions (status)
                    WHERE status = 'running';

                -- Supports the start-time range reads the repository exposes.
                CREATE INDEX ix_sessions_started_at_utc
                    ON sessions (started_at_utc);
                """),

        new SchemaMigration(
            Version: 3,
            Name: "application_settings",
            Sql: """
                CREATE TABLE application_settings (
                    singleton              INTEGER NOT NULL PRIMARY KEY,
                    work_duration_seconds  INTEGER NOT NULL,
                    break_duration_seconds INTEGER NOT NULL,
                    appearance             TEXT    NOT NULL,
                    work_color             TEXT    NOT NULL,
                    break_color            TEXT    NOT NULL,

                    CHECK (singleton = 1),
                    CHECK (work_duration_seconds > 0),
                    CHECK (break_duration_seconds > 0),
                    CHECK (appearance IN ('system', 'light', 'dark')),
                    CHECK (length(work_color) = 7 AND work_color GLOB '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]'),
                    CHECK (length(break_color) = 7 AND break_color GLOB '#[0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F][0-9A-F]')
                ) STRICT;

                INSERT INTO application_settings (
                    singleton, work_duration_seconds, break_duration_seconds,
                    appearance, work_color, break_color)
                VALUES (1, 1800, 600, 'system', '#183739', '#434763');
                """),

        new SchemaMigration(
            Version: 4,
            Name: "theme_settings",
            Sql: """
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

                -- Since version 4 is newly introduced but some dev environments might have the table already:
                -- Wait, we can just alter if it exists, or if it doesn't, we create it.
                -- SQLite does not support ADD COLUMN IF NOT EXISTS gracefully in older versions,
                -- but since this is version 4, clean installs will just run this block.
                
                INSERT OR IGNORE INTO theme_settings (
                    singleton, light_preset, light_background, light_foreground, light_accent,
                    dark_preset, dark_background, dark_foreground, dark_accent, contrast)
                VALUES (1, 'default', '#F2F5F5', '#0F1414', '#183739',
                           'carbon', '#121212', '#E0E0E0', '#4CC2FF', 'standard');
                           
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
                """),

        new SchemaMigration(
            Version: 5,
            Name: "historical_focus",
            Sql: """
                CREATE TABLE IF NOT EXISTS historical_focus (
                    id               INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                    date             TEXT    NOT NULL,
                    project          TEXT    NOT NULL DEFAULT '',
                    duration_seconds INTEGER NOT NULL,
                    source_hours     REAL    NOT NULL,
                    imported_at_utc  TEXT    NOT NULL,

                    CHECK (length(date) = 10),
                    CHECK (duration_seconds > 0),
                    CHECK (source_hours > 0),
                    CHECK (length(imported_at_utc) = 28),
                    UNIQUE (date, project)
                );

                CREATE INDEX IF NOT EXISTS ix_historical_focus_date
                    ON historical_focus (date);
                """),

        new SchemaMigration(
            Version: 6,
            Name: "session_sounds_setting",
            Sql: """
                ALTER TABLE application_settings ADD COLUMN session_sounds_enabled INTEGER NOT NULL DEFAULT 1;
                """),
    ];
}
