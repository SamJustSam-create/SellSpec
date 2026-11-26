namespace SellSpec.Core.Testing;

public enum ToneChannel
{
    Both,
    Left,
    Right,
}

/// <summary>Generates an original sine-wave test tone as an in-memory WAV file (16-bit PCM, stereo, 44.1 kHz).</summary>
public static class TestToneGenerator
{
    public const int SampleRate = 44_100;

    public static byte[] CreateWav(double frequencyHz = 440, double durationSeconds = 1.2, ToneChannel channel = ToneChannel.Both, double amplitude = 0.35)
    {
        if (frequencyHz <= 0) throw new ArgumentOutOfRangeException(nameof(frequencyHz));
        if (durationSeconds <= 0 || durationSeconds > 10) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        amplitude = Math.Clamp(amplitude, 0.05, 0.9);

        var frames = (int)(SampleRate * durationSeconds);
        const int channels = 2;
        const int bitsPerSample = 16;
        var dataBytes = frames * channels * (bitsPerSample / 8);
        var fade = (int)(SampleRate * 0.02); // 20 ms fade in/out avoids clicks

        using var ms = new MemoryStream(44 + dataBytes);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8);
        w.Write(36 + dataBytes);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1); // PCM
        w.Write((short)channels);
        w.Write(SampleRate);
        w.Write(SampleRate * channels * (bitsPerSample / 8));
        w.Write((short)(channels * (bitsPerSample / 8)));
        w.Write((short)bitsPerSample);
        w.Write("data"u8);
        w.Write(dataBytes);

        for (var i = 0; i < frames; i++)
        {
            var envelope = 1.0;
            if (i < fade) envelope = (double)i / fade;
            else if (i > frames - fade) envelope = (double)(frames - i) / fade;

            var sample = (short)(Math.Sin(2 * Math.PI * frequencyHz * i / SampleRate) * amplitude * envelope * short.MaxValue);
            w.Write(channel == ToneChannel.Right ? (short)0 : sample);
            w.Write(channel == ToneChannel.Left ? (short)0 : sample);
        }

        w.Flush();
        return ms.ToArray();
    }
}
