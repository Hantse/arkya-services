using System.Net;
using System.Text.RegularExpressions;

namespace Arkya.Gateway;

public sealed class GatewayOptions
{
    public bool CharactersEnabled { get; set; }
    public bool AdminEnabled { get; set; }
    public string AdminHost { get; set; } = "backoffice.arkya.online";
    public string AdminAddress { get; set; } = "";

    public static bool IsAdminHostValid(string? host) => host is { Length: <= 253 }
        && Regex.IsMatch(host, @"\A[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)+\z")
        && !IPAddress.TryParse(host, out _)
        && !host.Equals("api.arkya.online", StringComparison.OrdinalIgnoreCase);

    public static bool IsOriginValid(string? address) => !string.IsNullOrWhiteSpace(address)
        && !address.Any(char.IsWhiteSpace) && !address.Contains('\\')
        && Uri.IsWellFormedUriString(address, UriKind.Absolute)
        && Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && uri.Host.Length > 0
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && uri.AbsolutePath == "/";

    public static string[] AllowedHosts(IConfiguration configuration) =>
        (configuration["AllowedHosts"] ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public bool HasExplicitAllowedHosts(IConfiguration configuration)
    {
        var hosts = AllowedHosts(configuration);
        return hosts.Contains(AdminHost, StringComparer.OrdinalIgnoreCase)
            && hosts.Contains("api.arkya.online", StringComparer.OrdinalIgnoreCase)
            && hosts.All(host => !host.Contains('*'));
    }
}
