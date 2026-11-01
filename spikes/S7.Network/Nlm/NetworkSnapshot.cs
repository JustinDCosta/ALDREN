using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using S7.Network.Presence;

namespace S7.Network.Nlm;

/// <summary>One adapter's view of a connected network, with what the console shows next to the observation.</summary>
internal sealed record AdapterView(NetworkObservation Observation, string NetworkName, string Category, string? GatewayIp, string InterfaceName);

/// <summary>Connected networks from Windows' Network List Manager, each with its adapter, gateway MAC and (if Windows allows) Wi-Fi name.</summary>
internal static partial class NetworkSnapshot
{
    public static (List<AdapterView> Views, double Milliseconds) Take(bool withSsid)
    {
        var clock = Stopwatch.StartNew();
        var interfaces = NetworkInterface.GetAllNetworkInterfaces().ToDictionary(n => Guid.Parse(n.Id));
        var ssid = withSsid ? ConnectedSsid() : null;
        var views = new List<AdapterView>();
        var nlm = (INetworkListManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"))!)!;
        var networks = nlm.GetNetworks(NlmEnumNetwork.Connected);
        while (networks.Next(1, out var network, out var fetched) == 0 && fetched == 1 && network is not null)
        {
            var id = network.GetNetworkId();
            var name = network.GetName();
            var category = network.GetCategory().ToString();
            var connections = network.GetNetworkConnections();
            while (connections.Next(1, out var connection, out var got) == 0 && got == 1 && connection is not null)
            {
                if (!interfaces.TryGetValue(connection.GetAdapterId(), out var nic) || Ipv4Index(nic) is not { } index)
                {
                    continue;
                }

                var (gatewayIp, gatewayMac) = Gateway(index);
                var isWifi = nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
                var observation = new NetworkObservation(id, gatewayMac, isWifi ? ssid : null, nic.Description, !IsHardware(index));
                views.Add(new AdapterView(observation, name, category, gatewayIp, nic.Name));
            }
        }

        return (views, clock.Elapsed.TotalMilliseconds);
    }

    /// <summary>The default gateway on one interface (best route to an off-link address; nothing is sent) and its MAC address.</summary>
    private static (string? Ip, string? Mac) Gateway(int interfaceIndex)
    {
        var destination = new byte[SockaddrInetSize];
        BinaryPrimitives.WriteUInt16LittleEndian(destination, AfInet);
        IPAddress.Parse("1.1.1.1").GetAddressBytes().CopyTo(destination, 4);
        var route = new byte[IpForwardRow2Size];
        var source = new byte[SockaddrInetSize];
        if (GetBestRoute2(0, (uint)interfaceIndex, 0, destination, 0, route, source) != 0)
        {
            return (null, null);
        }

        var nextHop = route.AsSpan(NextHopOffset, SockaddrInetSize);
        var ip = new IPAddress(nextHop.Slice(4, 4));
        if (ip.Equals(IPAddress.Any))
        {
            return (null, null); // on-link only: no gateway on this interface
        }

        var neighbour = new byte[IpNetRow2Size];
        nextHop.CopyTo(neighbour);
        BinaryPrimitives.WriteUInt32LittleEndian(neighbour.AsSpan(28), (uint)interfaceIndex);
        if (ResolveIpNetEntry2(neighbour, 0) != 0)
        {
            return (ip.ToString(), null);
        }

        var length = BinaryPrimitives.ReadUInt32LittleEndian(neighbour.AsSpan(72));
        return (ip.ToString(), length == 6 ? Convert.ToHexString(neighbour, 40, 6) : null);
    }

    /// <summary>Physical adapters whose link is up, whether or not they have an address yet.</summary>
    public static HashSet<string> PhysicalLinksUp() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && Ipv4Index(n) is { } index && IsHardware(index))
            .Select(n => n.Name).ToHashSet();

    // GetIPv4Properties throws for adapters without IPv4 bound (some tunnels and virtual adapters), so ask first.
    private static int? Ipv4Index(NetworkInterface nic) =>
        nic.Supports(NetworkInterfaceComponent.IPv4) ? nic.GetIPProperties().GetIPv4Properties()?.Index : null;

    /// <summary>NDIS marks physical NICs as hardware interfaces; VPN tunnels (Tailscale's Wintun), Hyper-V and other virtual adapters aren't.</summary>
    private static bool IsHardware(int interfaceIndex)
    {
        var row = new byte[IfRow2Size];
        BinaryPrimitives.WriteUInt32LittleEndian(row.AsSpan(8), (uint)interfaceIndex);
        return GetIfEntry2(row) == 0 && (row[IfRow2FlagsOffset] & 1) != 0;
    }

    /// <summary>Wi-Fi name from netsh. Windows 11 hides it unless location access is allowed for desktop apps, so null is normal.</summary>
    private static string? ConnectedSsid()
    {
        try
        {
            using var netsh = Process.Start(new ProcessStartInfo("netsh.exe", "wlan show interfaces")
                { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true })!;
            var output = netsh.StandardOutput.ReadToEnd();
            netsh.WaitForExit(3000);
            return SsidLine().Match(output) is { Success: true } m ? m.Groups[1].Value.Trim() : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"^\s*SSID\s*:\s*(.+)$", RegexOptions.Multiline)]
    private static partial Regex SsidLine();

    // iphlpapi structures, read by offset (x64 layouts from netioapi.h).
    private const ushort AfInet = 2;
    private const int SockaddrInetSize = 28;
    private const int IpForwardRow2Size = 104, NextHopOffset = 44;
    private const int IpNetRow2Size = 88;
    private const int IfRow2Size = 1352, IfRow2FlagsOffset = 1152;

    [DllImport("iphlpapi.dll")]
    private static extern int GetBestRoute2(nint interfaceLuid, uint interfaceIndex, nint sourceAddress, byte[] destinationAddress,
        uint addressSortOptions, byte[] bestRoute, byte[] bestSourceAddress);

    [DllImport("iphlpapi.dll")]
    private static extern int ResolveIpNetEntry2(byte[] row, nint sourceAddress);

    [DllImport("iphlpapi.dll")]
    private static extern int GetIfEntry2(byte[] row);
}
