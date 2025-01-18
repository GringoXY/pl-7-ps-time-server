using Shared;
using System.Net;
using System.Net.Sockets;
using System.Text;

internal sealed class Client
{
    private static bool _isConnected = false;
    private static IPAddress _previousIpAddress;

    public void Start()
    {
        //UdpClient udpClient = new()
        //{
        //    ExclusiveAddressUse = false
        //};
        //udpClient.JoinMulticastGroup(Config.MulticastGroupIpAddress, Config.UdpDiscoverPort);
        //IPEndPoint endPoint = new(Config.MulticastGroupIpAddress, Config.UdpDiscoverPort);
        //string message = "DISCOVER";
        //byte[] buffer = Encoding.UTF8.GetBytes(message);
        //udpClient.Send(buffer, buffer.Length, endPoint);
        //Console.WriteLine("Wysłano DISCOVER");
        new Thread(UdpDiscoverHandler).Start();
    }

    private static void UdpDiscoverHandler()
    {
        UdpClient udpClient = new()
        {
            ExclusiveAddressUse = false,
            Client =
            {
                ReceiveTimeout = Config.UdpDiscoverSleepRequestInMilliseconds,
            }
        };
        udpClient.JoinMulticastGroup(Config.MulticastGroupIpAddress, Config.UdpDiscoverPort);
        IPEndPoint multicastEndPoint = new(Config.MulticastGroupIpAddress, Config.UdpDiscoverPort);

        Console.WriteLine($"Started UDP multicast DISCOVER: {multicastEndPoint.Address}");

        byte[] discoverMessage = Encoding.ASCII.GetBytes(Config.DiscoverMessageRequest);

        while (_isConnected == false)
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
                    Console.WriteLine($"{i + 1}. {offerIpAddresses[i].IPAddress}");
                }

                Console.Write("Choose IP address: ");
                int listPoint = -1;
                while (
                    int.TryParse(Console.ReadLine(), out listPoint) == false
                    || listPoint < 1
                    || listPoint > offerIpAddresses.Length
                )
                {
                    Console.Write("Choose IP address: ");
                }

                OfferIPAddress chosenIpAddress = offerIpAddresses[listPoint - 1];
                _previousIpAddress = IPAddress.Parse(chosenIpAddress.IPAddress);
                _isConnected = true;
                Console.Clear();
            }

            Thread.Sleep(Config.UdpDiscoverSleepRequestInMilliseconds);
        }
    }
}
