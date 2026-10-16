namespace Arkya.Characters.Api.Infrastructure.Identity;

public sealed class IdentityOptions
{
    public string BaseUrl { get; set; } = "";
    public string ServiceKey { get; set; } = "";

    public static bool HasSafeBaseUrl(IdentityOptions options, bool isDevelopment) =>
        Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
        (uri.Scheme == Uri.UriSchemeHttps || (isDevelopment && uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp));
}
