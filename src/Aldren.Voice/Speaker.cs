using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using SherpaOnnx;

namespace Aldren.Voice;

/// <summary>
/// Supertonic 3 (int8, CPU) speaking one sentence at a time: the first sentence plays while the next is synthesised.
/// Other apps are ducked while ALDREN speaks and restored afterwards. Cancelling stops playback at once.
/// </summary>
public sealed partial class Speaker : IDisposable
{
    private readonly OfflineTts _tts;
    private readonly VoiceOptions _options;

    public Speaker(VoiceOptions options)
    {
        _options = options;
        var config = new OfflineTtsConfig();
        config.Model.Supertonic.DurationPredictor = Path.Combine(options.Supertonic, "duration_predictor.int8.onnx");
        config.Model.Supertonic.TextEncoder = Path.Combine(options.Supertonic, "text_encoder.int8.onnx");
        config.Model.Supertonic.VectorEstimator = Path.Combine(options.Supertonic, "vector_estimator.int8.onnx");
        config.Model.Supertonic.Vocoder = Path.Combine(options.Supertonic, "vocoder.int8.onnx");
        config.Model.Supertonic.TtsJson = Path.Combine(options.Supertonic, "tts.json");
        config.Model.Supertonic.UnicodeIndexer = Path.Combine(options.Supertonic, "unicode_indexer.bin");
        config.Model.Supertonic.VoiceStyle = Path.Combine(options.Supertonic, "voice.bin");
        config.Model.NumThreads = options.Threads;
        config.Model.Provider = "cpu";
        config.MaxNumSentences = 1;
        _tts = new OfflineTts(config);
    }

    /// <summary>Time from the call to the first audible sample of the last reply.</summary>
    public TimeSpan? LastFirstAudio { get; private set; }

    [GeneratedRegex(@"(?<=[.!?…])\s+(?=[A-Z0-9""'(])")]
    private static partial Regex SentenceBreak();

    public static IReadOnlyList<string> Sentences(string text)
    {
        var clean = text.Replace("\r", " ").Replace("\n", " ").Replace("*", "").Trim();
        return SentenceBreak().Split(clean).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
    }

    public Task SpeakAsync(string text, CancellationToken ct)
    {
        var channel = Channel.CreateUnbounded<string>();
        foreach (var sentence in Sentences(text))
        {
            channel.Writer.TryWrite(sentence);
        }

        channel.Writer.Complete();
        return SpeakAsync(channel.Reader, ct);
    }

    /// <summary>
    /// Speaks sentences as they arrive (from a reply still being written): the first plays while later ones are synthesised.
    /// <see cref="LastFirstAudio"/> is measured from this call.
    /// </summary>
    /// <param name="onFirstAudio">Called once, as the first sound plays.</param>
    public async Task SpeakAsync(ChannelReader<string> sentences, CancellationToken ct, Action? onFirstAudio = null)
    {
        var clock = Stopwatch.StartNew();
        LastFirstAudio = null;
        IDisposable? ducking = null;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(_tts.SampleRate, 1);
        var buffer = new BufferedWaveProvider(format, TimeSpan.FromMinutes(5)) { ReadFully = false, DiscardOnBufferOverflow = false };
#pragma warning disable CS0618 // WasapiOut: the new player builder isn't needed for one buffered stream
        using var output = new WasapiOut(AudioClientShareMode.Shared, 60);
#pragma warning restore CS0618
        output.Init(buffer);

        // Stopping is immediate: playback halts inside Cancel(), without waiting for the sentence being synthesised.
        var gate = new Lock();
        var stopped = false;
        using var stopNow = ct.Register(() =>
        {
            lock (gate)
            {
                stopped = true;
                try
                {
                    output.Stop();
                }
                catch (Exception ex) when (ex is InvalidOperationException or COMException)
                {
                }
            }
        });

        try
        {
            await foreach (var sentence in sentences.ReadAllAsync(ct))
            {
                var samples = await Task.Run(() => Synthesize(sentence), ct);
                ct.ThrowIfCancellationRequested();
                var bytes = new byte[samples.Length * sizeof(float)];
                Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                buffer.AddSamples(bytes, 0, bytes.Length);
                if (LastFirstAudio is null)
                {
                    lock (gate)
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!stopped)
                        {
                            // Duck only once there is something to say.
                            ducking = Ducking.Apply(_options.DuckLevel);
                            output.Play();
                            LastFirstAudio = clock.Elapsed;
                        }
                    }

                    onFirstAudio?.Invoke();
                }
            }

            while (buffer.BufferedBytes > 0 && output.PlaybackState == PlaybackState.Playing)
            {
                await Task.Delay(50, ct);
            }

            await Task.Delay(120, ct); // the device's own buffer
        }
        finally
        {
            output.Stop();
            ducking?.Dispose();
        }
    }

    private float[] Synthesize(string sentence)
    {
        var config = new OfflineTtsGenerationConfig { Sid = _options.Voice, Speed = 1.0f, NumSteps = 5, SilenceScale = 0.2f };
        var audio = _tts.GenerateWithConfig(sentence, config, (_, _, _, _) => 1);
        try
        {
            return audio.Samples;
        }
        finally
        {
            audio.Dispose();
        }
    }

    public void Dispose() => _tts.Dispose();

    /// <summary>Lowers every other app's audio session on the default output, and puts each back as it was.</summary>
    private sealed class Ducking : IDisposable
    {
        private readonly List<(SimpleAudioVolume Volume, float Before)> _ducked = [];
        private readonly MMDeviceEnumerator _enumerator = new();

        private Ducking(float level)
        {
            try
            {
                var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var sessions = device.AudioSessionManager.Sessions;
                for (var i = 0; i < sessions.Count; i++)
                {
                    var session = sessions[i];
                    if (session.GetProcessID == (uint)Environment.ProcessId || session.IsSystemSoundsSession
                        || session.State != NAudio.CoreAudioApi.Interfaces.AudioSessionState.AudioSessionStateActive)
                    {
                        continue;
                    }

                    var volume = session.SimpleAudioVolume;
                    var before = volume.Volume;
                    if (before > level)
                    {
                        volume.Volume = level;
                        _ducked.Add((volume, before));
                    }
                }
            }
            catch (COMException)
            {
                // No output device or sessions unavailable: speak without ducking.
            }
        }

        public static Ducking Apply(float level) => new(level);

        public void Dispose()
        {
            foreach (var (volume, before) in _ducked)
            {
                try
                {
                    volume.Volume = before;
                }
                catch (COMException)
                {
                    // That app has gone.
                }
            }

            _enumerator.Dispose();
        }
    }
}
