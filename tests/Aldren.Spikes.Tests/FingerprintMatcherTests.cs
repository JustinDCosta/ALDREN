using S7.Network.Presence;
using Xunit;

namespace Aldren.Spikes.Tests;

public class FingerprintMatcherTests
{
    private static readonly Guid HomeNetwork = Guid.Parse("6f1c2a52-3c1d-4b8e-9a77-0d2f4c1e9b10");
    private static readonly NetworkObservation HomeEthernet = new(HomeNetwork, "AA:BB:CC:DD:EE:FF", null, "Realtek PCIe GbE Family Controller", false);
    private static readonly NetworkObservation Tailscale = new(Guid.NewGuid(), "11:22:33:44:55:66", null, "Tailscale Tunnel", true);
    private static readonly HashSet<string> Home = [Fingerprint.Of(HomeNetwork, "AA:BB:CC:DD:EE:FF")];

    [Fact]
    public void Matching_fingerprint_is_home() =>
        Assert.Equal(PresenceState.Home, FingerprintMatcher.Evaluate([Tailscale, HomeEthernet], Home));

    [Fact]
    public void Unknown_physical_network_is_away()
    {
        var hotspot = new NetworkObservation(Guid.NewGuid(), "02:5A:1B:3C:4D:5E", "Phone", "Intel Wi-Fi 6 AX201 160MHz", false);

        Assert.Equal(PresenceState.Away, FingerprintMatcher.Evaluate([hotspot], Home));
    }

    [Fact]
    public void No_physical_network_is_unknown() => Assert.Equal(PresenceState.Unknown, FingerprintMatcher.Evaluate([], Home));

    [Fact]
    public void Virtual_adapter_alone_is_unknown() => Assert.Equal(PresenceState.Unknown, FingerprintMatcher.Evaluate([Tailscale], Home));

    [Fact]
    public void Mac_formats_give_the_same_fingerprint() =>
        Assert.Equal(Fingerprint.Of(HomeNetwork, "AA:BB:CC:DD:EE:FF"), Fingerprint.Of(HomeNetwork, "aa-bb-cc-dd-ee-ff"));
}
