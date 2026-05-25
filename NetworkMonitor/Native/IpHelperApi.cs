using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;

namespace NetworkMonitor.Native;

internal static class IpHelperApi
{
    public const int AfInet = 2; // IPv4 address family constant for Windows APIs
    public const int AfInet6 = 23; // IPv6 address family constant for Windows APIs

    public const int TcpTableOwnerPidAll = 5; // Request full TCP table including owning process id

    public const int TcpConnectionEstatsData = 1; // Ask for per-connection data byte statistics

    // Loads the extended IPv4 TCP table from iphlpapi.dll
    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedTcpTable(
        nint pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved = 0);

    // Reads data-byte counters for one IPv4 TCP connection
    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetPerTcpConnectionEStats(
        ref MibTcpRow row,
        int estatsType,
        nint rw,
        uint rwVersion,
        uint rwSize,
        uint offset);

    // Loads the extended IPv6 TCP table from iphlpapi.dll
    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedTcp6Table(
        nint pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved = 0);

    // Reads data-byte counters for one IPv6 TCP connection
    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetPerTcp6ConnectionEStats(
        ref MibTcp6Row row,
        int estatsType,
        nint rw,
        uint rwVersion,
        uint rwSize,
        uint offset);

    // Windows layout for a basic IPv4 TCP row
    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
    }

    // Windows layout for a basic IPv6 TCP row
    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcp6Row
    {
        public uint State;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;

        // Copies fields from the owner-PID row shape into the stats API row shape
        public static MibTcp6Row FromOwnerRow(MibTcp6RowOwnerPid owner)
        {
            return new MibTcp6Row
            {
                State = owner.State,
                LocalAddr = (byte[])owner.LocalAddr.Clone(),
                LocalScopeId = owner.LocalScopeId,
                LocalPort = owner.LocalPort,
                RemoteAddr = (byte[])owner.RemoteAddr.Clone(),
                RemoteScopeId = owner.RemoteScopeId,
                RemotePort = owner.RemotePort,
            };
        }
    }

    // Windows layout for IPv4 TCP row plus owning process id
    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
        public uint OwningPid;
    }

    // Windows layout for IPv6 TCP row plus owning process id
    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcp6RowOwnerPid
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] LocalAddr;
        public uint LocalScopeId;
        public uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
        public byte[] RemoteAddr;
        public uint RemoteScopeId;
        public uint RemotePort;
        public uint State;
        public uint OwningPid;
    }

    // Windows layout for TCP data statistics returned by eStats APIs
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct TcpEstatsDataRodV0 // Rod = Raw Offload Data. This struct contains statistics for TCP connections that use raw offload.
    {
        public ulong DataBytesOut;
        public ulong DataBytesIn;
        public ulong DataSegsOut;
        public ulong DataSegsIn;
        public uint OutRsts;
        public uint MaxDataSegsOut;
        public uint MaxDataSegsIn;
        public uint SumDataSegsOut;
        public uint SumDataSegsIn;
        public uint MaxRwin;
        public uint MaxSwin;
        public uint MaxMss;
        public uint MinMss;
        public uint SumRwin;
        public uint SumSwin;
        public uint FastRexmit;
        public uint FastRecover;
        public uint FastTransmit;
        public uint DataTxCong;
        public uint PktsRetrans;
        public uint SlowStart;
        public uint CongAvoid;
        public uint OtherReductions;
        public uint CongOverAvgCount;
        public uint LimRwin;
        public uint LimSwin;
        public uint LimMss;
        public uint LimDataSize;
        public uint LimSndUna;
        public uint LimSndNxt;
        public uint LimCwnd;
        public uint LimSsthresh;
        public uint LimRwinScalePre;
        public uint LimRwinScalePost;
        public uint LimRtt;
        public uint LimMssPost;
    }

    // Converts a Windows network-order port field to host ushort
    public static ushort NetworkOrderPort(uint dwPort) =>
        BinaryPrimitives.ReverseEndianness((ushort)(dwPort & 0xFFFF));

    // Wraps a raw IPv4 address integer as System.Net.IPAddress
    public static IPAddress ToIPv4(uint addr) => new IPAddress(addr);

    // Wraps a 16-byte IPv6 address buffer as System.Net.IPAddress
    public static IPAddress ToIPv6(byte[] bytes)
    {
        if (bytes.Length != 16) // Windows always uses 16 bytes for IPv6
            throw new ArgumentException("IPv6 address must be 16 bytes.", nameof(bytes));
        return new IPAddress(bytes);
    }

    // Tries to read IPv4 TCP data byte counters for one connection row
    public static bool TryGetTcp4DataStats(ref MibTcpRow row, out TcpEstatsDataRodV0 data)
    {
        data = default;
        var size = (uint)Marshal.SizeOf<TcpEstatsDataRodV0>(); // Size of the stats struct
        var buffer = Marshal.AllocHGlobal((int)size); // Unmanaged buffer for the API
        try
        {
            var code = GetPerTcpConnectionEStats(ref row, TcpConnectionEstatsData, buffer, 0, size, 0); // Call Windows
            if (code != 0) // Non-zero means the stats call failed
                return false;
            data = Marshal.PtrToStructure<TcpEstatsDataRodV0>(buffer); // Copy stats into managed struct
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer); // Always free unmanaged memory
        }
    }

    // Tries to read IPv6 TCP data byte counters for one connection row
    public static bool TryGetTcp6DataStats(ref MibTcp6Row row, out TcpEstatsDataRodV0 data)
    {
        data = default;
        var size = (uint)Marshal.SizeOf<TcpEstatsDataRodV0>();
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var code = GetPerTcp6ConnectionEStats(ref row, TcpConnectionEstatsData, buffer, 0, size, 0);
            if (code != 0)
                return false;
            data = Marshal.PtrToStructure<TcpEstatsDataRodV0>(buffer);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // Lists all IPv4 TCP connections with byte counters and owning process
    public static List<Tcp4ConnectionSnapshot> EnumerateTcp4Connections()
    {
        var result = new List<Tcp4ConnectionSnapshot>(); // Output snapshots
        var size = 0; // Required buffer size starts at zero
        _ = GetExtendedTcpTable(nint.Zero, ref size, true, AfInet, TcpTableOwnerPidAll); // Size probe call
        if (size <= 0) // No table or API unavailable
            return result;

        var buffer = Marshal.AllocHGlobal(size); // Allocate table buffer
        try
        {
            var ret = GetExtendedTcpTable(buffer, ref size, true, AfInet, TcpTableOwnerPidAll); // Fill table
            if (ret != 0) // API error
                return result;

            var numEntries = Marshal.ReadInt32(buffer); // First int is row count
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>(); // Bytes per row
            var offset = sizeof(int); // Rows start after the count

            for (var i = 0; i < numEntries; i++) // Walk each TCP row
            {
                var ptr = IntPtr.Add(buffer, offset + i * rowSize); // Pointer to this row
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(ptr); // Unmarshal row
                var localIp = ToIPv4(row.LocalAddr); // Decode local address
                var remoteIp = ToIPv4(row.RemoteAddr); // Decode remote address
                var localPort = NetworkOrderPort(row.LocalPort); // Host-order local port
                var remotePort = NetworkOrderPort(row.RemotePort); // Host-order remote port

                var mib = new MibTcpRow // Row shape needed by stats API
                {
                    State = row.State,
                    LocalAddr = row.LocalAddr,
                    LocalPort = row.LocalPort,
                    RemoteAddr = row.RemoteAddr,
                    RemotePort = row.RemotePort,
                };

                if (!TryGetTcp4DataStats(ref mib, out var estats)) // Skip rows without stats
                    continue;

                result.Add(new Tcp4ConnectionSnapshot(
                    row.State,
                    localIp,
                    localPort,
                    remoteIp,
                    remotePort,
                    row.OwningPid,
                    estats.DataBytesOut,
                    estats.DataBytesIn));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer); // Release table memory
        }

        return result;
    }

    // Lists all IPv6 TCP connections with byte counters and owning process
    public static List<Tcp6ConnectionSnapshot> EnumerateTcp6Connections()
    {
        var result = new List<Tcp6ConnectionSnapshot>();
        var size = 0;
        _ = GetExtendedTcp6Table(nint.Zero, ref size, true, AfInet6, TcpTableOwnerPidAll);
        if (size <= 0)
            return result;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var ret = GetExtendedTcp6Table(buffer, ref size, true, AfInet6, TcpTableOwnerPidAll);
            if (ret != 0)
                return result;

            var numEntries = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibTcp6RowOwnerPid>();
            var offset = sizeof(int);

            for (var i = 0; i < numEntries; i++)
            {
                var ptr = IntPtr.Add(buffer, offset + i * rowSize);
                var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(ptr);
                var localIp = ToIPv6(row.LocalAddr);
                var remoteIp = ToIPv6(row.RemoteAddr);
                var localPort = NetworkOrderPort(row.LocalPort);
                var remotePort = NetworkOrderPort(row.RemotePort);

                var mib = MibTcp6Row.FromOwnerRow(row); // Convert to stats API layout

                if (!TryGetTcp6DataStats(ref mib, out var estats))
                    continue;

                result.Add(new Tcp6ConnectionSnapshot(
                    row.State,
                    localIp,
                    localPort,
                    remoteIp,
                    remotePort,
                    row.OwningPid,
                    estats.DataBytesOut,
                    estats.DataBytesIn));
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }
}

internal readonly record struct Tcp4ConnectionSnapshot(
    uint State,
    IPAddress LocalIp,
    ushort LocalPort,
    IPAddress RemoteIp,
    ushort RemotePort,
    uint OwningPid,
    ulong DataBytesOut,
    ulong DataBytesIn);

internal readonly record struct Tcp6ConnectionSnapshot(
    uint State,
    IPAddress LocalIp,
    ushort LocalPort,
    IPAddress RemoteIp,
    ushort RemotePort,
    uint OwningPid,
    ulong DataBytesOut,
    ulong DataBytesIn);
