using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aldren.Browser;

namespace Aldren.Core.Tools;

/// <summary>
/// "open youtube", "open github.com": well-known sites by name and anything that looks like a web address. Pages open in
/// a new tab through the Companion when it is connected, otherwise in your default browser (so websites work without the
/// extension; reading and operating pages still needs it).
/// </summary>
public static partial class WebSites
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["youtube"] = "https://www.youtube.com", ["yt"] = "https://www.youtube.com", ["youtube music"] = "https://music.youtube.com",
        ["google"] = "https://www.google.com", ["gmail"] = "https://mail.google.com", ["google mail"] = "https://mail.google.com",
        ["google drive"] = "https://drive.google.com", ["google calendar"] = "https://calendar.google.com",
        ["google maps"] = "https://maps.google.com", ["maps"] = "https://maps.google.com",
        ["google docs"] = "https://docs.google.com", ["google translate"] = "https://translate.google.com",
        ["github"] = "https://github.com", ["stack overflow"] = "https://stackoverflow.com", ["stackoverflow"] = "https://stackoverflow.com",
        ["reddit"] = "https://www.reddit.com", ["twitter"] = "https://x.com", ["x"] = "https://x.com",
        ["facebook"] = "https://www.facebook.com", ["instagram"] = "https://www.instagram.com", ["linkedin"] = "https://www.linkedin.com",
        ["netflix"] = "https://www.netflix.com", ["twitch"] = "https://www.twitch.tv", ["spotify"] = "https://open.spotify.com",
        ["whatsapp"] = "https://web.whatsapp.com", ["whatsapp web"] = "https://web.whatsapp.com", ["telegram"] = "https://web.telegram.org",
        ["wikipedia"] = "https://www.wikipedia.org", ["amazon"] = "https://www.amazon.com", ["chatgpt"] = "https://chatgpt.com",
        ["outlook"] = "https://outlook.live.com", ["discord"] = "https://discord.com/app", ["moodle"] = "https://moodle.org",
    };

    [GeneratedRegex(@"^(?:https?://)?[a-z0-9-]+(?:\.[a-z0-9-]+)*\.[a-z]{2,}(?::\d+)?(?:/\S*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    /// <summary>The address for a web address as typed or said ("github.com", "github dot com"); null when it isn't one.</summary>
    public static string? AddressOf(string text)
    {
        var t = text.Trim().Replace(" dot ", ".", StringComparison.OrdinalIgnoreCase);
        return Address().IsMatch(t) ? (t.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? t : "https://" + t) : null;
    }

    /// <summary>The address of a site known by name ("youtube"); null for anything else.</summary>
    public static string? ByName(string name) => Known.GetValueOrDefault(name.Trim());

    /// <summary>Opens an http(s) address; returns the host, for the reply.</summary>
    public static async Task<string> OpenAsync(BrowserBridge bridge, string address, CancellationToken ct)
    {
        if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("That isn't a web address I can open.");
        }

        if (bridge.Connected || await bridge.PingAsync(ct))
        {
            await bridge.SendAsync("open", new JsonObject { ["url"] = uri.AbsoluteUri }, ct);
        }
        else
        {
            // Only ever an http(s) address, handed to the shell to open with your default browser.
            using var _ = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }

        return uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
    }
}
