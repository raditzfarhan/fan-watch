using System.Runtime.InteropServices;

namespace FanWatch.Core;

/// <summary>A TCP port some process is listening on. LoopbackOnly = reachable only from this PC.</summary>
public sealed record Listener(int Port, int Pid, bool LoopbackOnly);

public interface IPortSource
{
    IReadOnlyList<Listener> Read();
}

/// <summary>Listening TCP sockets (IPv4 + IPv6) with their owning PID, via GetExtendedTcpTable. No admin rights needed.</summary>
public sealed unsafe class TcpPortSource : IPortSource
{
    private const int AfInet = 2, AfInet6 = 23, TcpTableOwnerPidListener = 3;
    private const int Row4Size = 24, Row6Size = 56;
    private byte[] _buffer = new byte[64 * 1024];

    public IReadOnlyList<Listener> Read()
    {
        var list = new List<Listener>(128);
        ReadFamily(AfInet, list);
        ReadFamily(AfInet6, list);
        return list;
    }

    private void ReadFamily(int family, List<Listener> into)
    {
        while (true)
        {
            int size = _buffer.Length;
            fixed (byte* buf = _buffer)
            {
                uint rc = GetExtendedTcpTable(buf, ref size, false, family, TcpTableOwnerPidListener, 0);
                if (rc == 122 /* ERROR_INSUFFICIENT_BUFFER */) { _buffer = new byte[size + 16 * 1024]; continue; }
                if (rc != 0) return;

                uint count = *(uint*)buf;
                int rowSize = family == AfInet ? Row4Size : Row6Size;
                for (uint i = 0; i < count; i++)
                {
                    long off = 4 + i * (long)rowSize;
                    if (off + rowSize > size) break;
                    byte* r = buf + off;
                    if (family == AfInet)
                    {
                        // MIB_TCPROW_OWNER_PID: state, localAddr, localPort, remoteAddr, remotePort, owningPid
                        bool loopback = r[4] == 127;
                        into.Add(new Listener(NetPort(*(uint*)(r + 8)), (int)*(uint*)(r + 20), loopback));
                    }
                    else
                    {
                        // MIB_TCP6ROW_OWNER_PID: localAddr[16], localScope, localPort, remoteAddr[16], remoteScope, remotePort, state, owningPid
                        bool loopback = IsIPv6Loopback(r);
                        into.Add(new Listener(NetPort(*(uint*)(r + 20)), (int)*(uint*)(r + 52), loopback));
                    }
                }
                return;
            }
        }
    }

    private static int NetPort(uint raw) => (int)(((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF));

    private static bool IsIPv6Loopback(byte* a)
    {
        for (int i = 0; i < 15; i++) if (a[i] != 0) return false;
        return a[15] == 1;
    }

    [DllImport("iphlpapi.dll")]
    private static extern uint GetExtendedTcpTable(byte* table, ref int size, bool order, int family, int tableClass, uint reserved);
}
