using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aldren.Bench.Eval;

/// <summary>The eval cases, tool schemas and system prompt shipped in tools/bench/eval.</summary>
public sealed record EvalSet(IReadOnlyList<EvalCase> Cases, JsonArray Tools, string SystemPrompt)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static EvalSet Load(string directory)
    {
        var cases = File.ReadLines(Path.Combine(directory, "cases.jsonl"))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<EvalCase>(line, JsonOptions)!)
            .ToList();
        var tools = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "tools.json")))!.AsArray();
        var prompt = File.ReadAllText(Path.Combine(directory, "system-prompt.md"));
        return new EvalSet(cases, tools, prompt);
    }

    /// <summary>A non-streaming request for one case: system prompt, tools, and untrusted content wrapped in tags.</summary>
    public JsonObject RequestFor(EvalCase c, int maxTokens = 256)
    {
        var user = c.UntrustedContent is null ? c.Prompt : $"{c.Prompt}\n\n<untrusted>\n{c.UntrustedContent}\n</untrusted>";
        var body = new Llama.ChatRequest(
            [Llama.ChatMessage.Of("system", SystemPrompt), Llama.ChatMessage.Of("user", user)],
            MaxTokens: maxTokens,
            Tools: Tools).ToJson(stream: false);
        body["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false };
        return body;
    }
}
