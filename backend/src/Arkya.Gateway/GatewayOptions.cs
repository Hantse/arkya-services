using System.Net;
using System.Text.RegularExpressions;

namespace Arkya.Gateway;

public sealed class GatewayOptions
{
    public bool CharactersEnabled { get; set; }
    public bool RealmGameEnabled { get; set; }
    public string RealmGameAddress { get; set; } = "";
    public bool AdminEnabled { get; set; }
    public string AdminHost { get; set; } = "backoffice.arkya.online";
    public string AdminAddress { get; set; } = "";
    public bool FrontendEnabled { get; set; }
    public string FrontendHost { get; set; } = "arkya.online";
    public string FrontendAddress { get; set; } = "";

    public static bool IsWebHostValid(string? host) => host is { Length: <= 253 }
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

    public static bool IsRealmGameOriginValid(string? address) => IsOriginValid(address)
        && Regex.IsMatch(address!, @"\Ahttp://[a-z](?:[a-z0-9-]{0,61}[a-z0-9])?\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.svc\.cluster\.local(?::[1-9][0-9]{0,4})?/?\z",
            RegexOptions.CultureInvariant);

    public static string[] AllowedHosts(IConfiguration configuration) =>
        (configuration["AllowedHosts"] ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public string[] PublicApiHosts(IConfiguration configuration) => AllowedHosts(configuration)
        .Where(host => (!AdminEnabled || !host.Equals(AdminHost, StringComparison.OrdinalIgnoreCase))
            && (!FrontendEnabled || !host.Equals(FrontendHost, StringComparison.OrdinalIgnoreCase)))
        .ToArray();

    public bool HasExplicitAllowedHosts(IConfiguration configuration)
    {
        var hosts = AllowedHosts(configuration);
        return (!AdminEnabled || hosts.Contains(AdminHost, StringComparer.OrdinalIgnoreCase))
            && (!FrontendEnabled || hosts.Contains(FrontendHost, StringComparer.OrdinalIgnoreCase))
            && hosts.Contains("api.arkya.online", StringComparer.OrdinalIgnoreCase)
            && hosts.All(host => !host.Contains('*'));
    }
}
