namespace FocusKey.Foundation.History;

/// <summary>
/// Represents aggregate historical focus time recorded for a specific calendar date and project.
/// This is distinct from native SessionRecord instances: it represents daily summary facts
/// rather than individual timer executions.
/// </summary>
public sealed record HistoricalFocusRecord(
    long Id,
    DateOnly Date,
    string Project,
    TimeSpan Duration,
    double SourceHours,
    DateTimeOffset ImportedAt);
