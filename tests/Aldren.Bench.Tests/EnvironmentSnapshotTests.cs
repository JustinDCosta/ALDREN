using Aldren.Bench.Machine;

namespace Aldren.Bench.Tests;

public class EnvironmentSnapshotTests
{
    private static EnvironmentSnapshot Snapshot(bool onAc = true, long vramMiB = 800) =>
        new(onAc, "High performance", 95, vramMiB, Array.Empty<GpuProcess>());

    [Fact]
    public void Gpu_bench_invalid_on_battery()
    {
        Assert.False(Snapshot(onAc: false).IsValidForGpuBench(allowBattery: false, out var reason));
        Assert.Contains("battery", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Gpu_bench_invalid_when_baseline_vram_above_1200_MiB()
    {
        Assert.False(Snapshot(vramMiB: 1300).IsValidForGpuBench(allowBattery: false, out var reason));
        Assert.Contains("1300", reason);
    }

    [Fact]
    public void Gpu_bench_valid_at_exactly_1200_MiB()
    {
        Assert.True(Snapshot(vramMiB: 1200).IsValidForGpuBench(allowBattery: false, out _));
    }

    [Fact]
    public void Gpu_bench_on_battery_allowed_with_flag()
    {
        Assert.True(Snapshot(onAc: false).IsValidForGpuBench(allowBattery: true, out _));
    }

    [Theory]
    [InlineData("80.00, 95.00, 768", 80, 768)]
    [InlineData("[N/A], 95.00, 768", 95, 768)] // what this laptop reports: no current power limit
    public void Gpu_query_line_is_parsed(string line, int expectedPowerW, long expectedUsedMiB)
    {
        var (powerW, usedMiB) = NvidiaSmi.ParseGpuQuery(line);

        Assert.Equal(expectedPowerW, powerW);
        Assert.Equal(expectedUsedMiB, usedMiB);
    }

    [Fact]
    public void Compute_apps_are_parsed_and_blank_lines_skipped()
    {
        var output = "1234, C:\\llama\\llama-server.exe, 2600\n\n5678, C:\\py\\python.exe, 310\n";

        var apps = NvidiaSmi.ParseComputeApps(output);

        Assert.Equal(
            new[] { new GpuProcess(1234, "C:\\llama\\llama-server.exe", 2600), new GpuProcess(5678, "C:\\py\\python.exe", 310) },
            apps);
    }
}
