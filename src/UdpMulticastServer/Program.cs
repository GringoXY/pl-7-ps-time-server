using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

class UdpMulticastServer
{
    const int Port = 12345;
    readonly IPAddress multicastIp = IPAddress.Parse("239.0.0.222");
    readonly IPAddress localIp = IPAddress.Parse("192.168.0.113");

    public void Start()
    {
        using (UdpClient server = new UdpClient(AddressFamily.InterNetwork))
        {
            server.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            server.Client.Bind(new IPEndPoint(localIp, Port));
            server.JoinMulticastGroup(multicastIp, localIp);
            IPEndPoint remoteEndPoint = new IPEndPoint(multicastIp, Port);

            Console.WriteLine("Server started...");

            while (true)
            {
                byte[] buffer = Encoding.UTF8.GetBytes("Hello from server");
                server.Send(buffer, buffer.Length, remoteEndPoint);
                Console.WriteLine("Message sent to multicast group.");

                // Sending message every 5 seconds
                System.Threading.Thread.Sleep(5000);
            }
        }
    }
}

class Program
{
    static void Main()
    {
        UdpMulticastServer server = new UdpMulticastServer();
        server.Start();
    }
}
