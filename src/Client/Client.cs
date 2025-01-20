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
            UdpClient udpClient = new()
            {
                ExclusiveAddressUse = false,
                Client =
                {
                    ReceiveTimeout = Config.UdpDiscoverTimeoutRequestInMilliseconds,
                },
                // Prevents the case when datagram is missed on switch/router
                Ttl = 2
            };
            udpClient.JoinMulticastGroup(Config.MulticastGroupIpAddress, Config.UdpDiscoverPort);
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
                            PrintErrorMessage($"{nameof(UdpDiscoverHandler)} TCP Client error", ode);
                        }
                        catch (SocketException se)
                        {
                            PrintErrorMessage($"{nameof(UdpDiscoverHandler)} TCP Socket error", se);
                        }
                        catch (Exception e)
                        {
                            PrintErrorMessage($"{nameof(UdpDiscoverHandler)} TCP Client error", e);
                        }
                    }
                }

                // Preventing overusage of the CPU
                Thread.Sleep(Config.UdpDiscoverSleepRequestInMilliseconds);
            }
        }
        catch (ObjectDisposedException ode)
        {
            PrintErrorMessage($"{nameof(UdpDiscoverHandler)} Client error", ode);
        }
        catch (SocketException se)
        {
            PrintErrorMessage($"{nameof(UdpDiscoverHandler)} Socket error", se);
        }
        catch (Exception e)
        {
            PrintErrorMessage($"{nameof(UdpDiscoverHandler)} Client error", e);
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
                    // 1. Get current client time
                    long t1 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    // 2. Send "TIME" TCP request to get server's time
                    byte[] serverTimeRequest = Encoding.ASCII.GetBytes(Config.TimeMessageRequest);
                    _tcpClient.Client.Send(serverTimeRequest);

                    byte[] buffer = new byte[256];
                    int receiveServerResponseBytes = _tcpClient.Client.Receive(buffer);
                    string serverUnixTimeInMilliseconds = Encoding.ASCII.GetString(buffer, 0, receiveServerResponseBytes);

                    if (long.TryParse(serverUnixTimeInMilliseconds, out long tServ))
                    {
                        long t2 = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        long tCli = t2;

                        long delta = tServ + ((t2 - t1) / 2) - tCli;
                        long calculatedCurrentServerTime = tCli + delta;
                        DateTimeOffset currentIso8601 = DateTimeOffset.FromUnixTimeMilliseconds(calculatedCurrentServerTime);
                        Console.WriteLine($"{currentIso8601:O} | delta={delta}ms");
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("");
                        Console.ForegroundColor = ConsoleColor.Gray;
                    }
                }

                Thread.Sleep(_timeTcpRequestFrequencyInMilliseconds ?? 1_000);
            }
        }
        catch (ObjectDisposedException ode)
        {
            PrintErrorMessage($"{nameof(TcpTimeHandler)} Client error", ode);
        }
        catch (SocketException se)
        {
            PrintErrorMessage($"{nameof(TcpTimeHandler)} Socket error", se);
        }
        catch (Exception e)
        {
            PrintErrorMessage($"{nameof(TcpTimeHandler)} Client error", e);
        }
        finally
        {
            _tcpClient?.Close();
        }
    }

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
