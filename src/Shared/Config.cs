using System.Net;

namespace Shared;

public static class Config
{
    public const int MinTcpPort = 1025;
    public const int MaxTcpPort = 65535;

    /// <summary>
    /// Based on docs
    /// </summary>
    public const int UdpDiscoverPort = 2222;

    /// <summary>
    /// Command/request name for client requesting list of IP addresses (<see cref="OfferMessageRequest"/> for more)
    /// </summary>
    public const string DiscoverMessageRequest = "DISCOVER";

    /// <summary>
    /// Command/request name for the server offering list of available IP addresses (v4 only)
    /// </summary>
    public const string OfferMessageRequest = "OFFER";

    /// <summary>
    /// Command/request name for the client when he requests server's time
    /// </summary>
    public const string TimeMessageRequest = "TIME";

    public static readonly IPAddress MulticastGroupIpAddress = IPAddress.Parse("239.0.0.222");

    /// <summary>
    /// Sleep in milliseconds only for UDP <see cref="DiscoverMessageRequest"/> requests
    /// in order to lower CPU usage.
    /// </summary>
    public const int UdpDiscoverSleepRequestInMilliseconds = 10_000;

    /// <summary>
    /// Sleep in milliseconds only for UDP <see cref="DiscoverMessageRequest"/> requests
    /// in order to lower CPU usage.
    /// </summary>
    public const int UdpDiscoverTimeoutRequestInMilliseconds = 10_000;

    /// <summary>
    /// Sleep in milliseconds only for TCP <see cref="TimeMessageRequest"/> requests
    /// in order to lower CPU usage.
    /// </summary>
    public const int TcpTimeRequestTimeoutInMilliseconds = 10_000;

    /// <summary>
    /// Min sleep in milliseconds only for TCP <see cref="TimeMessageRequest"/> requests
    /// in order to lower CPU usage.
    /// </summary>
    public const int MinTcpTimeRequestFrequencyInMilliseconds = 10;

    /// <summary>
    /// Max sleep in milliseconds only for TCP <see cref="TimeMessageRequest"/> requests
    /// in order to lower CPU usage.
    /// </summary>
    public const int MaxTcpTimeRequestFrequencyInMilliseconds = 1_000;
}
