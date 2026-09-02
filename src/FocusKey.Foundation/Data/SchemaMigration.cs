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
    ];
}
