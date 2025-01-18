using System.Net;

namespace Shared;

public static class Config
{
    public const int MinTcpPort = 1024;
    public const int MaxTcpPort = 65535;

    /// <summary>
    /// Based on docs
    /// </summary>
    public const int UdpDiscoverPort = 7;

    /// <summary>
    /// Command/request name for client requesting list of IP addresses (<see cref="OfferMessageRequest"/> for more)
    /// </summary>
    public const string DiscoverMessageRequest = "DISCOVER";

    /// <summary>
    /// Command/request name for the server offering list of available IP addresses (v4 only)
    /// </summary>
    public const string OfferMessageRequest = "OFFER";

    public const string OfferMessageElementsSeperator = ";";
    public const string OfferMessageRowsSeperator = "|";

    /// <summary>
    /// Command/request name for the client when he requests server's time
    /// </summary>
    public const string TimeMessageRequest = "TIME";

    public static readonly IPAddress MulticastGroupIpAddress = IPAddress.Parse("239.0.0.0");

    public const int UdpDiscoverSleepRequestInMilliseconds = 10_000;
}
