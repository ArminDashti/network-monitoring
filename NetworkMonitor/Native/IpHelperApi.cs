using System.Buffers.Binary;
using System.Net;
using System.Runtime.InteropServices;

namespace NetworkMonitor.Native;

internal static class IpHelperApi
{
    public const int AfInet = 2;
    public const int AfInet6 = 23;

    public const int TcpTableOwnerPidAll = 5;

    public const int TcpConnectionEstatsData = 1;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedTcpTable(
        nint pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved = 0);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetPerTcpConnectionEStats(
        ref MibTcpRow row,
        int estatsType,
        nint rw,
        uint rwVersion,
        uint rwSize,
        uint offset);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedTcp6Table(
        nint pTcpTable,
        ref int pdwSize,
        bool bOrder,
        int ulAf,
        int tableClass,
        uint reserved = 0);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetPerTcp6ConnectionEStats(
        ref MibTcp6Row row,
        int estatsType,
        nint rw,
        uint rwVersion,
        uint rwSize,
        uint offset);

    [StructLayout(LayoutKind.Sequential)]
    public struct MibTcpRow
    {
        public uint State;
        public uint LocalAddr;
        public uint LocalPort;
        public uint RemoteAddr;
        public uint RemotePort;
    }

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

    public static ushort NetworkOrderPort(uint dwPort) =>
        BinaryPrimitives.ReverseEndianness((ushort)(dwPort & 0xFFFF));

    public static IPAddress ToIPv4(uint addr) => new IPAddress(addr);

    public static IPAddress ToIPv6(byte[] bytes)
    {
        if (bytes.Length != 16)
            throw new ArgumentException("IPv6 address must be 16 bytes.", nameof(bytes));
        return new IPAddress(bytes);
    }

    public static bool TryGetTcp4DataStats(ref MibTcpRow row, out TcpEstatsDataRodV0 data)
    {
        data = default;
        var size = (uint)Marshal.SizeOf<TcpEstatsDataRodV0>();
        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            var code = GetPerTcpConnectionEStats(ref row, TcpConnectionEstatsData, buffer, 0, size, 0);
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

    public static List<Tcp4ConnectionSnapshot> EnumerateTcp4Connections()
    {
        var result = new List<Tcp4ConnectionSnapshot>();
        var size = 0;
        _ = GetExtendedTcpTable(nint.Zero, ref size, true, AfInet, TcpTableOwnerPidAll);
        if (size <= 0)
            return result;

        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var ret = GetExtendedTcpTable(buffer, ref size, true, AfInet, TcpTableOwnerPidAll);
            if (ret != 0)
                return result;

            var numEntries = Marshal.ReadInt32(buffer);
            var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
            var offset = sizeof(int);

            for (var i = 0; i < numEntries; i++)
            {
                var ptr = IntPtr.Add(buffer, offset + i * rowSize);
                var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(ptr);
                var localIp = ToIPv4(row.LocalAddr);
                var remoteIp = ToIPv4(row.RemoteAddr);
                var localPort = NetworkOrderPort(row.LocalPort);
                var remotePort = NetworkOrderPort(row.RemotePort);

                var mib = new MibTcpRow
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
            Marshal.FreeHGlobal(buffer);
        }

        return result;
    }

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

                var mib = MibTcp6Row.FromOwnerRow(row);

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
