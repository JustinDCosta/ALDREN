namespace S7.Network.Presence;

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
