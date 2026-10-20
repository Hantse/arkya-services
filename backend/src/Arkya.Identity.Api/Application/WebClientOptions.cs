namespace Arkya.Identity.Api.Application;

public sealed class WebClientOptions
{
    public const string CorsPolicyName = "arkya-web-client";
    public string[] AllowedOrigins { get; init; } = [];

    public bool IsAllowedReturnUrl(string value)
    {
        if (value.Length > 512) return false;
        foreach (var origin in AllowedOrigins)
        {
            var accountUrl = $"{origin}/account.html";
            if (value.Equals(accountUrl, StringComparison.Ordinal)) return true;
            if (!value.StartsWith(accountUrl + "?", StringComparison.Ordinal)) continue;

            // The return page accepts only these navigation choices, never an arbitrary target.
            var parameters = value[(accountUrl.Length + 1)..].Split('&');
            var names = new HashSet<string>(StringComparer.Ordinal);
            return parameters.All(parameter => parameter switch
            {
                "lang=fr" or "lang=en" => names.Add("lang"),
                "returnTo=play.html" => names.Add("returnTo"),
                _ => false
            });
        }
        return false;
    }

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
