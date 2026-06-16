using System.Collections.Concurrent;

using System.Net;

using System.Net.NetworkInformation;

using System.Net.Sockets;



namespace Netvan.Services;



internal sealed class NicResolver

{

    private readonly ConcurrentDictionary<string, string> _localIpToNic = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, bool> _localIpIsVpn = new(StringComparer.OrdinalIgnoreCase);



    private static readonly string[] VpnKeywords =

    [

        "vpn",

        "wireguard",

        "wintun",

        "tap-",

        "tun ",

        "openvpn",

        "nordlynx",

        "tailscale",

        "zerotier",

        "hamachi",

        "softether",

    ];



    public void Refresh()

    {

        _localIpToNic.Clear();

        _localIpIsVpn.Clear();



        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())

        {

            if (ni.OperationalStatus != OperationalStatus.Up)

                continue;



            var isVpn = IsVpnInterface(ni);



            foreach (var addr in ni.GetIPProperties().UnicastAddresses)

            {

                if (addr.Address.AddressFamily != AddressFamily.InterNetwork)

                    continue;



                var key = addr.Address.ToString();

                _localIpToNic[key] = ni.Name;

                if (isVpn)

                    _localIpIsVpn[key] = true;

            }

        }

    }



    public string ResolveNicName(IPAddress localIp)

    {

        var key = localIp.ToString();

        if (_localIpToNic.TryGetValue(key, out var name))

            return name;



        if (IPAddress.IsLoopback(localIp))

            return "Loopback";



        return "Unknown";

    }



    public bool IsVpnLocalIp(IPAddress localIp) =>

        _localIpIsVpn.ContainsKey(localIp.ToString());



    private static bool IsVpnInterface(NetworkInterface ni)

    {

        if (ni.NetworkInterfaceType is NetworkInterfaceType.Ppp or NetworkInterfaceType.Tunnel)

            return true;



        var haystack = $"{ni.Name} {ni.Description}".ToLowerInvariant();

        return VpnKeywords.Any(keyword => haystack.Contains(keyword, StringComparison.Ordinal));

    }

}


