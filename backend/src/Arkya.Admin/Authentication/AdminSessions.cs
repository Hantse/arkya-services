using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Authentication;

public sealed class AdminSessionOptions
{
    public TimeSpan AccessLifetime { get; init; } = TimeSpan.FromMinutes(30);
    public TimeSpan RefreshLifetime { get; init; } = TimeSpan.FromDays(30);
}

public sealed class AdminSessions(IAdminSessionStore store, IOptionsMonitor<AdminOptions> adminOptions,
    IOptions<AdminSessionOptions> options, TimeProvider time, IWebHostEnvironment environment)
{
    public const string SessionClaim = "arkya:admin:session";
    public const string RefreshCookie = "Arkya.Admin.Refresh";
    private const string PasswordProvider = "Password";
    private static readonly TimeSpan ConcurrentRefreshWindow = TimeSpan.FromSeconds(10);

    public async Task SigningInAsync(CookieSigningInContext context)
    {
        var principal = context.Principal ?? throw new AuthenticationFailureException("Administrator authorization is required.");
        if (principal.FindFirstValue(SessionClaim) is not null) return;
        var now = time.GetUtcNow();
        var admin = adminOptions.CurrentValue;
        var discord = principal.FindFirstValue(AdminDiscordAuthentication.ProviderClaim) == AdminDiscordAuthentication.Scheme;
        var token = RandomToken();
        var session = new AdminSession(RandomToken(), Hash(token), discord ? AdminDiscordAuthentication.Scheme : PasswordProvider,
            principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? PasswordProvider,
            principal.Identity?.Name ?? "Administrateur", discord ? "" : Hash(admin.Password),
            (now + options.Value.RefreshLifetime).UtcDateTime, now.UtcDateTime, []);
        if (!IsAuthorized(session)) throw new AuthenticationFailureException("Administrator authorization is required.");
        await store.CreateAsync(session, context.HttpContext.RequestAborted);
        context.Principal = Principal(session);
        SetAccessProperties(context.Properties, session);
        WriteRefreshCookie(context.HttpContext, session, token);
    }

    public async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var session = await GetAuthorizedAsync(context.Principal?.FindFirstValue(SessionClaim), context.HttpContext.RequestAborted);
        if (session is not null) return;
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public async Task SigningOutAsync(CookieSigningOutContext context)
    {
        var id = context.HttpContext.User.FindFirstValue(SessionClaim);
        if (id is not null) await store.RevokeAsync(id, context.HttpContext.RequestAborted);
        if (TryParse(context.Request.Cookies[RefreshCookie], out var refreshId, out var secret))
        {
            var session = await store.GetAsync(refreshId, context.HttpContext.RequestAborted);
            if (session is not null && (session.RefreshTokenHash == Hash(secret) || session.UsedTokenHashes.Contains(Hash(secret))))
                await store.RevokeAsync(refreshId, context.HttpContext.RequestAborted);
        }
        DeleteRefreshCookie(context.HttpContext);
    }

    public async Task RenewAsync(HttpContext context)
    {
        // Static assets, health probes and service MCP calls must not rotate browser sessions.
        if (context.Request.Path.StartsWithSegments("/health") || context.Request.Path.StartsWithSegments("/mcp")) return;
        var access = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        var now = time.GetUtcNow();
        if (access.Succeeded && access.Properties?.IssuedUtc is { } issued &&
            now - issued < options.Value.AccessLifetime / 2) return;
        if (!TryParse(context.Request.Cookies[RefreshCookie], out var id, out var secret)) return;
        var session = await GetAuthorizedAsync(id, context.RequestAborted);
        if (session is null) { DeleteRefreshCookie(context); return; }
        var hash = Hash(secret);
        if (session.RefreshTokenHash != hash)
        {
            await HandleConsumedAsync(context, session, hash, now);
            return;
        }
        // A refresh-only request immediately following another refresh does not grow token history.
        if (now.UtcDateTime - session.RotatedAt < ConcurrentRefreshWindow)
        {
            await SignInAsync(context, session);
            return;
        }
        var next = RandomToken();
        // Bound retained replay evidence even when a client repeatedly discards its access cookie.
        if (session.UsedTokenHashes.Length >= AdminSession.MaximumRotations)
        {
            await store.RevokeAsync(session.Id, context.RequestAborted);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            return;
        }
        if (!await store.RotateAsync(id, hash, Hash(next), now.UtcDateTime, context.RequestAborted))
        {
            session = await GetAuthorizedAsync(id, context.RequestAborted);
            if (session is not null) await HandleConsumedAsync(context, session, hash, now);
            return;
        }
        await SignInAsync(context, session);
        WriteRefreshCookie(context, session, next);
    }

    private async Task HandleConsumedAsync(HttpContext context, AdminSession session, string hash, DateTimeOffset now)
    {
        // Parallel tabs may carry the previous cookie while the winning response is in flight.
        // They can finish the current request but cannot receive or extend any credential.
        if (session.UsedTokenHashes.LastOrDefault() == hash && now.UtcDateTime - session.RotatedAt < ConcurrentRefreshWindow)
        {
            context.User = Principal(session);
            return;
        }
        if (session.UsedTokenHashes.Contains(hash))
        {
            await store.RevokeAsync(session.Id, context.RequestAborted);
            context.User = new ClaimsPrincipal(new ClaimsIdentity());
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    private async Task<AdminSession?> GetAuthorizedAsync(string? id, CancellationToken cancellationToken)
    {
        if (id is null) return null;
        var session = await store.GetAsync(id, cancellationToken);
        if (session is null) return null;
        if (session.SchemaVersion == 1 && session.ExpiresAt > time.GetUtcNow().UtcDateTime && IsAuthorized(session)) return session;
        await store.RevokeAsync(id, cancellationToken);
        return null;
    }

    private bool IsAuthorized(AdminSession session)
    {
        var admin = adminOptions.CurrentValue;
        return admin.DiscordEnabled
            ? session.Provider == AdminDiscordAuthentication.Scheme && AdminDiscordAuthentication.IsAllowed(admin, session.Subject)
            : session.Provider == PasswordProvider && session.CredentialVersion == Hash(admin.Password);
    }

    private static ClaimsPrincipal Principal(AdminSession session)
    {
        List<Claim> claims = [new(ClaimTypes.Name, session.Name), new(ClaimTypes.Role, "Administrator"),
            new(AdminDiscordAuthentication.ProviderClaim, session.Provider), new(SessionClaim, session.Id)];
        if (session.Provider == AdminDiscordAuthentication.Scheme) claims.Add(new(ClaimTypes.NameIdentifier, session.Subject));
        return new(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
    }

    private async Task SignInAsync(HttpContext context, AdminSession session)
    {
        var properties = new AuthenticationProperties();
        SetAccessProperties(properties, session);
        context.User = Principal(session);
        await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, context.User, properties);
    }

    private void SetAccessProperties(AuthenticationProperties properties, AdminSession session)
    {
        properties.IssuedUtc = time.GetUtcNow();
        properties.ExpiresUtc = new[] { time.GetUtcNow() + options.Value.AccessLifetime, new DateTimeOffset(session.ExpiresAt, TimeSpan.Zero) }.Min();
        properties.IsPersistent = true;
        properties.AllowRefresh = false;
    }

    private CookieOptions CookieOptions(DateTime? expiresAt = null) => new()
    {
        HttpOnly = true, Secure = !environment.IsDevelopment(), SameSite = SameSiteMode.Strict,
        Path = "/", IsEssential = true, Expires = expiresAt is { } expiry ? new DateTimeOffset(expiry, TimeSpan.Zero) : null
    };

    private void WriteRefreshCookie(HttpContext context, AdminSession session, string secret)
    {
        var cookie = CookieOptions(session.ExpiresAt);
        cookie.Secure |= context.Request.IsHttps;
        context.Response.Cookies.Append(RefreshCookie, session.Id + "." + secret, cookie);
    }

    private void DeleteRefreshCookie(HttpContext context)
    {
        var cookie = CookieOptions();
        cookie.Secure |= context.Request.IsHttps;
        context.Response.Cookies.Delete(RefreshCookie, cookie);
    }

    private static bool TryParse(string? value, out string id, out string secret)
    {
        id = secret = "";
        if (value is not { Length: 87 } || value[43] != '.' || value.Where((_, index) => index != 43)
                .Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_'))) return false;
        id = value[..43];
        secret = value[44..];
        return true;
    }

    private static string RandomToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
