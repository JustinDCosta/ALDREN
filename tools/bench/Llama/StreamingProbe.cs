using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Aldren.Bench.Llama;

public record CompletionTiming(double TtftMs, double TokensPerSecond, int OutputTokens, string Text);

public record ChunkInfo(string Text, int? CompletionTokens);

/// <summary>Sends one streaming chat completion and measures time to first token and generation speed.</summary>
public static class StreamingProbe
{
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    public static async Task<CompletionTiming> MeasureAsync(Uri baseUri, string apiKey, ChatRequest request, CancellationToken ct)
    {
        var json = request.ToJson(stream: true).ToJsonString();
        HttpRequestMessage Message()
        {
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "v1/chat/completions"))
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return message;
        }

        var clock = Stopwatch.StartNew();
        using var response = await HttpRetry.SendAsync(Http, Message, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(ct);

        double? ttftMs = null;
        var chunksWithText = 0;
        int? reportedTokens = null;
        var text = new StringBuilder();

        await foreach (var e in SseParser.ParseAsync(body, ct))
        {
            var chunk = ParseChunk(e.Data);
            if (chunk.Text.Length > 0)
            {
                ttftMs ??= clock.Elapsed.TotalMilliseconds;
                chunksWithText++;
                text.Append(chunk.Text);
            }

            reportedTokens = chunk.CompletionTokens ?? reportedTokens;
        }

        if (ttftMs is null)
        {
            throw new InvalidOperationException("The server streamed no tokens.");
        }

        return Compute(ttftMs.Value, clock.Elapsed.TotalMilliseconds, reportedTokens ?? chunksWithText, text.ToString());
    }

    /// <summary>Generation speed is measured after the first token, so prompt processing doesn't dilute it.</summary>
    public static CompletionTiming Compute(double ttftMs, double totalMs, int outputTokens, string text)
    {
        var generationSeconds = (totalMs - ttftMs) / 1000.0;
        var rate = generationSeconds > 0 ? outputTokens / generationSeconds : 0;
        return new CompletionTiming(ttftMs, rate, outputTokens, text);
    }

    public static ChunkInfo ParseChunk(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var text = new StringBuilder();

        if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            foreach (var choice in choices.EnumerateArray())
            {
                if (!choice.TryGetProperty("delta", out var delta))
                {
                    continue;
                }

                foreach (var field in new[] { "reasoning_content", "content" })
                {
                    if (delta.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String)
                    {
                        text.Append(value.GetString());
                    }
                }
            }
        }

        int? completionTokens = null;
        if (root.TryGetProperty("usage", out var usage)
            && usage.ValueKind == JsonValueKind.Object
            && usage.TryGetProperty("completion_tokens", out var tokens)
            && tokens.ValueKind == JsonValueKind.Number)
        {
            completionTokens = tokens.GetInt32();
        }

        return new ChunkInfo(text.ToString(), completionTokens);
    }
}
