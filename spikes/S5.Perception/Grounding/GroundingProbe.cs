using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aldren.Bench.Llama;

namespace S5.Perception.Grounding;

/// <summary>Asks the resident model (Qwen3.5-4B with its projector on the GPU) where things are on a screenshot.</summary>
internal sealed partial class GroundingProbe : IAsyncDisposable
{
    private const string Models = @"E:\AldrenData\models\qwen3.5-4b";
    private readonly LlamaServer _server;

    private GroundingProbe(LlamaServer server) => _server = server;

    public static async Task<GroundingProbe> StartAsync(CancellationToken ct) =>
        new(await LlamaServer.StartAsync(new LlamaServerOptions(
            Path.Combine(Models, "Qwen3.5-4B-Q4_K_M.gguf"), Path.Combine(Models, "mmproj-F16.gguf"), 8192, false,
            ExtraArgs: ["-np", "1", "--reasoning-budget", "0"]), ct));

    public async Task<(string Answer, double Milliseconds)> AskAsync(byte[] png, string question, int maxTokens, CancellationToken ct)
    {
        var content = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = question },
            new JsonObject { ["type"] = "image_url", ["image_url"] = new JsonObject { ["url"] = "data:image/png;base64," + Convert.ToBase64String(png) } },
        };
        var body = new ChatRequest([new ChatMessage("user", content)], MaxTokens: maxTokens).ToJson(stream: false);
        body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
        var (json, ms) = await ChatClient.CompleteAsync(_server.BaseUri, _server.ApiKey, body, ct);
        return ((JsonNode.Parse(json)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "").Trim(), ms);
    }

    public static string Describe(UiElementInfo e) => $"{e.ControlType.ToLowerInvariant()} named \"{e.Name}\"";

    public static string MarkQuestion(UiElementInfo e) =>
        $"The screenshot has numbered red boxes. Which number is on the {Describe(e)}? Answer with the number only.";

    // Asked in the JSON point format Qwen's vision models are trained on; asked for plain "x,y", the 4B model answers in prose.
    public static string PointQuestion(UiElementInfo e) =>
        $"Locate the {Describe(e)} in this screenshot. Output its centre point in JSON format, like [{{\"point_2d\": [x, y]}}], " +
        "with x and y on a 0-1000 scale where [0, 0] is the top-left corner.";

    public static int? ParseMark(string answer) =>
        Number().Match(answer) is { Success: true } m && int.TryParse(m.Value, CultureInfo.InvariantCulture, out var n) ? n : null;

    /// <summary>Reads x,y on the 0-1000 scale (from point_2d when present) and converts to pixels.</summary>
    public static Point? ParsePoint(string answer, int width, int height)
    {
        const string key = "point_2d";
        var start = answer.IndexOf(key, StringComparison.Ordinal);
        var numbers = Number().Matches(start < 0 ? answer : answer[(start + key.Length)..])
            .Select(m => double.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();
        return numbers.Count < 2 ? null : new Point(numbers[0] / 1000 * width, numbers[1] / 1000 * height);
    }

    public ValueTask DisposeAsync() => _server.DisposeAsync();

    [GeneratedRegex(@"\d+(\.\d+)?")]
    private static partial Regex Number();
}
