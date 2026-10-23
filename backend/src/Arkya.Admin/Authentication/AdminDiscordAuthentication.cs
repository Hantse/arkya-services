using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Authentication;

public sealed class AdminDiscordOptions
{
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string PublicBaseUrl { get; init; } = "";

    public bool IsValid() => AdminDiscordAuthentication.IsDiscordId(ClientId) &&
        ClientSecret.Length is >= 16 and <= 512 &&
        Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
        string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
        uri.AbsolutePath == "/" && PublicBaseUrl.TrimEnd('/') == uri.GetLeftPart(UriPartial.Authority);
}

public static class AdminDiscordAuthentication
{
    public const string Scheme = "Discord";
    public const string ProviderClaim = "arkya:admin:provider";
    public const string CallbackPath = "/signin-discord";

    public static bool IsDiscordId(string? value) => value is { Length: >= 17 and <= 20 } && value[0] != '0' &&
        value.All(char.IsAsciiDigit) && ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    public static bool IsAllowed(AdminOptions options, string? id) => options.DiscordEnabled &&
        IsDiscordId(id) && options.AllowedDiscordUserIds.Contains(id!, StringComparer.Ordinal);

    public static bool IsAuthorized(AdminOptions options, ClaimsPrincipal? principal)
    {
        var ids = principal?.FindAll(ClaimTypes.NameIdentifier).ToArray();
        return principal?.Identity?.IsAuthenticated == true && principal.IsInRole("Administrator") &&
            principal.FindAll(ProviderClaim).Select(claim => claim.Value).SequenceEqual([Scheme]) &&
            ids is { Length: 1 } && IsAllowed(options, ids[0].Value);
    }

    public static string LocalReturnUrl(string? value) => value is { Length: > 0 } && value[0] == '/' &&
        (value.Length == 1 || value[1] is not ('/' or '\\')) && !value.Any(char.IsControl) ? value : "/";
}

public sealed class AdminDiscordHandler(
    IOptionsMonitor<OAuthOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<AdminOptions> adminOptions,
    IOptions<AdminDiscordOptions> discordOptions) : OAuthHandler<OAuthOptions>(options, logger, encoder)
{
    private string CallbackUrl => discordOptions.Value.PublicBaseUrl.TrimEnd('/') + AdminDiscordAuthentication.CallbackPath;

    public override Task<bool> HandleRequestAsync() => adminOptions.CurrentValue.DiscordEnabled
        ? base.HandleRequestAsync() : Task.FromResult(false);

    protected override string BuildChallengeUrl(AuthenticationProperties properties, string redirectUri) =>
        base.BuildChallengeUrl(properties, CallbackUrl);

    protected override Task<HandleRequestResult> HandleRemoteAuthenticateAsync()
    {
        var issuer = Request.Query["iss"];
        if (issuer.Count > 0 && (issuer.Count != 1 || issuer[0] != "https://discord.com"))
            return Task.FromResult(HandleRequestResult.Fail("Discord authentication failed."));
        return base.HandleRemoteAuthenticateAsync();
    }

    protected override async Task<OAuthTokenResponse> ExchangeCodeAsync(OAuthCodeExchangeContext context)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Options.TokenEndpoint)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = Options.ClientId,
                    ["client_secret"] = Options.ClientSecret,
                    ["code"] = context.Code,
                    ["grant_type"] = "authorization_code",
                    ["redirect_uri"] = CallbackUrl
                })
            };
            using var response = await Backchannel.SendAsync(request, Context.RequestAborted);
            if (!response.IsSuccessStatusCode) return Failure();
            var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Context.RequestAborted));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("access_token", out var token) || token.ValueKind != JsonValueKind.String ||
                token.GetString() is not { Length: >= 1 and <= 4096 } ||
                !document.RootElement.TryGetProperty("token_type", out var type) || type.ValueKind != JsonValueKind.String ||
                !string.Equals(type.GetString(), "Bearer", StringComparison.OrdinalIgnoreCase))
            {
                document.Dispose();
                return Failure();
            }
            return OAuthTokenResponse.Success(document);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            return Failure();
        }
    }

    protected override async Task<AuthenticationTicket> CreateTicketAsync(ClaimsIdentity identity,
        AuthenticationProperties properties, OAuthTokenResponse tokens)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Options.UserInformationEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
            using var response = await Backchannel.SendAsync(request, Context.RequestAborted);
            if (!response.IsSuccessStatusCode) throw new AuthenticationFailureException("Discord authentication failed.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Context.RequestAborted));
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String ||
                !AdminDiscordAuthentication.IsAllowed(adminOptions.CurrentValue, id.GetString()))
                throw new AuthenticationFailureException("Discord authentication failed.");
            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, id.GetString()!));
            identity.AddClaim(new Claim(ClaimTypes.Name, "Discord administrator"));
            identity.AddClaim(new Claim(AdminDiscordAuthentication.ProviderClaim, AdminDiscordAuthentication.Scheme));
            identity.AddClaim(new Claim(ClaimTypes.Role, "Administrator"));
            properties.RedirectUri = AdminDiscordAuthentication.LocalReturnUrl(properties.RedirectUri);
            return new AuthenticationTicket(new ClaimsPrincipal(identity), properties, Scheme.Name);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException)
        {
            throw new AuthenticationFailureException("Discord authentication failed.");
        }
    }

    private static OAuthTokenResponse Failure() =>
        OAuthTokenResponse.Failed(new AuthenticationFailureException("Discord authentication failed."));
}
