using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

/// <summary>Readable session records for tests. Data only — no behaviour.</summary>
internal static class TestSessions
{
    internal static readonly DateTimeOffset Anchor = new(2026, 9, 2, 9, 0, 0, TimeSpan.Zero);

    internal static readonly TimeSpan WorkLength = TimeSpan.FromMinutes(30);

    internal static readonly TimeSpan BreakLength = TimeSpan.FromMinutes(10);

    internal static SessionRecord Running(
        SessionId? id = null,
        DateTimeOffset? startedAt = null,
        SessionType type = SessionType.Work,
        TimeSpan? plannedDuration = null) => new()
    {
        Id = id ?? SessionId.New(),
        Type = type,
        Status = SessionStatus.Running,
        StartedAt = startedAt ?? Anchor,
        ResumedAt = startedAt ?? Anchor,
        PlannedDuration = plannedDuration ?? WorkLength,
        EndedAt = null,
        CreatedAt = startedAt ?? Anchor,
    };

    internal static SessionRecord Finished(
        SessionStatus status,
        SessionId? id = null,
        DateTimeOffset? startedAt = null,
        SessionType type = SessionType.Work,
        TimeSpan? plannedDuration = null,
        TimeSpan? actualDuration = null)
    {
        DateTimeOffset start = startedAt ?? Anchor;
        TimeSpan planned = plannedDuration ?? WorkLength;

        return new SessionRecord
        {
            Id = id ?? SessionId.New(),
            Type = type,
            Status = status,
            StartedAt = start,
            ResumedAt = start,
            PlannedDuration = planned,
            EndedAt = start + (actualDuration ?? planned),
            CreatedAt = start,
        };
    }
}
