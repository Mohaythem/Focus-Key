using FocusKey.Foundation.Sessions;

namespace FocusKey.Foundation.Sounds;

/// <summary>
/// Authoritative rule engine for session sound playback events.
/// Enforces single start cue on Start (start_tick.wav),
/// single session action cue on Pause / Continue / Stop / Start New (session_action.wav),
/// single completion bell on Natural Completion (completion_bell.wav),
/// and silence on Interrupted.
/// </summary>
public sealed class SessionSoundCoordinator
{
    private readonly ISoundPlayer _player;

    public SessionSoundCoordinator(ISoundPlayer player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
    }

    /// <summary>New session started -> plays Start sound (start_tick.wav) once.</summary>
    public void HandleSessionStarted(SessionRecord session)
    {
        if (session is null) return;
        _player.PlayStartTick();
    }

    /// <summary>Pause session -> plays session action sound (session_action.wav) once if paused.</summary>
    public void HandleSessionPaused(SessionOutcome outcome)
    {
        if (outcome.Kind == SessionOutcomeKind.Paused)
        {
            _player.PlayPause();
        }
    }

    /// <summary>Continue after Pause -> plays session action sound (session_action.wav) once if continued.</summary>
    public void HandleSessionContinued(SessionOutcome outcome)
    {
        if (outcome.Kind == SessionOutcomeKind.Continued)
        {
            _player.PlayContinue();
        }
    }

    /// <summary>User Stop (or Start New from Paused) -> plays session action sound (session_action.wav) once if stopped.</summary>
    public void HandleSessionStopped(SessionOutcome outcome)
    {
        if (outcome.Kind == SessionOutcomeKind.Stopped)
        {
            _player.PlayStop();
        }
    }

    /// <summary>Natural timer completion -> plays completion bell (completion_bell.wav) ONCE ONLY.</summary>
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
