using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aldren.Security.Gateway;

public sealed record ToolCall(string Tool, JsonElement Arguments, string RequestId);

/// <param name="UntrustedSources">Untrusted content in the model's context; empty when there is none.</param>
public sealed record CallContext(CallOrigin Origin, IReadOnlyList<string> UntrustedSources)
{
    public bool Tainted => UntrustedSources.Count > 0;
}

public sealed record ToolOutcome(ToolOutcomeStatus Status, JsonNode? Result, string Message);
