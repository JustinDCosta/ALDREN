using System.Text.Json.Nodes;
using Aldren.Security.Gateway;
using Aldren.Security.Tools;

namespace Aldren.Security.Tests.Gateway;

public sealed class PolicyTests
{
    private static ToolDescriptor Tool(RiskTier tier) =>
        new("some_tool", "A tool", tier, "test.allowed", TimeSpan.FromSeconds(10), new JsonObject());

    private static readonly CallContext User = new(CallOrigin.User, []);
    private static readonly CallContext TaintedModel = new(CallOrigin.Model, ["web page: example.com"]);
    private static readonly CallContext CleanModel = new(CallOrigin.Model, []);

    [Theory]
    [InlineData(RiskTier.Read, true, ConfirmationLevel.None)]
    [InlineData(RiskTier.Reversible, true, ConfirmationLevel.None)]
    [InlineData(RiskTier.Impactful, true, ConfirmationLevel.Standard)]
    [InlineData(RiskTier.Destructive, true, ConfirmationLevel.Explicit)]
    [InlineData(RiskTier.Forbidden, false, ConfirmationLevel.None)]
    public void Tier_policy_follows_the_security_table(RiskTier tier, bool allowed, ConfirmationLevel confirmation)
    {
        var decision = new TierPolicy().Evaluate(Tool(tier), User);

        Assert.Equal(allowed, decision.Allowed);
        Assert.Equal(confirmation, decision.Confirmation);
    }

    [Theory]
    [InlineData(RiskTier.Impactful)]
    [InlineData(RiskTier.Destructive)]
    public void Taint_policy_asks_for_tainted_model_calls_at_tier2_and_above(RiskTier tier)
    {
        var decision = new TaintPolicy().Evaluate(Tool(tier), TaintedModel);

        Assert.True(decision.Allowed);
        Assert.Equal(ConfirmationLevel.Standard, decision.Confirmation);
    }

    [Fact]
    public void Taint_policy_ignores_reversible_calls()
    {
        Assert.Equal(ConfirmationLevel.None, new TaintPolicy().Evaluate(Tool(RiskTier.Reversible), TaintedModel).Confirmation);
    }

    [Fact]
    public void Taint_policy_ignores_clean_and_user_calls()
    {
        Assert.Equal(ConfirmationLevel.None, new TaintPolicy().Evaluate(Tool(RiskTier.Impactful), CleanModel).Confirmation);
        Assert.Equal(ConfirmationLevel.None, new TaintPolicy().Evaluate(Tool(RiskTier.Impactful), new CallContext(CallOrigin.User, ["clipboard"])).Confirmation);
    }
}
