using Arkya.Identity.Api.Application;

namespace Arkya.Identity.Api.Endpoints;

public static class RefreshCookie
{
    public const string Name = "arkya.refresh";
    public const string HeaderName = "X-Arkya-Refresh";

    public static bool IsAllowedRequest(HttpContext context, WebClientOptions options) =>
        context.Request.IsHttps && context.Request.Headers[HeaderName] == "1" &&
        context.Request.Headers.Origin.Count == 1 &&
        options.AllowedOrigins.Contains(context.Request.Headers.Origin.ToString(), StringComparer.Ordinal);

    public static void Write(HttpContext context, string token, DateTime expiresAt, TimeProvider clock) =>
        context.Response.Cookies.Append(Name, token, Options(expiresAt, expiresAt - clock.GetUtcNow().UtcDateTime));

    public static void Delete(HttpContext context) => context.Response.Cookies.Delete(Name, Options(null, null));

    private static CookieOptions Options(DateTime? expiresAt, TimeSpan? maxAge) => new()
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/v1/auth", IsEssential = true,
        Expires = expiresAt is { } value ? new DateTimeOffset(value, TimeSpan.Zero) : null, MaxAge = maxAge
    };
}
