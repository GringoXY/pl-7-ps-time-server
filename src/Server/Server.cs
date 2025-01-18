using Shared;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

internal sealed class Server
{
    private static readonly List<TcpListener> _tpcListeners = [];
    private static readonly IPAddress[] _availableIpv4Addresses = Dns.GetHostAddresses(Dns.GetHostName())
        .Where(ip =>
            ip.AddressFamily is AddressFamily.InterNetwork
            && IPAddress.IsLoopback(ip) == false // https://learn.microsoft.com/en-us/dotnet/api/system.net.ipaddress.isloopback?view=net-8.0
        )
        .ToArray();
    private static readonly ConcurrentDictionary<string, ClientState> _clientStats = [];

    public void Start()
    {
        new Thread(UdpDiscoverHandler).Start();

        foreach(IPAddress ip in _availableIpv4Addresses)
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
            using UdpClient udpClient = new()
            {
                // Based on docs: https://learn.microsoft.com/en-us/dotnet/api/system.net.sockets.socket.exclusiveaddressuse?view=net-8.0
                // """
                // Gets or sets a value that indicates whether the Socket allows only one process to bind to a port.
                ExclusiveAddressUse = false
            };
            IPEndPoint localEndPoint = new(IPAddress.Any, Config.UdpDiscoverPort);
            udpClient.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);
            udpClient.Client.Bind(localEndPoint);
            udpClient.JoinMulticastGroup(Config.DefaultMulticastIpAddress);

            Console.WriteLine($"Started listening UDP multicast: {udpClient.Client.LocalEndPoint}");

            while (true)
            {
                byte[] receivedBytes = udpClient.Receive(ref localEndPoint);
                string receivedMessage = Encoding.ASCII.GetString(receivedBytes);

                if (receivedMessage.Clear().Equals(Config.DiscoverMessageRequest, StringComparison.CurrentCultureIgnoreCase))
                {
                    IEnumerable<string> offerTcpIpAddresses = _tpcListeners.Select((tcpListener, index) => $"{index + 1}. {Config.OfferMessageRequest}: {tcpListener.LocalEndpoint}");
                    string offerMessage = string.Join(Environment.NewLine, offerTcpIpAddresses);
                    byte[] offerBytes = Encoding.ASCII.GetBytes(offerMessage);

                    udpClient.Send(offerBytes, offerBytes.Length, localEndPoint);

                    Console.WriteLine($"Sent {Config.OfferMessageRequest} to {localEndPoint.Address}:{localEndPoint.Port}");
                }
            }
        }
        catch (ObjectDisposedException ode)
        {
            PrintErrorMessage("UDP Server error", ode);
        }
        catch (SocketException se)
        {
            PrintErrorMessage("UDP Socket error", se);
        }
        catch (Exception e)
        {
            PrintErrorMessage("UDP Server error", e);
        }
    }

    private static void TcpConnectionHandler(TcpListener tcpListener)
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

    private static void TcpClientHandler(TcpClient tcpClient)
    {
        try
        {
            byte[] buffer = new byte[256];
            int bytesReceive = tcpClient.Client.Receive(buffer);
            string receiveMessage = Encoding.ASCII.GetString(buffer, 0, bytesReceive);

            while (receiveMessage.Clear().Equals(Config.TimeMessageRequest, StringComparison.CurrentCultureIgnoreCase))
            {
                // https://code-maze.com/convert-datetime-to-iso-8601-string-csharp/
                // https://stackoverflow.com/questions/114983/given-a-datetime-object-how-do-i-get-an\-iso-8601-date-in-string-format
                string currentTimeIso8601 = DateTime.UtcNow.ToString("o");
                byte[] sendMessageBytes = Encoding.ASCII.GetBytes(currentTimeIso8601);

                tcpClient.Client.Send(sendMessageBytes);
                Console.WriteLine($"Sent time: {currentTimeIso8601} to client: {tcpClient.Client.RemoteEndPoint}");

                bytesReceive = tcpClient.Client.Receive(buffer);
                receiveMessage = Encoding.ASCII.GetString(buffer, 0, bytesReceive);
            }
        }
        catch (ObjectDisposedException ode)
        {
            PrintErrorMessage("TCP Server error", ode);
        }
        catch (SocketException se)
        {
            PrintErrorMessage("TCP Socket error", se);
        }
        catch (Exception e)
        {
            PrintErrorMessage("TCP Server error", e);
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
    }
}
