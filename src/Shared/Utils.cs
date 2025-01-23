using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Shared;

public static class Utils
{
    public static IPAddress? GetLocalIPAddress()
    {
        NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
        foreach (NetworkInterface ni in networkInterfaces.Where(x => x.OperationalStatus == OperationalStatus.Up))
        {
            if (
                ni is
                {
                    SupportsMulticast: true,
                    NetworkInterfaceType: NetworkInterfaceType.Ethernet
                    or NetworkInterfaceType.Wireless80211
                }
                and not { NetworkInterfaceType: NetworkInterfaceType.Loopback }
            )
            {
                IPInterfaceProperties props = ni.GetIPProperties();
                UnicastIPAddressInformation? result = props.UnicastAddresses.FirstOrDefault(x =>
                    x.Address.AddressFamily == AddressFamily.InterNetwork);
                if (result is not null)
                {
                    return result.Address;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Helper method that generates random TCP port
    /// in range [<see cref="Config.MinTcpPort"/>; <see cref="Config.MaxTcpPort"/>].
    /// </summary>
    /// <returns>Port for TCP</returns>
    public static int GetRandomTcpPort()
        => new Random().Next(Config.MinTcpPort, Config.MaxTcpPort);
}
