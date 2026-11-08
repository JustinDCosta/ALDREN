using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;
using AngleSharp.Html.Parser;
using Aldren.Cloud;

namespace Aldren.Research;

public sealed record PageText(string Url, string Title, string Text);

/// <summary>
/// Fetches public web pages for research (ARCHITECTURE §9): http(s) only, an honest user agent, 8 s and 1.5 MB limits,
/// and addresses on this PC or the local network refused at connect time, redirects included, so a search result can never
/// reach ALDREN's own services, the router or other devices. Text is extracted locally and is untrusted.
/// </summary>
public static class WebFetcher
{
    private const int MaxBytes = 1_500_000;

    private static readonly HttpClient Http = CreateClient();

    public static async Task<PageText?> FetchAsync(string url, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var type = response.Content.Headers.ContentType?.MediaType ?? "";
            if (!response.IsSuccessStatusCode || !(type.Contains("html") || type.StartsWith("text/", StringComparison.Ordinal)))
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            var buffer = new MemoryStream();
            var chunk = new byte[16_384];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0 && buffer.Length < MaxBytes)
            {
                buffer.Write(chunk, 0, read);
            }

            var html = Encoding.UTF8.GetString(buffer.ToArray());
            return type.Contains("html") ? Extract(url, html) : new PageText(url, url, html);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or IOException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>The readable text: headings, paragraphs and list items of the article or main area, without menus and scripts.</summary>
    public static PageText Extract(string url, string html)
    {
        var document = new HtmlParser().ParseDocument(html);
        foreach (var junk in document.QuerySelectorAll("script, style, noscript, nav, footer, header, aside, form, iframe, svg, [role=navigation], [aria-hidden=true]").ToList())
        {
            junk.Remove();
        }

        var root = document.QuerySelector("article") ?? document.QuerySelector("main") ?? document.Body;
        var builder = new StringBuilder();
        foreach (var element in root?.QuerySelectorAll("h1, h2, h3, p, li, td, blockquote, pre") ?? Enumerable.Empty<AngleSharp.Dom.IElement>())
        {
            var text = string.Join(' ', element.TextContent.Split((char[])[' ', '\n', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries));
            if (text.Length >= 30 || element.LocalName is "h1" or "h2" or "h3")
            {
                builder.AppendLine(text);
            }
        }

        return new PageText(url, document.Title?.Trim() is { Length: > 0 } title ? title : url, builder.ToString());
    }

    /// <summary>Keyless fallback when the gateway's search isn't available: Wikipedia's public search API.</summary>
    public static async Task<IReadOnlyList<SearchHit>> WikipediaAsync(string query, int max, CancellationToken ct)
    {
        var url = $"https://en.wikipedia.org/w/api.php?action=query&list=search&format=json&srlimit={max}&srsearch={Uri.EscapeDataString(query)}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var json = await Http.GetStringAsync(url, timeout.Token);
        var hits = new List<SearchHit>();
        foreach (var item in JsonNode.Parse(json)?["query"]?["search"]?.AsArray() ?? [])
        {
            var title = item?["title"]?.GetValue<string>();
            if (title is null)
            {
                continue;
            }

            var snippet = System.Text.RegularExpressions.Regex.Replace(item?["snippet"]?.GetValue<string>() ?? "", "<.*?>", "");
            hits.Add(new SearchHit(title, "https://en.wikipedia.org/wiki/" + Uri.EscapeDataString(title.Replace(' ', '_')), WebUtility.HtmlDecode(snippet)));
        }

        return hits;
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 5,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectCallback = async (context, ct) =>
            {
                // Resolve here and refuse anything that isn't a public address (SSRF guard for every hop, redirects included).
                var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
                var target = addresses.FirstOrDefault(IsPublic)
                             ?? throw new HttpRequestException($"{context.DnsEndPoint.Host} isn't a public address; not fetched.");
                var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(target, context.DnsEndPoint.Port, ct);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ALDREN-personal-assistant", "0.6"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(local research client)"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        return client;
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast))
        {
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast);
        }

        var b = address.GetAddressBytes();
        return !(b[0] == 10 || b[0] == 127 || b[0] == 0 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168)
                 || (b[0] == 169 && b[1] == 254) || (b[0] == 100 && b[1] >= 64 && b[1] <= 127) || b[0] >= 224);
    }
}
