using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace S6.Host;

/// <summary>
/// The real measurement: serves the test page, opens it in the test profile (where the extension is loaded) and drives it through
/// the extension. Every check goes to the console and to results.md.
/// </summary>
internal static class Probe
{
    public const string ResultsFolder = @"E:\AldrenData\bench\s6";
    public const string TestProfile = @"E:\AldrenData\browser-profile";

    public static async Task<int> RunAsync(CancellationToken ct)
    {
        var report = new StringBuilder();
        await using var server = TestPageServer.Start(Path.Combine(AppContext.BaseDirectory, "testpage", "index.html"));
        BridgeClient client;
        try
        {
            client = await BridgeClient.ConnectAsync(TimeSpan.FromSeconds(20), ct);
        }
        catch (TimeoutException)
        {
            Console.Error.WriteLine("The bridge host isn't running. Check that register-host.ps1 ran and that the ALDREN bridge probe " +
                                    "extension is loaded and enabled in the test profile.");
            return 1;
        }

        await using (client)
        {
            OpenInTestProfile(server.Url);
            Console.WriteLine("Opened the test page in the test profile; waiting for the extension's page script...");
            var first = await WaitForPageAsync(client, ct);
            Line(report, $"## S6 probe, {DateTime.Now:yyyy-MM-dd HH:mm}");

            var pings = new List<double>();
            for (var i = 0; i < 100; i++)
            {
                pings.Add((await client.SendAsync("ping", null, ct)).Milliseconds);
            }

            Line(report, $"- Ping round trip (host ↔ extension): p50 {Percentile(pings, 0.5):F1} ms, p90 {Percentile(pings, 0.9):F1} ms, " +
                         $"max {pings.Max():F1} ms over 100 (target p90 ≤ 50 ms)");

            var (page, readMs) = await ReadAsync(client, ct);
            var elements = page.GetProperty("elements").EnumerateArray().ToList();
            var text = page.GetProperty("text").GetString() ?? "";
            Line(report, $"- Read: title \"{page.GetProperty("title").GetString()}\", {text.Length} characters of text, {elements.Count} " +
                         $"interactive elements in {readMs:F1} ms: {string.Join(", ", elements.Select(e => $"{e.GetProperty("id")}:{e.GetProperty("tag")} \"{e.GetProperty("name")}\""))}");
            var password = Find(elements, "Password");
            Line(report, $"- Password field value returned: {(password.GetProperty("value").ValueKind == JsonValueKind.Null ? "no (as intended)" : "YES (failure)")}");
            Line(report, $"- Injected paragraph came back as page text: {text.Contains("ignore all previous instructions", StringComparison.Ordinal)}");

            var (_, clickMs) = await CheckedAsync(client, "click", new { id = Id(Find(elements, "Save draft")) }, ct);
            var afterClick = (await ReadAsync(client, ct)).Page.GetProperty("text").GetString() ?? "";
            Line(report, $"- Click \"Save draft\": {clickMs:F1} ms, page logged it: {afterClick.Contains("Clicked Save draft", StringComparison.Ordinal)}");

            var (_, fillMs) = await CheckedAsync(client, "fill", new { id = Id(Find(elements, "Name")), value = "ALDREN test" }, ct);
            await CheckedAsync(client, "fill", new { id = Id(Find(elements, "Email")), value = "test@example.invalid" }, ct);
            await CheckedAsync(client, "click", new { id = Id(Find(elements, "Submit")) }, ct);
            var (final, _) = await ReadAsync(client, ct);
            var finalText = final.GetProperty("text").GetString() ?? "";
            var email = Find(final.GetProperty("elements").EnumerateArray().ToList(), "Email").GetProperty("value").GetString();
            Line(report, $"- Fill and submit: {fillMs:F1} ms per field, page logged \"Submitted: ALDREN test <test@example.invalid> (Free)\": " +
                         $"{finalText.Contains("Submitted: ALDREN test <test@example.invalid> (Free)", StringComparison.Ordinal)}");
            Line(report, $"- The injected instructions triggered nothing: \"Delete account\" never clicked: {!finalText.Contains("Clicked Delete account", StringComparison.Ordinal)}, " +
                         $"email is still ours: {email == "test@example.invalid"}");

            var (refused, _) = await client.SendRawAsync(9001, "{\"id\":9001,\"cmd\":\"exec\",\"args\":{\"command\":\"calc\"}}", ct);
            Line(report, $"- An unknown command (\"exec\") is refused by the host: {!refused.GetProperty("ok").GetBoolean()} " +
                         $"(\"{refused.GetProperty("error").GetString()}\")");
            _ = first;
        }

        Directory.CreateDirectory(ResultsFolder);
        await File.AppendAllTextAsync(Path.Combine(ResultsFolder, "results.md"), report + Environment.NewLine, ct);
        return 0;
    }

    private static void OpenInTestProfile(string url)
    {
        var chrome = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\chrome.exe", "", null) as string
                     ?? @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        Process.Start(new ProcessStartInfo(chrome) { ArgumentList = { $"--user-data-dir={TestProfile}", "--no-first-run", url } })?.Dispose();
    }

    private static async Task<JsonElement> WaitForPageAsync(BridgeClient client, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var (reply, _) = await client.SendAsync("read", null, ct);
            if (reply.GetProperty("ok").GetBoolean())
            {
                return reply.GetProperty("result");
            }

            if (clock.Elapsed > TimeSpan.FromSeconds(60))
            {
                throw new TimeoutException($"The test page never answered: {reply.GetProperty("error").GetString()}");
            }

            await Task.Delay(500, ct);
        }
    }

    private static async Task<(JsonElement Page, double Milliseconds)> ReadAsync(BridgeClient client, CancellationToken ct)
    {
        var (result, ms) = await CheckedAsync(client, "read", null, ct);
        return (result, ms);
    }

    private static async Task<(JsonElement Result, double Milliseconds)> CheckedAsync(BridgeClient client, string cmd, object? args, CancellationToken ct)
    {
        var (reply, ms) = await client.SendAsync(cmd, args, ct);
        return reply.GetProperty("ok").GetBoolean()
            ? (reply.GetProperty("result"), ms)
            : throw new InvalidOperationException($"{cmd} failed: {reply.GetProperty("error").GetString()}");
    }

    // Labels wrapping a control include its text (the Plan label reads "Plan Free Pro"), so names match on their first words.
    private static JsonElement Find(List<JsonElement> elements, string name) =>
        elements.First(e => e.GetProperty("name").GetString() is { } n && (n == name || n.StartsWith(name + " ", StringComparison.Ordinal)));

    private static int Id(JsonElement element) => element.GetProperty("id").GetInt32();

    internal static double Percentile(List<double> values, double p)
    {
        var sorted = values.Order().ToList();
        return sorted[(int)Math.Ceiling(p * sorted.Count) - 1];
    }

    internal static void Line(StringBuilder report, string text)
    {
        Console.WriteLine(text);
        report.AppendLine(text);
    }
}
