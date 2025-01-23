namespace Shared;

public record OfferIPAddress(string IPAddress, int Port)
{
    public override string ToString()
        => $"{IPAddress}:{Port}";
}
