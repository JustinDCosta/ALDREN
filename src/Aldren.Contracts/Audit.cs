namespace Aldren.Contracts;

public sealed record AuditQuery(int Limit = 100, long? BeforeId = null)
{
    public const int MaxLimit = 1_000;

    public int EffectiveLimit => Math.Clamp(Limit, 1, MaxLimit);
}

public sealed record AuditEntry(
    long Id,
    DateTimeOffset Timestamp,
    string Tool,
    string Tier,
    string Origin,
    string Outcome,
    string? Detail,
    int DurationMs,
    bool Confirmed,
    bool Tainted);
