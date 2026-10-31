using System.Globalization;
using System.Text;
using System.Text.Json;
using Aldren.Bench.Eval;

namespace Aldren.Bench.Commands;

/// <summary>Phase 0 / S1: benchmarks every candidate model and writes results.json and summary.md.</summary>
public static class ModelsCommand
{
    private const string ModelsRoot = @"E:\AldrenData\models";
    private const string BenchRoot = @"E:\AldrenData\bench";
    private const string TestImage = @"E:\AldrenData\bench\s1\test-dialog.png";

    public static readonly ModelSpec[] Candidates =
    [
        new("qwen3.5-4b", @"qwen3.5-4b\Qwen3.5-4B-Q4_K_M.gguf", @"qwen3.5-4b\mmproj-F16.gguf", OnGpu: true),
        new("qwen3.5-2b", @"qwen3.5-2b\Qwen3.5-2B-Q4_K_M.gguf", null, OnGpu: true),
        new("gemma-4-e2b", @"gemma-4-e2b\gemma-4-E2B_q4_0-it.gguf", @"gemma-4-e2b\gemma-4-E2B-it-mmproj.gguf", OnGpu: true),
        new("granite-4.2-3b", @"granite-4.2-3b\granite-4.2-3b-Q4_K_M.gguf", null, OnGpu: true),
        new("qwen3.5-9b-q3", @"qwen3.5-9b\Qwen3.5-9B-Q3_K_M.gguf", null, OnGpu: true),
        new("lfm2.5-1.2b-cpu", @"lfm2.5-1.2b\LFM2.5-1.2B-Instruct-Q4_K_M.gguf", null, OnGpu: false),
        new("qwen3.5-0.8b-cpu", @"qwen3.5-0.8b\Qwen3.5-0.8B-Q4_K_M.gguf", null, OnGpu: false),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static async Task<int> RunAsync(string[] args)
    {
        var only = Args.Value(args, "--only")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var specs = only is null ? Candidates : Candidates.Where(s => only.Contains(s.Name)).ToArray();
        var outDir = Path.Combine(BenchRoot, "s1", DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(outDir);

        var eval = EvalSet.Load(Path.Combine(AppContext.BaseDirectory, "eval"));
        var measurements = new ModelMeasurements(ModelsRoot, eval, TestImage, Args.Flag(args, "--allow-battery"));
        using var cts = new CancellationTokenSource(TimeSpan.FromHours(3));

        var results = new List<ModelResult>();
        foreach (var spec in specs)
        {
            try
            {
                results.Add(await measurements.MeasureAsync(spec, cts.Token));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ModelMeasurements.Log($"{spec.Name}: FAILED {ExceptionText.Chain(ex)}");
                Console.WriteLine(ex);
                results.Add(new ModelResult(spec.Name, spec.OnGpu, false, "", 0, 0, [], 0, null, 0, 0, 0, null, [], ExceptionText.Chain(ex)));
            }

            await SaveAsync(outDir, results, null, null);
        }

        List<SwitchResult>? switching = null;
        SlotResult? slot = null;
        if (!Args.Flag(args, "--skip-switch"))
        {
            var quick = Candidates.Single(s => s.Name == "qwen3.5-4b");
            var deep = Candidates.Single(s => s.Name == "qwen3.5-9b-q3");
            var switches = new SwitchMeasurements(measurements, quick, deep, ModelsRoot, BenchRoot, eval);
            switching = await switches.MeasureSwitchingAsync(cts.Token);
            slot = await switches.MeasureSlotRestoreAsync(cts.Token);
        }

        await SaveAsync(outDir, results, switching, slot);
        Console.WriteLine($"Results: {outDir}");
        return 0;
    }

    private static async Task SaveAsync(string outDir, List<ModelResult> results, List<SwitchResult>? switching, SlotResult? slot)
    {
        var json = JsonSerializer.Serialize(new { results, switching, slot }, JsonOptions);
        await File.WriteAllTextAsync(Path.Combine(outDir, "results.json"), json);
        await File.WriteAllTextAsync(Path.Combine(outDir, "summary.md"), Summary(results, switching, slot));
    }

    private static string Summary(List<ModelResult> results, List<SwitchResult>? switching, SlotResult? slot)
    {
        static string Score(EvalSummary? e, string kind) =>
            e is not null && e.ByKind.TryGetValue(kind, out var k) ? $"{k.Passed}/{k.Total}" : "-";

        var md = new StringBuilder();
        md.AppendLine("| Model | Device | Load s | VRAM 4K/8K/16K MiB | Server RAM MiB | Warm TTFT p50/p90 ms | Gen tok/s | pp512 / tg128 | Route | Tool | Injection | Persona | Total | Eval p50 ms | Prompt tokens | Screenshot GPU / CPU projector |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var r in results)
        {
            if (r.Error is not null)
            {
                md.AppendLine($"| {r.Name} | failed: {r.Error} |||||||||||||||");
                continue;
            }

            var vram = r.OnGpu ? $"{r.VramMiBByContext.GetValueOrDefault(4096)}/{r.VramMiBByContext.GetValueOrDefault(8192)}/{r.VramMiBByContext.GetValueOrDefault(16384)}" : "-";
            var warm = r.WarmTtftMs is null ? "-" : $"{r.WarmTtftMs.P50:F0}/{r.WarmTtftMs.P90:F0}";
            var vision = r.Vision.Count == 0 ? "-" : string.Join(" / ", r.Vision.Select(v => $"{v.LatencyMs:F0} ms {(v.Correct ? "✓" : "✗")}"));
            md.AppendLine(CultureInfo.InvariantCulture, $"| {r.Name}{(r.ValidEnvironment ? "" : " (invalid env)")} | {(r.OnGpu ? "GPU" : "CPU")} | {r.LoadSeconds:F1} | {vram} | {r.ServerRamMiB} | {warm} | {r.GenerationTokensPerSecond:F1} | {r.BenchPromptTokensPerSecond:F0} / {r.BenchGenerationTokensPerSecond:F1} | {Score(r.Eval, "route")} | {Score(r.Eval, "tool")} | {Score(r.Eval, "injection")} | {Score(r.Eval, "persona")} | {r.Eval?.Passed}/{r.Eval?.Total} | {r.Eval?.MedianLatencyMs:F0} | {r.Eval?.MedianPromptTokens} | {vision} |");
        }

        if (switching is not null)
        {
            md.AppendLine();
            foreach (var s in switching)
            {
                md.AppendLine(s.Error is null ? $"- Switch 4B → 9B via {s.Method}: {s.SwitchMs:F0} ms to first token" : $"- Switch via {s.Method} failed: {s.Error}");
            }
        }

        if (slot is not null)
        {
            md.AppendLine(slot.Error is null
                ? $"- Reload with saved prompt cache: first token {slot.RestoredTtftMs:F0} ms vs {slot.ColdTtftMs:F0} ms cold"
                : $"- Slot restore failed: {slot.Error}");
        }

        return md.ToString();
    }
}
