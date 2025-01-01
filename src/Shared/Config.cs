namespace Shared;

public class Config
{
    public const int MinTcpPort = 1024;
    public const int MaxTcpPort = 65535;

    /// <summary>
    /// Based on docs
    /// </summary>
    public const int UdpDiscoverPort = 7;

    /// <summary>
    /// Client requests list of IP addresses (<see cref="OfferMessageRequest"/> or more)
    /// </summary>
    public const string DiscoverMessageRequest = "DISCOVER";

    /// <summary>
    /// Server offers list of available IP addresses (v4 only)
    /// </summary>
    public const string OfferMessageRequest = "OFFER";

    /// <summary>
    /// Client requests server's time
    /// </summary>
    public const string TimeMessageRequest = "TIME";
}
