using Aldren.Contracts;

namespace Aldren.Security.Audit;

public interface IAuditLog
{
    Task<long> AppendAsync(AuditRecord record, CancellationToken ct);

    /// <summary>Finished entries, newest first.</summary>
    Task<IReadOnlyList<AuditEntry>> QueryAsync(AuditQuery query, CancellationToken ct);

    Task<int> PruneAsync(DateTimeOffset olderThan, CancellationToken ct);
}
