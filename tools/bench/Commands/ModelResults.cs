using Aldren.Bench.Eval;
using Aldren.Bench.Stats;

namespace Aldren.Bench.Commands;

public record ModelSpec(string Name, string ModelFile, string? ProjectorFile, bool OnGpu);

public record KindScore(int Passed, int Total);

public record EvalSummary(
    int Passed,
    int Total,
    Dictionary<string, KindScore> ByKind,
    double MedianLatencyMs,
    int MedianPromptTokens,
    List<EvalResult> Failures);

public record VisionResult(bool ProjectorOnCpu, double LatencyMs, bool Correct, string Answer);

public record ModelResult(
    string Name,
    bool OnGpu,
    bool ValidEnvironment,
    string EnvironmentNote,
    long BaselineVramMiB,
    double LoadSeconds,
    Dictionary<int, long> VramMiBByContext,
    long ServerRamMiB,
    Percentiles? WarmTtftMs,
    double GenerationTokensPerSecond,
    double BenchPromptTokensPerSecond,
    double BenchGenerationTokensPerSecond,
    EvalSummary? Eval,
    List<VisionResult> Vision,
    string? Error);

public record SwitchResult(string Method, double SwitchMs, string? Error);

public record SlotResult(double ColdTtftMs, double RestoredTtftMs, string? Error);
