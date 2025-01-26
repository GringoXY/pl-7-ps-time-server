using Shared;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Client;

internal sealed class Client : IDisposable
{
    private static readonly CancellationTokenSource _cancellationTokenSource = new();

    private static Task _selectServerIPAddressTask;

    private static UdpClient _udpOfferClient;
    private static Thread _udpOfferThread;

    private static UdpClient _udpDiscoverClient;
    private static Thread _udpDiscoverThread;

    private static TcpClient _tcpTimeClient;
    private static Thread _tcpTimeThread;

    private static int? _timeTcpRequestFrequencyInMilliseconds = null;
    private static readonly ConcurrentDictionary<string, int> _availableServerIPAddresses = [];

    public void Start()
    {
        Cache.LoadCachedIPAddress();
        _selectServerIPAddressTask = new(SelectServerIPAddressHandler, _cancellationTokenSource.Token);
        _selectServerIPAddressTask.Start();

        _udpOfferThread = new(UdpOfferHandler);
        _udpOfferThread.Start();

        _udpDiscoverThread = new(UdpDiscoverHandler);
        _udpDiscoverThread.Start();

        _tcpTimeThread = new(TcpTimeHandler);
        _tcpTimeThread.Start();

        Console.CancelKeyPress += (object? sender, ConsoleCancelEventArgs e) =>
        {
            Dispose();
            e.Cancel = true;
        };
    }

    private static void SelectServerIPAddressHandler()
    {
        while (_cancellationTokenSource.IsCancellationRequested == false)
        {
            // Preventing CPU overusage
            _cancellationTokenSource.Token.WaitHandle.WaitOne(Config.SelectServerSleepRequestInMilliseconds);

            if (_tcpTimeClient?.Connected is null or false)
            {
                int defaultIndex = -1, listPoint = -1;

                Console.WriteLine("Available servers' IP addresses:");
                for (int i = 0; i < _availableServerIPAddresses.Count; i += 1)
                {
                    KeyValuePair<string, int> ip = _availableServerIPAddresses.ElementAt(i);
                    if (Cache.PreviousIPAddress == ip.Key)
                    {
                        defaultIndex = i + 1; // Store index of default IP
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"{i + 1}. {ip.Value} (default) - press \"ENTER\" to proceed");
                        Console.ForegroundColor = ConsoleColor.Gray;
                    }
                    else
                    {
                        Console.WriteLine($"{i + 1}. {ip.Value}");
                    }
                }

                Console.Write($"Choose IP address (1-{_availableServerIPAddresses.Count})");
                if (defaultIndex != -1) Console.Write(" or press \"ENTER\" for default: ");
                else Console.Write(": ");

                string input = Console.ReadLine();
                if (defaultIndex != -1 && string.IsNullOrEmpty(input))
                {
                    listPoint = defaultIndex;
                }
                else
                {
                    while (
                        !int.TryParse(input, out listPoint)
                        || listPoint < 1
                        || listPoint > _availableServerIPAddresses.Count
                    )
                    {
                        Console.WriteLine("Invalid choice... Please try again:");
                        input = Console.ReadLine();

                        Console.Clear();
                    }
                }

                (string ipAddress, int port) = _availableServerIPAddresses.ElementAt(listPoint - 1);

                try
                {
                    // May throw exception on connection attempt
                    Console.WriteLine($"Connecting to {ipAddress}:{port}");
                    _tcpTimeClient = new(ipAddress, port)
                    {
                        SendTimeout = Config.TcpTimeRequestTimeoutInMilliseconds
                    };

                    Cache.SaveIPAddress(ipAddress);

                    Console.Write($"Connected! Provide time request frequency in ms (10-1000): ");

                    int requestFrequency = 0;
                    while (
                        int.TryParse(Console.ReadLine(), out requestFrequency) == false
                        || requestFrequency < Config.MinTcpTimeRequestFrequencyInMilliseconds
                        || requestFrequency > Config.MaxTcpTimeRequestFrequencyInMilliseconds
                    )
                    {
                        Console.Write($"Invalid timeout... Provide time request frequency in ms (10-1000): ");
                    }

                    _timeTcpRequestFrequencyInMilliseconds = requestFrequency;

                    Console.Clear();
                }
                catch (ObjectDisposedException ode)
                {
                    ode.PrintErrorMessage($"{nameof(SelectServerIPAddressHandler)} UDP Client error");
                }
                catch (SocketException se)
                {
                    _availableServerIPAddresses.TryRemove(ipAddress, out _);
                    se.PrintErrorMessage($"{nameof(SelectServerIPAddressHandler)} UDP Socket error");
                }
                catch (Exception e)
                {
                    e.PrintErrorMessage($"{nameof(SelectServerIPAddressHandler)} UDP Client error");
                }
            }
        }
    }

    private static void UdpOfferHandler()
    {
        try
        {
            _udpOfferClient = new()
            {
                ExclusiveAddressUse = false,
                MulticastLoopback = false,
            };

            _udpOfferClient.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);

            IPAddress localIpAddress = Utils.GetLocalIPAddress();
            IPEndPoint localEndPoint = new(localIpAddress, Config.UdpDiscoverPort);
            _udpOfferClient.JoinMulticastGroup(Config.MulticastGroupIpAddress, localIpAddress);

            _udpOfferClient.Client.Bind(localEndPoint);
            IPEndPoint multicastEndPoint = new(Config.MulticastGroupIpAddress, Config.UdpDiscoverPort);

            while (_cancellationTokenSource.IsCancellationRequested == false)
            {
                if (_tcpTimeClient?.Connected is null or false)
                {
                    IPEndPoint remoteEndPoint = new(localIpAddress, Config.UdpDiscoverPort);
                    byte[] offerReceiveBytes = new byte[Config.ReceiveBufferSize];
                    try
                    {
                        offerReceiveBytes = _udpOfferClient.Receive(ref remoteEndPoint);
                    }
                    catch (SocketException se)
                    {
                        se.PrintErrorMessage($"{nameof(UdpOfferHandler)}");

                        continue;
                    }

                    string offerResponseMessage = Encoding.ASCII.GetString(offerReceiveBytes);
                    if (offerResponseMessage.StartsWith(Config.OfferMessageRequest))
                    {
                        OfferIPAddress offerIpAddress = offerResponseMessage.ParseOfferMessage();
                        // Removing old entry
                        _availableServerIPAddresses.TryRemove(offerIpAddress.IPAddress, out _);

                        // Adding new entry
                        _availableServerIPAddresses.TryAdd(offerIpAddress.IPAddress, offerIpAddress.Port);
                    }
                }

                // Preventing overusage of the CPU
                _cancellationTokenSource.Token.WaitHandle.WaitOne(Config.UdpOfferSleepRequestInMilliseconds);
            }
        }
        catch (ObjectDisposedException ode)
        {
            ode.PrintErrorMessage($"{nameof(UdpOfferHandler)} Client error");
        }
        catch (SocketException se)
        {
            se.PrintErrorMessage($"{nameof(UdpOfferHandler)} Socket error");
        }
        catch (Exception e)
        {
            e.PrintErrorMessage($"{nameof(UdpOfferHandler)} Client error");
        }
    }

    private static void UdpDiscoverHandler()
    {
        try
        {
            _udpDiscoverClient = new()
            {
                ExclusiveAddressUse = false,
                MulticastLoopback = false,
            };

            _udpDiscoverClient.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);

            IPAddress localIpAddress = Utils.GetLocalIPAddress();
            IPEndPoint localEndPoint = new(localIpAddress, Config.UdpDiscoverPort);
            _udpDiscoverClient.JoinMulticastGroup(Config.MulticastGroupIpAddress, localIpAddress);

            _udpDiscoverClient.Client.Bind(localEndPoint);
            IPEndPoint multicastEndPoint = new(Config.MulticastGroupIpAddress, Config.UdpDiscoverPort);

            byte[] discoverMessage = Encoding.ASCII.GetBytes(Config.DiscoverMessageRequest);

            while (_cancellationTokenSource.IsCancellationRequested == false)
            {
                if (_tcpTimeClient?.Connected is null or false)
                {
                    IPEndPoint remoteEndPoint = new(localIpAddress, Config.UdpDiscoverPort);
                    Console.WriteLine($"Sending {Config.DiscoverMessageRequest} request to {multicastEndPoint.Address}:{multicastEndPoint.Port}");
                    _udpDiscoverClient.Send(discoverMessage, discoverMessage.Length, multicastEndPoint);
                }

                // Preventing overusage of the CPU
                _cancellationTokenSource.Token.WaitHandle.WaitOne(Config.UdpDiscoverSleepRequestInMilliseconds);
            }
        }
        catch (ObjectDisposedException ode)
        {
            ode.PrintErrorMessage($"{nameof(UdpDiscoverHandler)} Client error");
        }
        catch (SocketException se)
        {
            se.PrintErrorMessage($"{nameof(UdpDiscoverHandler)} Socket error");
        }
        catch (Exception e)
        {
            e.PrintErrorMessage($"{nameof(UdpDiscoverHandler)} Client error");
        }
    }

    private static void TcpTimeHandler()
    {
        while (_cancellationTokenSource.IsCancellationRequested == false)
        {
            if (_tcpTimeClient?.Connected is true && _timeTcpRequestFrequencyInMilliseconds is not null)
            {
                try
                {
                    // 1. Get current client time [ms]
                    long currentClientTime1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    // 2. Send "TIME" TCP request to get server's time [ms]
                    byte[] serverTimeRequest = Encoding.ASCII.GetBytes(Config.TimeMessageRequest);
                    _tcpTimeClient.Client.Send(serverTimeRequest);

                    byte[] buffer = new byte[Config.ReceiveBufferSize];
                    int receiveServerResponseBytes = _tcpTimeClient.Client.Receive(buffer);
                    string serverUnixTimeInMilliseconds = Encoding.ASCII.GetString(buffer, 0, receiveServerResponseBytes);

                    if (long.TryParse(serverUnixTimeInMilliseconds, out long serverTime))
                    {
                        // 3. Save again current client time [ms]
                        long currentClientTime2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        long clientTime = currentClientTime2;

                        // 4. Calculating difference between server and client time
                        long delta = serverTime + ((currentClientTime2 - currentClientTime1) / 2) - clientTime;
                        long calculatedCurrentServerTime = clientTime + delta;
                        DateTimeOffset currentIso8601 = DateTimeOffset.FromUnixTimeMilliseconds(calculatedCurrentServerTime);
                        Console.WriteLine($"{currentIso8601:O} | delta={delta}ms");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("Invalid time from server. Should be in milliseconds [ms]! Fix the server...");
                        Console.ForegroundColor = ConsoleColor.Gray;
                    }
                }
                catch (ObjectDisposedException ode)
                {
                    ode.PrintErrorMessage($"{nameof(TcpTimeHandler)} Client error");
                }
                catch (SocketException se)
                {
                    _tcpTimeClient?.Close();
                    _timeTcpRequestFrequencyInMilliseconds = null;
                    se.PrintErrorMessage($"{nameof(TcpTimeHandler)} Socket error");
                }
                catch (Exception e)
                {
                    e.PrintErrorMessage($"{nameof(TcpTimeHandler)} Client error");
                }
            }

            _cancellationTokenSource.Token.WaitHandle.WaitOne(_timeTcpRequestFrequencyInMilliseconds ?? 1_000);
        }
    }
        
    public void Dispose()
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("Shutting down the client. Please wait...");
        Console.ForegroundColor = ConsoleColor.Gray;

        _cancellationTokenSource.Cancel();

        _availableServerIPAddresses.Clear();

        _udpOfferClient?.DropMulticastGroup(Config.MulticastGroupIpAddress);
        _udpOfferClient?.Close();
        _udpDiscoverClient?.DropMulticastGroup(Config.MulticastGroupIpAddress);
        _udpDiscoverClient?.Close();
        _tcpTimeClient?.Close();

        _udpOfferThread.Join();
        _udpDiscoverThread.Join();
        _tcpTimeThread.Join();
    }
}
