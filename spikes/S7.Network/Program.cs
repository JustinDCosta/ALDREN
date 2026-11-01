using System.Text.Json;
using S7.Network.Nlm;
using S7.Network.Presence;

// S7 network identity probe. Nothing leaves the PC; home networks are kept as fingerprint hashes in E:\AldrenData\bench\s7\home.json.
//   snapshot   connected networks with their adapters, gateways and the presence state
//   enroll     adds the physical networks connected right now to the home fingerprints
//   watch      re-evaluates every 200 ms and logs each change, timed from the moment a link came up (Ctrl+C stops)
const string Folder = @"E:\AldrenData\bench\s7";
var homeFile = Path.Combine(Folder, "home.json");
var json = new JsonSerializerOptions { WriteIndented = true };

return args.FirstOrDefault() switch
{
    "snapshot" => Snapshot(),
    "enroll" => Enroll(),
    "watch" => await WatchAsync(),
    _ => Usage(),
};

int Usage()
{
    Console.WriteLine("S7.Network snapshot | enroll | watch");
    return 1;
}

int Snapshot()
{
    var home = LoadHome();
    var (views, ms) = NetworkSnapshot.Take(withSsid: true);
    Console.WriteLine($"Connected networks ({ms:F0} ms):");
    foreach (var v in views)
    {
        var o = v.Observation;
        var print = o.NetworkId is { } id && o.GatewayMac is { } mac ? Fingerprint.Of(id, mac) : null;
        Console.WriteLine($"  {v.InterfaceName} ({o.AdapterDescription}): \"{v.NetworkName}\", {v.Category}, {(o.IsVirtual ? "virtual" : "physical")}, " +
                          $"gateway {v.GatewayIp ?? "none"} {(o.GatewayMac is null ? "" : Fingerprint.NormaliseMac(o.GatewayMac))}" +
                          $"{(o.Ssid is null ? "" : $", Wi-Fi \"{o.Ssid}\"")}{(print is null ? "" : $", fingerprint {print[..12]}{(home.Contains(print) ? " (home)" : "")}")}");
    }

    var wifi = views.Any(v => v.InterfaceName.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase));
    if (wifi && views.All(v => v.Observation.Ssid is null))
    {
        Console.WriteLine("  (Windows didn't give the Wi-Fi name: it needs location access for desktop apps. The fingerprint doesn't use it.)");
    }

    Console.WriteLine($"State: {FingerprintMatcher.Evaluate(views.Select(v => v.Observation).ToList(), home)} ({home.Count} home fingerprints)");
    return 0;
}

int Enroll()
{
    var stored = LoadEntries();
    var (views, _) = NetworkSnapshot.Take(withSsid: false);
    var added = 0;
    foreach (var v in views.Where(v => !v.Observation.IsVirtual && v.Observation.NetworkId is not null && v.Observation.GatewayMac is not null))
    {
        var print = Fingerprint.Of(v.Observation.NetworkId!.Value, v.Observation.GatewayMac!);
        if (stored.All(e => e.Hash != print))
        {
            stored.Add(new HomeEntry(print, $"{v.InterfaceName} ({v.Observation.AdapterDescription})", DateTime.Now));
            Console.WriteLine($"Added {v.InterfaceName}: {print[..12]}");
            added++;
        }
    }

    Directory.CreateDirectory(Folder);
    File.WriteAllText(homeFile, JsonSerializer.Serialize(stored, json));
    Console.WriteLine(added == 0 ? "Nothing new to add (no physical network, or already enrolled)." : $"{stored.Count} home fingerprints in {homeFile}");
    return 0;
}

async Task<int> WatchAsync()
{
    var home = LoadHome();
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
    };

    Directory.CreateDirectory(Folder);
    using var log = new StreamWriter(Path.Combine(Folder, "watch.log"), append: true) { AutoFlush = true };
    void Log(string text)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {text}";
        Console.WriteLine(line);
        log.WriteLine(line);
    }

    var links = NetworkSnapshot.PhysicalLinksUp(); // links already up are the baseline, not timed
    Log($"watching with {home.Count} home fingerprints, links up: {(links.Count == 0 ? "none" : string.Join(", ", links))}; Ctrl+C stops");
    PresenceState? last = null;
    DateTime? linkUp = null;
    var known = new List<double>();
    var changes = 0;
    while (!cts.IsCancellationRequested)
    {
        var now = DateTime.Now;
        var current = NetworkSnapshot.PhysicalLinksUp();
        foreach (var name in current.Except(links))
        {
            Log($"link up: {name}");
            linkUp = now;
        }

        foreach (var name in links.Except(current))
        {
            Log($"link down: {name}");
        }

        links = current;
        var (views, ms) = NetworkSnapshot.Take(withSsid: false);
        var state = FingerprintMatcher.Evaluate(views.Select(v => v.Observation).ToList(), home);
        if (state != last)
        {
            var after = linkUp is { } t ? (now - t).TotalMilliseconds : (double?)null;
            Log($"state {state}{(after is { } a ? $", {a:F0} ms after the link came up" : "")} (snapshot took {ms:F0} ms)");
            if (after is { } a2 && state != PresenceState.Unknown)
            {
                known.Add(a2);
                linkUp = null;
            }

            last = state;
            changes++;
        }

        try
        {
            await Task.Delay(200, cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
    }

    var summary = $"- S7 watch {DateTime.Now:yyyy-MM-dd HH:mm}: {changes} state changes; state known after a link came up in " +
                  (known.Count == 0 ? "no measured case" : $"{string.Join(", ", known.Select(k => $"{k:F0}"))} ms (max {known.Max():F0} ms, target ≤ 2,000 ms)");
    Log(summary);
    File.AppendAllText(Path.Combine(Folder, "results.md"), summary + Environment.NewLine);
    return 0;
}

List<HomeEntry> LoadEntries() =>
    File.Exists(homeFile) ? JsonSerializer.Deserialize<List<HomeEntry>>(File.ReadAllText(homeFile)) ?? [] : [];

HashSet<string> LoadHome() => LoadEntries().Select(e => e.Hash).ToHashSet();

internal sealed record HomeEntry(string Hash, string Label, DateTime Enrolled);
