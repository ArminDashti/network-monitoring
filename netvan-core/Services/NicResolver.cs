using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;

namespace Netvan.Services;

internal sealed class NicResolver
{
    private readonly ConcurrentDictionary<string, string> _localIpToNic = new(StringComparer.OrdinalIgnoreCase); // Maps local IP to NIC display name

    // Rebuilds the local IP to network interface name map from active adapters
    public void Refresh()
    {
        _localIpToNic.Clear(); // Drop stale addresses from unplugged or disabled NICs
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) // Walk every adapter on the machine
        {
            if (ni.OperationalStatus != OperationalStatus.Up) // Skip adapters that are down
                continue;

            foreach (var addr in ni.GetIPProperties().UnicastAddresses) // Each assigned IP on this adapter
            {
                var key = NormalizeLocalKey(addr.Address); // Use a stable string key for lookups
                _localIpToNic[key] = ni.Name; // Remember which NIC owns this local address
            }
        }
    }

    // Finds the network interface name for a connection's local IP address
    public string ResolveNicName(IPAddress localIp)
    {
        var key = NormalizeLocalKey(localIp); // Match the same key format used in Refresh
        if (_localIpToNic.TryGetValue(key, out var name)) // Known address from a live adapter
            return name;

        if (IPAddress.IsLoopback(localIp)) // 127.0.0.1 style traffic
            return "Loopback";

        return "Unknown"; // Address not seen on any up interface
    }

    // Builds a lookup key that treats IPv4-mapped IPv6 like plain IPv4
    private static string NormalizeLocalKey(IPAddress ip)
    {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6) // IPv6 family
        {
            if (ip.IsIPv4MappedToIPv6) // ::ffff:192.0.2.1 style address
                return ip.MapToIPv4().ToString(); // Store as dotted IPv4 text
        }

        return ip.ToString(); // Default string form for the address
    }
}
