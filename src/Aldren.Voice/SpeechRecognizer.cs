using System.Diagnostics;
using SherpaOnnx;

namespace Aldren.Voice;

/// <summary>Parakeet-TDT 0.6B v3 (int8) on the CPU, through sherpa-onnx. Loaded once; about 0.6 GB of RAM.</summary>
public sealed class SpeechRecognizer : IDisposable
{
    /// <summary>Silence around a clip: Parakeet spells out or garbles very short, tightly cut words ("pause" → "B A U S E").</summary>
    private static readonly float[] Padding = new float[VoiceOptions.SampleRate / 4];

    private readonly OfflineRecognizer _recognizer;
    private readonly Lock _lock = new();

    public SpeechRecognizer(VoiceOptions options)
    {
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = VoiceOptions.SampleRate;
        config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = Path.Combine(options.Parakeet, "encoder.int8.onnx");
        config.ModelConfig.Transducer.Decoder = Path.Combine(options.Parakeet, "decoder.int8.onnx");
        config.ModelConfig.Transducer.Joiner = Path.Combine(options.Parakeet, "joiner.int8.onnx");
        config.ModelConfig.Tokens = Path.Combine(options.Parakeet, "tokens.txt");
        config.ModelConfig.ModelType = "nemo_transducer";
        config.ModelConfig.NumThreads = options.Threads;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";
        _recognizer = new OfflineRecognizer(config);
    }

    public (string Text, TimeSpan Elapsed) Transcribe(float[] samples)
    {
        var clock = Stopwatch.StartNew();
        var padded = new float[Padding.Length * 2 + samples.Length];
        samples.CopyTo(padded, Padding.Length);
        lock (_lock)
        {
            using var stream = _recognizer.CreateStream();
            stream.AcceptWaveform(VoiceOptions.SampleRate, padded);
            _recognizer.Decode(stream);
            return (stream.Result.Text.Trim(), clock.Elapsed);
        }
    }

    public void Dispose() => _recognizer.Dispose();
}

/// <summary>
/// Silero VAD end-pointing on a live stream. Every sample since listening began is kept, and the utterance is cut using
/// the VAD's own segment start (minus a 300 ms pre-roll), so the first sound of a word isn't lost to detection lag. A pause
/// shorter than the end-of-speech time stays inside one utterance.
/// </summary>
public sealed class UtteranceCollector : IDisposable
{
    private const int Window = 512;

    private readonly VoiceActivityDetector _vad;
    private readonly int _preRoll;
    private readonly int _tail;
    private readonly int _maxSamples;
    private readonly List<float> _all = [];
    private readonly float[] _pending = new float[Window];
    private int _pendingCount;
    private long _firstSpeech = -1;
    private long _fedBeforeReset;

    public UtteranceCollector(VoiceOptions options)
    {
        var config = new VadModelConfig();
        config.SileroVad.Model = options.SileroVad;
        config.SileroVad.Threshold = 0.5f;
        config.SileroVad.MinSilenceDuration = options.EndOfSpeechSeconds;
        config.SileroVad.MinSpeechDuration = 0.15f;
        config.SileroVad.MaxSpeechDuration = (float)options.MaxUtterance.TotalSeconds;
        config.SileroVad.WindowSize = Window;
        config.SampleRate = VoiceOptions.SampleRate;
        config.NumThreads = 1;
        config.Provider = "cpu";
        _vad = new VoiceActivityDetector(config, 60);
        _preRoll = (int)(options.PreRollSeconds * VoiceOptions.SampleRate);
        _tail = VoiceOptions.SampleRate / 5;
        _maxSamples = (int)((options.MaxUtterance.TotalSeconds + options.NoSpeechTimeout.TotalSeconds + 2) * VoiceOptions.SampleRate);
    }

    /// <summary>True once speech has started.</summary>
    public bool HeardSpeech => _firstSpeech >= 0;

    /// <summary>Feeds audio; returns the finished utterance once the speaker stops, otherwise null.</summary>
    public float[]? Accept(ReadOnlySpan<float> samples)
    {
        foreach (var s in samples)
        {
            _pending[_pendingCount++] = s;
            if (_pendingCount < Window)
            {
                continue;
            }

            _pendingCount = 0;
            var window = (float[])_pending.Clone();
            _all.AddRange(window);
            _vad.AcceptWaveform(window);
            if (_firstSpeech < 0 && _vad.IsSpeechDetected())
            {
                // Detection lags the real start; the exact start comes with the segment, this is only the fallback.
                _firstSpeech = Math.Max(0, _all.Count - (int)(0.4f * VoiceOptions.SampleRate));
            }

            if (!_vad.IsEmpty())
            {
                // The VAD closed a segment: you paused long enough. Use its true start.
                var segment = _vad.Front();
                // Whether the VAD counts from its creation or from the last reset, the segment must end just before now.
                var segmentStart = segment.Start;
                if (segmentStart + segment.Samples.Length > _all.Count + Window)
                {
                    segmentStart -= _fedBeforeReset;
                }

                var start = (int)Math.Clamp(segmentStart - _preRoll, 0, _all.Count);
                var end = (int)Math.Clamp(segmentStart + segment.Samples.Length + _tail, start, _all.Count);
                while (!_vad.IsEmpty())
                {
                    _vad.Pop();
                }

                var utterance = _all.GetRange(start, end - start).ToArray();
                Reset();
                return utterance;
            }

            if (HeardSpeech && _all.Count >= _maxSamples)
            {
                var utterance = _all.GetRange((int)_firstSpeech, _all.Count - (int)_firstSpeech).ToArray();
                Reset();
                return utterance;
            }
        }

        return null;
    }

    public void Reset()
    {
        _fedBeforeReset += _all.Count;
        _vad.Reset();
        _all.Clear();
        _pendingCount = 0;
        _firstSpeech = -1;
    }

    public void Dispose() => _vad.Dispose();
}
