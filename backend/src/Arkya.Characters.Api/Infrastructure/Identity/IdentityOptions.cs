using System.Text.RegularExpressions;

namespace Arkya.Characters.Api.Infrastructure.Identity;

public sealed class IdentityOptions
{
    public string BaseUrl { get; set; } = "";
    public string ServiceKey { get; set; } = "";
    public bool AllowClusterHttp { get; set; }

    public static bool HasSafeBaseUrl(IdentityOptions options, bool isDevelopment) =>
        Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
        (uri.Scheme == Uri.UriSchemeHttps || isDevelopment && uri.IsLoopback && uri.Scheme == Uri.UriSchemeHttp ||
            options.AllowClusterHttp && uri.Scheme == Uri.UriSchemeHttp && Regex.IsMatch(options.BaseUrl,
                @"\Ahttp://[a-z](?:[a-z0-9-]{0,61}[a-z0-9])?\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.svc\.cluster\.local(?::[1-9][0-9]{0,4})?/?\z",
                RegexOptions.CultureInvariant));
}
