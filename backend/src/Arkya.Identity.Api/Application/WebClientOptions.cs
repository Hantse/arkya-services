namespace Arkya.Identity.Api.Application;

public sealed class WebClientOptions
{
    public const string CorsPolicyName = "arkya-web-client";
    public string[] AllowedOrigins { get; init; } = [];

    public bool HasValidOrigins(bool development)
    {
        return AllowedOrigins is not null && AllowedOrigins.All(value =>
            !string.IsNullOrWhiteSpace(value) && value == value.Trim() && !value.Contains('*') &&
            !value.EndsWith('/') && Uri.TryCreate(value, UriKind.Absolute, out var origin) &&
            (origin.Scheme == Uri.UriSchemeHttps ||
                (development && origin.Scheme == Uri.UriSchemeHttp && origin.Host is "localhost" or "127.0.0.1")) &&
            value.Equals(origin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase) &&
            origin.AbsolutePath == "/" && string.IsNullOrEmpty(origin.Query) &&
            string.IsNullOrEmpty(origin.Fragment) && string.IsNullOrEmpty(origin.UserInfo));
    }
}
