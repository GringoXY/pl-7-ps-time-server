using System.Text.Json;

namespace Shared;

public static class Extensions
{
    public static string Clear(this string @string)
        => @string.Trim();

    /// <summary>
    /// Parses offer message UDP response
    /// </summary>
    /// <param name="string">Response to parse</param>
    /// <returns>List of offered IP addresses</returns>
    public static OfferIPAddress[] ParseOfferMessage(this string @string)
    {
        string json = @string.Replace(Config.OfferMessageRequest, string.Empty);
        return JsonSerializer.Deserialize<OfferIPAddress[]>(json) ?? [];
    }

    public static void PrintErrorMessage(this Exception exception, string baseMessage = "")
    {
        Console.ForegroundColor = ConsoleColor.Red;

        Console.WriteLine($"{baseMessage}: {exception.Message}");
        if (exception.InnerException is not null)
        {
            Console.WriteLine($"Wyjątek wewnętrzny: {exception.InnerException?.Message}");
        }

        Console.ForegroundColor = ConsoleColor.Gray;
    }
}
