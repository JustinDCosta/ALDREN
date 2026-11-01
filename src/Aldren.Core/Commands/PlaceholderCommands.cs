using System.Text;
using System.Text.Json;
using Aldren.Security.Gateway;

namespace Aldren.Core.Commands;

/// <summary>Stands in for Phase 2's fast path: a few fixed phrases, each run through the Tool Gateway.</summary>
public sealed class PlaceholderCommands(ToolGateway gateway)
{
    public const string NotYet = "I can't do that yet: understanding requests arrives in Phase 2.";

    private static readonly HashSet<string> TimePhrases = ["time", "what time is it", "whats the time"];
    private static readonly JsonElement NoArguments = JsonDocument.Parse("{}").RootElement.Clone();
    private static readonly CallContext Typed = new(CallOrigin.User, []);

    public async Task<string> HandleAsync(string text, CancellationToken ct)
    {
        var phrase = Normalize(text);
        if (TimePhrases.Contains(phrase))
        {
            var outcome = await InvokeAsync("get_time", ct);
            return outcome.Status == ToolOutcomeStatus.Succeeded
                ? $"It's {outcome.Result!["localTime"]} ({outcome.Result["timeZone"]})."
                : outcome.Message;
        }

        if (phrase == "test confirmation")
        {
            var outcome = await InvokeAsync("diagnostics_confirm_test", ct);
            return outcome.Status == ToolOutcomeStatus.Succeeded ? "Confirmation test passed." : outcome.Message;
        }

        return NotYet;
    }

    private Task<ToolOutcome> InvokeAsync(string tool, CancellationToken ct) =>
        gateway.InvokeAsync(new ToolCall(tool, NoArguments, Guid.NewGuid().ToString("N")), Typed, ct);

    // Lower case, punctuation dropped, spaces collapsed: "What's the time?" → "whats the time".
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
            }
            else if (char.IsWhiteSpace(c) && builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().TrimEnd();
    }
}
