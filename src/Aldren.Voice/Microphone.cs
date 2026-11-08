using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Aldren.Voice;

/// <summary>
/// 16 kHz mono capture from one input, chosen by policy (ARCHITECTURE §10): the Maono PD300X, then the laptop's microphone
/// array, then the Windows default, never a Bluetooth hands-free microphone. If that input goes away, capture ends with
/// <see cref="Lost"/>; it never moves to another device on its own.
/// </summary>
public sealed class Microphone : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly WasapiRecorder _capture;
    private readonly BufferedWaveProvider _buffer;
    private readonly ISampleProvider _output;
    private readonly float[] _chunk = new float[VoiceOptions.SampleRate];
    private int _lost;

    private Microphone(MMDevice device)
    {
        Id = device.ID;
        Name = device.FriendlyName;
        // Raw mode bypasses Windows' microphone effects. With them on (the default after a fresh install), the PD300X
        // stream came through noise-gated: up to 45% of each utterance forced to exact zero, soft consonants and word
        // starts cut off, and recognition dropped to about half right.
        try
        {
            _capture = Builder(device).WithRawMode().Build();
            Raw = true;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            _capture = Builder(device).Build();
            RawUnavailableReason = ex.Message;
        }

        _buffer = new BufferedWaveProvider(_capture.WaveFormat) { DiscardOnBufferOverflow = true, ReadFully = false };
        _output = new WdlResamplingSampleProvider(new DownmixSampleProvider(_buffer.ToSampleProvider()), VoiceOptions.SampleRate);
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += (_, e) => ReportLost(e.Exception is null ? "capture stopped" : e.Exception.Message);
    }

    private static WasapiRecorderBuilder Builder(MMDevice device) =>
        new WasapiRecorderBuilder().WithDevice(device).WithSharedMode().WithEventSync().WithBufferLength(20);

    public string Id { get; }
    public string Name { get; }

    /// <summary>True when Windows' microphone effects are bypassed.</summary>
    public bool Raw { get; }

    public string? RawUnavailableReason { get; }

    /// <summary>16 kHz mono samples, on the capture thread.</summary>
    public event Action<float[]>? Samples;

    public event Action<string>? Lost;

    public static Microphone Open()
    {
        using var enumerator = new MMDeviceEnumerator();
        var inputs = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).ToList();
        var usable = inputs.Where(d => !IsHandsFree(d.FriendlyName)).ToList();
        var device = usable.FirstOrDefault(d => d.FriendlyName.Contains("PD300X", StringComparison.OrdinalIgnoreCase))
                     ?? usable.FirstOrDefault(d => d.FriendlyName.Contains("Microphone Array", StringComparison.OrdinalIgnoreCase))
                     ?? DefaultIfUsable(enumerator)
                     ?? usable.FirstOrDefault()
                     ?? throw new InvalidOperationException("No usable microphone is connected (Bluetooth headset microphones are never used).");
        return new Microphone(enumerator.GetDevice(device.ID));
    }

    public void Start() => _capture.StartRecording();

    public void Dispose()
    {
        // Closing it ourselves isn't losing it: suppress Lost before the capture reports "stopped".
        Interlocked.Exchange(ref _lost, 1);
        _capture.DataAvailable -= OnData;
        try
        {
            _capture.StopRecording();
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
        }

        _capture.Dispose();
        _enumerator.Dispose();
    }

    private static MMDevice? DefaultIfUsable(MMDeviceEnumerator enumerator)
    {
        try
        {
            var d = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            return IsHandsFree(d.FriendlyName) ? null : d;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    // Bluetooth headsets expose their microphone as a "Hands-Free" endpoint; opening it drops music to call quality.
    private static bool IsHandsFree(string name) =>
        name.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) || name.Contains("Headset", StringComparison.OrdinalIgnoreCase) && name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);

    private void OnData(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        // A "silent" packet may carry stale bytes: WASAPI means zeros.
        var bytes = (flags & AudioClientBufferFlags.Silent) != 0 ? new byte[data.Length] : data.ToArray();
        _buffer.AddSamples(bytes, 0, bytes.Length);
        int read;
        while ((read = _output.Read(_chunk)) > 0)
        {
            Samples?.Invoke(_chunk[..read]);
        }
    }

    private void ReportLost(string reason)
    {
        if (Interlocked.Exchange(ref _lost, 1) == 0)
        {
            Lost?.Invoke(reason);
        }
    }
}

/// <summary>Averages all channels into one.</summary>
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
