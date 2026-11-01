using Aldren.Security.Permissions;
using Aldren.Security.Tools;

namespace Aldren.Core.Tools;

public sealed record DiagnosticsConfirmArgs;

/// <summary>Changes nothing; exists so the confirmation path can be tried end to end.</summary>
public sealed class DiagnosticsConfirmTool : Tool<DiagnosticsConfirmArgs, string>
{
    public override string Name => "diagnostics_confirm_test";
    public override string Description => "Run the confirmation test";
    public override RiskTier Tier => RiskTier.Impactful;
    public override string Permission => PermissionCatalog.DiagnosticsTest;

    public override string Summarize(DiagnosticsConfirmArgs args) => "Run ALDREN's confirmation test. Nothing on your PC changes.";

    protected override Task<string> ExecuteAsync(DiagnosticsConfirmArgs args, CancellationToken ct) => Task.FromResult("confirmed");
}
