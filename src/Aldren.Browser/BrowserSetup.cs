using System.Text.Json;
using Microsoft.Win32;

namespace Aldren.Browser;

public enum SiteLevel
{
    ReadOnly,
    Interact,
    Forms,
}

/// <summary>
/// Per-site levels (ARCHITECTURE §12): reading everywhere; clicking unless you set a site read-only; filling forms asks each
/// time unless you allowed forms on that site. Kept as JSON in ALDREN's data folder.
/// </summary>
public sealed class SitePolicy(string path)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public SiteLevel For(string url)
    {
        var host = Host(url);
        var levels = Load();
        // The most specific saved entry wins ("mail.example.com" over "example.com").
        var match = levels.Keys.Where(k => host == k || host.EndsWith("." + k, StringComparison.Ordinal)).OrderByDescending(k => k.Length).FirstOrDefault();
        return match is null ? SiteLevel.Interact : levels[match];
    }

    public IReadOnlyDictionary<string, SiteLevel> All() => Load();

    public void Set(string site, SiteLevel? level)
    {
        var levels = Load();
        var host = Host(site.Contains("://", StringComparison.Ordinal) ? site : "https://" + site);
        if (level is null)
        {
            levels.Remove(host);
        }
        else
        {
            levels[host] = level.Value;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(levels, Json));
    }

    public static string Host(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u)
            ? (u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host).ToLowerInvariant()
            : url.ToLowerInvariant();

    private Dictionary<string, SiteLevel> Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<Dictionary<string, SiteLevel>>(File.ReadAllText(path), Json) ?? [] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}

/// <summary>
/// Registers the browser host with Chrome and Edge for the current user: one manifest file next to the host and one HKCU
/// key per browser. Done only from Settings > Browser, on your click; "Disconnect" removes the keys.
/// </summary>
public static class HostRegistration
{
    public const string HostName = "com.aldren.companion";

    /// <summary>Fixed by the "key" in the extension's manifest (the same key as the S6 probe).</summary>
    public const string ExtensionId = "mhipkigoofdhdiigilekjfpkhemnjajm";

    private static readonly string[] Keys =
    [
        $@"Software\Google\Chrome\NativeMessagingHosts\{HostName}",
        $@"Software\Microsoft\Edge\NativeMessagingHosts\{HostName}",
    ];

    public static bool IsRegistered => Keys.Any(k => Registry.CurrentUser.OpenSubKey(k)?.GetValue(null) is string);

    public static string Register(string hostExe)
    {
        if (!File.Exists(hostExe))
        {
            throw new FileNotFoundException("The browser host isn't next to ALDREN; publish it first.", hostExe);
        }

        var manifest = Path.Combine(Path.GetDirectoryName(hostExe)!, $"{HostName}.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new
        {
            name = HostName,
            description = "ALDREN browser host",
            path = hostExe,
            type = "stdio",
            allowed_origins = new[] { $"chrome-extension://{ExtensionId}/" },
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var k in Keys)
        {
            using var key = Registry.CurrentUser.CreateSubKey(k);
            key.SetValue(null, manifest);
        }

        return manifest;
    }

    public static void Unregister()
    {
        foreach (var k in Keys)
        {
            Registry.CurrentUser.DeleteSubKeyTree(k, throwOnMissingSubKey: false);
        }
    }
}
