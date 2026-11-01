using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace S6.Host;

/// <summary>Runs the host the way Chrome does, with this process playing the extension, so the host is checked without Chrome.</summary>
internal static class SelfTest
{
    public static async Task<int> RunAsync(CancellationToken ct)
    {
        var report = new StringBuilder();
        using var host = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "chrome-extension://selftest/")
            { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false })!;
        var toHost = host.StandardInput.BaseStream;
        var forwarded = new ConcurrentQueue<string>();
        var extension = Task.Run(() =>
        {
            while (NativeMessaging.ReadFrame(host.StandardOutput.BaseStream) is { } json)
            {
                using var doc = JsonDocument.Parse(json);
                var cmd = doc.RootElement.GetProperty("cmd").GetString()!;
                forwarded.Enqueue(cmd);
                var reply = JsonSerializer.Serialize(new { id = doc.RootElement.GetProperty("id").GetInt32(), ok = true, result = new { cmd }, error = (string?)null });
                toHost.Write(NativeMessaging.Frame(reply));
                toHost.Flush();
            }
        }, ct);

        Probe.Line(report, $"## S6 host selftest, {DateTime.Now:yyyy-MM-dd HH:mm} (this process stands in for the extension)");
        await using (var client = await BridgeClient.ConnectAsync(TimeSpan.FromSeconds(10), ct))
        {
            var pings = new List<double>();
            for (var i = 0; i < 200; i++)
            {
                pings.Add((await client.SendAsync("ping", null, ct)).Milliseconds);
            }

            Probe.Line(report, $"- Ping through the host: p50 {Probe.Percentile(pings, 0.5):F2} ms, p90 {Probe.Percentile(pings, 0.9):F2} ms, max {pings.Max():F2} ms over 200");
            var (exec, _) = await client.SendRawAsync(9001, "{\"id\":9001,\"cmd\":\"exec\",\"args\":{\"command\":\"calc\"}}", ct);
            var (garbage, _) = await client.SendRawAsync(0, "not json", ct);
            var (read, _) = await client.SendAsync("read", new { }, ct);
            Probe.Line(report, $"- \"exec\" refused by the host: {!exec.GetProperty("ok").GetBoolean()}, and never reached the extension: {!forwarded.Contains("exec")}");
            Probe.Line(report, $"- Malformed line refused: {!garbage.GetProperty("ok").GetBoolean()}; a valid read still goes through: {read.GetProperty("ok").GetBoolean()}");
        }

        var clock = Stopwatch.StartNew();
        host.StandardInput.Close(); // what Chrome does when the extension disconnects
        var exited = await Task.WhenAny(host.WaitForExitAsync(ct), Task.Delay(TimeSpan.FromSeconds(5), ct)) is { IsCompletedSuccessfully: true } t && host.HasExited;
        Probe.Line(report, exited
            ? $"- Host exits when Chrome closes the connection: after {clock.Elapsed.TotalMilliseconds:F0} ms, exit code {host.ExitCode}"
            : "- Host did NOT exit within 5 s of Chrome closing the connection");
        if (!exited)
        {
            host.Kill();
        }

        await extension.WaitAsync(TimeSpan.FromSeconds(5), ct);
        Directory.CreateDirectory(Probe.ResultsFolder);
        await File.AppendAllTextAsync(Path.Combine(Probe.ResultsFolder, "results.md"), report + Environment.NewLine, ct);
        return exited ? 0 : 1;
    }
}
