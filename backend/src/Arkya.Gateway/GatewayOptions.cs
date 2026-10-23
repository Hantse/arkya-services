using System.Net;
using System.Text.RegularExpressions;

namespace Arkya.Gateway;

public sealed class GatewayOptions
{
    public bool CharactersEnabled { get; set; }
    public bool ContentEnabled { get; set; }
    public bool RealmGameEnabled { get; set; }
    public string RealmGameAddress { get; set; } = "";
    public RealmGamePartition[] RealmGamePartitions { get; set; } = [];
    public bool ReportsEnabled { get; set; }
    public bool InferenceEnabled { get; set; }
    public bool AdminEnabled { get; set; }
    public string AdminHost { get; set; } = "backoffice.arkya.online";
    public string AdminAddress { get; set; } = "";
    public bool FrontendEnabled { get; set; }
    public string FrontendHost { get; set; } = "arkya.online";
    public string FrontendAddress { get; set; } = "";
    public bool PublicRealmsEnabled { get; set; }
    public string PublicRealmsAddress { get; set; } = "";
    public bool RealmSitesEnabled { get; set; }
    public string RealmBaseDomain { get; set; } = "arkya.online";

    public string RealmAdminHostPattern => $"*.admin.{RealmBaseDomain}";
    public string RealmPublicHostPattern => $"*.{RealmBaseDomain}";

    public string? RealmIdForHost(string host, bool admin = false)
    {
        var suffix = admin ? $".admin.{RealmBaseDomain}" : $".{RealmBaseDomain}";
        if (!RealmSitesEnabled || !host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return null;
        var id = host[..^suffix.Length].ToLowerInvariant();
        return Regex.IsMatch(id, @"\A[a-z][a-z0-9-]{1,29}[a-z0-9]\z", RegexOptions.CultureInvariant)
            && id is not ("api" or "admin" or "www" or "backoffice" or "beta-game") ? id : null;
    }

    public bool IsIncomingHostAllowed(string host, IConfiguration configuration) =>
        AllowedHosts(configuration).Any(allowed => !allowed.Contains('*') && host.Equals(allowed, StringComparison.OrdinalIgnoreCase))
        || RealmIdForHost(host) is not null || RealmIdForHost(host, admin: true) is not null;

    public bool IsFrontendHost(string host) => FrontendEnabled &&
        (host.Equals(FrontendHost, StringComparison.OrdinalIgnoreCase) || RealmIdForHost(host) is not null);

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

    public bool HasValidRealmGamePartitions() => RealmGamePartitions is { Length: <= 256 }
        && RealmGamePartitions.All(partition => partition is not null && partition.IsValid())
        && RealmGamePartitions.Select(partition => (partition.RealmId, partition.ServerId)).Distinct().Count() == RealmGamePartitions.Length
        && RealmGamePartitions.Select(partition => new Uri(partition.Address).AbsoluteUri).Distinct(StringComparer.OrdinalIgnoreCase).Count() == RealmGamePartitions.Length;

    public static string[] AllowedHosts(IConfiguration configuration) =>
        (configuration["AllowedHosts"] ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    public string[] PublicApiHosts(IConfiguration configuration) => AllowedHosts(configuration)
        .Where(host => !host.Contains('*') && (!AdminEnabled || !host.Equals(AdminHost, StringComparison.OrdinalIgnoreCase))
            && (!FrontendEnabled || !host.Equals(FrontendHost, StringComparison.OrdinalIgnoreCase)))
        .ToArray();

    public bool HasExplicitAllowedHosts(IConfiguration configuration)
    {
        var hosts = AllowedHosts(configuration);
        return (!AdminEnabled || hosts.Contains(AdminHost, StringComparer.OrdinalIgnoreCase))
            && (!FrontendEnabled || hosts.Contains(FrontendHost, StringComparer.OrdinalIgnoreCase))
            && hosts.Contains("api.arkya.online", StringComparer.OrdinalIgnoreCase)
            && (!RealmSitesEnabled || hosts.Contains(RealmPublicHostPattern, StringComparer.OrdinalIgnoreCase)
                && hosts.Contains(RealmAdminHostPattern, StringComparer.OrdinalIgnoreCase))
            && hosts.All(host => !host.Contains('*') || RealmSitesEnabled &&
                (host.Equals(RealmPublicHostPattern, StringComparison.OrdinalIgnoreCase)
                 || host.Equals(RealmAdminHostPattern, StringComparison.OrdinalIgnoreCase)));
    }
}

public sealed class RealmGamePartition
{
    public string RealmId { get; set; } = "";
    public string ServerId { get; set; } = "";
    public string Address { get; set; } = "";
    public string ClusterId => $"realm-partition-{RealmId}_{ServerId}";
    public string PublicPath => $"/game/{RealmId}/{ServerId}/";

    public bool IsValid() => ValidId(RealmId) && ValidId(ServerId) && GatewayOptions.IsRealmGameOriginValid(Address);

    private static bool ValidId(string? value) => value is not null && Regex.IsMatch(value,
        @"\A[a-z][a-z0-9-]{1,29}[a-z0-9]\z", RegexOptions.CultureInvariant);
}
