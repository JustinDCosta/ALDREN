namespace Aldren.Bench.Gateway;

public record RoutedVia(string Platform, string Model);

public record FailedHop(string Platform, string Model, string Reason);

/// <summary>
/// FreeLLMAPI's routing headers (v0.16.14, server/src/lib/header-value.ts and fallback-loop.ts). X-Routed-Via is
/// "platform/model"; X-Fallback-Trail is "platform/model keyN=reason" hops joined by "; ", possibly ending "; +N more". Model
/// ids keep their slashes; '%', control and non-ASCII characters arrive percent-encoded.
/// </summary>
public static class RoutingHeaders
{
    /// <summary>Null when the value names no platform, as with "cache" or "idempotency".</summary>
    public static RoutedVia? ParseRoutedVia(string? header)
    {
        var slash = header?.IndexOf('/') ?? -1;
        return slash <= 0 || slash == header!.Length - 1
            ? null
            : new RoutedVia(Uri.UnescapeDataString(header[..slash]), Uri.UnescapeDataString(header[(slash + 1)..]));
    }

    public static IReadOnlyList<FailedHop> ParseTrail(string? header)
    {
        var hops = new List<FailedHop>();
        foreach (var segment in (header ?? "").Split("; ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var space = segment.LastIndexOf(' ');
            var equals = space < 0 ? -1 : segment.IndexOf('=', space);
            if (equals < 0 || ParseRoutedVia(segment[..space]) is not { } via)
            {
                continue; // "+N more", or a hop this parser doesn't recognise
            }

            hops.Add(new FailedHop(via.Platform, via.Model, segment[(equals + 1)..]));
        }

        return hops;
    }

    public static bool IsAllowed(RoutedVia via, IReadOnlySet<string> allowedPlatforms) => allowedPlatforms.Contains(via.Platform);
}
