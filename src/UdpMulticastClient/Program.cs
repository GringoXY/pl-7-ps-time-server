using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

class UdpMulticastClient
{
    const int Port = 12345;
    readonly IPAddress multicastIp = IPAddress.Parse("239.0.0.222");

    public void Start()
    {
        using (UdpClient client = new UdpClient(Port))
        {
            client.JoinMulticastGroup(multicastIp);
            IPEndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, Port);

            Console.WriteLine("Client started...");

            while (true)
            {
                byte[] buffer = client.Receive(ref remoteEndPoint);
                string message = Encoding.UTF8.GetString(buffer);
                Console.WriteLine($"Message received: {message}");
            }
        }
    }
}

class Program
{
    static void Main()
    {
        UdpMulticastClient client = new UdpMulticastClient();
        client.Start();
    }
}
