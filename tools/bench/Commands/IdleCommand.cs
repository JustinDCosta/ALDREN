using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Aldren.Bench.Commands;

/// <summary>
/// Samples Aldren.Core and Aldren.App every 5 s and reports CPU (as Task Manager shows it, both processes together) and
/// private memory against Phase 1's idle targets: under 1% CPU and under 300 MB.
/// </summary>
public static class IdleCommand
{
    public const double CpuTargetPercent = 1.0;
    public const double MemoryTargetMb = 300;
    private static readonly string[] ProcessNames = ["Aldren.Core", "Aldren.App"];
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    public static async Task<int> RunAsync(string[] args)
    {
        var minutes = double.Parse(Args.Value(args, "--minutes") ?? "10", CultureInfo.InvariantCulture);
        var output = Args.Value(args, "--out") ?? @"E:\AldrenData\bench\phase1\idle.md";
        var duration = TimeSpan.FromMinutes(minutes);

        if (Snapshot() is not { Count: 2 } first)
        {
            Console.Error.WriteLine("Both Aldren.Core and Aldren.App must be running.");
            return 1;
        }

        Console.WriteLine($"Sampling Core (pid {first["Aldren.Core"].Pid}) and App (pid {first["Aldren.App"].Pid}) for {minutes:0.#} min. Leave ALDREN idle.");
        var start = Stopwatch.GetTimestamp();
        var startCpu = first.Values.Sum(s => s.Cpu.Ticks);
        var peakMb = first.Values.Sum(s => s.PrivateMb);
        var peakCpu = 0.0;
        var previous = first;
        var previousAt = start;

        while (Stopwatch.GetElapsedTime(start) < duration)
        {
            await Task.Delay(Interval);
            var now = Stopwatch.GetTimestamp();
            var sample = Snapshot();
            if (sample.Count != 2 || sample.Any(kv => kv.Value.Pid != first[kv.Key].Pid))
            {
                Console.Error.WriteLine("An ALDREN process exited or restarted during the run; the result is not valid.");
                return 1;
            }

            var cpu = CpuPercent(
                TimeSpan.FromTicks(sample.Values.Sum(s => s.Cpu.Ticks) - previous.Values.Sum(s => s.Cpu.Ticks)),
                Stopwatch.GetElapsedTime(previousAt, now),
                Environment.ProcessorCount);
            var mb = sample.Values.Sum(s => s.PrivateMb);
            peakCpu = Math.Max(peakCpu, cpu);
            peakMb = Math.Max(peakMb, mb);
            Console.WriteLine($"{Stopwatch.GetElapsedTime(start):mm\\:ss}  CPU {cpu,5:F2}%  memory {mb,6:F1} MB");
            (previous, previousAt) = (sample, now);
        }

        var wall = Stopwatch.GetElapsedTime(start);
        var average = CpuPercent(TimeSpan.FromTicks(previous.Values.Sum(s => s.Cpu.Ticks) - startCpu), wall, Environment.ProcessorCount);
        var finalMb = previous.Values.Sum(s => s.PrivateMb);
        var cpuOk = average < CpuTargetPercent;
        var memoryOk = peakMb < MemoryTargetMb;

        var line = string.Create(CultureInfo.InvariantCulture,
            $"| {DateTime.Now:yyyy-MM-dd HH:mm} | {wall.TotalMinutes:F1} | {average:F2} ({(cpuOk ? "pass" : "FAIL")}) | {peakCpu:F2} | {peakMb:F1} ({(memoryOk ? "pass" : "FAIL")}) | {finalMb:F1} | " +
            $"Core {previous["Aldren.Core"].PrivateMb:F1}, App {previous["Aldren.App"].PrivateMb:F1} |");
        Console.WriteLine();
        Console.WriteLine($"Average CPU {average:F2}% (target < {CpuTargetPercent}%): {(cpuOk ? "pass" : "FAIL")}");
        Console.WriteLine($"Peak memory {peakMb:F1} MB, final {finalMb:F1} MB (target < {MemoryTargetMb} MB): {(memoryOk ? "pass" : "FAIL")}");
        Append(output, line);
        Console.WriteLine($"Appended to {output}");
        return cpuOk && memoryOk ? 0 : 1;
    }

    /// <summary>CPU as Task Manager shows it: share of all logical processors over the wall-clock interval.</summary>
    public static double CpuPercent(TimeSpan cpu, TimeSpan wall, int logicalProcessors) =>
        wall <= TimeSpan.Zero ? 0 : cpu.TotalMilliseconds / (wall.TotalMilliseconds * logicalProcessors) * 100;

    private static Dictionary<string, (int Pid, TimeSpan Cpu, double PrivateMb)> Snapshot()
    {
        var result = new Dictionary<string, (int, TimeSpan, double)>();
        foreach (var name in ProcessNames)
        {
            var processes = Process.GetProcessesByName(name);
            try
            {
                if (processes.Length == 1)
                {
                    var p = processes[0];
                    p.Refresh();
                    result[name] = (p.Id, p.TotalProcessorTime, p.PrivateMemorySize64 / 1024.0 / 1024.0);
                }
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }
        }

        return result;
    }

    private static void Append(string path, string line)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var text = new StringBuilder();
        if (!File.Exists(path))
        {
            text.AppendLine("# Phase 1 idle check");
            text.AppendLine();
            text.AppendLine("Targets: average CPU under 1% (Core and App together, as Task Manager shows it) and memory under 300 MB (private bytes).");
            text.AppendLine();
            text.AppendLine("| When | Minutes | Average CPU % | Peak CPU % (5 s) | Peak MB | Final MB | Final split |");
            text.AppendLine("|---|---|---|---|---|---|---|");
        }

        text.AppendLine(line);
        File.AppendAllText(path, text.ToString());
    }
}
