using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LocalXI.Core.Network;

public enum NetworkKind
{
    Lan,
    RadminVpn,
    Hamachi,
    ZeroTier,
    Tailscale,
    Virtual,
    Other,
}

/// <summary>An IPv4 address on this PC that friends could use to reach a hosted server.</summary>
public sealed record HostAddress(string Address, string AdapterName, NetworkKind Kind, bool HasGateway)
{
    public string Label => Kind switch
    {
        NetworkKind.RadminVpn => $"Radmin VPN - {Address}",
        NetworkKind.Hamachi => $"Hamachi - {Address}",
        NetworkKind.ZeroTier => $"ZeroTier - {Address}",
        NetworkKind.Tailscale => $"Tailscale - {Address}",
        NetworkKind.Lan => $"Home network ({AdapterName}) - {Address}",
        NetworkKind.Virtual => $"Virtual adapter ({AdapterName}) - {Address}",
        _ => $"{AdapterName} - {Address}",
    };

    public bool IsVpn => Kind is NetworkKind.RadminVpn or NetworkKind.Hamachi or NetworkKind.ZeroTier or NetworkKind.Tailscale;
}

public static class NetworkInfo
{
    public static IReadOnlyList<HostAddress> GetHostAddresses()
    {
        var results = new List<HostAddress>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
            {
                continue;
            }

            var properties = nic.GetIPProperties();
            var hasGateway = properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
            foreach (var unicast in properties.UnicastAddresses)
            {
                var ip = unicast.Address;
                if (ip.AddressFamily != AddressFamily.InterNetwork || IsLinkLocal(ip))
                {
                    continue;
                }

                results.Add(new HostAddress(ip.ToString(), nic.Name, Classify(nic, ip), hasGateway));
            }
        }

        // VPNs first (that's what remote friends need), then the real home network, then everything else.
        return results
            .OrderBy(a => a.IsVpn ? 0 : a.Kind == NetworkKind.Lan && a.HasGateway ? 1 : a.Kind == NetworkKind.Lan ? 2 : 3)
            .ThenBy(a => a.Address, StringComparer.Ordinal)
            .ToList();
    }

    public static HostAddress? Suggest() => GetHostAddresses().FirstOrDefault(a => a.Kind != NetworkKind.Virtual);

    internal static NetworkKind Classify(NetworkInterface nic, IPAddress ip) => Classify(nic.Name + " " + nic.Description, ip);

    internal static NetworkKind Classify(string adapterText, IPAddress ip)
    {
        var text = adapterText.ToLowerInvariant();
        if (text.Contains("radmin"))
        {
            return NetworkKind.RadminVpn;
        }

        if (text.Contains("hamachi"))
        {
            return NetworkKind.Hamachi;
        }

        if (text.Contains("zerotier"))
        {
            return NetworkKind.ZeroTier;
        }

        if (text.Contains("tailscale"))
        {
            return NetworkKind.Tailscale;
        }

        if (text.Contains("vethernet") || text.Contains("hyper-v") || text.Contains("virtualbox") || text.Contains("vmware") || text.Contains("wsl"))
        {
            return NetworkKind.Virtual;
        }

        return IsPrivate(ip) ? NetworkKind.Lan : NetworkKind.Other;
    }

    internal static bool IsPrivate(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168);
    }

    private static bool IsLinkLocal(IPAddress ip)
    {
        var b = ip.GetAddressBytes();
        return b[0] == 169 && b[1] == 254;
    }
}
