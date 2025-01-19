using Shared;
using System.Net;
using System.Net.Sockets;
using System.Text;

internal sealed class Client
{
    private static OfferIPAddress _previousIpAddress;
    private static TcpClient _tcpClient;

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
                    ReceiveTimeout = Config.UdpDiscoverSleepRequestInMilliseconds,
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

                        OfferIPAddress chosenIpAddress = offerIpAddresses[listPoint - 1];
                        _previousIpAddress = chosenIpAddress;
                        _tcpClient = new(chosenIpAddress.IPAddress, chosenIpAddress.Port);

                        Console.Clear();
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
                if (_tcpClient.Connected)
                {
                    
                }
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
