using System.Text.Json;

namespace Aldren.Security.Tools;

/// <summary>
/// The untyped face of <see cref="Tool{TArgs, TResult}"/>, used only by the Tool Gateway. Tools never run except through it.
/// </summary>
public interface ITool
{
    ToolDescriptor Descriptor { get; }

    /// <summary>Schema checks, then the tool's own checks.</summary>
    BindResult<object> Bind(JsonElement arguments);

    string Summarize(object arguments);

    /// <summary>The tier for these arguments; the gateway only ever uses it to raise the declared tier.</summary>
    RiskTier TierFor(object arguments) => Descriptor.Tier;

    Task<object?> RunAsync(object arguments, CancellationToken ct);

    Task<bool> VerifyAsync(object arguments, object? result, CancellationToken ct);
}
