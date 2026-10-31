using System.Net;
using Aldren.Bench.Llama;

namespace Aldren.Bench.Tests;

public class HttpRetryTests
{
    private sealed class FlakyHandler(int failures) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Calls <= failures
                ? throw new HttpRequestException("An error occurred while sending the request.")
                : Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static HttpRequestMessage Request() => new(HttpMethod.Post, "http://127.0.0.1:1/v1/chat/completions");

    [Fact]
    public async Task Send_failure_is_retried_once()
    {
        var handler = new FlakyHandler(failures: 1);
        using var http = new HttpClient(handler);

        using var response = await HttpRetry.SendAsync(http, Request, HttpCompletionOption.ResponseContentRead, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Second_send_failure_is_thrown()
    {
        var handler = new FlakyHandler(failures: 2);
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            HttpRetry.SendAsync(http, Request, HttpCompletionOption.ResponseContentRead, CancellationToken.None));
        Assert.Equal(2, handler.Calls);
    }
}
