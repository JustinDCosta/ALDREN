namespace Aldren.Contracts;

/// <param name="Summary">The action in plain words.</param>
/// <param name="UntrustedSources">Untrusted content in the model's context when it proposed the call; empty otherwise.</param>
public sealed record ConfirmationRequest(
    string RequestId,
    string Tool,
    string Tier,
    string Summary,
    IReadOnlyList<string> UntrustedSources,
    int TimeoutSeconds);
