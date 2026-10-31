using System.Text.Json.Nodes;

namespace Aldren.Bench.Eval;

/// <summary>One eval case. Kind is route, tool, persona or injection.</summary>
public record EvalCase(
    string Id,
    string Lang,
    string Kind,
    string Prompt,
    string? UntrustedContent,
    string? ExpectedRoute,
    string? ExpectedTool,
    JsonObject? ExpectedArgs);

/// <summary>What the model did: Route is derived from the tool it called (quick when none).</summary>
public record ModelResponse(string Route, string? ToolName, JsonObject? ToolArgs, string Text);

public record EvalResult(string CaseId, bool Passed, string? Reason);
