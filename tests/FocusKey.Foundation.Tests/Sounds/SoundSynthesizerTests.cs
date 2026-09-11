using System.Text;
using FocusKey.Foundation.Sounds;

namespace FocusKey.Foundation.Tests.Sounds;

public sealed class SoundSynthesizerTests
{
    [Fact]
    public void GenerateStartTick_ProducesValid16BitPcmWav()
    {
        byte[] wav = SoundSynthesizer.GenerateStartTick();
        Assert.NotNull(wav);
        Assert.True(wav.Length > 1000);

        ValidateWavHeader(wav, expectedSampleRate: 44100, expectedChannels: 1, expectedBitsPerSample: 16);

        // Ensure asset file exists on disk
        string targetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FocusKey.App", "Assets", "Sounds"));
        if (Directory.Exists(Path.GetDirectoryName(targetDir)))
        {
            Directory.CreateDirectory(targetDir);
            File.WriteAllBytes(Path.Combine(targetDir, "start_tick.wav"), wav);
        }
    }

    [Fact]
    public void GenerateCompletionBell_ProducesValid16BitPcmWav()
    {
        byte[] wav = SoundSynthesizer.GenerateCompletionBell();
        Assert.NotNull(wav);
        Assert.True(wav.Length > 10000); // 2.2s is ~194 kB

        ValidateWavHeader(wav, expectedSampleRate: 44100, expectedChannels: 1, expectedBitsPerSample: 16);

        // Ensure asset file exists on disk
        string targetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "FocusKey.App", "Assets", "Sounds"));
        if (Directory.Exists(Path.GetDirectoryName(targetDir)))
        {
            Directory.CreateDirectory(targetDir);
            File.WriteAllBytes(Path.Combine(targetDir, "completion_bell.wav"), wav);
        }
    }

    private static void ValidateWavHeader(byte[] wav, int expectedSampleRate, short expectedChannels, short expectedBitsPerSample)
    {
        // RIFF header
        string riff = Encoding.ASCII.GetString(wav, 0, 4);
        Assert.Equal("RIFF", riff);

        string wave = Encoding.ASCII.GetString(wav, 8, 4);
        Assert.Equal("WAVE", wave);

        // fmt chunk
        string fmt = Encoding.ASCII.GetString(wav, 12, 4);
        Assert.Equal("fmt ", fmt);

        short format = BitConverter.ToInt16(wav, 20);
        Assert.Equal(1, format); // PCM = 1

        short channels = BitConverter.ToInt16(wav, 22);
        Assert.Equal(expectedChannels, channels);

        int sampleRate = BitConverter.ToInt32(wav, 24);
        Assert.Equal(expectedSampleRate, sampleRate);

        short bitsPerSample = BitConverter.ToInt16(wav, 34);
        Assert.Equal(expectedBitsPerSample, bitsPerSample);

        // data chunk
        string data = Encoding.ASCII.GetString(wav, 36, 4);
        Assert.Equal("data", data);

        int dataSize = BitConverter.ToInt32(wav, 40);
        Assert.Equal(wav.Length - 44, dataSize);
    }
}
