using Arkya.ServiceDefaults;

namespace Arkya.Realms.Api.Application;

public sealed class RealmServiceOptions
{
    public string RealmId { get; set; } = "";
    public string CharactersBaseUrl { get; set; } = "";
}

public sealed class RealmRuntimeOptions
{
    public string DedicatedKeySeed { get; set; } = "";
    public string CharactersBaseUrl { get; set; } = "";
    public string RealmsBaseUrl { get; set; } = "";
    public RealmServiceOptions[] Services { get; set; } = [];
    public bool AllowClusterHttp { get; set; }
    public string RegistryDockerConfigFile { get; set; } = "";
    public string InferenceBaseUrl { get; set; } = "";
    public string InferenceServiceKey { get; set; } = "";

    public bool IsValid()
    {
        if (RegistryDockerConfigFile.Length > 1024 || RegistryDockerConfigFile.Any(char.IsControl) ||
            RegistryDockerConfigFile.Length > 0 && !Path.IsPathRooted(RegistryDockerConfigFile)) return false;
        if (Services is not { Length: <= 100 } || Services.Any(service => service is null ||
                !GameProtocol.ValidId(service.RealmId) || !ValidCharactersUrl(service.CharactersBaseUrl)) ||
            Services.Select(service => service.RealmId).Distinct(StringComparer.Ordinal).Count() != Services.Length) return false;
        if (RealmsBaseUrl.Length > 0 && !ValidServiceUrl(RealmsBaseUrl,
                "http://arkya-realms.arkya-beta.svc.cluster.local:8080")) return false;
        if (!ValidInference()) return false;
        if (DedicatedKeySeed.Length == 0) return CharactersBaseUrl.Length == 0 && RealmsBaseUrl.Length == 0 &&
            Services.Length == 0 && !AllowClusterHttp;
        if (DedicatedKeySeed.Length is < 32 or > 512 || DedicatedKeySeed != DedicatedKeySeed.Trim() || DedicatedKeySeed.Any(char.IsControl) ||
            !ValidCharactersUrl(CharactersBaseUrl)) return false;
        return true;
    }

    public string CharactersUrlFor(string realmId) =>
        Services.FirstOrDefault(service => service.RealmId == realmId)?.CharactersBaseUrl ?? CharactersBaseUrl;

    private bool ValidCharactersUrl(string? address) =>
        ValidServiceUrl(address, "http://arkya-characters.arkya-beta.svc.cluster.local:8080");

    private bool ValidServiceUrl(string? address, string clusterOrigin)
    {
        if (address is not { Length: > 0 and <= 512 } || address.Any(char.IsWhiteSpace) || address.Contains('\\') ||
            !Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/") return false;
        return uri.Scheme == "https" || AllowClusterHttp && address.TrimEnd('/') == clusterOrigin;
    }

    private bool ValidInference()
    {
        if (InferenceBaseUrl.Length == 0 && InferenceServiceKey.Length == 0) return true;
        if (DedicatedKeySeed.Length == 0 || InferenceServiceKey.Length is < 32 or > 512 ||
            InferenceServiceKey != InferenceServiceKey.Trim() || InferenceServiceKey.Any(char.IsControl) ||
            !Uri.TryCreate(InferenceBaseUrl, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/") return false;
        return uri.Scheme == "https" || AllowClusterHttp &&
            InferenceBaseUrl.TrimEnd('/') == "http://arkya-inference.arkya-beta.svc.cluster.local:8080";
    }
}
