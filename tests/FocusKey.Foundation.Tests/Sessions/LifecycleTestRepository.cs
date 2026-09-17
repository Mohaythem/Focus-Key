using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

/// <summary>Small failure/counting seam for recovery and coordinator tests.</summary>
internal sealed class LifecycleTestRepository(ISessionRepository inner) : ISessionRepository
{
    internal int RunningReads { get; private set; }
    internal int ConditionalWrites { get; private set; }
    internal bool FailReads { get; set; }
    internal bool FailWrites { get; set; }
    internal bool ReturnConflict { get; set; }
    internal Func<Task>? AfterRead { get; set; }
    internal Action? AfterWrite { get; set; }

    public Task AddAsync(SessionRecord session, CancellationToken cancellationToken = default) =>
        inner.AddAsync(session, cancellationToken);

    public Task<SessionRecord?> GetAsync(SessionId id, CancellationToken cancellationToken = default) =>
        inner.GetAsync(id, cancellationToken);

    public Task UpdateAsync(SessionRecord session, CancellationToken cancellationToken = default) =>
        inner.UpdateAsync(session, cancellationToken);

    public async Task<bool> TryUpdateAsync(SessionRecord expected, SessionRecord replacement,
        CancellationToken cancellationToken = default)
    {
        ConditionalWrites++;
        cancellationToken.ThrowIfCancellationRequested();
        if (FailWrites) throw new InvalidOperationException("test update failure");
        if (ReturnConflict) return false;
        bool changed = await inner.TryUpdateAsync(expected, replacement, cancellationToken);
        AfterWrite?.Invoke();
        return changed;
    }

    public async Task<SessionRecord?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        RunningReads++;
        cancellationToken.ThrowIfCancellationRequested();
        if (FailReads) throw new InvalidOperationException("test read failure");
        var result = await inner.GetActiveAsync(cancellationToken);
        if (AfterRead is not null) await AfterRead();
        return result;
    }

    public async Task<SessionRecord?> GetRunningAsync(CancellationToken cancellationToken = default)
    {
        RunningReads++;
        cancellationToken.ThrowIfCancellationRequested();
        if (FailReads) throw new InvalidOperationException("test read failure");
        var result = await inner.GetRunningAsync(cancellationToken);
        if (AfterRead is not null) await AfterRead();
        return result;
    }

    public Task<IReadOnlyList<SessionRecord>> GetStartedBetweenAsync(DateTimeOffset fromInclusive,
        DateTimeOffset toExclusive, CancellationToken cancellationToken = default) =>
        inner.GetStartedBetweenAsync(fromInclusive, toExclusive, cancellationToken);
}
