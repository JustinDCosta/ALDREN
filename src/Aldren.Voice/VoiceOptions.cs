namespace Aldren.Voice;

/// <summary>Model paths and voice timings (ARCHITECTURE §10).</summary>
public sealed record VoiceOptions
{
    public const int SampleRate = 16_000;

    public string ModelsRoot { get; init; } =
        Path.Combine(Environment.GetEnvironmentVariable("ALDREN_DATA_ROOT") ?? @"E:\AldrenData", @"voice\models");

    public string SileroVad => Path.Combine(ModelsRoot, "silero_vad.onnx");
    public string Parakeet => Path.Combine(ModelsRoot, "sherpa-onnx-nemo-parakeet-tdt-0.6b-v3-int8");
    public string Supertonic => Path.Combine(ModelsRoot, "sherpa-onnx-supertonic-3-tts-int8-2026-05-11");
    public string WakeFeatures => Path.Combine(ModelsRoot, "openwakeword");
    public string WakeModel => Path.Combine(ModelsRoot, "aldren-wakeword-4.npz");

    /// <summary>Supertonic preset voice 5 (ADR-0011).</summary>
    public int Voice { get; init; } = 5;

    /// <summary>0.6 s, not the 0.4 s planned: live, a short pause mid-command ended the utterance ("It's just um").</summary>
    public float EndOfSpeechSeconds { get; init; } = 0.6f;
    public float PreRollSeconds { get; init; } = 0.3f;

    /// <summary>How long to wait for speech to start after you press the hotkey.</summary>
    public TimeSpan NoSpeechTimeout { get; init; } = TimeSpan.FromSeconds(6);

    public TimeSpan MaxUtterance { get; init; } = TimeSpan.FromSeconds(20);
    public TimeSpan FollowUpWindow { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>Model 4 at 0.95 (Phase 0: 3.7% misses at 0.9; false wakes fall as the threshold rises).</summary>
    public float WakeThreshold { get; init; } = 0.95f;

    /// <summary>Other apps' volume while ALDREN speaks.</summary>
    public float DuckLevel { get; init; } = 0.3f;

    public int Threads { get; init; } = 4;
}
