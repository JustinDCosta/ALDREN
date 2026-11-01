using System.Diagnostics;
using S3.Voice.Audio;
using SherpaOnnx;

namespace S3.Voice.Pipeline;

public sealed record Endpointed(float[] Utterance, int Segments, double DelayMs);

/// <summary>The pipeline under test, all on the CPU: Silero VAD → Parakeet-TDT 0.6B v3 (STT) → Kokoro (TTS).</summary>
internal sealed class VoiceProbe : IDisposable
{
    private readonly string _models;
    private readonly OfflineRecognizer _recognizer;
    private readonly OfflineTts _tts;

    public VoiceProbe(string models, int threads, float hangoverSeconds, bool englishOnlyFrontEnd = false, string ttsEngine = "kokoro",
        bool fullPrecisionKokoro = false)
    {
        _models = models;
        Threads = threads;
        Hangover = hangoverSeconds;

        var parakeet = Path.Combine(models, "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8");
        var asr = new OfflineRecognizerConfig();
        asr.FeatConfig.SampleRate = AudioUtil.SampleRate;
        asr.FeatConfig.FeatureDim = 80;
        asr.ModelConfig.Transducer.Encoder = Path.Combine(parakeet, "encoder.int8.onnx");
        asr.ModelConfig.Transducer.Decoder = Path.Combine(parakeet, "decoder.int8.onnx");
        asr.ModelConfig.Transducer.Joiner = Path.Combine(parakeet, "joiner.int8.onnx");
        asr.ModelConfig.Tokens = Path.Combine(parakeet, "tokens.txt");
        asr.ModelConfig.ModelType = "nemo_transducer";
        asr.ModelConfig.NumThreads = threads;
        asr.ModelConfig.Provider = "cpu";
        asr.DecodingMethod = "greedy_search";
        _recognizer = new OfflineRecognizer(asr);

        var kokoro = Path.Combine(models, fullPrecisionKokoro ? "kokoro-multi-lang-v1_1" : "kokoro-int8-multi-lang-v1_1");
        var tts = new OfflineTtsConfig();
        tts.Model.Kokoro.Model = Path.Combine(kokoro, fullPrecisionKokoro ? "model.onnx" : "model.int8.onnx");
        tts.Model.Kokoro.Voices = Path.Combine(kokoro, "voices.bin");
        tts.Model.Kokoro.Tokens = Path.Combine(kokoro, "tokens.txt");
        tts.Model.Kokoro.DataDir = Path.Combine(kokoro, "espeak-ng-data");
        tts.Model.Kokoro.DictDir = englishOnlyFrontEnd ? "" : Path.Combine(kokoro, "dict");
        tts.Model.Kokoro.Lexicon = englishOnlyFrontEnd
            ? Path.Combine(kokoro, "lexicon-us-en.txt")
            : $"{Path.Combine(kokoro, "lexicon-us-en.txt")},{Path.Combine(kokoro, "lexicon-zh.txt")}";
        if (ttsEngine == "piper")
        {
            // Piper (VITS) instead, for comparison: a lighter model with lower voice quality.
            var piper = Path.Combine(models, "vits-piper-en_US-libritts_r-medium");
            tts = new OfflineTtsConfig();
            tts.Model.Vits.Model = Path.Combine(piper, "en_US-libritts_r-medium.onnx");
            tts.Model.Vits.Tokens = Path.Combine(piper, "tokens.txt");
            tts.Model.Vits.DataDir = Path.Combine(piper, "espeak-ng-data");
        }

        tts.Model.NumThreads = threads;
        tts.Model.Provider = "cpu";
        tts.MaxNumSentences = 1; // a callback per sentence, so the first sentence can play while the rest is generated
        _tts = new OfflineTts(tts);
        TtsEngine = ttsEngine;
    }

    public int Threads { get; }
    public float Hangover { get; }
    public string TtsEngine { get; }
    public int TtsSampleRate => _tts.SampleRate;

    public VoiceActivityDetector CreateVad()
    {
        var config = new VadModelConfig();
        config.SileroVad.Model = Path.Combine(_models, "silero_vad.onnx");
        config.SileroVad.Threshold = 0.5f;
        config.SileroVad.MinSilenceDuration = Hangover;
        config.SileroVad.MinSpeechDuration = 0.25f;
        config.SileroVad.MaxSpeechDuration = 20f;
        config.SileroVad.WindowSize = 512;
        config.SampleRate = AudioUtil.SampleRate;
        config.NumThreads = 1;
        config.Provider = "cpu";
        return new VoiceActivityDetector(config, 60);
    }

    /// <summary>
    /// Feeds audio in 32 ms chunks, as a microphone would, and returns the speech it heard. The delay is how much audio
    /// had arrived after the last speech sample when the VAD closed the final segment (NaN if speech ran to the end).
    /// </summary>
    /// <summary>Audio kept from before the VAD's trigger point: soft first sounds ("skip", "ouvre") start before it fires.</summary>
    public double PreRollSeconds { get; init; } = 0.3;

    public Endpointed? Endpoint(VoiceActivityDetector vad, float[] audio)
    {
        vad.Reset();
        long first = -1, last = -1;
        var segments = 0;
        var delay = double.NaN;
        const int chunk = 512;
        for (var offset = 0; offset < audio.Length; offset += chunk)
        {
            var end = Math.Min(offset + chunk, audio.Length);
            vad.AcceptWaveform(audio[offset..end]);
            while (!vad.IsEmpty())
            {
                var segment = vad.Front();
                first = first < 0 ? segment.Start : first;
                last = segment.Start + segment.Samples.Length;
                segments++;
                delay = (end - last) * 1000.0 / AudioUtil.SampleRate;
                vad.Pop();
            }
        }

        vad.Flush();
        while (!vad.IsEmpty())
        {
            var segment = vad.Front();
            first = first < 0 ? segment.Start : first;
            last = segment.Start + segment.Samples.Length;
            segments++;
            delay = double.NaN;
            vad.Pop();
        }

        if (segments == 0)
        {
            return null;
        }

        // The utterance runs continuously from just before the first segment to the end of the last one.
        var start = Math.Max(0, first - (long)(PreRollSeconds * AudioUtil.SampleRate));
        return new Endpointed(audio[(int)start..(int)Math.Min(last, audio.Length)], segments, delay);
    }

    public (string Text, double Ms) Transcribe(float[] samples)
    {
        var clock = Stopwatch.StartNew();
        using var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(AudioUtil.SampleRate, samples);
        _recognizer.Decode(stream);
        return (stream.Result.Text.Trim(), clock.Elapsed.TotalMilliseconds);
    }

    /// <summary>Synthesises a reply in memory (nothing is played): time to the first audio chunk, total time, samples.</summary>
    public (double FirstChunkMs, double TotalMs, float[] Samples) Speak(string text, int speaker)
    {
        double? first = null;
        var clock = Stopwatch.StartNew();
        int OnChunk(IntPtr samples, int n)
        {
            first ??= clock.Elapsed.TotalMilliseconds;
            return 1;
        }

        var audio = _tts.GenerateWithCallback(text, 1.0f, speaker, OnChunk); // has Dispose() but not IDisposable
        try
        {
            var total = clock.Elapsed.TotalMilliseconds;
            return (first ?? total, total, audio.Samples);
        }
        finally
        {
            audio.Dispose();
        }
    }

    public void Dispose()
    {
        _recognizer.Dispose();
        _tts.Dispose();
    }
}
