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
        Assert.Equal(7100, wav.Length); // 80ms @ 44.1kHz 16-bit mono = 3528 samples * 2 + 44 header

        ValidateWavHeader(wav, expectedSampleRate: 44100, expectedChannels: 1, expectedBitsPerSample: 16);
    }

    [Fact]
    public void GenerateCompletionBell_ProducesValid16BitPcmWav()
    {
        byte[] wav = SoundSynthesizer.GenerateCompletionBell();
        Assert.NotNull(wav);
        Assert.Equal(44144, wav.Length); // 500ms @ 44.1kHz 16-bit mono = 22050 samples * 2 + 44 header

        ValidateWavHeader(wav, expectedSampleRate: 44100, expectedChannels: 1, expectedBitsPerSample: 16);
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
