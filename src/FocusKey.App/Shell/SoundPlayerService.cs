using System.Runtime.InteropServices;
using FocusKey.Foundation.Sounds;

namespace FocusKey.Shell;

/// <summary>
/// Lightweight, non-blocking native sound player using Win32 PlaySound.
/// Respects the user's SessionSoundsEnabled master gate, StartSoundEnabled,
/// and CompletionSoundEnabled preferences, as well as system volume/mute.
/// </summary>
public sealed class SoundPlayerService : ISoundPlayer
{
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_FILENAME = 0x00020000;

    private readonly Func<bool> _masterSoundsEnabled;
    private readonly Func<bool> _startSoundEnabled;
    private readonly Func<bool> _completionSoundEnabled;
    private readonly string _soundsDirectory;

    public SoundPlayerService(
        Func<bool> masterSoundsEnabled,
        Func<bool> startSoundEnabled,
        Func<bool> completionSoundEnabled,
        string? customSoundsDirectory = null)
    {
        _masterSoundsEnabled = masterSoundsEnabled ?? throw new ArgumentNullException(nameof(masterSoundsEnabled));
        _startSoundEnabled = startSoundEnabled ?? throw new ArgumentNullException(nameof(startSoundEnabled));
        _completionSoundEnabled = completionSoundEnabled ?? throw new ArgumentNullException(nameof(completionSoundEnabled));
        _soundsDirectory = customSoundsDirectory ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");
    }

    /// <summary>Plays start_tick.wav once if master and start sound settings are enabled.</summary>
    public void PlayStartTick()
    {
        if (!_masterSoundsEnabled() || !_startSoundEnabled()) return;
        PlayFile("start_tick.wav");
    }

    /// <summary>Plays session_action.wav once if master and start sound settings are enabled (for Pause, Continue, Stop, Start New).</summary>
    public void PlaySessionAction()
    {
        if (!_masterSoundsEnabled() || !_startSoundEnabled()) return;
        PlayFile("session_action.wav");
    }

    /// <summary>Plays pause sound cue once (session_action.wav).</summary>
    public void PlayPause() => PlaySessionAction();

    /// <summary>Plays continue sound cue once (session_action.wav).</summary>
    public void PlayContinue() => PlaySessionAction();

    /// <summary>Plays manual stop sound cue once (session_action.wav).</summary>
    public void PlayStop() => PlaySessionAction();

    /// <summary>Alias for PlayStop (session_action.wav) for backward compatibility.</summary>
    public void PlayCompletionBell() => PlaySessionAction();

    /// <summary>Plays completion_bell.wav ONCE ONLY if master and completion sound settings are enabled (on natural timer completion).</summary>
    public void PlayNaturalCompletionBell()
    {
        if (!_masterSoundsEnabled() || !_completionSoundEnabled()) return;
        PlayFile("completion_bell.wav");
    }

    /// <summary>Previews start_tick.wav once unconditionally.</summary>
    public void PreviewStartTick()
    {
        PlayFile("start_tick.wav");
    }

    /// <summary>Previews completion_bell.wav once unconditionally.</summary>
    public void PreviewCompletionBell()
    {
        PlayFile("completion_bell.wav");
    }

    /// <summary>Previews session_action.wav once unconditionally.</summary>
    public void PreviewSessionAction()
    {
        PlayFile("session_action.wav");
    }

    private void PlayFile(string filename)
    {
        string? path = ResolveSoundPath(filename);
        if (path is not null)
        {
            try
            {
                PlaySound(path, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT);
            }
            catch { }
        }
    }

    private string? ResolveSoundPath(string filename)
    {
        string candidate = Path.Combine(_soundsDirectory, filename);
        if (File.Exists(candidate)) return candidate;

        candidate = Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds", filename);
        if (File.Exists(candidate)) return candidate;

        candidate = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Sounds", filename);
        if (File.Exists(candidate)) return candidate;

        candidate = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FocusKey.App", "Assets", "Sounds", filename));
        if (File.Exists(candidate)) return candidate;

        return null;
    }
}
