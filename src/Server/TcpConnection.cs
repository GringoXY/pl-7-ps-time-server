using System.Net.Sockets;
using System.Net;
using System.Text;
using Shared;
using System.Collections.Concurrent;

namespace Server;

internal sealed class TcpConnection(IPAddress LocalIPAddress, int Port, CancellationToken CancellationToken) : IDisposable
{
    private TcpListener _listener;
    private readonly ConcurrentDictionary<TcpClient, Thread> _tcpClients = [];

    public void Start()
    {
        Handle();
    }

    private void Handle()
    {
        try
        {
            _listener = new(LocalIPAddress, Port);
            _listener.Start();

            while (CancellationToken.IsCancellationRequested == false)
            {
                TcpClient tcpClient = _listener.AcceptTcpClient();
                EndPoint? clientRemoteEndPoint = tcpClient.Client.RemoteEndPoint;

                Thread tcpClientThread = new(() => TcpClientHandler(tcpClient));
                tcpClientThread.Start();

                _tcpClients.TryAdd(tcpClient, tcpClientThread);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"Client: {clientRemoteEndPoint} connected");
                Console.ForegroundColor = ConsoleColor.Gray;
                // _clientStats.TryAdd(client.Client.RemoteEndPoint?.ToString(), ClientState.Connected);
            }
        }
        catch (ObjectDisposedException ode)
        {
            ode.PrintErrorMessage($"{nameof(TcpConnection)} Server error");
        }
        catch (SocketException se)
        {
            se.PrintErrorMessage($"{nameof(TcpConnection)} Socket error");
        }
        catch (Exception e)
        {
            e.PrintErrorMessage($"{nameof(TcpConnection)} Server error");
        }
        finally
        {
            Shutdown();
        }
    }

    private void TcpClientHandler(TcpClient tcpClient)
    {
        try
        {
            byte[] buffer = new byte[1024];
            int bytesReceive = tcpClient.Client.Receive(buffer);
            string receiveMessage = Encoding.ASCII.GetString(buffer, 0, bytesReceive);

            while (receiveMessage.Clear().Equals(Config.TimeMessageRequest, StringComparison.CurrentCultureIgnoreCase) && CancellationToken.IsCancellationRequested == false)
            {
                long milliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                byte[] sendMessageBytes = Encoding.ASCII.GetBytes(milliseconds.ToString());

                tcpClient.Client.Send(sendMessageBytes);
                Console.WriteLine($"Sent time: {DateTimeOffset.FromUnixTimeMilliseconds(milliseconds)} ({milliseconds}ms) to client: {tcpClient.Client.RemoteEndPoint}");

                bytesReceive = tcpClient.Client.Receive(buffer);
                receiveMessage = Encoding.ASCII.GetString(buffer, 0, bytesReceive);
            }
        }
        catch (ObjectDisposedException ode)
        {
            ode.PrintErrorMessage($"{nameof(TcpClientHandler)} Server error");
        }
        catch (SocketException se)
        {
            se.PrintErrorMessage($"{nameof(TcpClientHandler)} Socket error");
        }
        catch (Exception e)
        {
            e.PrintErrorMessage($"{nameof(TcpClientHandler)} Server error");
        }
        finally
        {
            EndPoint? closedClientAddress = tcpClient?.Client?.RemoteEndPoint;
            tcpClient?.Client?.Close();
            tcpClient?.Close();
            _tcpClients.Remove(tcpClient, out Thread? thread);

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Client: {closedClientAddress} closed");
            Console.ForegroundColor = ConsoleColor.Gray;
            thread?.Join();
        }
    }

    public void Shutdown()
    {
        Dispose();
    }

    public void Dispose()
    {
        foreach ((TcpClient tcpClient, Thread thread) in _tcpClients)
        {
            tcpClient.Close();
            thread.Join();
        }

        _tcpClients.Clear();

        _listener?.Stop();
    }
}
