using System.Text.Json.Nodes;

namespace Aldren.Bench.Llama;

/// <summary>A chat message; Content is a string or an array of OpenAI content parts (text, image_url).</summary>
public record ChatMessage(string Role, JsonNode Content)
{
    public static ChatMessage Of(string role, string text) => new(role, JsonValue.Create(text));
}

public record ChatRequest(IReadOnlyList<ChatMessage> Messages, int MaxTokens = 256, double Temperature = 0, JsonArray? Tools = null, string? Model = null)
{
    public JsonObject ToJson(bool stream)
    {
        var messages = new JsonArray();
        foreach (var m in Messages)
        {
            messages.Add(new JsonObject { ["role"] = m.Role, ["content"] = m.Content.DeepClone() });
        }

        var body = new JsonObject
        {
            ["messages"] = messages,
            ["max_tokens"] = MaxTokens,
            ["temperature"] = Temperature,
            ["stream"] = stream,
        };

        if (stream)
        {
            body["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        if (Tools is not null)
        {
            body["tools"] = Tools.DeepClone();
        }

        if (Model is not null)
        {
            body["model"] = Model;
        }

        return body;
    }
}
