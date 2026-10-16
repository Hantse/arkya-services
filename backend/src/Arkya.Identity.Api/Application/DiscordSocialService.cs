using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Arkya.Identity.Api.Contracts;
using Arkya.Identity.Api.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Arkya.Identity.Api.Application;

public interface IDiscordSocialClient
{
    Task<DiscordSocialGrant> ExchangeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken);
    Task<DiscordSocialGrant> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
}

public sealed record DiscordSocialGrant(string AccessToken, string RefreshToken, int ExpiresIn, string Scope, string DiscordUserId);

public sealed class DiscordSocialException(string code, int statusCode) : Exception(code)
{
    public string Code { get; } = code;
    public int StatusCode { get; } = statusCode;
}

public sealed partial class DiscordSocialService(
    IIdentityStore store, IDiscordSocialClient discord, IDataProtectionProvider protection,
    TimeProvider clock, IOptions<DiscordOptions> options)
{
    public const string RequiredScopes = "openid sdk.social_layer identify";
    public const string DesktopRedirectUri = "http://127.0.0.1/callback";
    private readonly IDataProtector refreshProtector = protection.CreateProtector("Arkya.Identity.DiscordSocial.Refresh.v1");

    public DiscordSocialConfiguration Configuration => new(options.Value.ClientId, RequiredScopes, DesktopRedirectUri);

    public async Task<DiscordSocialTokenResponse> ExchangeAsync(Session session, DiscordSocialExchangeRequest request, CancellationToken cancellationToken)
    {
        if (!IsBoundedSecret(request.Code, 2048) || request.CodeVerifier is null || !VerifierPattern().IsMatch(request.CodeVerifier) ||
            !IsAllowedRedirectUri(request.RedirectUri))
            throw new DiscordSocialException("invalid_social_exchange", 400);

        var account = await GetAccountAsync(session, cancellationToken);
        var grant = await discord.ExchangeAsync(request.Code!, request.CodeVerifier, request.RedirectUri!, cancellationToken);
        return await CompleteAsync(session, account, grant, cancellationToken);
    }

    public async Task<DiscordSocialTokenResponse> RefreshAsync(Session session, DiscordSocialRefreshRequest request, CancellationToken cancellationToken)
    {
        RefreshPayload? payload;
        try
        {
            if (!IsBoundedSecret(request.RefreshTicket, 16384)) throw new CryptographicException();
            payload = JsonSerializer.Deserialize<RefreshPayload>(refreshProtector.Unprotect(request.RefreshTicket!));
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException)
        {
            throw new DiscordSocialException("invalid_refresh_ticket", 400);
        }

        if (payload is null || payload.SessionHash != session.TokenHash || payload.AccountId != session.AccountId ||
            payload.ExpiresAt <= clock.GetUtcNow().UtcDateTime || !IsBoundedSecret(payload.RefreshToken, 4096))
            throw new DiscordSocialException("invalid_refresh_ticket", 400);

        var account = await GetAccountAsync(session, cancellationToken);
        var grant = await discord.RefreshAsync(payload.RefreshToken, cancellationToken);
        return await CompleteAsync(session, account, grant, cancellationToken);
    }

    private async Task<Account> GetAccountAsync(Session session, CancellationToken cancellationToken) =>
        await store.GetAccountAsync(session.AccountId, cancellationToken) ?? throw new DiscordSocialException("invalid_session", 401);

    private async Task<DiscordSocialTokenResponse> CompleteAsync(Session session, Account account, DiscordSocialGrant grant, CancellationToken cancellationToken)
    {
        if (grant.DiscordUserId != account.DiscordId)
            throw new DiscordSocialException("discord_account_mismatch", 403);
        if (await store.GetSessionAsync(session.TokenHash, clock.GetUtcNow().UtcDateTime, cancellationToken) is null)
            throw new DiscordSocialException("invalid_session", 401);

        var ticket = refreshProtector.Protect(JsonSerializer.Serialize(new RefreshPayload(
            session.TokenHash, session.AccountId, session.ExpiresAt, grant.RefreshToken)));
        return new(grant.AccessToken, ticket, "Bearer", grant.ExpiresIn, grant.Scope, grant.DiscordUserId);
    }

    public static bool IsAllowedRedirectUri(string? value) =>
        IsBoundedSecret(value, 2048) && Uri.TryCreate(value, UriKind.Absolute, out var redirect) && redirect.Scheme == Uri.UriSchemeHttp &&
        redirect.Host == "127.0.0.1" && redirect.AbsolutePath == "/callback" &&
        string.IsNullOrEmpty(redirect.UserInfo) && string.IsNullOrEmpty(redirect.Query) && string.IsNullOrEmpty(redirect.Fragment);

    public static bool IsBoundedSecret(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maxLength && !value.Any(char.IsControl);

    private sealed record RefreshPayload(string SessionHash, string AccountId, DateTime ExpiresAt, string RefreshToken);

    [GeneratedRegex("\\A[A-Za-z0-9._~-]{43,128}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VerifierPattern();
}
