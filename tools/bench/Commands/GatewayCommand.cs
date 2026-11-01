using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aldren.Bench.Gateway;
using Aldren.Bench.Llama;
using Aldren.Bench.Stats;

namespace Aldren.Bench.Commands;

/// <summary>
/// S8: checks a running FreeLLMAPI gateway against ADR-0007 (checks a–l of the Phase 0 plan) and appends the results to
/// E:\AldrenData\bench\s8\results.md. The unified key is read with hidden input and only kept in memory.
/// </summary>
public static partial class GatewayCommand
{
    private const string ResultsFile = @"E:\AldrenData\bench\s8\results.md";
    private const string LocalModelPath = @"E:\AldrenData\models\qwen3.5-0.8b\Qwen3.5-0.8B-Q4_K_M.gguf";
    private static readonly string DbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Aldren\gateway\freeapi.db");
    private const string LogPath = @"E:\AldrenData\gateway\logs\gateway.log";

    // 64x64 PNG: a red circle on white.
    private const string RedCircle = "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAALTSURBVHhe7ZstTwQxEIZPIpFIJJKfgEQikUgkEncSiUQikUgkEolEIpFI5JAnlyYwe3cz/dpts/smr1mSfjztdma6x0pmrpV+MDctAPSDuWkBoB/MTQsA/WBumg7A25vI6+t/T6BxALy/i6zXIqenIquV7ZMTkZubUaDUA/D5KXJ9LXJ8PJxgjA8PRa6uNhArqDyA7+/N6h0cDCeT68tLka8v3WOWygK4u9usmB54SQP29lbk50f3nqQyAFj18/PhYGua86TAbsgHwLvuPdxK++hoE00ylAeAg6n2lrfMK/H0pEfmVjoAVn7qyQcDITFkpgHgnSdW64FMaRbj40OP1FQagLEPPK/JOVicCMUDINTpjlvyxYUe8V7FASDs1EhwSjsia4wDQIanO2vREbvAD4ADpofVD3550TPYKj8AChLdScs+O9Mz2Co/ALIu3UnLZrc6IoIPAEmG7qAHPz7qmQzkA0D1pRvvwZTPhnwAWsv6vCY7NMpmHwDdcE+mZtkjGwAN6EZ7slEk2QCot3WjPdkolW0Az8/DRnvy/b2e0T/ZAB4eho32ZK7j98gGwBbSjfZkqtc9sgH0mgQFG8mQDWD2UYBEQjfak41rMhsAauXyM8VGQeQD0FspHOwoiX0Aeo0ERgRAPgCcAz3dBgUbdQDyAUCtXoXvMhWsQ34AvaXERgoc5AeAetkFrL5xDxAUB4D7dt1Zi2a3OhUHALUeEvlUH6F4ACQWub/7qWUiVcRXIRQPALX6kSRi6welAUCtRQVH0rNN6QBQK1+KOZcSlQcAkSZP+ToYNz6W8gEgLk7HrhgzfxsUVAYAIu+m+tIDrWFCXeRpv0vlAARxONb62Rzh17jiilV5AEFsz1Kf1Jg4ub0zvY1RPQBBvBoMPvb1YBdxwBXa6rtUH8BfkUWG/w0ghDLBvw5/K/ATWK/GBdCgFgD6wdy0ANAP5qYFgH4wN/0CX6XDBpNzcCIAAAAASUVORK5CYII=";

    private const string ToolsJson = """
        [
          {"type": "function", "function": {"name": "get_weather", "description": "Current weather for a city",
            "parameters": {"type": "object", "properties": {"city": {"type": "string"}, "unit": {"type": "string", "enum": ["celsius", "fahrenheit"]}},
                           "required": ["city", "unit"], "additionalProperties": false}}},
          {"type": "function", "function": {"name": "set_timer", "description": "Start a countdown timer",
            "parameters": {"type": "object", "properties": {"minutes": {"type": "integer", "minimum": 1}, "label": {"type": "string"}},
                           "required": ["minutes"], "additionalProperties": false}}}
        ]
        """;

    private const string WeatherSchemaJson = """
        {"type": "json_schema", "json_schema": {"name": "weather", "strict": true, "schema": {"type": "object",
          "properties": {"city": {"type": "string"}, "temperature_c": {"type": "number"}, "sunny": {"type": "boolean"}},
          "required": ["city", "temperature_c", "sunny"], "additionalProperties": false}}}
        """;

    private static readonly string[] Cities = ["Paris", "Lyon", "Tokyo", "Nairobi", "Lima", "Oslo", "Cairo", "Toronto", "Mumbai", "Sydney",
        "Berlin", "Madrid", "Seoul", "Dakar", "Quito", "Hanoi", "Dublin", "Denver", "Manila", "Tunis"];

    private static readonly (string Prompt, string Tool)[] ToolCases =
    [
        .. Cities.Take(12).Select((c, i) => ($"What's the weather in {c} right now, in {(i % 2 == 0 ? "celsius" : "fahrenheit")}?", "get_weather")),
        ("Set a timer for 5 minutes.", "set_timer"), ("Start a 25 minute timer called focus.", "set_timer"),
        ("Remind me in 10 minutes with a timer.", "set_timer"), ("Timer for 3 minutes for the tea, please.", "set_timer"),
        ("Count down 45 minutes for the laundry.", "set_timer"), ("Put a one hour timer on, label it oven.", "set_timer"),
        ("I need a 7 minute timer.", "set_timer"), ("Set a 15-minute timer named break.", "set_timer"),
    ];

    private const string PortFile = @"E:\AldrenData\gateway\port.txt";

    /// <summary>
    /// The gateway's address: <paramref name="url"/> when given (null when it isn't an http URL with a valid port), otherwise the port
    /// start-gateway.ps1 saved.
    /// </summary>
    public static Uri? ResolveUrl(string? url, string? savedPort)
    {
        if (url is not null)
        {
            return Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var given) && given.Scheme == Uri.UriSchemeHttp ? given : null;
        }

        return int.TryParse(savedPort?.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var port)
               && port is > 0 and < 65536
            ? new Uri($"http://127.0.0.1:{port}/")
            : null;
    }

    private sealed record Reply(int Status, double Milliseconds, string? RoutedViaHeader, string? Trail, JsonNode? Json)
    {
        public RoutedVia? Via => RoutingHeaders.ParseRoutedVia(RoutedViaHeader);
        public string Content => Json?["choices"]?[0]?["message"]?["content"] is JsonValue v && v.TryGetValue<string>(out var s) ? s : "";
        public JsonArray? ToolCalls => Json?["choices"]?[0]?["message"]?["tool_calls"] as JsonArray;
        public string Error => Json?["error"]?["message"]?.ToString() ?? "";
    }

    public static async Task<int> RunAsync(string[] args)
    {
        var url = Args.Value(args, "--url");
        if (ResolveUrl(url, File.Exists(PortFile) ? File.ReadAllText(PortFile) : null) is not { } baseUri)
        {
            Console.Error.WriteLine(url is null
                ? @"Start the gateway first (spikes\S8.Gateway\start-gateway.ps1), or pass --url http://127.0.0.1:<port>."
                : $"Not a usable URL: {url}. Leave --url out to use the gateway that start-gateway.ps1 started.");
            Console.Error.WriteLine("usage: aldren-bench gateway [--url http://127.0.0.1:<port>] [--checks abcdefghikl] [--idle-minutes 10] " +
                                    "[--fast-platforms groq,cerebras] [--vision-platforms google,groq] [--gemini gemini-2.5-flash] [--local-model aldren-local] [--tavily]");
            return 2;
        }

        var checks = (Args.Value(args, "--checks") ?? "abcdefghikl").ToHashSet();
        if (Args.Flag(args, "--tavily"))
        {
            checks.Add('j');
        }

        var fast = Platforms(Args.Value(args, "--fast-platforms") ?? "groq,cerebras");
        var vision = Platforms(Args.Value(args, "--vision-platforms") ?? "google,groq");
        var pid = TcpTable.All().FirstOrDefault(r => r.State == "LISTEN" && r.Local.Port == baseUri.Port).ProcessId;
        if (pid == 0)
        {
            Console.Error.WriteLine($@"Nothing is listening on port {baseUri.Port}: start the gateway first (spikes\S8.Gateway\start-gateway.ps1).");
            return 1;
        }

        var report = new StringBuilder();
        Line(report, $"## S8 gateway checks, {DateTime.Now:yyyy-MM-dd HH:mm} (port {baseUri.Port}, node pid {pid})");
        using var cts = new CancellationTokenSource(TimeSpan.FromHours(2));
        var ct = cts.Token;

        if (checks.Contains('a'))
        {
            await Guarded(report, 'a', () => CheckListener(report, pid));
        }

        if (checks.Contains('b'))
        {
            await Guarded(report, 'b', () => CheckHostHeaderAsync(report, baseUri, ct));
        }

        var idleMinutes = int.Parse(Args.Value(args, "--idle-minutes") ?? "10", System.Globalization.CultureInfo.InvariantCulture);
        HashSet<IPEndPoint> idleRemotes = [];
        if ((checks.Contains('c') || checks.Contains('k')) && idleMinutes > 0)
        {
            Console.WriteLine($"Idle for {idleMinutes} minutes: memory and outbound connections are sampled. Leave the gateway alone meanwhile.");
            await using (var idle = new OutboundSampler(pid))
            {
                await Task.Delay(TimeSpan.FromMinutes(idleMinutes), ct);
                idleRemotes = idle.Snapshot();
            }

            using var node = Process.GetProcessById(pid);
            Line(report, $"- (c) memory after {idleMinutes} idle minutes: {node.WorkingSet64 / 1048576} MB working set, " +
                         $"{node.PrivateMemorySize64 / 1048576} MB private bytes (target ≤ 100 MB)");
        }

        var keyed = new[] { 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'l' }.Where(checks.Contains).ToList();
        HashSet<IPEndPoint> burstRemotes = [];
        if (keyed.Count > 0)
        {
            var key = ReadSecret("Gateway unified key (input hidden): ");
            using var api = new HttpClient { BaseAddress = baseUri, Timeout = TimeSpan.FromSeconds(90) };
            api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            var routing = new List<(string Chain, Reply Reply)>();
            await using (var burst = new OutboundSampler(pid))
            {
                foreach (var check in keyed)
                {
                    await Guarded(report, check, check switch
                    {
                        'd' => () => CheckOverheadAsync(report, api, Args.Value(args, "--local-model") ?? "aldren-local", ct),
                        'e' => () => CheckFailoverAsync(report, api, fast, routing, ct),
                        'f' => () => CheckToolCallsAsync(report, api, routing, ct),
                        'g' => () => CheckJsonSchemaAsync(report, api, routing, ct),
                        'h' => () => CheckImagesAsync(report, api, vision, fast, routing, ct),
                        'i' => () => CheckGoogleSearchAsync(report, api, Args.Value(args, "--gemini") ?? "gemini-2.5-flash", ct),
                        'j' => () => CheckSearchLayerAsync(report, api, ct),
                        _ => () => CheckCanaryAsync(report, api, routing, ct),
                    });
                }

                burstRemotes = burst.Snapshot();
            }

            var served = routing.Where(r => r.Reply.Status == 200).ToList();
            var inChain = served.Count(r => r.Reply.Via is { } via && RoutingHeaders.IsAllowed(via, r.Chain == "auto:aldren-vision" ? vision : fast));
            Line(report, $"- X-Routed-Via present and inside the requested chain on {inChain}/{served.Count} served chain requests (target: all)");
        }

        if (checks.Contains('k'))
        {
            await Guarded(report, 'k', () => DescribeRemotesAsync(report, idleRemotes, burstRemotes, fast.Concat(vision)));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ResultsFile)!);
        await File.AppendAllTextAsync(ResultsFile, report + Environment.NewLine, CancellationToken.None);
        Console.WriteLine($"Results appended to {ResultsFile}");
        return 0;
    }

    private static Task CheckListener(StringBuilder report, int pid)
    {
        var listening = TcpTable.All().Where(r => r.ProcessId == pid && r.State == "LISTEN").Select(r => r.Local).ToList();
        var loopbackOnly = listening.All(e => IPAddress.IsLoopback(e.Address));
        Line(report, $"- (a) listening on {string.Join(", ", listening)}: {(loopbackOnly ? "loopback only (pass)" : "NOT loopback only (fail)")}");
        return Task.CompletedTask;
    }

    private static async Task CheckHostHeaderAsync(StringBuilder report, Uri baseUri, CancellationToken ct)
    {
        using var http = new HttpClient { BaseAddress = baseUri };
        async Task<int> PingAs(string host)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "api/ping");
            request.Headers.Host = host;
            using var response = await http.SendAsync(request, ct);
            return (int)response.StatusCode;
        }

        var foreign = await PingAs("evil.example");
        var local = await PingAs("localhost");
        Line(report, $"- (b) Host: evil.example → {foreign}, Host: localhost → {local}: {(foreign == 403 && local == 200 ? "pass" : "fail")}");
    }

    private static async Task CheckOverheadAsync(StringBuilder report, HttpClient api, string localModel, CancellationToken ct)
    {
        await using var server = await LlamaServer.StartAsync(new LlamaServerOptions(LocalModelPath, null, 4096, false,
            ExtraArgs: ["--alias", localModel, "-np", "1", "--reasoning-budget", "0"]), ct);
        Console.WriteLine($"(d) A local llama-server is running. In the dashboard, add a temporary custom endpoint: base URL {server.BaseUri}v1, " +
                          $"API key {server.ApiKey} (a throwaway for this server only), model \"{localModel}\".");
        if (!Confirm("Press Enter once it's added, or type skip: "))
        {
            Line(report, "- (d) skipped");
            return;
        }

        using var direct = new HttpClient { BaseAddress = server.BaseUri, Timeout = TimeSpan.FromSeconds(60) };
        direct.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", server.ApiKey);
        var body = Body(localModel, "Reply with the single word OK.", maxTokens: 4);
        await ChatAsync(direct, body, ct);
        var warm = await ChatAsync(api, body, ct);
        if (warm.Status != 200)
        {
            Line(report, $"- (d) the gateway didn't serve \"{localModel}\": {warm.Status} {warm.Error}");
            return;
        }

        List<double> directMs = [], gatewayMs = [];
        HashSet<string> vias = [];
        for (var i = 0; i < 20; i++)
        {
            directMs.Add((await ChatAsync(direct, body, ct)).Milliseconds);
            var reply = await ChatAsync(api, body, ct);
            gatewayMs.Add(reply.Milliseconds);
            vias.Add(reply.RoutedViaHeader ?? "none");
        }

        double d = Percentiles.Of(directMs).P50, g = Percentiles.Of(gatewayMs).P50;
        Line(report, $"- (d) per-request overhead, 20 requests each: direct p50 {d:F1} ms, through the gateway p50 {g:F1} ms, so {g - d:F1} ms added " +
                     $"(target ≤ 20 ms); routed via {string.Join(", ", vias)}");
        Console.WriteLine("(d) Done: remove the custom endpoint from the dashboard.");
    }

    private static async Task CheckFailoverAsync(StringBuilder report, HttpClient api, IReadOnlySet<string> fast, List<(string, Reply)> routing,
        CancellationToken ct)
    {
        Console.WriteLine("(e) In the dashboard, disable the key used by the first model of aldren-fast.");
        if (!Confirm("Press Enter once it's disabled, or type skip: "))
        {
            Line(report, "- (e) skipped");
            return;
        }

        var replies = new List<Reply>();
        for (var i = 0; i < 10; i++)
        {
            var reply = await ChatAsync(api, Body("auto:aldren-fast", "Say hello in three words."), ct);
            replies.Add(reply);
            routing.Add(("auto:aldren-fast", reply));
        }

        var inChain = replies.Count(r => r.Status == 200 && r.Via is { } via && RoutingHeaders.IsAllowed(via, fast));
        var hops = replies.SelectMany(r => RoutingHeaders.ParseTrail(r.Trail)).Select(h => $"{h.Platform}/{h.Model} {h.Reason}").Distinct().ToList();
        Line(report, $"- (e) failover with the top key disabled: {inChain}/10 served inside aldren-fast (target 10/10), served by " +
                     $"{string.Join(", ", replies.Select(r => r.RoutedViaHeader ?? $"none ({r.Status})").Distinct())}; failed hops in the trail: " +
                     $"{(hops.Count == 0 ? "none (a disabled key is skipped rather than tried)" : string.Join("; ", hops))}");
        Console.WriteLine("(e) Done: re-enable that key.");
    }

    private static async Task CheckToolCallsAsync(StringBuilder report, HttpClient api, List<(string, Reply)> routing, CancellationToken ct)
    {
        foreach (var chain in new[] { "auto:aldren-fast", "auto:aldren-vision" })
        {
            var byPlatform = new Dictionary<string, (int Valid, int Total)>();
            foreach (var (prompt, tool) in ToolCases)
            {
                var body = Body(chain, prompt);
                body["tools"] = JsonNode.Parse(ToolsJson);
                var reply = await ChatAsync(api, body, ct);
                routing.Add((chain, reply));
                var platform = reply.Via?.Platform ?? $"error {reply.Status}";
                var (valid, total) = byPlatform.GetValueOrDefault(platform);
                byPlatform[platform] = (valid + (ValidToolCall(reply, tool) ? 1 : 0), total + 1);
            }

            var all = byPlatform.Values.Aggregate((Valid: 0, Total: 0), (a, v) => (a.Valid + v.Valid, a.Total + v.Total));
            Line(report, $"- (f) tool calls on {chain}: {all.Valid}/{all.Total} valid (target ≥ 95%); by provider: " +
                         string.Join(", ", byPlatform.Select(p => $"{p.Key} {p.Value.Valid}/{p.Value.Total}")));
        }
    }

    // The expected function, with arguments that satisfy its schema and nothing else.
    private static bool ValidToolCall(Reply reply, string expected)
    {
        if (reply.ToolCalls is not [JsonObject call, ..] || call["function"]?["name"]?.ToString() != expected)
        {
            return false;
        }

        try
        {
            if (JsonNode.Parse(call["function"]?["arguments"]?.ToString() ?? "") is not JsonObject a)
            {
                return false;
            }

            return expected == "get_weather"
                ? a.Count == 2 && a["city"] is JsonValue c && c.TryGetValue<string>(out var city) && city.Length > 0
                  && a["unit"] is JsonValue u && u.TryGetValue<string>(out var unit) && unit is "celsius" or "fahrenheit"
                : a["minutes"] is JsonValue m && m.TryGetValue<int>(out var minutes) && minutes >= 1
                  && a.All(p => p.Key is "minutes" || (p.Key is "label" && p.Value is JsonValue l && l.TryGetValue<string>(out _)));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static async Task CheckJsonSchemaAsync(StringBuilder report, HttpClient api, List<(string, Reply)> routing, CancellationToken ct)
    {
        var valid = 0;
        HashSet<string> vias = [];
        foreach (var city in Cities)
        {
            var body = Body("auto:aldren-fast", $"Invent today's weather for {city}.");
            body["response_format"] = JsonNode.Parse(WeatherSchemaJson);
            var reply = await ChatAsync(api, body, ct);
            routing.Add(("auto:aldren-fast", reply));
            vias.Add(reply.RoutedViaHeader ?? $"error {reply.Status}");
            try
            {
                if (JsonNode.Parse(reply.Content) is JsonObject o && o.Count == 3 && o["city"] is JsonValue c && c.TryGetValue<string>(out _)
                    && o["temperature_c"] is JsonValue t && t.TryGetValue<double>(out _) && o["sunny"] is JsonValue s && s.TryGetValue<bool>(out _))
                {
                    valid++;
                }
            }
            catch (JsonException)
            {
            }
        }

        Line(report, $"- (g) json_schema output on auto:aldren-fast: {valid}/{Cities.Length} match the schema (target ≥ 95%); served by {string.Join(", ", vias)}");
    }

    private static async Task CheckImagesAsync(StringBuilder report, HttpClient api, IReadOnlySet<string> vision, IReadOnlySet<string> fast,
        List<(string, Reply)> routing, CancellationToken ct)
    {
        JsonArray Picture() => new(
            new JsonObject { ["type"] = "text", ["text"] = "What colour is the circle? Answer with one word." },
            new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64," + RedCircle } });

        var correct = 0;
        HashSet<string> vias = [];
        for (var i = 0; i < 5; i++)
        {
            var reply = await ChatAsync(api, Body("auto:aldren-vision", Picture()), ct);
            routing.Add(("auto:aldren-vision", reply));
            vias.Add(reply.RoutedViaHeader ?? $"error {reply.Status}");
            if (reply.Status == 200 && reply.Via is { } via && RoutingHeaders.IsAllowed(via, vision)
                && reply.Content.Contains("red", StringComparison.OrdinalIgnoreCase))
            {
                correct++;
            }
        }

        var text = await ChatAsync(api, Body("auto:aldren-fast", Picture()), ct);
        var leaked = text.Status == 200 || (text.Via is { } v && !RoutingHeaders.IsAllowed(v, fast));
        Line(report, $"- (h) image to auto:aldren-vision: {correct}/5 answered \"red\" by a model in the chain ({string.Join(", ", vias)}); image to " +
                     $"auto:aldren-fast: {text.Status} \"{Shorten(text.Error)}\" ({(leaked ? "FAIL: it was served" : text.Status == 422 ? "pass" : "refused, but not with 422")})");
    }

    private static async Task CheckGoogleSearchAsync(StringBuilder report, HttpClient api, string gemini, CancellationToken ct)
    {
        var body = Body(gemini, "Which version of .NET was released most recently? Answer in one sentence and give the source.", maxTokens: 400);
        body["tools"] = JsonNode.Parse("""[{"type": "function", "function": {"name": "google_search", "description": "Google Search", "parameters": {"type": "object", "properties": {}}}}]""");
        var reply = await ChatAsync(api, body, ct);
        var message = reply.Json?["choices"]?[0]?["message"];
        var structured = message?["annotations"] is JsonArray { Count: > 0 } || reply.Json?["citations"] is JsonArray { Count: > 0 };
        var links = Url().Matches(reply.Content).Count;
        Line(report, $"- (i) google_search on {gemini}: {reply.Status} via {reply.RoutedViaHeader ?? "none"}; source links: " +
                     $"{(structured ? "structured citations returned" : links > 0 ? $"{links} URL(s) in the text only" : "none")}");
    }

    private static async Task CheckSearchLayerAsync(StringBuilder report, HttpClient api, CancellationToken ct)
    {
        using var content = new StringContent("""{"query": "latest .NET release"}""", Encoding.UTF8, "application/json");
        using var response = await api.PostAsync("v1/search", content, ct);
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject;
        var results = json?["results"] as JsonArray;
        Line(report, $"- (j) search layer: {(int)response.StatusCode}; {(results is { Count: > 0 } && results[0]?["url"] is not null ? $"{results.Count} structured results (title/url/content)" : $"keys: {string.Join(", ", json?.Select(p => p.Key) ?? [])}")}");
    }

    private static async Task CheckCanaryAsync(StringBuilder report, HttpClient api, List<(string, Reply)> routing, CancellationToken ct)
    {
        var canary = $"ALDREN-CANARY-{Guid.NewGuid():N}";
        var reply = await ChatAsync(api, Body("auto:aldren-fast", $"Repeat this token exactly and nothing else: {canary}"), ct);
        routing.Add(("auto:aldren-fast", reply));
        await Task.Delay(3000, ct); // let the gateway finish its writes
        var needle = Encoding.UTF8.GetBytes(canary);
        var found = new[] { DbPath, DbPath + "-wal", DbPath + "-shm", LogPath }.Where(f => Contains(f, needle)).Select(Path.GetFileName).ToList();
        Line(report, $"- (l) canary sent through auto:aldren-fast ({reply.Status}, echoed: {reply.Content.Contains(canary, StringComparison.Ordinal)}): " +
                     $"{(found.Count == 0 ? "absent from the database and the log (pass)" : $"FOUND in {string.Join(", ", found)} (fail)")}");
    }

    private static async Task DescribeRemotesAsync(StringBuilder report, HashSet<IPEndPoint> idle, HashSet<IPEndPoint> burst, IEnumerable<string> platforms)
    {
        var hosts = new[] { "api.freellmapi.co" }.Concat(platforms.Distinct().Select(p => p switch
        {
            "groq" => "api.groq.com", "cerebras" => "api.cerebras.ai", "google" => "generativelanguage.googleapis.com",
            "openrouter" => "openrouter.ai", "mistral" => "api.mistral.ai", _ => p,
        })).ToList();
        var known = new Dictionary<IPAddress, string>();
        foreach (var host in hosts)
        {
            try
            {
                foreach (var address in await Dns.GetHostAddressesAsync(host))
                {
                    known.TryAdd(address, host);
                }
            }
            catch (System.Net.Sockets.SocketException)
            {
            }
        }

        async Task<string> Describe(IPEndPoint e)
        {
            if (known.TryGetValue(e.Address, out var host))
            {
                return $"{e} ({host})";
            }

            try
            {
                return $"{e} (reverse DNS: {(await Dns.GetHostEntryAsync(e.Address)).HostName}, not an expected host)";
            }
            catch (System.Net.Sockets.SocketException)
            {
                return $"{e} (no reverse DNS, not an expected host)";
            }
        }

        var idleText = idle.Count == 0 ? "none" : string.Join("; ", await Task.WhenAll(idle.Select(Describe)));
        var burstText = burst.Count == 0 ? "none" : string.Join("; ", await Task.WhenAll(burst.Select(Describe)));
        Line(report, $"- (k) outbound connections while idle: {idleText}. During the checks: {burstText}. Expected only {string.Join(", ", hosts)} " +
                     "(sampled every second, so connections shorter than that can be missed; CDN addresses change, so an unmatched address needs a second look)");
    }

    private static async Task<Reply> ChatAsync(HttpClient http, JsonObject body, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("v1/chat/completions", content, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        JsonNode? json;
        try
        {
            json = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            json = null;
        }

        return new Reply((int)response.StatusCode, clock.Elapsed.TotalMilliseconds, Header(response, "X-Routed-Via"), Header(response, "X-Fallback-Trail"), json);
    }

    private static JsonObject Body(string model, JsonNode content, int maxTokens = 300) => new()
    {
        ["model"] = model,
        ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = content }),
        ["max_tokens"] = maxTokens,
        ["temperature"] = 0,
    };

    private static JsonObject Body(string model, string text, int maxTokens = 300) => Body(model, JsonValue.Create(text), maxTokens);

    private static string? Header(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static HashSet<string> Platforms(string list) => [.. list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

    private static bool Contains(string file, byte[] needle)
    {
        if (!File.Exists(file))
        {
            return false;
        }

        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.GetBuffer().AsSpan(0, (int)copy.Length).IndexOf(needle) >= 0;
    }

    private static string Shorten(string text) => text.Length > 120 ? text[..120] + "…" : text;

    private static async Task Guarded(StringBuilder report, char check, Func<Task> run)
    {
        try
        {
            await run();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Line(report, $"- ({check}) error: {ExceptionText.Chain(ex)}");
        }
    }

    private static bool Confirm(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine() is { } line && !line.Trim().Equals("skip", StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        if (Console.IsInputRedirected)
        {
            return Console.ReadLine() ?? "";
        }

        var secret = new StringBuilder();
        while (Console.ReadKey(intercept: true) is var key && key.Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace)
            {
                secret.Length = Math.Max(0, secret.Length - 1);
            }
            else if (!char.IsControl(key.KeyChar))
            {
                secret.Append(key.KeyChar);
            }
        }

        Console.WriteLine();
        return secret.ToString();
    }

    private static void Line(StringBuilder report, string text)
    {
        Console.WriteLine(text);
        report.AppendLine(text);
    }

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex Url();

    /// <summary>Collects the gateway's remote endpoints every second until disposed.</summary>
    private sealed class OutboundSampler : IAsyncDisposable
    {
        private readonly int _pid;
        private readonly HashSet<IPEndPoint> _seen = [];
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        public OutboundSampler(int pid)
        {
            _pid = pid;
            _loop = Task.Run(LoopAsync);
        }

        public HashSet<IPEndPoint> Snapshot()
        {
            lock (_seen)
            {
                return [.. _seen];
            }
        }

        private async Task LoopAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                foreach (var row in TcpTable.All().Where(r => r.ProcessId == _pid && r.State is not "LISTEN" && !IPAddress.IsLoopback(r.Remote.Address)
                                                             && !r.Remote.Address.Equals(IPAddress.Any) && !r.Remote.Address.Equals(IPAddress.IPv6Any)))
                {
                    lock (_seen)
                    {
                        _seen.Add(row.Remote);
                    }
                }

                try
                {
                    await Task.Delay(1000, _stop.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _loop;
            _stop.Dispose();
        }
    }
}
