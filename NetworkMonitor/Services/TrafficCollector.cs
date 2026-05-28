#if WINDOWS
using System.Net;
using NetworkMonitor.Native;
using NetworkMonitor.Storage;

namespace NetworkMonitor.Services;

internal sealed class TrafficCollector
{
    private readonly NicResolver _nics; // Resolves local IP to NIC name
    private readonly HostNameCache _hosts; // Resolves remote IP to host label
    private readonly Dictionary<string, (ulong Out, ulong In)> _last = new(StringComparer.Ordinal); // Last byte counters per connection key
    // Wires helper services used when sampling TCP connections
    public TrafficCollector(NicResolver nics, HostNameCache hosts)
    {
        _nics = nics;
        _hosts = hosts;
    }

    // Samples all TCP connections and returns new byte deltas since the last sample
    public IReadOnlyList<TrafficDelta> CollectDeltas()
    {
        _nics.Refresh(); // Update NIC map before we read local addresses
        var deltas = new List<TrafficDelta>(); // New traffic since last poll
        var seen = new HashSet<string>(StringComparer.Ordinal); // Connection keys still active this sample

        foreach (var c in IpHelperApi.EnumerateTcp4Connections()) // Every IPv4 TCP row from Windows
        {
            if (IsNonPeer(c.RemoteIp, c.RemotePort)) // Skip listening or unset remote endpoints
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

        try
        {
            foreach (var c in IpHelperApi.EnumerateTcp6Connections()) // Every IPv6 TCP row from Windows
            {
                if (IsNonPeer(c.RemoteIp, c.RemotePort)) // Skip listening or unset remote endpoints
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
        }
        catch (Exception)
        {
            // IPv6 table/stats APIs may be unavailable; IPv4 collection continues.
        }

        foreach (var stale in _last.Keys.Where(k => !seen.Contains(k)).ToList()) // Closed connections
            _last.Remove(stale); // Drop counters we will not see again

        return deltas;
    }

    // Turns one TCP row into a traffic delta when counters moved since last time
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
        var nic = _nics.ResolveNicName(localIp); // Which adapter this socket uses
        var remoteKey = remoteIp.ToString(); // Text form for storage and display
        var key = $"{familyTag}|{localIp}|{remoteKey}|{remotePort}"; // Unique key per connection
        seen.Add(key); // Mark as active in this sample

        var prevOut = 0UL; // Previous outbound counter for this key
        var prevIn = 0UL; // Previous inbound counter for this key
        if (_last.TryGetValue(key, out var prev)) // We saw this connection before
        {
            prevOut = prev.Out;
            prevIn = prev.In;
        }

        var deltaOut = Delta(bytesOut, prevOut); // Bytes sent since last sample
        var deltaIn = Delta(bytesIn, prevIn); // Bytes received since last sample
        _last[key] = (bytesOut, bytesIn); // Remember counters for next poll

        if (deltaOut == 0 && deltaIn == 0) // No new data on this connection
            return;

        var host = _hosts.GetHostLabel(remoteIp); // Friendly remote host name
        var app = ProcessNameResolver.GetAppName(owningPid); // Process that owns the socket

        deltas.Add(new TrafficDelta(
            app,
            nic,
            remoteKey,
            remotePort,
            host,
            (long)deltaOut,
            (long)deltaIn));
    }

    // Computes growth since last sample, treating counter reset as a fresh total
    private static ulong Delta(ulong current, ulong previous)
    {
        if (current >= previous) // Normal monotonic counter
            return current - previous;
        return current; // Counter wrapped or connection reset
    }

    // True when the remote side is not a real peer (listen or zero address)
    private static bool IsNonPeer(IPAddress remote, ushort remotePort)
    {
        if (remotePort == 0) // Not connected to a remote port yet
            return true;

        if (remote.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) // IPv4
        {
            var bytes = remote.GetAddressBytes();
            if (bytes.Length == 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0 && bytes[3] == 0) // 0.0.0.0
                return true;
        }

        return false;
    }
}
#endif
