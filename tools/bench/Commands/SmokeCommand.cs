using System.Globalization;
using Aldren.Bench.Llama;
using Aldren.Bench.Machine;

namespace Aldren.Bench.Commands;

/// <summary>Loads one model, sends one short prompt and prints load time, TTFT and speed.</summary>
public static class SmokeCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var model = Args.Value(args, "--model");
        if (model is null)
        {
            Console.Error.WriteLine("usage: aldren-bench smoke --model <path.gguf> [--ctx 4096]");
            return 2;
        }

        var contextSize = int.Parse(Args.Value(args, "--ctx") ?? "4096", CultureInfo.InvariantCulture);
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));

        var machine = await EnvironmentSnapshot.CaptureAsync(cts.Token);
        Console.WriteLine($"Power: {(machine.OnAc ? "AC" : "battery")}, plan: {machine.PowerPlan}, " +
                          $"GPU limit: {machine.GpuPowerLimitW} W, VRAM already in use: {machine.BaselineVramMiB} MiB");
        if (!machine.IsValidForGpuBench(allowBattery: false, out var reason))
        {
            Console.WriteLine($"Note: not valid for GPU measurements. {reason}");
        }

        int serverPid;
        await using (var server = await LlamaServer.StartAsync(new LlamaServerOptions(model, null, contextSize, false), cts.Token))
        {
            serverPid = server.ProcessId;
            Console.WriteLine($"Loaded in {server.LoadTime.TotalSeconds:F1} s (log: {server.LogPath})");

            var request = new ChatRequest([ChatMessage.Of("user", "Say hello in five words.")], MaxTokens: 64);
            var timing = await StreamingProbe.MeasureAsync(server.BaseUri, server.ApiKey, request, cts.Token);
            Console.WriteLine($"TTFT {timing.TtftMs:F0} ms, {timing.TokensPerSecond:F1} tokens/s, {timing.OutputTokens} tokens");
            Console.WriteLine($"Reply: {timing.Text.Trim()}");
        }

        var stillRunning = IsRunning(serverPid);
        Console.WriteLine(stillRunning ? $"llama-server (pid {serverPid}) is STILL RUNNING" : $"llama-server (pid {serverPid}) stopped");
        return stillRunning ? 1 : 0;
    }

    // Windows keeps an exited process listed while another program (here the GPU driver) still holds a handle
    // to it, so "listed" is not the same as "running".
    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
