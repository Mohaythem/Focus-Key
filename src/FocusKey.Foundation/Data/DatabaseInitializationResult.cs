namespace FocusKey.Foundation.Data;

/// <summary>Factual outcome of one database initialization pass.</summary>
public sealed record DatabaseInitializationResult
{
    /// <summary>True when the database file did not exist before initialization.</summary>
    public required bool DatabaseFileCreated { get; init; }

    /// <summary>Schema version found before any migration ran.</summary>
    public required int SchemaVersionBefore { get; init; }

    /// <summary>Schema version after initialization completed.</summary>
    public required int SchemaVersionAfter { get; init; }

    /// <summary>Versions applied during this pass. Empty when the database was already current.</summary>
    public required IReadOnlyList<int> AppliedMigrations { get; init; }

    /// <summary>Full path of the database file that was initialized.</summary>
    public required string DatabaseFile { get; init; }
}
