using System.Net;
using System.Net.Sockets;
using System.Text;

public class UdpMulticastSocket
{
    public Socket socket;
    private IPEndPoint _localEndPoint;
    private IPAddress _multicastAddress;
    private IPAddress _localAddress;

    public UdpMulticastSocket(string multicastIp, int port, string localIp, int ttl, int timeout)
    {
        _multicastAddress = IPAddress.Parse(multicastIp);
        _localAddress = IPAddress.Parse(localIp);
        _localEndPoint = new IPEndPoint(IPAddress.Any, port);

        // Create the socket
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

        // Set socket options
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        //socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, ttl);

        // Bind the socket to the local endpoint
        socket.Bind(_localEndPoint);

        // Join the multicast group
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(_multicastAddress));

        // Set the multicast interface
        //socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, _localAddress.GetAddressBytes());

        // Set the socket timeout
        socket.ReceiveTimeout = timeout;

        Console.WriteLine("Multicast socket created");
    }

    // Additional methods for sending/receiving data can be added here
}

// Usage example
public class Program
{
    public static void Main()
    {
        string multicastIp = "239.0.0.222";
        int port = 12345;
        string localIp = "192.168.0.113";
        int ttl = 1;
        int timeout = 10_000; // in milliseconds


        IPEndPoint multicastEndPoint = new(IPAddress.Parse(multicastIp), port);

        UdpMulticastSocket multicastSocket = new(multicastIp, port, localIp, ttl, timeout);
        byte[] buffer = new byte[1024];
        int bytes = multicastSocket.socket.Receive(buffer);
        string response = Encoding.ASCII.GetString(buffer, 0, bytes);
        Console.WriteLine("Odebrane {0}", response);
    }
}