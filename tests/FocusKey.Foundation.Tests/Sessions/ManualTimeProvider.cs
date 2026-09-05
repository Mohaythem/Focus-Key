namespace FocusKey.Foundation.Tests.Sessions;

internal sealed class ManualTimeProvider : TimeProvider
{
    private DateTimeOffset _utcNow;

    internal ManualTimeProvider(DateTimeOffset utcNow) => _utcNow = utcNow.ToUniversalTime();

    public override DateTimeOffset GetUtcNow() => _utcNow;

    internal void Set(DateTimeOffset value) => _utcNow = value.ToUniversalTime();

    internal void Advance(TimeSpan amount) => _utcNow = _utcNow.Add(amount);
}
