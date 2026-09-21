namespace FocusKey.Foundation.Sounds;

/// <summary>
/// Synthesizes clean 16-bit PCM RIFF WAV audio files for session start and completion.
/// Zero external dependencies; produces valid standalone WAV byte arrays.
/// </summary>
public static class SoundSynthesizer
{
    public static byte[] GenerateStartTick()
    {
        const int sampleRate = 44100;
        const double duration = 0.080; // 80ms soft organic confirmation tick
        int totalSamples = (int)(sampleRate * duration);
        short[] samples = new short[totalSamples];

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;
            double attack = t < 0.003 ? Math.Sin((t / 0.003) * (Math.PI / 2)) : 1.0;
            double endFade = t > 0.065 ? ((duration - t) / 0.015) : 1.0;

            double w1 = Math.Sin(2 * Math.PI * 587.33 * t) * Math.Exp(-t / 0.020);
            double w2 = Math.Sin(2 * Math.PI * 1174.66 * t) * Math.Exp(-t / 0.012) * 0.30;
            double w3 = Math.Sin(2 * Math.PI * 293.66 * t) * Math.Exp(-t / 0.025) * 0.20;

            double val = (w1 + w2 + w3) * attack * endFade * 0.40;
            samples[i] = (short)(Math.Clamp(val, -1.0, 1.0) * 32767.0);
        }

        return CreateWav(sampleRate, samples);
    }

    public static byte[] GenerateCompletionBell()
    {
        const int sampleRate = 44100;
        const double duration = 0.500; // 500ms warm pleasant musical chime
        int totalSamples = (int)(sampleRate * duration);
        short[] samples = new short[totalSamples];

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;
            double attack = t < 0.005 ? Math.Sin((t / 0.005) * (Math.PI / 2)) : 1.0;
            double endFade = t > 0.420 ? ((duration - t) / 0.080) : 1.0;

            double c1 = Math.Sin(2 * Math.PI * 523.25 * t) * Math.Exp(-t / 0.160) * 0.50;
            double c2 = Math.Sin(2 * Math.PI * 659.25 * t) * Math.Exp(-t / 0.140) * 0.35;
            double c3 = Math.Sin(2 * Math.PI * 783.99 * t) * Math.Exp(-t / 0.120) * 0.25;
            double c4 = Math.Sin(2 * Math.PI * 1046.50 * t) * Math.Exp(-t / 0.080) * 0.15;

            double val = (c1 + c2 + c3 + c4) * attack * endFade * 0.45;
            samples[i] = (short)(Math.Clamp(val, -1.0, 1.0) * 32767.0);
        }

        return CreateWav(sampleRate, samples);
    }

    private static byte[] CreateWav(int sampleRate, short[] samples)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        int channels = 1;
        int bitsPerSample = 16;
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        int blockAlign = channels * (bitsPerSample / 8);
        int subchunk2Size = samples.Length * blockAlign;
        int chunkSize = 36 + subchunk2Size;

        bw.Write("RIFF"u8);
        bw.Write(chunkSize);
        bw.Write("WAVE"u8);

        bw.Write("fmt "u8);
        bw.Write(16); // Subchunk1Size
        bw.Write((short)1); // AudioFormat = PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)blockAlign);
        bw.Write((short)bitsPerSample);

        bw.Write("data"u8);
        bw.Write(subchunk2Size);

        for (int i = 0; i < samples.Length; i++)
        {
            bw.Write(samples[i]);
        }

        bw.Flush();
        return ms.ToArray();
    }
}
