using Shared;
using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

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
            using UdpClient udpServer = new()
            {
                ExclusiveAddressUse = false,
            };

            udpServer.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);

            udpServer.JoinMulticastGroup(Config.MulticastGroupIpAddress);

            IPEndPoint localEndPoint = new(IPAddress.Any, Config.UdpDiscoverPort);
            udpServer.Client.Bind(localEndPoint);

            IPEndPoint multicastEndPoint = new(Config.MulticastGroupIpAddress, Config.UdpDiscoverPort);

            while (true)
            {
                byte[] receivedBytes = udpServer.Receive(ref localEndPoint);
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

                    udpServer.Send(offerBytes, offerBytes.Length, multicastEndPoint);

                    Console.WriteLine($"Sent {Config.OfferMessageRequest} to {udpServer.Client.LocalEndPoint}");
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
        }
    }

    /// <summary>
    /// Helper method that generates random TCP port
    /// in range [<see cref="Config.MinTcpPort"/>; <see cref="Config.MaxTcpPort"/>].
    /// </summary>
    /// <returns>Port for TCP</returns>
    private static int GetRandomTcpPort()
        => new Random().Next(Config.MinTcpPort, Config.MaxTcpPort);

    private static IPAddress? GetLocalIpAddress()
    {
        if (NetworkInterface.GetIsNetworkAvailable() == false)
        {
            return null;
        }

        IPHostEntry iPHostEntry = Dns.GetHostEntry(Dns.GetHostName());

        return iPHostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
    }
}
