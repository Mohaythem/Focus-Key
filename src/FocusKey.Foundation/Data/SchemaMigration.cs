namespace FocusKey.Foundation.Data;

/// <summary>One ordered, forward-only change to the local database schema.</summary>
/// <param name="Version">Schema version this migration produces. Must be sequential from 1.</param>
/// <param name="Name">Short identifier recorded in <c>schema_migrations</c>.</param>
/// <param name="Sql">Statements applied inside a single transaction.</param>
public sealed record SchemaMigration(int Version, string Name, string Sql);

/// <summary>
/// The complete migration list. Phase 0 intentionally contains schema metadata only —
/// no product tables exist yet.
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
    ];
}
