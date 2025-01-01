using Shared;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

internal sealed class Server()
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
        foreach(IPAddress ip in _availableIpv4Addresses)
        {
            int tcpPort = GetRandomTcpPort();
            TcpListener tcpListener = new(ip, tcpPort);
            _tpcListeners.Add(tcpListener);

            new Thread(() => TcpConnectionHandler(tcpListener)).Start();

            Console.WriteLine($"Listening on {ip}:{tcpPort}");
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
            PrintErrorMessage("Server error", ode);
        }
        catch (SocketException se)
        {
            PrintErrorMessage("Socket error", se);
        }
        catch (Exception e)
        {
            PrintErrorMessage("Server error", e);
        }
        finally
        {
            string remoteEndPoint = tcpClient.Client.RemoteEndPoint?.ToString();
            if (_clientStats.ContainsKey(remoteEndPoint))
            {
                _clientStats[remoteEndPoint] = ClientState.Disconnected;
            }
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
