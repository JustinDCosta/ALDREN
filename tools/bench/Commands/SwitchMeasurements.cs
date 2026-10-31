using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Aldren.Bench.Eval;
using Aldren.Bench.Llama;

namespace Aldren.Bench.Commands;

/// <summary>How fast the Quick model can hand over to the 9B, and whether a saved prompt cache speeds up a reload.</summary>
public sealed class SwitchMeasurements(ModelMeasurements models, ModelSpec quick, ModelSpec deep, string modelsRoot, string benchRoot, EvalSet eval)
{
    public async Task<List<SwitchResult>> MeasureSwitchingAsync(CancellationToken ct) =>
    [
        await TryAsync("restart process", () => RestartSwitchAsync(ct)),
        await TryAsync("router mode", () => RouterSwitchAsync(ct)),
    ];

    public async Task<SlotResult> MeasureSlotRestoreAsync(CancellationToken ct)
    {
        try
        {
            var slots = Path.Combine(benchRoot, "slots");
            Directory.CreateDirectory(slots);
            var warmup = WithSystemPrompt("Hello.");
            var question = WithSystemPrompt("What is the capital of Italy?");

            await using (var server = await models.StartAsync(quick, 8192, false, ct, "--slot-save-path", slots))
            {
                await StreamingProbe.MeasureAsync(server.BaseUri, server.ApiKey, warmup, ct);
                await ChatClient.PostAsync(server.BaseUri, server.ApiKey, "slots/0?action=save", new JsonObject { ["filename"] = "quick-prefix.bin" }, ct);
            }

            double cold;
            await using (var server = await models.StartAsync(quick, 8192, false, ct, "--slot-save-path", slots))
            {
                cold = (await StreamingProbe.MeasureAsync(server.BaseUri, server.ApiKey, question, ct)).TtftMs;
            }

            await using (var server = await models.StartAsync(quick, 8192, false, ct, "--slot-save-path", slots))
            {
                await ChatClient.PostAsync(server.BaseUri, server.ApiKey, "slots/0?action=restore", new JsonObject { ["filename"] = "quick-prefix.bin" }, ct);
                var restored = (await StreamingProbe.MeasureAsync(server.BaseUri, server.ApiKey, question, ct)).TtftMs;
                ModelMeasurements.Log($"slot restore: first-token time {cold:F0} ms cold vs {restored:F0} ms with a restored prompt cache");
                return new SlotResult(cold, restored, null);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ModelMeasurements.Log($"slot restore FAILED: {ex.Message}");
            return new SlotResult(0, 0, ex.Message);
        }
    }

    private async Task<double> RestartSwitchAsync(CancellationToken ct)
    {
        var quickServer = await models.StartAsync(quick, 8192, false, ct);
        await StreamingProbe.MeasureAsync(quickServer.BaseUri, quickServer.ApiKey, Short(), ct);

        var clock = Stopwatch.StartNew();
        await quickServer.DisposeAsync();
        await using var deepServer = await models.StartAsync(deep, 8192, false, ct);
        var beforeRequest = clock.Elapsed.TotalMilliseconds;
        var timing = await StreamingProbe.MeasureAsync(deepServer.BaseUri, deepServer.ApiKey, Short(), ct);
        return beforeRequest + timing.TtftMs;
    }

    private async Task<double> RouterSwitchAsync(CancellationToken ct)
    {
        var folder = Path.Combine(benchRoot, "router-models");
        Directory.CreateDirectory(folder);
        foreach (var spec in new[] { quick, deep })
        {
            var source = Path.Combine(modelsRoot, spec.ModelFile);
            var link = Path.Combine(folder, Path.GetFileName(source));
            // A hard link avoids copying gigabytes; both paths are on E:.
            if (!File.Exists(link) && !CreateHardLink(link, source, IntPtr.Zero))
            {
                throw new IOException($"Could not link {source}: error {Marshal.GetLastWin32Error()}.");
            }
        }

        string[] args = ["-c", "8192", "-np", "1", "-ngl", "999", "-fa", "on", "--reasoning-budget", "0", "--cache-type-k", "q8_0", "--cache-type-v", "q8_0"];
        await using var router = await LlamaServer.StartRouterAsync(folder, 1, args, ct);

        var listing = JsonNode.Parse(await ChatClient.GetAsync(router.BaseUri, router.ApiKey, "v1/models", ct));
        var ids = listing?["data"]?.AsArray().Select(m => m?["id"]?.GetValue<string>() ?? "").ToList() ?? [];
        var quickId = ids.First(id => id.Contains("4B", StringComparison.OrdinalIgnoreCase));
        var deepId = ids.First(id => id.Contains("9B", StringComparison.OrdinalIgnoreCase));

        await StreamingProbe.MeasureAsync(router.BaseUri, router.ApiKey, Short() with { Model = quickId }, ct);
        var timing = await StreamingProbe.MeasureAsync(router.BaseUri, router.ApiKey, Short() with { Model = deepId }, ct);
        return timing.TtftMs;
    }

    private ChatRequest WithSystemPrompt(string user) =>
        new([ChatMessage.Of("system", eval.SystemPrompt), ChatMessage.Of("user", user)], MaxTokens: 16, Tools: eval.Tools);

    private static ChatRequest Short() => new([ChatMessage.Of("user", "Name three primary colours.")], MaxTokens: 16);

    private static async Task<SwitchResult> TryAsync(string method, Func<Task<double>> measure)
    {
        try
        {
            var ms = await measure();
            ModelMeasurements.Log($"switch to 9B via {method}: {ms:F0} ms to first token");
            return new SwitchResult(method, ms, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            ModelMeasurements.Log($"switch via {method} FAILED: {ex.Message}");
            return new SwitchResult(method, 0, ex.Message);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
}
