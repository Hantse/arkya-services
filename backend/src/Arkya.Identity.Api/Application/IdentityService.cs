using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Arkya.Identity.Api.Domain;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace Arkya.Identity.Api.Application;

public sealed class DiscordOptions
{
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string RedirectUri { get; init; } = "";
    public string PublicBaseUrl { get; init; } = "https://localhost:7141";

    public bool HasValidEndpoints()
    {
        return Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var origin) &&
            Uri.TryCreate(RedirectUri, UriKind.Absolute, out var callback) &&
            origin.Scheme == Uri.UriSchemeHttps && callback.Scheme == Uri.UriSchemeHttps &&
            origin.Authority.Equals(callback.Authority, StringComparison.OrdinalIgnoreCase) &&
            origin.AbsolutePath == "/" && callback.AbsolutePath == "/v1/auth/discord/callback" &&
            string.IsNullOrEmpty(origin.Query) && string.IsNullOrEmpty(origin.Fragment) && string.IsNullOrEmpty(origin.UserInfo) &&
            string.IsNullOrEmpty(callback.Query) && string.IsNullOrEmpty(callback.Fragment) && string.IsNullOrEmpty(callback.UserInfo);
    }
}

public sealed class SessionOptions
{
    public TimeSpan Lifetime { get; init; } = TimeSpan.FromHours(8);
}

public sealed record ChallengeResult(string ChallengeId, string AuthorizationUrl, DateTime ExpiresAt, int PollIntervalSeconds = 5);
public sealed record SessionResult(string AccountId, string DisplayName, string SessionToken, DateTime ExpiresAt);
public sealed record ExchangeResult(string Status, SessionResult? Session = null);
public sealed record BrowserAuthorization(string RedirectUrl, string CookieName, string CookieValue);

public sealed partial class IdentityService(
    IIdentityStore store, IDiscordIdentityClient discord, TimeProvider clock,
    IOptions<DiscordOptions> discordOptions, IOptions<SessionOptions> sessionOptions)
{
    public async Task<ChallengeResult?> CreateChallengeAsync(string? codeChallenge, CancellationToken cancellationToken)
    {
        if (codeChallenge is null || !ChallengePattern().IsMatch(codeChallenge))
        {
            return null;
        }
        var challenge = new LoginChallenge(Guid.NewGuid().ToString("N"), codeChallenge, clock.GetUtcNow().UtcDateTime.AddMinutes(5));
        await store.CreateChallengeAsync(challenge, cancellationToken);
        return new(challenge.Id, $"{discordOptions.Value.PublicBaseUrl.TrimEnd('/')}/v1/auth/discord/authorize/{challenge.Id}", challenge.ExpiresAt);
    }

    public async Task<BrowserAuthorization?> StartBrowserAsync(string id, CancellationToken cancellationToken)
    {
        var state = RandomSecret();
        var nonce = RandomSecret();
        if (!await store.StartBrowserAsync(id, Hash(state), Hash(nonce), clock.GetUtcNow().UtcDateTime, cancellationToken))
        {
            return null;
        }
        var options = discordOptions.Value;
        var url = QueryHelpers.AddQueryString("https://discord.com/oauth2/authorize", new Dictionary<string, string?>
        {
            ["response_type"] = "code", ["client_id"] = options.ClientId, ["scope"] = "identify",
            ["redirect_uri"] = options.RedirectUri, ["state"] = state, ["prompt"] = "consent"
        });
        return new(url, CookieName(id), nonce);
    }

    public async Task<bool> CompleteBrowserAsync(string? state, string? code, string? error,
        Func<string, string?> readCookie, CancellationToken cancellationToken)
    {
        if (state is null || !ChallengePattern().IsMatch(state))
        {
            return false;
        }
        var candidate = await store.FindStateAsync(Hash(state), cancellationToken);
        if (candidate is null)
        {
            return false;
        }
        var nonce = readCookie(CookieName(candidate.Id));
        if (nonce is null || !ChallengePattern().IsMatch(nonce))
        {
            return false;
        }
        var claimed = await store.ClaimStateAsync(Hash(state), Hash(nonce), clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (claimed is null)
        {
            return false;
        }
        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code) || code.Length > 2048)
        {
            await store.CompleteChallengeAsync(claimed.Id, null, cancellationToken);
            return false;
        }
        try
        {
            var user = await discord.ExchangeCodeAsync(code, cancellationToken);
            var account = await store.UpsertAccountAsync(user.Id, user.DisplayName, clock.GetUtcNow().UtcDateTime, cancellationToken);
            await store.CompleteChallengeAsync(claimed.Id, account, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidDataException or TaskCanceledException)
        {
            // A claimed callback cannot be replayed after a provider failure.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await store.CompleteChallengeAsync(claimed.Id, null, cleanup.Token);
            return false;
        }
    }

    public async Task<ExchangeResult> ExchangeAsync(string? id, string? verifier, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 64 || verifier is null || !VerifierPattern().IsMatch(verifier))
        {
            return new("invalid");
        }
        var candidate = await store.GetChallengeAsync(id, cancellationToken);
        var proof = CodeChallenge(verifier);
        if (candidate is null || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(proof), Encoding.ASCII.GetBytes(candidate.CodeChallenge)))
        {
            return new("invalid");
        }
        var now = clock.GetUtcNow().UtcDateTime;
        if (candidate.ExpiresAt <= now || candidate.Status is ChallengeStatus.Failed or ChallengeStatus.Consumed)
        {
            return new("expired");
        }
        if (candidate.Status is ChallengeStatus.Pending or ChallengeStatus.Processing)
        {
            return new("pending");
        }
        var redeemed = await store.RedeemChallengeAsync(id, proof, now, cancellationToken);
        if (redeemed is null)
        {
            return new("expired");
        }
        var token = RandomSecret();
        var expiresAt = now.Add(sessionOptions.Value.Lifetime);
        await store.CreateSessionAsync(new(Hash(token), redeemed.AccountId!, redeemed.DisplayName!, expiresAt), cancellationToken);
        return new("completed", new(redeemed.AccountId!, redeemed.DisplayName!, token, expiresAt));
    }

    public Task<Session?> ValidateSessionAsync(string? token, CancellationToken cancellationToken)
    {
        return token is not null && ChallengePattern().IsMatch(token)
            ? store.GetSessionAsync(Hash(token), clock.GetUtcNow().UtcDateTime, cancellationToken)
            : Task.FromResult<Session?>(null);
    }

    public Task RevokeAsync(string token, CancellationToken cancellationToken) => store.RevokeSessionAsync(Hash(token), cancellationToken);
    public static string CodeChallenge(string verifier) => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static string CookieName(string id) => $"arkya.discord.{id}";
    private static string RandomSecret() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    [GeneratedRegex("\\A[A-Za-z0-9_-]{43}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex ChallengePattern();
    [GeneratedRegex("\\A[A-Za-z0-9._~-]{43,128}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex VerifierPattern();
}
