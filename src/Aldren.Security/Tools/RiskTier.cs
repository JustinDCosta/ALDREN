namespace Aldren.Security.Tools;

/// <summary>SECURITY.md's risk tiers.</summary>
public enum RiskTier
{
    Read = 0,
    Reversible = 1,
    Impactful = 2,
    Destructive = 3,
    Forbidden = 4,
}
