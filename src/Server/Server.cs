using Shared;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

internal sealed class Server
{
    private static readonly List<TcpListener> _tpcListeners = [];
    // https://stackoverflow.com/questions/9855230/how-do-i-get-the-network-interface-and-its-right-ipv4-address
    private static readonly IEnumerable<IPAddress> _availableIpAddresses
        = NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus is OperationalStatus.Up)
            .Select(ni => ni.GetIPProperties().UnicastAddresses)
            .SelectMany(u => u)
            .Where(u =>
                u.Address.AddressFamily == AddressFamily.InterNetwork
                && IPAddress.IsLoopback(u.Address) == false
            ).Select(u => u.Address);

    private static readonly ConcurrentDictionary<string, ClientState> _clientStats = [];

    public void Start()
    {
        new Thread(UdpDiscoverHandler).Start();

        foreach (IPAddress ip in _availableIpAddresses)
        {
            int tcpPort = GetRandomTcpPort();
            TcpListener tcpListener = new(ip, tcpPort);
            _tpcListeners.Add(tcpListener);

            new Thread(() => TcpConnectionHandler(tcpListener)).Start();

            Console.WriteLine($"Listening on {ip}:{tcpPort}");
        }
    }

    private static void UdpDiscoverHandler()
    {
        try
        {
            UdpClient udpClient = new()
            {
                // Prevents the case when datagram is missed on switch/router
                Ttl = 2
            };
            udpClient.JoinMulticastGroup(Config.MulticastGroupIpAddress);

            IPEndPoint localEndPoint = new(IPAddress.Any, Config.UdpDiscoverPort);
            udpClient.Client.Bind(localEndPoint);

            Console.WriteLine($"Started listening UDP multicast {Config.DiscoverMessageRequest}: {udpClient.Client.LocalEndPoint}");

            while (true)
            {
                byte[] receivedBytes = udpClient.Receive(ref localEndPoint);
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

                    udpClient.Send(offerBytes, offerBytes.Length, localEndPoint);

                    Console.WriteLine($"Sent {Config.OfferMessageRequest} to {udpClient.Client.LocalEndPoint}");
                }
            }
        }
        catch (ObjectDisposedException ode)
        {
            PrintErrorMessage($"{nameof(UdpDiscoverHandler)} Server error", ode);
        }
        catch (SocketException se)
        {
            PrintErrorMessage($"{nameof(UdpDiscoverHandler)} Socket error", se);
        }
        catch (Exception e)
        {
            PrintErrorMessage($"{nameof(UdpDiscoverHandler)} Server error", e);
        }
    }

    private static void TcpConnectionHandler(TcpListener tcpListener)
    {
        try
        {
            tcpListener.Start();

            while (true)
            {
                TcpClient client = tcpListener.AcceptTcpClient();
                EndPoint? clientRemoteEndPoint = client.Client.RemoteEndPoint;
                Console.WriteLine($"Client connected {clientRemoteEndPoint}");
                _clientStats.TryAdd(client.Client.RemoteEndPoint?.ToString(), ClientState.Connected);
                new Thread(() => TcpClientHandler(client)).Start();
            }
        }
        catch (ObjectDisposedException ode)
        {
            PrintErrorMessage($"{nameof(TcpConnectionHandler)} Server error", ode);
        }
        catch (SocketException se)
        {
            PrintErrorMessage($"{nameof(TcpConnectionHandler)} Socket error", se);
        }
        catch (Exception e)
        {
            PrintErrorMessage($"{nameof(TcpConnectionHandler)} Server error", e);
        }
    }

    private static void TcpClientHandler(TcpClient tcpClient)
    {
        try
        {
            byte[] buffer = new byte[256];
            int bytesReceive = tcpClient.Client.Receive(buffer);
            string receiveMessage = Encoding.ASCII.GetString(buffer, 0, bytesReceive);

            while (receiveMessage.Clear().Equals(Config.TimeMessageRequest, StringComparison.CurrentCultureIgnoreCase))
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
            PrintErrorMessage($"{nameof(TcpClientHandler)} Server error", ode);
        }
        catch (SocketException se)
        {
            PrintErrorMessage($"{nameof(TcpClientHandler)} Socket error", se);
        }
        catch (Exception e)
        {
            PrintErrorMessage($"{nameof(TcpClientHandler)} Server error", e);
        }
        finally
        {
            string remoteEndPoint = tcpClient.Client.RemoteEndPoint?.ToString();
            if (_clientStats.ContainsKey(remoteEndPoint))
            {
                _clientStats[remoteEndPoint] = ClientState.Disconnected;
            }

            tcpClient.Close();
        }
    }

    /// <summary>
    /// Helper method that generates random TCP port
    /// in range [<see cref="Config.MinTcpPort"/>; <see cref="Config.MaxTcpPort"/>].
    /// </summary>
    /// <returns>Port for TCP</returns>
    private static int GetRandomTcpPort()
        => new Random().Next(Config.MinTcpPort, Config.MaxTcpPort);

    private static void PrintErrorMessage(string baseMessage, Exception exception)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"{baseMessage}: {exception.Message}");
        if (exception.InnerException is not null)
        {
            Console.WriteLine($"Wyjątek wewnętrzny: {exception.InnerException?.Message}");
        }
        Console.ForegroundColor = ConsoleColor.Gray;
    }
}
