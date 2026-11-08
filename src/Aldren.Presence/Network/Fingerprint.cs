using System.Security.Cryptography;
using System.Text;

namespace Aldren.Presence.Network;

/// <summary>One connected network as seen from one adapter. Virtual adapters (VPNs such as Tailscale, Hyper-V) never decide presence.</summary>
public record NetworkObservation(Guid? NetworkId, string? GatewayMac, string? Ssid, string AdapterDescription, bool IsVirtual);

public enum PresenceState
{
    Home,
    Away,
    Unknown,
}

/// <summary>
/// A network's identity: Windows' network ID plus the gateway's MAC address, hashed so the stored value reveals neither.
/// </summary>
public static class Fingerprint
{
    public static string Of(Guid networkId, string gatewayMac) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{networkId:D}|{NormaliseMac(gatewayMac)}")));

    /// <summary>"aa-bb-cc-dd-ee-ff", "AA:BB:CC:DD:EE:FF" and "aabbccddeeff" all become "AA:BB:CC:DD:EE:FF".</summary>
    public static string NormaliseMac(string mac)
    {
        var hex = new string([.. mac.Where(Uri.IsHexDigit)]).ToUpperInvariant();
        return hex.Length == 12
            ? string.Join(':', Enumerable.Range(0, 6).Select(i => hex.Substring(i * 2, 2)))
            : throw new FormatException($"Not a MAC address: {mac}");
    }
}

public static class FingerprintMatcher
{
    /// <summary>
    /// Home if any physical network matches a home fingerprint, Away if physical networks are up but none matches, and Unknown
    /// without one (offline, still identifying, or only a VPN up).
    /// </summary>
    public static PresenceState Evaluate(IReadOnlyList<NetworkObservation> observations, IReadOnlySet<string> homeFingerprints)
    {
        var physical = observations.Where(o => !o.IsVirtual && o.NetworkId is not null && o.GatewayMac is not null).ToList();
        if (physical.Count == 0)
        {
            return PresenceState.Unknown;
        }

        return physical.Any(o => homeFingerprints.Contains(Fingerprint.Of(o.NetworkId!.Value, o.GatewayMac!)))
            ? PresenceState.Home
            : PresenceState.Away;
    }
}
