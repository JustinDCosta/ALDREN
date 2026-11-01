using System.Text.Json;

namespace S6.Host;

/// <summary>The host forwards only these commands, each shaped {"id": int, "cmd": name, "args": {}}, and refuses the rest itself.</summary>
public static class CommandCheck
{
    private static readonly HashSet<string> Commands = ["ping", "read", "click", "fill"];

    /// <summary>Why the line can't be forwarded, or null when it can.</summary>
    public static string? Problem(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number
                || !id.TryGetInt32(out _))
            {
                return "Each command needs an integer id.";
            }

            if (!root.TryGetProperty("cmd", out var cmd) || cmd.ValueKind != JsonValueKind.String || !Commands.Contains(cmd.GetString()!))
            {
                return "Unknown command.";
            }

            return root.TryGetProperty("args", out var args) && args.ValueKind != JsonValueKind.Object ? "args must be an object." : null;
        }
        catch (JsonException)
        {
            return "Not JSON.";
        }
    }
}
