using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace NetworkMonitor.Services;

internal sealed class HostNameCache
{
    private readonly ConcurrentDictionary<string, (string Host, DateTime ExpiresUtc)> _cache = new(StringComparer.OrdinalIgnoreCase); // DNS results keyed by IP text
    private readonly TimeSpan _ttl; // How long a successful lookup stays valid

    // Creates the cache with an optional time-to-live for host names
    public HostNameCache(TimeSpan? ttl = null)
    {
        _ttl = ttl ?? TimeSpan.FromHours(6); // Default six hour cache for resolved names
    }

    // Returns a friendly host label for a remote IP, using cache and reverse DNS
    public string GetHostLabel(IPAddress remote)
    {
        if (remote.AddressFamily == AddressFamily.InterNetworkV6 && remote.IsIPv4MappedToIPv6) // Normalize dual-stack form
            remote = remote.MapToIPv4();

        if (IPAddress.IsLoopback(remote)) // Local machine endpoints
            return "localhost";

        var key = remote.ToString(); // Cache key is the IP string
        if (_cache.TryGetValue(key, out var e) && e.ExpiresUtc > DateTime.UtcNow) // Fresh cached name
            return e.Host;

        try
        {
            var entry = Dns.GetHostEntry(remote); // Reverse DNS lookup
            var host = entry.HostName; // Primary host name from DNS
            if (string.Equals(host, key, StringComparison.OrdinalIgnoreCase)) // DNS only echoed the IP
                host = key;
            _cache[key] = (host, DateTime.UtcNow.Add(_ttl)); // Store success until TTL expires
            return host;
        }
        catch
        {
            _cache[key] = (key, DateTime.UtcNow.Add(TimeSpan.FromMinutes(15))); // Short cache on lookup failure
            return key; // Fall back to showing the IP address
        }
    }
}
