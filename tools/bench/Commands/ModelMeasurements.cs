using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using Aldren.Bench.Eval;
using Aldren.Bench.Llama;
using Aldren.Bench.Machine;
using Aldren.Bench.Stats;

namespace Aldren.Bench.Commands;

/// <summary>Everything measured for one model: load, VRAM, warm latency, speed, eval, screenshot questions.</summary>
public sealed class ModelMeasurements(string modelsRoot, EvalSet eval, string testImagePath, bool allowBattery)
{
    private static readonly string[] WarmPrompts =
    [
        "Give me one fun fact about octopuses.",
        "What is the boiling point of water at sea level?",
        "Name a famous painting by Monet.",
        "How many legs does a spider have?",
        "What does CPU stand for?",
        "Suggest a name for a grey cat.",
        "What is the square root of 144?",
        "Which planet is closest to the sun?",
        "Give me a word that rhymes with light.",
        "What language is spoken in Brazil?",
    ];

    public async Task<ModelResult> MeasureAsync(ModelSpec spec, CancellationToken ct)
    {
        var machine = await EnvironmentSnapshot.CaptureAsync(ct);
        var valid = machine.IsValidForGpuBench(allowBattery, out var note) || (!spec.OnGpu && (machine.OnAc || allowBattery));
        Log($"{spec.Name}: power {(machine.OnAc ? "AC" : "battery")}, VRAM in use {machine.BaselineVramMiB} MiB{(valid ? "" : " (INVALID: " + note + ")")}");

        var vram = new Dictionary<int, long>();
        double loadSeconds = 0;
        if (spec.OnGpu)
        {
            foreach (var ctx in new[] { 4096, 8192, 16384 })
            {
                await using (var sized = await StartAsync(spec, ctx, projectorOnCpu: false, ct))
                {
                    vram[ctx] = await EnvironmentSnapshot.QueryVramUsedMiBAsync(ct) - machine.BaselineVramMiB;
                    if (ctx == 8192)
                    {
                        loadSeconds = sized.LoadTime.TotalSeconds;
                    }
                }

                await EnvironmentSnapshot.WaitForVramAsync(machine.BaselineVramMiB, ct);
            }

            Log($"{spec.Name}: VRAM 4K/8K/16K = {vram[4096]}/{vram[8192]}/{vram[16384]} MiB, load {loadSeconds:F1} s");
        }

        long ramMiB;
        Percentiles? warm;
        double generation;
        EvalSummary evalSummary;
        var vision = new List<VisionResult>();

        await using (var server = await StartAsync(spec, 8192, projectorOnCpu: false, ct))
        {
            if (!spec.OnGpu)
            {
                loadSeconds = server.LoadTime.TotalSeconds;
            }

            using (var process = Process.GetProcessById(server.ProcessId))
            {
                ramMiB = process.WorkingSet64 / (1024 * 1024);
            }

            warm = await WarmTtftAsync(server, ct);
            Log($"{spec.Name}: warm TTFT p50 {warm?.P50:F0} ms, p90 {warm?.P90:F0} ms");

            generation = await GenerationSpeedAsync(server, ct);
            Log($"{spec.Name}: generation {generation:F1} tokens/s");

            evalSummary = await RunEvalAsync(spec, server, ct);
            Log($"{spec.Name}: eval {evalSummary.Passed}/{evalSummary.Total} " +
                string.Join(", ", evalSummary.ByKind.Select(k => $"{k.Key} {k.Value.Passed}/{k.Value.Total}")));

            if (spec.ProjectorFile is not null && File.Exists(testImagePath))
            {
                vision.Add(await VisionAsync(server, projectorOnCpu: false, ct));
            }
        }

        if (spec.ProjectorFile is not null && File.Exists(testImagePath))
        {
            await EnvironmentSnapshot.WaitForVramAsync(machine.BaselineVramMiB, ct);
            await using var cpuProjector = await StartAsync(spec, 8192, projectorOnCpu: true, ct);
            vision.Add(await VisionAsync(cpuProjector, projectorOnCpu: true, ct));
            Log($"{spec.Name}: screenshot answer {string.Join(", ", vision.Select(v => $"{(v.ProjectorOnCpu ? "CPU" : "GPU")} projector {v.LatencyMs:F0} ms ({(v.Correct ? "correct" : "wrong")})"))}");
        }

        await EnvironmentSnapshot.WaitForVramAsync(machine.BaselineVramMiB, ct);
        var (benchPrompt, benchGeneration) = await RunLlamaBenchAsync(spec, ct);
        Log($"{spec.Name}: llama-bench pp512 {benchPrompt:F0} tokens/s, tg128 {benchGeneration:F1} tokens/s");

        return new ModelResult(spec.Name, spec.OnGpu, valid, note, machine.BaselineVramMiB, loadSeconds, vram, ramMiB,
            warm, generation, benchPrompt, benchGeneration, evalSummary, vision, Error: null);
    }

    public Task<LlamaServer> StartAsync(ModelSpec spec, int contextSize, bool projectorOnCpu, CancellationToken ct, params string[] more)
    {
        var projector = spec.ProjectorFile is null ? null : Path.Combine(modelsRoot, spec.ProjectorFile);
        var options = new LlamaServerOptions(Path.Combine(modelsRoot, spec.ModelFile), projector, contextSize, projectorOnCpu,
            ExtraArgs: [.. BaseArgs(spec), .. more]);
        return LlamaServer.StartAsync(options, ct);
    }

    // One slot (the Quick Brain serves one user) and no hidden "thinking" before the answer.
    private static IEnumerable<string> BaseArgs(ModelSpec spec) =>
        spec.OnGpu ? ["-np", "1", "--reasoning-budget", "0"] : ["-np", "1", "--reasoning-budget", "0", "-ngl", "0"];

    private async Task<Percentiles?> WarmTtftAsync(LlamaServer server, CancellationToken ct)
    {
        var samples = new List<double>();
        foreach (var prompt in WarmPrompts.Prepend("Hello."))
        {
            var request = new ChatRequest([ChatMessage.Of("system", eval.SystemPrompt), ChatMessage.Of("user", prompt)], MaxTokens: 24, Tools: eval.Tools);
            try
            {
                var timing = await StreamingProbe.MeasureAsync(server.BaseUri, server.ApiKey, request, ct);
                samples.Add(timing.TtftMs);
            }
            catch (InvalidOperationException)
            {
                // The model answered with a tool call instead of text; that prompt yields no TTFT sample.
            }
        }

        // The first request fills the prompt cache, so it is not a warm sample.
        return samples.Count > 1 ? Percentiles.Of(samples.Skip(1).ToList()) : null;
    }

    private static async Task<double> GenerationSpeedAsync(LlamaServer server, CancellationToken ct)
    {
        var rates = new List<double>();
        for (var i = 0; i < 3; i++)
        {
            var request = new ChatRequest(
                [ChatMessage.Of("user", "Write a detailed story of about 400 words about a lighthouse keeper who finds a message in a bottle.")],
                MaxTokens: 256);
            rates.Add((await StreamingProbe.MeasureAsync(server.BaseUri, server.ApiKey, request, ct)).TokensPerSecond);
        }

        return Percentiles.Of(rates).P50;
    }

    private async Task<EvalSummary> RunEvalAsync(ModelSpec spec, LlamaServer server, CancellationToken ct)
    {
        var results = new List<(EvalCase Case, EvalResult Result)>();
        var failures = new List<EvalResult>();
        var latencies = new List<double>();
        var promptTokens = new List<double>();

        foreach (var c in eval.Cases)
        {
            try
            {
                var (json, ms) = await ChatClient.CompleteAsync(server.BaseUri, server.ApiKey, eval.RequestFor(c), ct);
                latencies.Add(ms);
                if (JsonNode.Parse(json)?["usage"]?["prompt_tokens"] is JsonValue tokens)
                {
                    promptTokens.Add(tokens.GetValue<int>());
                }

                var reply = EvalRunner.ParseResponse(json);
                var result = EvalScorer.Score(c, reply);
                results.Add((c, result));
                if (!result.Passed)
                {
                    failures.Add(result with { Reason = $"{result.Reason} | {Describe(reply)}" });
                }
            }
            catch (HttpRequestException ex)
            {
                var result = new EvalResult(c.Id, false, $"request failed: {ex.Message}");
                results.Add((c, result));
                failures.Add(result);
            }
        }

        var byKind = results.GroupBy(r => r.Case.Kind)
            .ToDictionary(g => g.Key, g => new KindScore(g.Count(r => r.Result.Passed), g.Count()));
        Log($"{spec.Name}: {failures.Count} eval failures");

        return new EvalSummary(results.Count(r => r.Result.Passed), results.Count, byKind,
            latencies.Count > 0 ? Percentiles.Of(latencies).P50 : 0,
            promptTokens.Count > 0 ? (int)Percentiles.Of(promptTokens).P50 : 0,
            failures);
    }

    private static string Describe(ModelResponse reply)
    {
        var text = reply.Text.Length > 120 ? reply.Text[..120] + "…" : reply.Text;
        return reply.ToolName is null ? $"text: {text}" : $"{reply.ToolName} {reply.ToolArgs?.ToJsonString() ?? "(malformed args)"}";
    }

    private async Task<VisionResult> VisionAsync(LlamaServer server, bool projectorOnCpu, CancellationToken ct)
    {
        var image = "data:image/png;base64," + Convert.ToBase64String(await File.ReadAllBytesAsync(testImagePath, ct));
        var content = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = "What does this dialog say, and which buttons does it have? Answer in one sentence." },
            new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = image } },
        };
        var body = new ChatRequest([new ChatMessage("user", content)], MaxTokens: 96).ToJson(stream: false);
        body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };

        var (json, ms) = await ChatClient.CompleteAsync(server.BaseUri, server.ApiKey, body, ct);
        var answer = EvalRunner.ParseResponse(json).Text;
        var correct = new[] { "report.docx", "retry", "cancel" }.All(word => answer.Contains(word, StringComparison.OrdinalIgnoreCase));
        return new VisionResult(projectorOnCpu, ms, correct, answer);
    }

    private async Task<(double Prompt, double Generation)> RunLlamaBenchAsync(ModelSpec spec, CancellationToken ct)
    {
        var bench = Path.Combine(Path.GetDirectoryName(LlamaServer.ExecutablePath)!, "llama-bench.exe");
        var psi = new ProcessStartInfo(bench)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[] { "-m", Path.Combine(modelsRoot, spec.ModelFile), "-p", "512", "-n", "128", "-fa", "1", "-r", "2", "-o", "json",
                     "-ngl", spec.OnGpu ? "99" : "0" })
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("llama-bench did not start.");
        var errorTask = process.StandardError.ReadToEndAsync(ct);
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        await errorTask;
        return process.ExitCode == 0 ? LlamaBench.Parse(output) : (0, 0);
    }

    internal static void Log(string message) =>
        Console.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] {message}");
}
