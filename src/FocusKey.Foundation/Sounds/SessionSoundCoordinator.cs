using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Sounds;

/// <summary>
/// Authoritative rule engine for session sound playback events.
/// Enforces single start cue on Start/Continue, single completion cue on Stop,
/// triple repetition on Natural Completion, and silence on Interrupted.
/// </summary>
public sealed class SessionSoundCoordinator
{
    private readonly ISoundPlayer _player;

    public SessionSoundCoordinator(ISoundPlayer player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    /// <summary>New session started -> plays Start sound once.</summary>
    public void HandleSessionStarted(SessionRecord session)
    {
        if (session is null) return;
        _player.PlayStartTick();
    }

    /// <summary>Continue after Pause -> plays Start sound once if continued.</summary>
    public void HandleSessionContinued(SessionOutcome outcome)
    {
        if (outcome.Kind == SessionOutcomeKind.Continued)
        {
            _player.PlayStartTick();
        }
    }

    /// <summary>User Stop (or Start New from Paused) -> plays Completion sound once if stopped.</summary>
    public void HandleSessionStopped(SessionOutcome outcome)
    {
        if (outcome.Kind == SessionOutcomeKind.Stopped)
        {
            _player.PlayCompletionBell();
        }
    }

    /// <summary>Natural timer completion -> plays Completion sound three times.</summary>
    public void HandleSessionCompleted(SessionRecord session)
    {
        if (session is null) return;
        _player.PlayNaturalCompletionBell();
    }

    /// <summary>Interrupted/crash/shutdown -> silence (no sound played).</summary>
    public void HandleSessionInterrupted()
    {
        // Explicitly silent
    }
}
