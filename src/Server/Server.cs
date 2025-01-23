using Shared;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

internal sealed class Server
{
    private static Thread _udpDiscoverThread;
    private static UdpClient _udpServerDiscover;

    private static Thread _serverShutdownThread;
    private static bool _shutdownServer = false;

    private static readonly ConcurrentDictionary<TcpListener, Thread> _tcpConnectionThreads = [];
    private static readonly ConcurrentDictionary<TcpClient, Thread> _tcpClientThreads = [];

    private static readonly List<TcpListener> _tpcListeners = [];
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

        _udpDiscoverThread = new(UdpDiscoverHandler);
        _udpDiscoverThread.Start();

        foreach (IPAddress ip in _availableIpAddresses)
        {
            int tcpPort = Utils.GetRandomTcpPort();
            TcpListener tcpListener = new(ip, tcpPort);
            _tpcListeners.Add(tcpListener);

            Thread tcpListenerThread = new(() => TcpConnectionHandler(tcpListener));
            tcpListenerThread.Start();

            _tcpConnectionThreads.TryAdd(tcpListener, tcpListenerThread);

            Console.WriteLine($"Listening on {ip}:{tcpPort}");
        }
    }

    private static void ShutdownHandler()
    {
        Console.WriteLine("\"Q\" shutdowns server");
        while (Console.ReadKey(true).Key != ConsoleKey.Q);

        _shutdownServer = true;

        _udpServerDiscover?.Close();
        _udpDiscoverThread?.Join();

        foreach ((TcpListener tcpListener, Thread thread) in _tcpConnectionThreads)
        {
            tcpListener.Stop();
            thread.Join();
        }

        foreach ((TcpClient tcpClient, Thread thread) in _tcpClientThreads)
        {
            tcpClient.Close();
            thread.Join();
        }

        _serverShutdownThread?.Join();
    }

    private static void UdpDiscoverHandler()
    {
        try
        {
            _udpServerDiscover = new()
            {
                ExclusiveAddressUse = false,
            };

            _udpServerDiscover.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);

            IPAddress localIpAddress = Utils.GetLocalIPAddress();
            IPEndPoint localEndPoint = new(localIpAddress, Config.UdpDiscoverPort);
            _udpServerDiscover.JoinMulticastGroup(Config.MulticastGroupIpAddress, localIpAddress);

            _udpServerDiscover.Client.Bind(localEndPoint);

            IPEndPoint multicastEndPoint = new(Config.MulticastGroupIpAddress, Config.UdpDiscoverPort);

            while (_shutdownServer == false)
            {
                byte[] receivedBytes = _udpServerDiscover.Receive(ref multicastEndPoint);
                string receivedMessage = Encoding.ASCII.GetString(receivedBytes);

                if (receivedMessage.Clear().Equals(Config.DiscoverMessageRequest, StringComparison.CurrentCultureIgnoreCase))
                {
                    OfferIPAddress[] offerIpAddresses = _tpcListeners.Select(tcpListener =>
                    {
                        IPEndPoint ipEndPoint = (IPEndPoint)tcpListener.LocalEndpoint;
                        return new OfferIPAddress(ipEndPoint.Address.ToString(), ipEndPoint.Port);
                    }).ToArray();

                    string offerMessage = $"{Config.OfferMessageRequest}{JsonSerializer.Serialize(offerIpAddresses)}";
                    byte[] offerBytes = Encoding.ASCII.GetBytes(offerMessage);

                    _udpServerDiscover.Send(offerBytes, offerBytes.Length, multicastEndPoint);

                    Console.WriteLine($"Sent {Config.OfferMessageRequest} to {multicastEndPoint}");
                }
            }
        }
        catch (ObjectDisposedException ode)
        {
            ode.PrintErrorMessage($"{nameof(UdpDiscoverHandler)} Server error");
        }
        catch (SocketException se)
        {
            se.PrintErrorMessage($"{nameof(UdpDiscoverHandler)} Socket error");
        }
        catch (Exception e)
        {
            e.PrintErrorMessage($"{nameof(UdpDiscoverHandler)} Server error");
        }
        finally
        {
            _udpServerDiscover?.Close();
            _udpDiscoverThread?.Join();
        }
    }

    private static void TcpConnectionHandler(TcpListener tcpListener)
    {
        try
        {
            tcpListener.Start();

            while (_shutdownServer == false)
            {
                TcpClient client = tcpListener.AcceptTcpClient();
                EndPoint? clientRemoteEndPoint = client.Client.RemoteEndPoint;

                Thread tcpClientThread = new(() => TcpClientHandler(client));
                tcpClientThread.Start();

                _tcpClientThreads.TryAdd(client, tcpClientThread);

                Console.WriteLine($"Client connected {clientRemoteEndPoint}");
                _clientStats.TryAdd(client.Client.RemoteEndPoint?.ToString(), ClientState.Connected);
            }
        }
        catch (ObjectDisposedException ode)
        {
            ode.PrintErrorMessage($"{nameof(TcpConnectionHandler)} Server error");
        }
        catch (SocketException se)
        {
            se.PrintErrorMessage($"{nameof(TcpConnectionHandler)} Socket error");
        }
        catch (Exception e)
        {
            e.PrintErrorMessage($"{nameof(TcpConnectionHandler)} Server error");
        }
        finally
        {
            tcpListener.Stop();
            _ = _tcpConnectionThreads.Remove(tcpListener, out Thread? tcpConnectionThread);
            tcpConnectionThread?.Join();
        }
    }

    private static void TcpClientHandler(TcpClient tcpClient)
    {
        try
        {
            byte[] buffer = new byte[1024];
            int bytesReceive = tcpClient.Client.Receive(buffer);
            string receiveMessage = Encoding.ASCII.GetString(buffer, 0, bytesReceive);

            while (receiveMessage.Clear().Equals(Config.TimeMessageRequest, StringComparison.CurrentCultureIgnoreCase) && _shutdownServer == false)
            {
                long milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                byte[] sendMessageBytes = Encoding.ASCII.GetBytes(milliseconds.ToString());

                tcpClient.Client.Send(sendMessageBytes);
                Console.WriteLine($"Sent time: {DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)} ({milliseconds}ms) to client: {tcpClient.Client.RemoteEndPoint}");

                bytesReceive = tcpClient.Client.Receive(buffer);
                receiveMessage = Encoding.ASCII.GetString(buffer, 0, bytesReceive);
            }
        }
        catch (ObjectDisposedException ode)
        {
            ode.PrintErrorMessage($"{nameof(TcpClientHandler)} Server error");
        }
        catch (SocketException se)
        {
            se.PrintErrorMessage($"{nameof(TcpClientHandler)} Socket error");
        }
        catch (Exception e)
        {
            e.PrintErrorMessage($"{nameof(TcpClientHandler)} Server error");
        }
        finally
        {
            string remoteEndPoint = tcpClient.Client.RemoteEndPoint?.ToString();
            if (_clientStats.ContainsKey(remoteEndPoint))
            {
                _clientStats[remoteEndPoint] = ClientState.Disconnected;
            }

            tcpClient.Close();
            _ = _tcpClientThreads.Remove(tcpClient, out Thread? tcpClientThread);
            tcpClientThread?.Join();
        }
    }
}
