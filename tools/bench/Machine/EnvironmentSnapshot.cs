using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Aldren.Bench.Machine;

public record GpuProcess(int Pid, string Name, long UsedMiB);

/// <summary>The machine state a GPU measurement depends on, recorded with every result.</summary>
public record EnvironmentSnapshot(bool OnAc, string PowerPlan, int GpuPowerLimitW, long BaselineVramMiB, IReadOnlyList<GpuProcess> GpuProcesses)
{
    /// <summary>Above this much VRAM already in use by other apps, VRAM and latency numbers aren't comparable.</summary>
    public const long MaxBaselineVramMiB = 1200;

    public bool IsValidForGpuBench(bool allowBattery, out string reason)
    {
        if (!OnAc && !allowBattery)
        {
            reason = "Running on battery: plug in, or pass --allow-battery and treat the results as throttled.";
            return false;
        }

        if (BaselineVramMiB > MaxBaselineVramMiB)
        {
            reason = $"Other apps already use {BaselineVramMiB} MiB of VRAM (limit {MaxBaselineVramMiB} MiB): close GPU-heavy apps first.";
            return false;
        }

        reason = "";
        return true;
    }

    public static async Task<EnvironmentSnapshot> CaptureAsync(CancellationToken ct)
    {
        var plan = await RunAsync("powercfg", "/getactivescheme", ct);
        var gpu = await RunAsync("nvidia-smi", "--query-gpu=power.limit,power.max_limit,memory.used --format=csv,noheader,nounits", ct);
        var apps = await RunAsync("nvidia-smi", "--query-compute-apps=pid,process_name,used_memory --format=csv,noheader,nounits", ct);

        var (powerLimitW, usedMiB) = NvidiaSmi.ParseGpuQuery(gpu.Trim());
        return new EnvironmentSnapshot(PowerStatus.IsOnAc(), PlanName(plan), powerLimitW, usedMiB, NvidiaSmi.ParseComputeApps(apps));
    }

    // "Power Scheme GUID: 8c5e7fda-... (High performance)" -> "High performance"
    private static string PlanName(string powercfgOutput)
    {
        var match = Regex.Match(powercfgOutput, @"\(([^)]*)\)\s*$");
        return match.Success ? match.Groups[1].Value : powercfgOutput.Trim();
    }

    private static async Task<string> RunAsync(string fileName, string arguments, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {fileName}.");
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);
        return output;
    }
}

public static class NvidiaSmi
{
    /// <summary>Parses "power.limit, power.max_limit, memory.used"; laptops may report the limit as [N/A].</summary>
    public static (int PowerLimitW, long UsedMiB) ParseGpuQuery(string line)
    {
        var parts = line.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            throw new FormatException($"Unexpected nvidia-smi output: '{line}'.");
        }

        var limit = Watts(parts[0]) ?? Watts(parts[1]) ?? 0;
        return (limit, long.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    public static IReadOnlyList<GpuProcess> ParseComputeApps(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(',', StringSplitOptions.TrimEntries))
            .Where(parts => parts.Length >= 3 && int.TryParse(parts[0], CultureInfo.InvariantCulture, out _))
            .Select(parts => new GpuProcess(
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                parts[1],
                long.TryParse(parts[2], CultureInfo.InvariantCulture, out var mib) ? mib : 0))
            .ToList();

    private static int? Watts(string value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var watts) ? (int)Math.Round(watts) : null;
}

internal static class PowerStatus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus status);

    // ACLineStatus: 0 = on battery, 1 = plugged in, 255 = unknown (treated as not plugged in).
    public static bool IsOnAc() => GetSystemPowerStatus(out var status) && status.ACLineStatus == 1;
}
