using System.Globalization;
using Aldren.Bench.Commands;

namespace Aldren.Bench.Llama;

/// <summary>
/// Retries a request once when it could not be sent. The full S1 run saw rare "error while sending the request"
/// failures on loopback that never reproduced in isolation; each retry is logged so a recurrence leaves evidence.
/// </summary>
public static class HttpRetry
{
    public static async Task<HttpResponseMessage> SendAsync(HttpClient http, Func<HttpRequestMessage> createRequest,
        HttpCompletionOption completion, CancellationToken ct)
    {
        using (var first = createRequest())
        {
            try
            {
                return await http.SendAsync(first, completion, ct);
            }
            catch (HttpRequestException ex) when (!ct.IsCancellationRequested)
            {
                Console.WriteLine($"[{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}] retrying {first.RequestUri?.AbsolutePath} after: {ExceptionText.Chain(ex)}");
            }
        }

        using var second = createRequest();
        return await http.SendAsync(second, completion, ct);
    }
}
