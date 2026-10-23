namespace Arkya.Realms.Api.Application;

public sealed class RealmRuntimeOptions
{
    public string DedicatedKeySeed { get; set; } = "";
    public string CharactersBaseUrl { get; set; } = "";
    public bool AllowClusterHttp { get; set; }
    public string RegistryDockerConfigFile { get; set; } = "";
    public string InferenceBaseUrl { get; set; } = "";
    public string InferenceServiceKey { get; set; } = "";

    public bool IsValid()
    {
        if (RegistryDockerConfigFile.Length > 1024 || RegistryDockerConfigFile.Any(char.IsControl) ||
            RegistryDockerConfigFile.Length > 0 && !Path.IsPathRooted(RegistryDockerConfigFile)) return false;
        if (!ValidInference()) return false;
        if (DedicatedKeySeed.Length == 0) return CharactersBaseUrl.Length == 0 && !AllowClusterHttp;
        if (DedicatedKeySeed.Length is < 32 or > 512 || DedicatedKeySeed != DedicatedKeySeed.Trim() || DedicatedKeySeed.Any(char.IsControl) ||
            !Uri.TryCreate(CharactersBaseUrl, UriKind.Absolute, out var uri) ||
            uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.AbsolutePath != "/") return false;
        return uri.Scheme == "https" || AllowClusterHttp &&
            CharactersBaseUrl.TrimEnd('/') == "http://arkya-characters.arkya-beta.svc.cluster.local:8080";
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
