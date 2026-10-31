using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace S3.Voice.Audio;

internal static class AudioUtil
{
    public const int SampleRate = 16_000;

    public static float[] Resample(float[] input, int fromRate, int toRate)
    {
        if (fromRate == toRate)
        {
            return input;
        }

        var resampler = new WdlResamplingSampleProvider(new ArraySampleProvider(input, fromRate), toRate);
        var output = new List<float>(input.Length * toRate / fromRate + 4096);
        var buffer = new float[8192];
        int read;
        while ((read = resampler.Read(buffer)) > 0)
        {
            output.AddRange(buffer.AsSpan(0, read));
        }

        return [.. output];
    }

    /// <summary>Reads a WAV file as 16 kHz mono floats.</summary>
    public static float[] ReadWav(string path)
    {
        using var reader = new AudioFileReader(path);
        var mono = new DownmixSampleProvider(reader);
        var samples = new List<float>();
        var buffer = new float[8192];
        int read;
        while ((read = mono.Read(buffer)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read));
        }

        return Resample([.. samples], reader.WaveFormat.SampleRate, SampleRate);
    }

    public static float[] Silence(double seconds) => new float[(int)(seconds * SampleRate)];
}

internal sealed class ArraySampleProvider(float[] samples, int rate) : ISampleProvider
{
    private int _position;

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(rate, 1);

    public int Read(Span<float> buffer)
    {
        var n = Math.Min(buffer.Length, samples.Length - _position);
        samples.AsSpan(_position, n).CopyTo(buffer);
        _position += n;
        return n;
    }
}

/// <summary>Averages any number of channels down to mono.</summary>
internal sealed class DownmixSampleProvider(ISampleProvider source) : ISampleProvider
{
    private readonly int _channels = source.WaveFormat.Channels;
    private float[] _frames = [];

    public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

    public int Read(Span<float> buffer)
    {
        if (_channels == 1)
        {
            return source.Read(buffer);
        }

        if (_frames.Length < buffer.Length * _channels)
        {
            _frames = new float[buffer.Length * _channels];
        }

        var read = source.Read(_frames.AsSpan(0, buffer.Length * _channels)) / _channels;
        for (var i = 0; i < read; i++)
        {
            var sum = 0f;
            for (var c = 0; c < _channels; c++)
            {
                sum += _frames[i * _channels + c];
            }

            buffer[i] = sum / _channels;
        }

        return read;
    }
}
