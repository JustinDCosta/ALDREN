using System.Net;
using System.Runtime.InteropServices;

namespace Aldren.Bench.Gateway;

public readonly record struct TcpRow(IPEndPoint Local, IPEndPoint Remote, string State, int ProcessId);

/// <summary>Every TCP socket with its owning process (GetExtendedTcpTable), IPv4 and IPv6. Closed connections show as PID 0.</summary>
public static class TcpTable
{
    private const int AfInet = 2, AfInet6 = 23, OwnerPidAll = 5;
    private static readonly string[] States = ["?", "CLOSED", "LISTEN", "SYN_SENT", "SYN_RCVD", "ESTABLISHED", "FIN_WAIT1", "FIN_WAIT2",
        "CLOSE_WAIT", "CLOSING", "LAST_ACK", "TIME_WAIT", "DELETE_TCB"];

    public static List<TcpRow> All() => [.. Read(AfInet), .. Read(AfInet6)];

    private static List<TcpRow> Read(int family)
    {
        var size = 0;
        _ = GetExtendedTcpTable(0, ref size, false, family, OwnerPidAll, 0);
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, family, OwnerPidAll, 0) != 0)
            {
                return [];
            }

            var rows = new List<TcpRow>();
            var count = Marshal.ReadInt32(buffer);
            for (var i = 0; i < count; i++)
            {
                if (family == AfInet)
                {
                    // MIB_TCPROW_OWNER_PID: state, local address, local port, remote address, remote port, pid
                    var row = buffer + 4 + i * 24;
                    rows.Add(new TcpRow(
                        new IPEndPoint(new IPAddress((uint)Marshal.ReadInt32(row, 4)), Port(row, 8)),
                        new IPEndPoint(new IPAddress((uint)Marshal.ReadInt32(row, 12)), Port(row, 16)),
                        State(Marshal.ReadInt32(row)), Marshal.ReadInt32(row, 20)));
                }
                else
                {
                    // MIB_TCP6ROW_OWNER_PID: local address[16], scope, port, remote address[16], scope, port, state, pid
                    var row = buffer + 4 + i * 56;
                    rows.Add(new TcpRow(
                        new IPEndPoint(Address6(row, 0), Port(row, 20)),
                        new IPEndPoint(Address6(row, 24), Port(row, 44)),
                        State(Marshal.ReadInt32(row, 48)), Marshal.ReadInt32(row, 52)));
                }
            }

            return rows;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // Ports are stored in network byte order in the low 16 bits.
    private static int Port(nint row, int offset)
    {
        var raw = Marshal.ReadInt32(row, offset);
        return ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF);
    }

    private static IPAddress Address6(nint row, int offset)
    {
        var bytes = new byte[16];
        Marshal.Copy(row + offset, bytes, 0, 16);
        return new IPAddress(bytes);
    }

    private static string State(int state) => state > 0 && state < States.Length ? States[state] : "?";

    [DllImport("iphlpapi.dll")]
    private static extern int GetExtendedTcpTable(nint table, ref int size, bool order, int family, int tableClass, int reserved);
}
