using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Aldren.Bench.Llama;

/// <summary>Plain (non-streaming) calls to a llama-server instance.</summary>
public static class ChatClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static async Task<(string Json, double Milliseconds)> CompleteAsync(Uri baseUri, string apiKey, JsonObject body, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        var json = await PostAsync(baseUri, apiKey, "v1/chat/completions", body, ct);
        return (json, clock.Elapsed.TotalMilliseconds);
    }

    public static async Task<string> PostAsync(Uri baseUri, string apiKey, string path, JsonNode body, CancellationToken ct)
    {
        var content = body.ToJsonString();
        HttpRequestMessage Message()
        {
            var message = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, path))
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json"),
            };
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return message;
        }

        using var response = await HttpRetry.SendAsync(Http, Message, HttpCompletionOption.ResponseContentRead, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"{path} returned {(int)response.StatusCode}: {json}");
        }

        return json;
    }

    public static async Task<string> GetAsync(Uri baseUri, string apiKey, string path, CancellationToken ct)
    {
        HttpRequestMessage Message()
        {
            var message = new HttpRequestMessage(HttpMethod.Get, new Uri(baseUri, path));
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            return message;
        }

        using var response = await HttpRetry.SendAsync(Http, Message, HttpCompletionOption.ResponseContentRead, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(ct);
    }
}
