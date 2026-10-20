namespace Arkya.Realms.Api.Application;

public sealed class RealmRuntimeOptions
{
    public string DedicatedKeySeed { get; set; } = "";
    public string CharactersBaseUrl { get; set; } = "";
    public bool AllowClusterHttp { get; set; }
    public string RegistryDockerConfigFile { get; set; } = "";

    public bool IsValid()
    {
        if (RegistryDockerConfigFile.Length > 1024 || RegistryDockerConfigFile.Any(char.IsControl) ||
            RegistryDockerConfigFile.Length > 0 && !Path.IsPathRooted(RegistryDockerConfigFile)) return false;
        if (DedicatedKeySeed.Length == 0) return CharactersBaseUrl.Length == 0 && !AllowClusterHttp;
        if (DedicatedKeySeed.Length is < 32 or > 512 || DedicatedKeySeed != DedicatedKeySeed.Trim() || DedicatedKeySeed.Any(char.IsControl) ||
            !Uri.TryCreate(CharactersBaseUrl, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/") return false;
        return uri.Scheme == "https" || AllowClusterHttp &&
            CharactersBaseUrl.TrimEnd('/') == "http://arkya-characters.arkya-beta.svc.cluster.local:8080";
    }
}
