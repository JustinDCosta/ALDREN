using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace S3.Voice.Audio;

/// <summary>
/// Captures 16 kHz mono audio from one input endpoint, pinned by its device ID. If that endpoint is removed, disabled
/// or fails, capture ends with <see cref="Lost"/>; it never moves to another device, including the Windows default.
/// </summary>
internal sealed class CaptureDevice : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly MMDeviceNotificationClient _notifications;
    private readonly WasapiCapture _capture;
    private readonly BufferedWaveProvider _buffer;
    private readonly ISampleProvider _output;
    private readonly float[] _chunk = new float[16_000];
    private int _lost;

    private CaptureDevice(string name)
    {
        var matches = _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
            .Where(d => d.FriendlyName.Contains(name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count != 1)
        {
            _enumerator.Dispose();
            throw new InvalidOperationException($"\"{name}\" matches {matches.Count} active inputs: {string.Join("; ", Inputs())}");
        }

        var device = matches[0];
        Id = device.ID;
        Name = device.FriendlyName;
        _capture = new WasapiCapture(device, true, 20);
        _buffer = new BufferedWaveProvider(_capture.WaveFormat) { DiscardOnBufferOverflow = true, ReadFully = false };
        _output = new WdlResamplingSampleProvider(new DownmixSampleProvider(_buffer.ToSampleProvider()), AudioUtil.SampleRate);
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += (_, e) => ReportLost(e.Exception is null ? "capture stopped" : e.Exception.Message);

        // The default-device notification is deliberately not used: ALDREN never follows the default.
        _notifications = _enumerator.CreateNotificationClient(false);
        _notifications.DeviceStateChanged += (_, e) =>
        {
            if (e.DeviceId == Id && e.NewState != DeviceState.Active)
            {
                ReportLost($"{Name} is now {e.NewState}");
            }
        };
        _notifications.DeviceRemoved += (_, e) =>
        {
            if (e.DeviceId == Id)
            {
                ReportLost($"{Name} was removed");
            }
        };
    }

    public string Id { get; }
    public string Name { get; }
    public string Format => $"{_capture.WaveFormat.SampleRate} Hz, {_capture.WaveFormat.Channels} ch";

    /// <summary>16 kHz mono samples as they arrive (on the capture thread).</summary>
    public event Action<float[]>? Samples;

    /// <summary>Raised once, with the reason, when the pinned device goes away.</summary>
    public event Action<string>? Lost;

    public static List<string> Inputs()
    {
        using var enumerator = new MMDeviceEnumerator();
        return [.. enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active).Select(d => d.FriendlyName)];
    }

    /// <summary>Exactly one active input must contain the name; anything else is an error, never a guess.</summary>
    public static CaptureDevice Open(string name) => new(name);

    public void Start() => _capture.StartRecording();

    public void Dispose()
    {
        _notifications.Dispose();
        _capture.DataAvailable -= OnData;
        _capture.Dispose();
        _enumerator.Dispose();
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        _buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
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
