using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace LocalXI.Core.Network;

public static class PortProbe
{
    public static bool IsTcpListening(int port) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(e => e.Port == port);

    public static bool IsUdpBound(int port) =>
        IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners().Any(e => e.Port == port);

    /// <summary>First port at or after <paramref name="start"/> that nothing is listening on.</summary>
    public static int FindFreeTcpPort(int start, int attempts = 100)
    {
        var used = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Select(e => e.Port).ToHashSet();
        for (var port = start; port < start + attempts; port++)
        {
            if (!used.Contains(port))
            {
                return port;
            }
        }

        throw new InvalidOperationException($"No free port found between {start} and {start + attempts}.");
    }

    /// <summary>Whether a TCP connection to host:port can be opened - used to check a friend's server is reachable.</summary>
    public static async Task<bool> CanConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var client = new TcpClient();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(host, port, cts.Token);
            return true;
        }
        catch (Exception e) when (e is SocketException or OperationCanceledException)
        {
            return false;
        }
    }

    public static bool IsLocalAddress(string address) =>
        IPAddress.TryParse(address, out var ip) &&
        (IPAddress.IsLoopback(ip) || NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Any(u => u.Address.Equals(ip)));
}
