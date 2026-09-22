namespace FocusKey.Foundation.Sounds;

/// <summary>
/// Audio playback interface for session events and settings previews.
/// </summary>
public interface ISoundPlayer
{
    /// <summary>Plays the session start sound cue once (start_tick.wav) if enabled in settings.</summary>
    void PlayStartTick();

    /// <summary>Plays the manual stop sound cue once (complete.wav) if enabled in settings (e.g. on manual Stop or Start New from Paused).</summary>
    void PlayStop();

    /// <summary>Alias for PlayStop (complete.wav) for backward compatibility.</summary>
    void PlayCompletionBell();

    /// <summary>Plays the natural completion sound cue once (completion_bell.wav, which already contains 3 chimes internally) if enabled in settings.</summary>
    void PlayNaturalCompletionBell();

    /// <summary>Previews the session start sound cue once (start_tick.wav, unconditionally).</summary>
    void PreviewStartTick();

    /// <summary>Previews the completion sound cue once (completion_bell.wav, unconditionally).</summary>
    void PreviewCompletionBell();
}
