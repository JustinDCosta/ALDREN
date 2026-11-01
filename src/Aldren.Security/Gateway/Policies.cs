using Aldren.Security.Tools;

namespace Aldren.Security.Gateway;

public enum ConfirmationLevel
{
    None,
    Standard,

    /// <summary>Shows the full details of the action.</summary>
    Explicit,
}

public sealed record PolicyDecision(bool Allowed, ConfirmationLevel Confirmation, string? Reason)
{
    public static PolicyDecision Allow(ConfirmationLevel confirmation = ConfirmationLevel.None, string? reason = null) =>
        new(true, confirmation, reason);

    public static PolicyDecision Refuse(string reason) => new(false, ConfirmationLevel.None, reason);
}

public interface IToolPolicy
{
    PolicyDecision Evaluate(ToolDescriptor tool, CallContext context);
}

/// <summary>SECURITY.md's tier rules.</summary>
public sealed class TierPolicy : IToolPolicy
{
    public PolicyDecision Evaluate(ToolDescriptor tool, CallContext context) => tool.Tier switch
    {
        RiskTier.Read or RiskTier.Reversible => PolicyDecision.Allow(),
        RiskTier.Impactful => PolicyDecision.Allow(ConfirmationLevel.Standard),
        RiskTier.Destructive => PolicyDecision.Allow(ConfirmationLevel.Explicit),
        _ => PolicyDecision.Refuse($"{tool.Name} is in the Forbidden tier and never runs."),
    };
}

/// <summary>
/// A model-proposed call at tier Impactful or above, made while untrusted content is in context, needs confirmation
/// whatever the settings say. The tier rule asks anyway today; this keeps the rule once a tier-2 tool can be relaxed.
/// </summary>
public sealed class TaintPolicy : IToolPolicy
{
    public PolicyDecision Evaluate(ToolDescriptor tool, CallContext context) =>
        context.Origin == CallOrigin.Model && context.Tainted && tool.Tier >= RiskTier.Impactful
            ? PolicyDecision.Allow(ConfirmationLevel.Standard, "Untrusted content is in context.")
            : PolicyDecision.Allow();
}
