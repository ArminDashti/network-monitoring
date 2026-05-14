#if WINDOWS
using System.Net;
using NetworkMonitor.Native;
using NetworkMonitor.Storage;

namespace NetworkMonitor.Services;

internal sealed class TrafficCollector
{
    private readonly NicResolver _nics;
    private readonly HostNameCache _hosts;
    private readonly Dictionary<string, (ulong Out, ulong In)> _last = new(StringComparer.Ordinal);

    public TrafficCollector(NicResolver nics, HostNameCache hosts)
    {
        _nics = nics;
        _hosts = hosts;
    }

    public IReadOnlyList<TrafficDelta> CollectDeltas()
    {
        _nics.Refresh();
        var deltas = new List<TrafficDelta>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var c in IpHelperApi.EnumerateTcp4Connections())
        {
            if (IsNonPeer(c.RemoteIp, c.RemotePort))
                continue;

            AddConnection(
                seen,
                deltas,
                c.LocalIp,
                c.RemoteIp,
                c.RemotePort,
                c.OwningPid,
                c.DataBytesOut,
                c.DataBytesIn,
                "tcp4");
        }

        foreach (var c in IpHelperApi.EnumerateTcp6Connections())
        {
            if (IsNonPeer(c.RemoteIp, c.RemotePort))
                continue;

            AddConnection(
                seen,
                deltas,
                c.LocalIp,
                c.RemoteIp,
                c.RemotePort,
                c.OwningPid,
                c.DataBytesOut,
                c.DataBytesIn,
                "tcp6");
        }

        foreach (var stale in _last.Keys.Where(k => !seen.Contains(k)).ToList())
            _last.Remove(stale);

        return deltas;
    }

    private void AddConnection(
        HashSet<string> seen,
        List<TrafficDelta> deltas,
        IPAddress localIp,
        IPAddress remoteIp,
        ushort remotePort,
        uint owningPid,
        ulong bytesOut,
        ulong bytesIn,
        string familyTag)
    {
        var nic = _nics.ResolveNicName(localIp);
        var remoteKey = remoteIp.ToString();
        var key = $"{familyTag}|{localIp}|{remoteKey}|{remotePort}";
        seen.Add(key);

        var prevOut = 0UL;
        var prevIn = 0UL;
        if (_last.TryGetValue(key, out var prev))
        {
            prevOut = prev.Out;
            prevIn = prev.In;
        }

        var deltaOut = Delta(bytesOut, prevOut);
        var deltaIn = Delta(bytesIn, prevIn);
        _last[key] = (bytesOut, bytesIn);

        if (deltaOut == 0 && deltaIn == 0)
            return;

        var host = _hosts.GetHostLabel(remoteIp);
        var app = ProcessNameResolver.GetAppName(owningPid);

        deltas.Add(new TrafficDelta(
            app,
            nic,
            remoteKey,
            remotePort,
            host,
            (long)deltaOut,
            (long)deltaIn));
    }

    private static ulong Delta(ulong current, ulong previous)
    {
        if (current >= previous)
            return current - previous;
        return current;
    }

    private static bool IsNonPeer(IPAddress remote, ushort remotePort)
    {
        if (remotePort == 0)
            return true;

        if (remote.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var bytes = remote.GetAddressBytes();
            if (bytes.Length == 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0)
                return true;
        }

        return false;
    }
}
#endif
