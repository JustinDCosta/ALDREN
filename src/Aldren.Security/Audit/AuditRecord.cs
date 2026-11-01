using Aldren.Security.Gateway;
using Aldren.Security.Tools;

namespace Aldren.Security.Audit;

public enum AuditStage
{
    Started,
    Finished,
}

/// <param name="Arguments">Raw JSON; the log redacts and shortens it before storing.</param>
public sealed record AuditRecord(
    DateTimeOffset Timestamp,
    string RequestId,
    string Tool,
    RiskTier Tier,
    CallOrigin Origin,
    string Arguments,
    AuditStage Stage,
    ToolOutcomeStatus? Outcome,
    string? Detail,
    TimeSpan? Duration,
    bool Confirmed,
    bool Tainted);
