#if WINDOWS
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

    public const uint MibTcpStateEstablished = 5;
    private const uint ErrorInsufficientBuffer = 122;
    private static readonly Lazy<bool> Tcp6ApisAvailable = new(DetectTcp6Apis);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> Tcp4CollectionEnabled = new(StringComparer.Ordinal);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> Tcp6CollectionEnabled = new(StringComparer.Ordinal);

    private static bool DetectTcp6Apis()
    {
        if (!NativeLibrary.TryLoad("iphlpapi.dll", out var handle))
            return false;

        return NativeLibrary.TryGetExport(handle, "GetExtendedTcp6Table", out _)
            && NativeLibrary.TryGetExport(handle, "GetPerTcp6ConnectionEStats", out _);
    }

    // Loads the extended IPv4 TCP table from iphlpapi.dll
    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedTcpTable(
        nint pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved = 0);

    // Enables or disables extended statistics for one IPv4 TCP connection
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint SetPerTcpConnectionEStats(
        ref MibTcpRow row,
        int estatsType,
        nint rw,
        uint rwVersion,
        uint rwSize,
        uint offset);

    // Reads extended statistics for one IPv4 TCP connection
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetPerTcpConnectionEStats(
        ref MibTcpRow row,
        int estatsType,
        nint rw,
        uint rwVersion,
        uint rwSize,
        nint ros,
        uint rosVersion,
        uint rosSize,
        nint rod,
        uint rodVersion,
        uint rodSize);
    // Enables or disables extended statistics for one IPv6 TCP connection
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint SetPerTcp6ConnectionEStats(
        ref MibTcp6Row row,
        int estatsType,
        nint rw,
        uint rwVersion,
        uint rwSize,
        uint offset);

    // Reads extended statistics for one IPv6 TCP connection
    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetPerTcp6ConnectionEStats(
        ref MibTcp6Row row,
        int estatsType,
        nint rw,
        uint rwVersion,
        uint rwSize,
        nint ros,
        uint rosVersion,
        uint rosSize,
        nint rod,
        uint rodVersion,
        uint rodSize);

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

    // Read/write configuration for TCP data eStats (enable collection)
    [StructLayout(LayoutKind.Sequential)]
    private struct TcpEstatsDataRwV0
    {
        [MarshalAs(UnmanagedType.U1)]
        public byte EnableCollection;
    }

    // Windows TCP_ESTATS_DATA_ROD_v0 (tcpestats.h)
    [StructLayout(LayoutKind.Sequential)]
    public struct TcpEstatsDataRodV0
    {
        public ulong DataBytesOut;
        public ulong DataSegsOut;
        public ulong DataBytesIn;
        public ulong DataSegsIn;
        public ulong SegsOut;
        public ulong SegsIn;
        public uint SoftErrors;
        public uint SoftErrorReason;
        public uint SndUna;
        public uint SndNxt;
        public uint SndMax;
        public ulong ThruBytesAcked;
        public uint RcvNxt;
        public ulong ThruBytesReceived;
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

    // Writes eStats API diagnostics to stdout (NETM_DEBUG=1).
    public static void PrintStatsDiagnostics()
    {
        const uint established = 5;
        Console.WriteLine($"TcpEstatsDataRodV0 size: {Marshal.SizeOf<TcpEstatsDataRodV0>()} bytes");
        Console.WriteLine($"TcpEstatsDataRwV0 size:   {Marshal.SizeOf<TcpEstatsDataRwV0>()} bytes");
        Console.WriteLine($"EnumerateTcp4Connections: {EnumerateTcp4Connections().Count}");

        var size = 0;
        _ = GetExtendedTcpTable(nint.Zero, ref size, true, AfInet, TcpTableOwnerPidAll);
        if (size <= 0)
            return;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, true, AfInet, TcpTableOwnerPidAll) != 0)
                return;

            var numEntries = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            var offset = sizeof(int);
            for (var i = 0; i < numEntries; i++)
            {
                var ptr = IntPtr.Add(buffer, offset + i * rowSize);
                var owner = Marshal.PtrToStructure<MibTcpRowOwnerPid>(ptr);
                if (owner.State != established || owner.RemotePort == 0)
                    continue;

                var mib = new MibTcpRow
                {
                    State = owner.State,
                    LocalAddr = owner.LocalAddr,
                    LocalPort = owner.LocalPort,
                    RemoteAddr = owner.RemoteAddr,
                    RemotePort = owner.RemotePort,
                };

                var rwSize = (uint)Marshal.SizeOf<TcpEstatsDataRwV0>();
                var rodSize = (uint)Marshal.SizeOf<TcpEstatsDataRodV0>();
                var rwSet = Marshal.AllocHGlobal((int)rwSize);
                var rwGet = Marshal.AllocHGlobal((int)rwSize);
                var rodBuffer = Marshal.AllocHGlobal((int)rodSize);
                try
                {
                    Marshal.StructureToPtr(new TcpEstatsDataRwV0 { EnableCollection = 1 }, rwSet, false);
                    var setCode = SetPerTcpConnectionEStats(ref mib, TcpConnectionEstatsData, rwSet, 0, rwSize, 0);
                    var getFull = GetPerTcpConnectionEStats(
                        ref mib, TcpConnectionEstatsData,
                        rwGet, 0, rwSize, nint.Zero, 0, 0, rodBuffer, 0, rodSize);
                    var rw = Marshal.PtrToStructure<TcpEstatsDataRwV0>(rwGet);
                    var rod = Marshal.PtrToStructure<TcpEstatsDataRodV0>(rodBuffer);
                    Console.WriteLine(
                        $"Sample row set={setCode} get={getFull} enabled={rw.EnableCollection} out={rod.DataBytesOut} in={rod.DataBytesIn}");
                }
                finally
                {
                    Marshal.FreeHGlobal(rwSet);
                    Marshal.FreeHGlobal(rwGet);
                    Marshal.FreeHGlobal(rodBuffer);
                }

                return;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    // Tries to read IPv4 TCP data byte counters for one connection row
    public static bool TryGetTcp4DataStats(ref MibTcpRow row, out TcpEstatsDataRodV0 data)
    {
        data = default;
        TryEnableTcp4DataCollection(ref row);

        var rwSize = (uint)Marshal.SizeOf<TcpEstatsDataRwV0>();
        var rodSize = (uint)Marshal.SizeOf<TcpEstatsDataRodV0>();
        var rwBuffer = Marshal.AllocHGlobal((int)rwSize);
        var rodBuffer = Marshal.AllocHGlobal((int)rodSize);
        try
        {
            var code = GetPerTcpConnectionEStats(
                ref row,
                TcpConnectionEstatsData,
                rwBuffer, 0, rwSize,
                nint.Zero, 0, 0,
                rodBuffer, 0, rodSize);
            if (code != 0)
                return false;

            var rw = Marshal.PtrToStructure<TcpEstatsDataRwV0>(rwBuffer);
            if (rw.EnableCollection == 0)
                return false;

            data = Marshal.PtrToStructure<TcpEstatsDataRodV0>(rodBuffer);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(rwBuffer);
            Marshal.FreeHGlobal(rodBuffer);
        }
    }

    // Tries to read IPv6 TCP data byte counters for one connection row
    public static bool TryGetTcp6DataStats(ref MibTcp6Row row, out TcpEstatsDataRodV0 data)
    {
        data = default;
        TryEnableTcp6DataCollection(ref row);

        var rwSize = (uint)Marshal.SizeOf<TcpEstatsDataRwV0>();
        var rodSize = (uint)Marshal.SizeOf<TcpEstatsDataRodV0>();
        var rwBuffer = Marshal.AllocHGlobal((int)rwSize);
        var rodBuffer = Marshal.AllocHGlobal((int)rodSize);
        try
        {
            var code = GetPerTcp6ConnectionEStats(
                ref row,
                TcpConnectionEstatsData,
                rwBuffer, 0, rwSize,
                nint.Zero, 0, 0,
                rodBuffer, 0, rodSize);
            if (code != 0)
                return false;

            var rw = Marshal.PtrToStructure<TcpEstatsDataRwV0>(rwBuffer);
            if (rw.EnableCollection == 0)
                return false;

            data = Marshal.PtrToStructure<TcpEstatsDataRodV0>(rodBuffer);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(rwBuffer);
            Marshal.FreeHGlobal(rodBuffer);
        }
    }

    private static void TryEnableTcp4DataCollection(ref MibTcpRow row)
    {
        var key = $"{row.LocalAddr}:{row.LocalPort}:{row.RemoteAddr}:{row.RemotePort}";
        if (!Tcp4CollectionEnabled.TryAdd(key, 1))
            return;

        var rw = new TcpEstatsDataRwV0 { EnableCollection = 1 };
        var rwSize = (uint)Marshal.SizeOf<TcpEstatsDataRwV0>();
        var rwBuffer = Marshal.AllocHGlobal((int)rwSize);
        try
        {
            Marshal.StructureToPtr(rw, rwBuffer, false);
            _ = SetPerTcpConnectionEStats(ref row, TcpConnectionEstatsData, rwBuffer, 0, rwSize, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(rwBuffer);
        }
    }

    private static void TryEnableTcp6DataCollection(ref MibTcp6Row row)
    {
        var key = $"{row.LocalAddr[0]}:{row.LocalPort}:{row.RemoteAddr[0]}:{row.RemotePort}";
        if (!Tcp6CollectionEnabled.TryAdd(key, 1))
            return;

        var rw = new TcpEstatsDataRwV0 { EnableCollection = 1 };
        var rwSize = (uint)Marshal.SizeOf<TcpEstatsDataRwV0>();
        var rwBuffer = Marshal.AllocHGlobal((int)rwSize);
        try
        {
            Marshal.StructureToPtr(rw, rwBuffer, false);
            _ = SetPerTcp6ConnectionEStats(ref row, TcpConnectionEstatsData, rwBuffer, 0, rwSize, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(rwBuffer);
        }
    }

    public static int LastTcp4RawRowCount { get; private set; }

    // Lists all IPv4 TCP connections with byte counters and owning process
    public static List<Tcp4ConnectionSnapshot> EnumerateTcp4Connections()
    {
        LastTcp4RawRowCount = 0;
        var result = new List<Tcp4ConnectionSnapshot>(); // Output snapshots
        var size = 0;
        nint buffer = nint.Zero;
        try
        {
            uint ret;
            while (true)
            {
                ret = GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidAll);
                if (ret == 0)
                    break;
                if (ret != ErrorInsufficientBuffer)
                    return result;
                if (buffer != nint.Zero)
                    Marshal.FreeHGlobal(buffer);
                if (size <= 0)
                    return result;
                buffer = Marshal.AllocHGlobal(size);
            }

            var numEntries = Marshal.ReadInt32(buffer); // First int is row count
            LastTcp4RawRowCount = numEntries;
            if (numEntries < 0 || numEntries > 100_000)
                return result;
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

                if (row.State != MibTcpStateEstablished)
                    continue;

                var mib = new MibTcpRow // Row shape needed by stats API
                {
                    State = row.State,
                    LocalAddr = row.LocalAddr,
                    LocalPort = row.LocalPort,
                    RemoteAddr = row.RemoteAddr,
                    RemotePort = row.RemotePort,
                };

                if (!TryGetTcp4DataStats(ref mib, out var estats))
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
            if (buffer != nint.Zero)
                Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

    // Lists all IPv6 TCP connections with byte counters and owning process
    public static List<Tcp6ConnectionSnapshot> EnumerateTcp6Connections()
    {
        if (!Tcp6ApisAvailable.Value)
            return [];

        var result = new List<Tcp6ConnectionSnapshot>();
        var size = 0;
        nint buffer = nint.Zero;
        try
        {
            uint ret;
            while (true)
            {
                ret = GetExtendedTcpTable(buffer, ref size, false, AfInet6, TcpTableOwnerPidAll);
                if (ret == 0)
                    break;
                if (ret != ErrorInsufficientBuffer)
                    return result;
                if (buffer != nint.Zero)
                    Marshal.FreeHGlobal(buffer);
                if (size <= 0)
                    return result;
                buffer = Marshal.AllocHGlobal(size);
            }

            var numEntries = Marshal.ReadInt32(buffer);
            if (numEntries < 0 || numEntries > 100_000)
                return result;
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

                if (row.State != MibTcpStateEstablished)
                    continue;

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
            if (buffer != nint.Zero)
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
#endif
