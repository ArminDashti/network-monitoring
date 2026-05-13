using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;

namespace NetworkMonitor.Services;

internal sealed class NicResolver
{
    private readonly ConcurrentDictionary<string, string> _localIpToNic = new(StringComparer.OrdinalIgnoreCase);

    public void Refresh()
    {
        _localIpToNic.Clear();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;

            foreach (var addr in ni.GetIPProperties().UnicastAddresses)
            {
                var key = NormalizeLocalKey(addr.Address);
                _localIpToNic[key] = ni.Name;
            }
        }
    }

    public string ResolveNicName(IPAddress localIp)
    {
        var key = NormalizeLocalKey(localIp);
        if (_localIpToNic.TryGetValue(key, out var name))
            return name;

        if (IPAddress.IsLoopback(localIp))
            return "Loopback";

        return "Unknown";
    }

    private static string NormalizeLocalKey(IPAddress ip)
    {
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv4MappedToIPv6)
                return ip.MapToIPv4().ToString();
        }

        return ip.ToString();
    }
}
