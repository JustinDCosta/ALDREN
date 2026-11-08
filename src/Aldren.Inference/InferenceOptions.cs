namespace Aldren.Inference;

/// <summary>Where the pinned llama.cpp build and models live, and the residency timings (ARCHITECTURE §7).</summary>
public sealed record InferenceOptions
{
    public string DataRoot { get; init; } = Environment.GetEnvironmentVariable("ALDREN_DATA_ROOT") ?? @"E:\AldrenData";

    public string LlamaServerPath => Path.Combine(DataRoot, @"tools\llama.cpp\b12023\llama-server.exe");
    public string QuickModelPath => Path.Combine(DataRoot, @"models\qwen3.5-4b\Qwen3.5-4B-Q4_K_M.gguf");
    public string ProjectorPath => Path.Combine(DataRoot, @"models\qwen3.5-4b\mmproj-F16.gguf");
    public string DeepModelPath => Path.Combine(DataRoot, @"models\qwen3.5-9b\Qwen3.5-9B-Q3_K_M.gguf");
    public string CpuModelPath => Path.Combine(DataRoot, @"models\qwen3.5-0.8b\Qwen3.5-0.8B-Q4_K_M.gguf");

    public int ContextSize { get; init; } = 8192;

    /// <summary>Free VRAM needed before the 4B loads (3,856 MiB measured in S1, plus headroom).</summary>
    public long RequiredFreeVramMiB { get; init; } = 4200;

    public TimeSpan PreloadDelay { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan IdleUnloadOnAc { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan IdleUnloadOnBattery { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan CpuIdleUnload { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromMinutes(2);
}
