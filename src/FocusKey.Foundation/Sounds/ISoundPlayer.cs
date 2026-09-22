namespace FocusKey.Foundation.Sounds;

/// <summary>
/// Audio playback interface for session events and settings previews.
/// </summary>
public interface ISoundPlayer
{
    /// <summary>Plays the session start sound cue once (start_tick.wav) if enabled in settings.</summary>
    void PlayStartTick();

    /// <summary>Plays the session action sound cue once (session_action.wav) for Pause, Continue, Stop, and Start New if enabled in settings.</summary>
    void PlaySessionAction();

    /// <summary>Plays the pause sound cue once (session_action.wav) if enabled in settings.</summary>
    void PlayPause();

    /// <summary>Plays the continue sound cue once (session_action.wav) if enabled in settings.</summary>
    void PlayContinue();

    /// <summary>Plays the manual stop sound cue once (session_action.wav) if enabled in settings (e.g. on manual Stop or Start New from Paused).</summary>
    void PlayStop();

    /// <summary>Alias for PlayStop (session_action.wav) for backward compatibility.</summary>
    void PlayCompletionBell();

    /// <summary>Plays the natural completion sound cue once (completion_bell.wav) if enabled in settings.</summary>
    void PlayNaturalCompletionBell();

    /// <summary>Previews the session start sound cue once (start_tick.wav, unconditionally).</summary>
    void PreviewStartTick();

    /// <summary>Previews the completion sound cue once (completion_bell.wav, unconditionally).</summary>
    void PreviewCompletionBell();

    /// <summary>Previews the session action sound cue once (session_action.wav, unconditionally).</summary>
    void PreviewSessionAction();
}
