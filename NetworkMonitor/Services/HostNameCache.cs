using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace NetworkMonitor.Services;

internal sealed class HostNameCache
{
    private readonly ConcurrentDictionary<string, (string Host, DateTime ExpiresUtc)> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _ttl;

    public HostNameCache(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromHours(6);
    }

    public string GetHostLabel(IPAddress remote)
    {
        if (remote.AddressFamily == AddressFamily.InterNetworkV6 && remote.IsIPv4MappedToIPv6)
            remote = remote.MapToIPv4();

        if (IPAddress.IsLoopback(remote))
            return "localhost";

        var key = remote.ToString();
        if (_cache.TryGetValue(key, out var e) && e.ExpiresUtc > DateTime.UtcNow)
            return e.Host;

        try
        {
            var entry = Dns.GetHostEntry(remote);
            var host = entry.HostName;
            if (string.Equals(host, key, StringComparison.OrdinalIgnoreCase))
                host = key;
            _cache[key] = (host, DateTime.UtcNow.Add(_ttl));
            return host;
        }
        catch
        {
            _cache[key] = (key, DateTime.UtcNow.Add(TimeSpan.FromMinutes(15)));
            return key;
        }
    }
}
