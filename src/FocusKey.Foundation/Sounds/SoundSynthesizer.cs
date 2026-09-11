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
        const double duration = 0.14; // 140ms subtle mechanical tick
        int totalSamples = (int)(sampleRate * duration);
        short[] samples = new short[totalSamples];

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;
            double snap = Math.Sin(2 * Math.PI * 2600 * t) * Math.Exp(-t / 0.008);
            double body = Math.Sin(2 * Math.PI * 520 * t) * Math.Exp(-t / 0.035);
            double val = (snap * 0.45 + body * 0.55) * Math.Exp(-t / 0.04) * 0.40;
            samples[i] = (short)(Math.Clamp(val, -1.0, 1.0) * 32767.0);
        }

        return CreateWav(sampleRate, samples);
    }

    public static byte[] GenerateCompletionBell()
    {
        const int sampleRate = 44100;
        const double duration = 2.2; // 2.2s calm meditation singing chime
        int totalSamples = (int)(sampleRate * duration);
        short[] samples = new short[totalSamples];

        for (int i = 0; i < totalSamples; i++)
        {
            double t = (double)i / sampleRate;
            double attack = t < 0.008 ? Math.Sin((t / 0.008) * (Math.PI / 2)) : 1.0;
            double f1 = Math.Sin(2 * Math.PI * 528.0 * t) * Math.Exp(-t / 0.85);
            double f2 = Math.Sin(2 * Math.PI * 1457.0 * t) * Math.Exp(-t / 0.45) * 0.35;
            double f3 = Math.Sin(2 * Math.PI * 2851.0 * t) * Math.Exp(-t / 0.25) * 0.15;
            double endFade = t > (duration - 0.1) ? ((duration - t) / 0.1) : 1.0;

            double val = (f1 + f2 + f3) * attack * endFade * 0.55;
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
