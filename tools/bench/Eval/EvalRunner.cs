using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aldren.Bench.Eval;

public static class EvalRunner
{
    /// <summary>Reads the first tool call (if any) and the text of a non-streaming chat completion.</summary>
    public static ModelResponse ParseResponse(string json)
    {
        var root = JsonNode.Parse(json);
        var message = root?["choices"] is JsonArray { Count: > 0 } choices ? choices[0]?["message"] : null;

        var text = message?["content"] is JsonValue content && content.TryGetValue<string>(out var s) ? s.Trim() : "";
        var call = message?["tool_calls"] is JsonArray { Count: > 0 } calls ? calls[0]?["function"] : null;
        var toolName = call?["name"]?.GetValue<string>();

        JsonObject? args = call?["arguments"] switch
        {
            JsonObject obj => (JsonObject)obj.DeepClone(),
            JsonValue raw when raw.TryGetValue<string>(out var argsJson) => TryParseObject(argsJson),
            _ => null,
        };

        return new ModelResponse(EvalScorer.RouteOf(toolName), toolName, args, text);
    }

    private static JsonObject? TryParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
