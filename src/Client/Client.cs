using Shared;
using System.Net;
using System.Net.Sockets;
using System.Text;

internal sealed class Client
{
    private static OfferIPAddress _previousIpAddress;
    private static TcpClient _tcpClient;
    private static int? _timeTcpRequestFrequencyInMilliseconds = null;

    public void Start()
    {
        new Thread(UdpDiscoverHandler).Start();
        new Thread(TcpTimeHandler).Start();
    }

    private static void UdpDiscoverHandler()
    {
        try
        {
            using UdpClient udpClient = new()
            {
                Client =
                {
                    ReceiveTimeout = Config.UdpDiscoverTimeoutRequestInMilliseconds,
                },
                ExclusiveAddressUse = false,
            };

            udpClient.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);

            udpClient.JoinMulticastGroup(Config.MulticastGroupIpAddress);

            IPEndPoint localEndPoint = new(IPAddress.Any, Config.UdpDiscoverPort);
            udpClient.Client.Bind(localEndPoint);

            IPEndPoint multicastEndPoint = new(Config.MulticastGroupIpAddress, Config.UdpDiscoverPort);

            Console.WriteLine($"Started UDP multicast {Config.DiscoverMessageRequest}: {multicastEndPoint.Address}");

            byte[] discoverMessage = Encoding.ASCII.GetBytes(Config.DiscoverMessageRequest);

            while (true)
            {
                if (_tcpClient?.Connected is null or false)
                {
                    udpClient.Send(discoverMessage, discoverMessage.Length, multicastEndPoint);

                    byte[] offerReceiveBytes = udpClient.Receive(ref multicastEndPoint);
                    string offerResponseMessage = Encoding.ASCII.GetString(offerReceiveBytes);
                    if (offerResponseMessage.StartsWith(Config.OfferMessageRequest))
                    {
                        OfferIPAddress[] offerIpAddresses = offerResponseMessage.ParseOfferMessage();
                        Console.WriteLine("Available IP addresses:");
                        for (int i = 0; i < offerIpAddresses.Length; i += 1)
                        {
                            Console.WriteLine($"{i + 1}. {offerIpAddresses[i]}");
                        }

                        Console.Write($"Choose IP address (1-{offerIpAddresses.Length}): ");
                        int listPoint = -1;
                        while (
                            int.TryParse(Console.ReadLine(), out listPoint) == false
                            || listPoint < 1
                            || listPoint > offerIpAddresses.Length
                        )
                        {
                            Console.Write($"Invalid choice... Choose IP address (1-{offerIpAddresses.Length}): ");
                        }

                        Console.Clear();
                        OfferIPAddress chosenIpAddress = offerIpAddresses[listPoint - 1];

                        try
                        {
                            // May throw exception on connection attempt
                            _tcpClient = new(chosenIpAddress.IPAddress, chosenIpAddress.Port)
                            {
                                SendTimeout = Config.TcpTimeRequestTimeoutInMilliseconds
                            };
                            _previousIpAddress = chosenIpAddress;

                            Console.Write($"Provide time request frequency in ms (10-1000): ");

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
                        }
                        catch (ObjectDisposedException ode)
                        {
                            ode.PrintErrorMessage($"{nameof(UdpDiscoverHandler)} TCP Client error");
                        }
                        catch (SocketException se)
                        {
                            se.PrintErrorMessage($"{nameof(UdpDiscoverHandler)} TCP Socket error");
                        }
                        catch (Exception e)
                        {
                            e.PrintErrorMessage($"{nameof(UdpDiscoverHandler)} TCP Client error");
                        }
                    }
                }

                // Preventing overusage of the CPU
                Thread.Sleep(Config.UdpDiscoverSleepRequestInMilliseconds);
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
        try
        {
            while (true)
            {
                if (_tcpClient?.Connected is true && _timeTcpRequestFrequencyInMilliseconds is not null)
                {
                    // 1. Get current client time [ms]
                    long currentClientTime1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    // 2. Send "TIME" TCP request to get server's time [ms]
                    byte[] serverTimeRequest = Encoding.ASCII.GetBytes(Config.TimeMessageRequest);
                    _tcpClient.Client.Send(serverTimeRequest);

                    byte[] buffer = new byte[256];
                    int receiveServerResponseBytes = _tcpClient.Client.Receive(buffer);
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
                        Console.WriteLine("Invalid time from server. Should be in milliseconds! Fix the server...");
                        Console.ForegroundColor = ConsoleColor.Gray;
                    }
                }

                Thread.Sleep(_timeTcpRequestFrequencyInMilliseconds ?? 1_000);
            }
        }
        catch (ObjectDisposedException ode)
        {
            ode.PrintErrorMessage($"{nameof(TcpTimeHandler)} Client error");
        }
        catch (SocketException se)
        {
            se.PrintErrorMessage($"{nameof(TcpTimeHandler)} Socket error");
        }
        catch (Exception e)
        {
            e.PrintErrorMessage($"{nameof(TcpTimeHandler)} Client error");
        }
        finally
        {
            _tcpClient?.Close();
        }
    }
}
