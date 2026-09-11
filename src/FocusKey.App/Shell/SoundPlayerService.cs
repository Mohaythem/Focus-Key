using System.Runtime.InteropServices;
using FocusKey.Foundation.Settings;
using FocusKey.Foundation.Sounds;

namespace FocusKey.Shell;

public interface ISoundPlayer
{
    void PlayStartTick();
    void PlayCompletionBell();
}

/// <summary>
/// Lightweight, non-blocking native sound player using Win32 PlaySound.
/// Respects the user's SessionSoundsEnabled setting and system volume/mute.
/// </summary>
public sealed class SoundPlayerService : ISoundPlayer
{
    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool PlaySound(string? pszSound, IntPtr hmod, uint fdwSound);

    private const uint SND_ASYNC = 0x0001;
    private const uint SND_NODEFAULT = 0x0002;
    private const uint SND_FILENAME = 0x00020000;

    private readonly Func<bool> _soundsEnabled;
    private readonly string _soundsDirectory;

    public SoundPlayerService(Func<bool> soundsEnabled, string? customSoundsDirectory = null)
    {
        _soundsEnabled = soundsEnabled ?? throw new ArgumentNullException(nameof(soundsEnabled));
        _soundsDirectory = customSoundsDirectory ?? Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");

        EnsureSoundAssets();
    }

    public void PlayStartTick()
    {
        if (!_soundsEnabled()) return;

        string path = Path.Combine(_soundsDirectory, "start_tick.wav");
        if (File.Exists(path))
        {
            try
            {
                PlaySound(path, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT);
            }
            catch { }
        }
    }

    public void PlayCompletionBell()
    {
        if (!_soundsEnabled()) return;

        string path = Path.Combine(_soundsDirectory, "completion_bell.wav");
        if (File.Exists(path))
        {
            try
            {
                PlaySound(path, IntPtr.Zero, SND_ASYNC | SND_FILENAME | SND_NODEFAULT);
            }
            catch { }
        }
    }

    public void EnsureSoundAssets()
    {
        try
        {
            Directory.CreateDirectory(_soundsDirectory);

            string startPath = Path.Combine(_soundsDirectory, "start_tick.wav");
            if (!File.Exists(startPath) || new FileInfo(startPath).Length < 100)
            {
                File.WriteAllBytes(startPath, SoundSynthesizer.GenerateStartTick());
            }

            string bellPath = Path.Combine(_soundsDirectory, "completion_bell.wav");
            if (!File.Exists(bellPath) || new FileInfo(bellPath).Length < 100)
            {
                File.WriteAllBytes(bellPath, SoundSynthesizer.GenerateCompletionBell());
            }
        }
        catch { }
    }
}
