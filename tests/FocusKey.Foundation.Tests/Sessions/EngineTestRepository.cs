using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Tests.Sessions;

internal sealed class EngineTestRepository : ISessionRepository
{
    private readonly ISessionRepository _inner;

    internal EngineTestRepository(ISessionRepository inner) => _inner = inner;

    internal bool FailAdds { get; set; }
    internal bool FailUpdates { get; set; }
    internal bool FailReads { get; set; }

    internal TaskCompletionSource<bool>? AddEntered { get; set; }
    internal TaskCompletionSource<bool>? AllowAdd { get; set; }
    internal Action? BeforeUpdate { get; set; }
    internal Action? AfterUpdate { get; set; }

    public async Task AddAsync(SessionRecord session, CancellationToken cancellationToken = default)
    {
        if (FailAdds) throw new InvalidOperationException("test add failure");
        AddEntered?.TrySetResult(true);
        if (AllowAdd is not null) await AllowAdd.Task.WaitAsync(cancellationToken);
        await _inner.AddAsync(session, cancellationToken);
    }

    public Task<SessionRecord?> GetAsync(SessionId id, CancellationToken cancellationToken = default) =>
        FailReads ? Task.FromException<SessionRecord?>(new InvalidOperationException("test read failure")) : _inner.GetAsync(id, cancellationToken);

    public Task UpdateAsync(SessionRecord session, CancellationToken cancellationToken = default) =>
        FailUpdates ? Task.FromException(new InvalidOperationException("test update failure")) : _inner.UpdateAsync(session, cancellationToken);

    public async Task<bool> TryUpdateAsync(SessionRecord expected, SessionRecord replacement, CancellationToken cancellationToken = default)
    {
        if (FailUpdates) throw new InvalidOperationException("test update failure");
        BeforeUpdate?.Invoke();
        bool written = await _inner.TryUpdateAsync(expected, replacement, cancellationToken);
        AfterUpdate?.Invoke();
        return written;
    }

    public Task<SessionRecord?> GetActiveAsync(CancellationToken cancellationToken = default) =>
        FailReads ? Task.FromException<SessionRecord?>(new InvalidOperationException("test read failure")) : _inner.GetActiveAsync(cancellationToken);

    public Task<SessionRecord?> GetRunningAsync(CancellationToken cancellationToken = default) =>
        FailReads ? Task.FromException<SessionRecord?>(new InvalidOperationException("test read failure")) : _inner.GetRunningAsync(cancellationToken);

    public Task<IReadOnlyList<SessionRecord>> GetStartedBetweenAsync(DateTimeOffset fromInclusive, DateTimeOffset toExclusive, CancellationToken cancellationToken = default) =>
        _inner.GetStartedBetweenAsync(fromInclusive, toExclusive, cancellationToken);
}
