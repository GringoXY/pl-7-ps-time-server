using Shared;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text;

namespace Server;

internal sealed class UdpDiscover(IPAddress LocalIPAddress, int Port, CancellationToken CancellationToken) : IDisposable
{
    private UdpClient _client;

    public void Start()
    {
        Handle();
    }

    private void Handle()
    {
        try
        {
            _client = new()
            {
                ExclusiveAddressUse = false,
            };

            _client.Client.SetSocketOption(
                SocketOptionLevel.Socket,
                SocketOptionName.ReuseAddress,
                true);

            IPEndPoint localEndPoint = new(LocalIPAddress, Config.UdpDiscoverPort);
            _client.Client.Bind(localEndPoint);

            _client.JoinMulticastGroup(Config.MulticastGroupIpAddress, LocalIPAddress);

            while (CancellationToken.IsCancellationRequested == false)
            {
                IPEndPoint multicastEndPoint = new(Config.MulticastGroupIpAddress, Config.UdpDiscoverPort);
                Console.WriteLine($"Waiting for {Config.DiscoverMessageRequest} receive via {multicastEndPoint.Address}:{multicastEndPoint.Port} on network interface with IP {localEndPoint}");
                IPEndPoint remoteEndPoint = new(LocalIPAddress, Config.UdpDiscoverPort);
                byte[] receivedBytes = _client.Receive(ref remoteEndPoint);
                string receivedMessage = Encoding.ASCII.GetString(receivedBytes);

                if (receivedMessage.Clear().Equals(Config.DiscoverMessageRequest, StringComparison.CurrentCultureIgnoreCase))
                {
                    OfferIPAddress offerIpAddress = new(LocalIPAddress.ToString(), Port);

                    string offerMessage = $"{Config.OfferMessageRequest}{JsonSerializer.Serialize(offerIpAddress)}";
                    byte[] offerBytes = Encoding.ASCII.GetBytes(offerMessage);

                    _client.Send(offerBytes, offerBytes.Length, multicastEndPoint);

                    Console.WriteLine($"Sent {Config.OfferMessageRequest}: {offerIpAddress} to {multicastEndPoint}");
                }
            }
        }
        catch (ObjectDisposedException ode)
        {
            ode.PrintErrorMessage($"{nameof(UdpDiscover)} Server error");
        }
        catch (SocketException se)
        {
            se.PrintErrorMessage($"{nameof(UdpDiscover)} Socket error");
        }
        catch (Exception e)
        {
            e.PrintErrorMessage($"{nameof(UdpDiscover)} Server error");
        }
        finally
        {
            Shutdown();
        }
    }

    public void Shutdown()
    {
        Dispose();
    }

    public void Dispose()
    {
        _client?.Close();
    }
}
