using System.Text.Json.Nodes;

namespace Aldren.Security.Tools;

/// <param name="ArgumentSchema">Exported with the binder's options, so a model sees exactly what the binder enforces.</param>
public sealed record ToolDescriptor(
    string Name,
    string Description,
    RiskTier Tier,
    string Permission,
    TimeSpan Timeout,
    JsonNode ArgumentSchema);
