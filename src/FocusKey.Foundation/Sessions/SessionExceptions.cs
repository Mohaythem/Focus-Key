namespace FocusKey.Foundation.Sessions;

/// <summary>The requested session does not exist in storage.</summary>
public sealed class SessionNotFoundException : InvalidOperationException
{
    public SessionNotFoundException(SessionId id)
        : base($"No session with id '{id}' exists.") => Id = id;

    public SessionId Id { get; }
}

/// <summary>A session with the same identity is already stored.</summary>
public sealed class DuplicateSessionIdException : InvalidOperationException
{
    public DuplicateSessionIdException(SessionId id)
        : base($"A session with id '{id}' is already stored.") => Id = id;

    public SessionId Id { get; }
}

/// <summary>
/// Storage already holds a running session, and Focus Key allows only one at a time.
/// The data layer refuses the write; deciding what to do about it belongs to session behaviour.
/// </summary>
public sealed class ActiveSessionAlreadyExistsException : InvalidOperationException
{
    public ActiveSessionAlreadyExistsException(SessionId existingId)
        : base($"Session '{existingId}' is already running; only one session may be running at a time.") =>
        ExistingId = existingId;

    public SessionId ExistingId { get; }
}
