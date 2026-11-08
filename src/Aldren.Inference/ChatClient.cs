using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aldren.Inference;

public sealed record ChatToolCall(string Id, string Name, string ArgumentsJson);

public sealed record ChatReply(string Content, IReadOnlyList<ChatToolCall> ToolCalls, TimeSpan Elapsed, string Model);

/// <summary>OpenAI-compatible chat completions against a local llama-server. Never the cloud gateway.</summary>
public sealed class ChatClient(LlamaServerProcess server)
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public string Model => Path.GetFileNameWithoutExtension(server.Launch.ModelPath);

    /// <summary>
    /// The same request streamed: <paramref name="onText"/> receives the reply's text as it is written, so speech can start
    /// on the first sentence. Tool calls are assembled from their fragments and returned at the end.
    /// </summary>
    public async Task<ChatReply> StreamAsync(JsonArray messages, JsonArray? tools, int maxTokens, Action<string> onText, CancellationToken ct)
    {
        var body = Body(messages, tools, maxTokens);
        body["stream"] = true;
        var json = body.ToJsonString();
        var clock = Stopwatch.StartNew();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server.BaseUri, "v1/chat/completions"))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.ApiKey);
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (HttpRequestException)
        {
            // A dropped keep-alive connection fails before any byte arrives (Phase 0, S1): fall back to one plain retry.
            return await CompleteAsync(messages, tools, maxTokens, ct);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"llama-server answered {(int)response.StatusCode}: {Shorten(await response.Content.ReadAsStringAsync(ct))}");
            }

            var content = new StringBuilder();
            var calls = new SortedDictionary<int, (string? Id, string? Name, StringBuilder Arguments)>();
            using var reader = new StreamReader(await response.Content.ReadAsStreamAsync(ct));
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var data = line[5..].Trim();
                if (data == "[DONE]")
                {
                    break;
                }

                var delta = JsonNode.Parse(data)?["choices"]?[0]?["delta"];
                if (delta?["content"] is JsonValue text && text.TryGetValue(out string? piece) && piece.Length > 0)
                {
                    content.Append(piece);
                    onText(piece);
                }

                if (delta?["tool_calls"] is JsonArray fragments)
                {
                    foreach (var fragment in fragments)
                    {
                        var index = fragment?["index"]?.GetValue<int>() ?? 0;
                        var current = calls.TryGetValue(index, out var c) ? c : (null, null, new StringBuilder());
                        current.Id ??= fragment?["id"]?.GetValue<string>();
                        current.Name ??= fragment?["function"]?["name"]?.GetValue<string>();
                        if (fragment?["function"]?["arguments"] is JsonValue args && args.TryGetValue(out string? argPiece))
                        {
                            current.Arguments.Append(argPiece);
                        }

                        calls[index] = current;
                    }
                }
            }

            var toolCalls = calls.Values
                .Where(c => !string.IsNullOrEmpty(c.Name))
                .Select(c => new ChatToolCall(c.Id ?? Guid.NewGuid().ToString("N"), c.Name!, c.Arguments.Length > 0 ? c.Arguments.ToString() : "{}"))
                .ToList();
            return new ChatReply(content.ToString().Trim(), toolCalls, clock.Elapsed, Model);
        }
    }

    private static JsonObject Body(JsonArray messages, JsonArray? tools, int maxTokens)
    {
        var body = new JsonObject
        {
            ["messages"] = messages.DeepClone(),
            ["max_tokens"] = maxTokens,
            ["temperature"] = 0.3,
            ["cache_prompt"] = true,
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
        };
        if (tools is { Count: > 0 })
        {
            body["tools"] = tools.DeepClone();
        }

        return body;
    }

    /// <param name="messages">OpenAI-style message objects.</param>
    /// <param name="tools">OpenAI-style function tools, or null.</param>
    public async Task<ChatReply> CompleteAsync(JsonArray messages, JsonArray? tools, int maxTokens, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["messages"] = messages.DeepClone(),
            ["max_tokens"] = maxTokens,
            ["temperature"] = 0.3,
            ["cache_prompt"] = true,
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
        };
        if (tools is { Count: > 0 })
        {
            body["tools"] = tools.DeepClone();
        }

        var json = body.ToJsonString();
        var clock = Stopwatch.StartNew();
        JsonNode? response;
        try
        {
            response = await SendAsync(json, ct);
        }
        catch (HttpRequestException)
        {
            // llama-server sometimes drops a kept-alive connection before answering (Phase 0, S1): retry once.
            response = await SendAsync(json, ct);
        }

        var message = response?["choices"]?[0]?["message"];
        var content = message?["content"]?.GetValue<string>() ?? "";
        var calls = new List<ChatToolCall>();
        if (message?["tool_calls"] is JsonArray toolCalls)
        {
            foreach (var call in toolCalls)
            {
                var function = call?["function"];
                var name = function?["name"]?.GetValue<string>();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var arguments = function?["arguments"] switch
                {
                    JsonValue v when v.TryGetValue(out string? s) => s,
                    JsonNode n => n.ToJsonString(),
                    _ => "{}",
                };
                calls.Add(new ChatToolCall(call?["id"]?.GetValue<string>() ?? Guid.NewGuid().ToString("N"), name, arguments ?? "{}"));
            }
        }

        return new ChatReply(content.Trim(), calls, clock.Elapsed, Model);
    }

    private async Task<JsonNode?> SendAsync(string json, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server.BaseUri, "v1/chat/completions"))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", server.ApiKey);
        using var response = await Http.SendAsync(request, ct);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"llama-server answered {(int)response.StatusCode}: {Shorten(text)}");
        }

        return JsonNode.Parse(text);
    }

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300] + "…";
}
