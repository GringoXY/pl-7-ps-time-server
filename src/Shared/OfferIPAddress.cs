namespace Shared;

public class OfferIPAddress(string iPAddress, int port)
{
    public string IPAddress => iPAddress;
    public int Port => port;

    public override string ToString()
        => $"{IPAddress}:{Port}";
}
