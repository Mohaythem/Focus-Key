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

    private const uint SND_SYNC = 0x0000;
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

        EnsureSoundAssets();
    }

    public void PlayStartTick()
    {
        if (!_masterSoundsEnabled() || !_startSoundEnabled()) return;
        PlayFile("start_tick.wav");
    }

    public void PlayCompletionBell()
    {
        if (!_masterSoundsEnabled() || !_completionSoundEnabled()) return;
        PlayFile("completion_bell.wav");
    }

    public void PlayNaturalCompletionBell()
    {
        if (!_masterSoundsEnabled() || !_completionSoundEnabled()) return;
        PlayFileRepeated("completion_bell.wav", repeatCount: 3, gapMs: 180);
    }

    public void PreviewStartTick()
    {
        PlayFile("start_tick.wav");
    }

    public void PreviewCompletionBell()
    {
        PlayFile("completion_bell.wav");
    }

    private void PlayFile(string filename)
    {
        string path = Path.Combine(_soundsDirectory, filename);
        if (File.Exists(path))
        {
            try
            {
                PlaySound(path, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT);
            }
            catch { }
        }
    }

    private void PlayFileRepeated(string filename, int repeatCount, int gapMs)
    {
        string path = Path.Combine(_soundsDirectory, filename);
        if (!File.Exists(path)) return;

        Task.Run(async () =>
        {
            for (int i = 0; i < repeatCount; i++)
            {
                try
                {
                    PlaySound(path, IntPtr.Zero, SND_SYNC | SND_FILENAME | SND_NODEFAULT);
                }
                catch { }

                if (i < repeatCount - 1 && gapMs > 0)
                {
                    try
                    {
                        await Task.Delay(gapMs).ConfigureAwait(false);
                    }
                    catch { }
                }
            }
        });
    }

    public void EnsureSoundAssets()
    {
        try
        {
            Directory.CreateDirectory(_soundsDirectory);

            string startPath = Path.Combine(_soundsDirectory, "start_tick.wav");
            File.WriteAllBytes(startPath, SoundSynthesizer.GenerateStartTick());

            string bellPath = Path.Combine(_soundsDirectory, "completion_bell.wav");
            File.WriteAllBytes(bellPath, SoundSynthesizer.GenerateCompletionBell());
        }
        catch { }
    }
}
