namespace FocusKey.Foundation.Sounds;

/// <summary>
/// Audio playback interface for session events and settings previews.
/// </summary>
public interface ISoundPlayer
{
    /// <summary>Plays the session start sound cue once (if enabled in settings).</summary>
    void PlayStartTick();

    /// <summary>Plays the completion sound cue once (if enabled in settings, e.g. on manual Stop).</summary>
    void PlayCompletionBell();

    /// <summary>Plays the completion sound cue three times sequentially (if enabled in settings, on natural timer completion).</summary>
    void PlayNaturalCompletionBell();

    /// <summary>Previews the session start sound cue once (unconditionally).</summary>
    void PreviewStartTick();

    /// <summary>Previews the completion sound cue once (unconditionally, NOT 3 times).</summary>
    void PreviewCompletionBell();
}
