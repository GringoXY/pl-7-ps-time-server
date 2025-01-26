using Shared;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Server;

internal sealed class Server
{
    private static Thread _serverShutdownThread;
    private static readonly CancellationTokenSource _cancellationTokenSource = new();

    private static readonly ConcurrentDictionary<UdpDiscover, Thread> _udpDiscovers = [];
    private static readonly ConcurrentDictionary<TcpConnection, Thread> _tcpConnections = [];

    // https://stackoverflow.com/questions/9855230/how-do-i-get-the-network-interface-and-its-right-ipv4-address
    private static readonly IEnumerable<IPAddress> _availableIpAddresses
        = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni =>
                ni is
                {
                    OperationalStatus: OperationalStatus.Up,
                    NetworkInterfaceType: NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211,
                    SupportsMulticast: true,
                }
                and not { NetworkInterfaceType: NetworkInterfaceType.Loopback }
            )
            .Select(ni => ni.GetIPProperties().UnicastAddresses)
            .SelectMany(u => u)
            .Where(u => u.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(u => u.Address);

    private static readonly ConcurrentDictionary<string, ClientState> _clientStats = [];

    public void Start()
    {
        _serverShutdownThread = new(ShutdownHandler);
        _serverShutdownThread.Start();

        foreach (IPAddress ipAddress in _availableIpAddresses)
        {
            int port = Utils.GetRandomTcpPort();

            UdpDiscover udpDiscover = new(ipAddress, port, _cancellationTokenSource.Token);
            Thread udpDiscoverThread = new(udpDiscover.Start);
            udpDiscoverThread.Start();
            _udpDiscovers.TryAdd(udpDiscover, udpDiscoverThread);

            TcpConnection tcpConnection = new(ipAddress, port, _cancellationTokenSource.Token);
            Thread tcpConnectionThread = new(tcpConnection.Start);
            tcpConnectionThread.Start();
            _tcpConnections.TryAdd(tcpConnection, tcpConnectionThread);

            Console.WriteLine($"Listening on {ipAddress}:{port}");
        }
    }

    private static void ShutdownHandler()
    {
        Console.WriteLine($"\"{Config.ShutdownKey}\" shutdowns server");
        while (Console.ReadKey(true).Key != Config.ShutdownKey);

        _cancellationTokenSource.Cancel();

        foreach ((UdpDiscover udpDiscover, Thread thread) in _udpDiscovers)
        {
            udpDiscover.Shutdown();
            thread.Join();
        }

        foreach ((TcpConnection tcpConnection, Thread thread) in _tcpConnections)
        {
            tcpConnection.Shutdown();
            thread.Join();
        }
    }
}
