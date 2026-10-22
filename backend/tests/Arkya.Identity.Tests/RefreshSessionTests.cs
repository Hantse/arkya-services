using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Endpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Arkya.Identity.Tests;

public sealed class RefreshSessionTests
{
    private const string Origin = "https://arkya.online";

    [Fact]
    public async Task BrowserReceivesPrivateRefreshCookieAndCanRenewAfterAccessExpires()
    {
        await using var factory = Factory();
        using var browser = Browser(factory);
        var (first, cookie) = await LoginAsync(browser);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1/auth", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime.AddDays(30), first.RefreshExpiresAt);
        var family = Assert.Single(factory.Store.RefreshSessions);
        Assert.Equal(IdentityService.Hash(Token(cookie)), family.TokenHash);
        Assert.NotEqual(Token(cookie), family.TokenHash);

        factory.Clock.Advance(TimeSpan.FromHours(8));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(browser, first.SessionToken));
        using var refresh = await browser.PostAsync("/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.True(refresh.Headers.CacheControl!.NoStore);
        var json = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.TryGetProperty("refreshToken", out _));
        var next = json.Deserialize<SessionResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(first.AccountId, next.AccountId);
        Assert.NotEqual(first.SessionToken, next.SessionToken);
        Assert.Equal(first.RefreshExpiresAt, next.RefreshExpiresAt);
        Assert.NotEqual(Token(cookie), Token(Assert.Single(refresh.Headers.GetValues("Set-Cookie"))));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(browser, next.SessionToken));
        Assert.Single(Assert.Single(factory.Store.RefreshSessions).UsedTokenHashes);
    }

    [Fact]
    public async Task RotationPreservesExistingAccessButLogoutRevokesEveryAccessInTheFamily()
    {
        await using var factory = Factory();
        using var browser = Browser(factory);
        var (first, _) = await LoginAsync(browser);
        using var refreshed = await browser.PostAsync("/v1/auth/refresh", null);
        var second = (await refreshed.Content.ReadFromJsonAsync<SessionResult>())!;
        Assert.Equal(HttpStatusCode.OK, await MeAsync(browser, first.SessionToken));
        Assert.Equal(HttpStatusCode.OK, await MeAsync(browser, second.SessionToken));

        browser.DefaultRequestHeaders.Authorization = null;
        using var logout = await browser.PostAsync("/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains("expires=Thu, 01 Jan 1970", Assert.Single(logout.Headers.GetValues("Set-Cookie")));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(browser, first.SessionToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(browser, second.SessionToken));
        using var scope = factory.Services.CreateScope();
        var identity = scope.ServiceProvider.GetRequiredService<IdentityService>();
        Assert.Null(await identity.ValidateSessionReferenceAsync(IdentityService.Hash(first.SessionToken), default));
        using var repeat = await browser.PostAsync("/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);
    }

    [Fact]
    public async Task LogoutWithExpiredAccessRevokesRefreshCookie()
    {
        await using var factory = Factory();
        using var browser = Browser(factory);
        var (first, cookie) = await LoginAsync(browser);
        factory.Clock.Advance(TimeSpan.FromHours(9));
        browser.DefaultRequestHeaders.Authorization = new("Bearer", first.SessionToken);
        using var logout = await browser.PostAsync("/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var replay = Browser(factory, cookie);
        using var refresh = await replay.PostAsync("/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task ConsumedRefreshReplayRevokesSuccessorAndOriginalAccess()
    {
        await using var factory = Factory();
        using var browser = Browser(factory);
        var (first, originalCookie) = await LoginAsync(browser);
        using var refresh = await browser.PostAsync("/v1/auth/refresh", null);
        var next = (await refresh.Content.ReadFromJsonAsync<SessionResult>())!;
        using var replayClient = Browser(factory, originalCookie);
        using var replay = await replayClient.PostAsync("/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var successor = await browser.PostAsync("/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, successor.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(browser, first.SessionToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(browser, next.SessionToken));
    }

    [Fact]
    public async Task GuessedSecretCannotRevokeKnownFamily()
    {
        await using var factory = Factory();
        using var browser = Browser(factory);
        var (first, cookie) = await LoginAsync(browser);
        var forged = Token(cookie)[..33] + new string('a', 43);
        using var attacker = Browser(factory, $"{RefreshCookie.Name}={forged}");
        using var refresh = await attacker.PostAsync("/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.False(Assert.Single(factory.Store.RefreshSessions).Revoked);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(browser, first.SessionToken));
    }

    [Theory]
    [InlineData(null, "1")]
    [InlineData("https://attacker.example", "1")]
    [InlineData("null", "1")]
    [InlineData(Origin, null)]
    [InlineData(Origin, "wrong")]
    public async Task CookieMutationRequiresAllowedOriginAndCustomHeader(string? origin, string? header)
    {
        await using var factory = Factory();
        using var browser = Browser(factory);
        var (first, cookie) = await LoginAsync(browser);
        using var unsafeClient = Browser(factory, cookie);
        unsafeClient.DefaultRequestHeaders.Remove("Origin");
        unsafeClient.DefaultRequestHeaders.Remove(RefreshCookie.HeaderName);
        if (origin is not null) unsafeClient.DefaultRequestHeaders.Add("Origin", origin);
        if (header is not null) unsafeClient.DefaultRequestHeaders.Add(RefreshCookie.HeaderName, header);
        foreach (var path in new[] { "/v1/auth/refresh", "/v1/auth/logout" })
        {
            using var response = await unsafeClient.PostAsync(path, null);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.False(response.Headers.Contains("Set-Cookie"));
        }
        using var exchange = await unsafeClient.PostAsJsonAsync("/v1/auth/discord/exchange", new { challengeId = "x", codeVerifier = "x", useRefreshCookie = true });
        Assert.Equal(HttpStatusCode.Forbidden, exchange.StatusCode);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(browser, first.SessionToken));
        Assert.Empty(Assert.Single(factory.Store.RefreshSessions).UsedTokenHashes);
    }

    [Fact]
    public async Task AbsoluteRefreshExpiryDoesNotSlideAndCapsAccessExpiry()
    {
        await using var factory = Factory();
        using var browser = Browser(factory);
        var (first, _) = await LoginAsync(browser);
        factory.Clock.Advance(TimeSpan.FromDays(30) - TimeSpan.FromMinutes(1));
        using var renewed = await browser.PostAsync("/v1/auth/refresh", null);
        var last = (await renewed.Content.ReadFromJsonAsync<SessionResult>())!;
        Assert.Equal(first.RefreshExpiresAt, last.RefreshExpiresAt);
        Assert.Equal(first.RefreshExpiresAt, last.ExpiresAt);
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        using var expired = await browser.PostAsync("/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(browser, last.SessionToken));
        Assert.Single(factory.Store.RefreshSessions);
    }

    [Fact]
    public async Task BanAndSubsequentUnbanCannotRestoreRefreshSession()
    {
        await using var factory = Factory();
        using var browser = Browser(factory);
        var (first, _) = await LoginAsync(browser);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        await factory.Store.AdministerAccountAsync(first.AccountId, 0, "ban", null, "Abuse", "admin", now, default);
        await factory.Store.AdministerAccountAsync(first.AccountId, 1, "unban", null, "Appeal", "admin", now, default);
        using var refresh = await browser.PostAsync("/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(browser, first.SessionToken));
    }

    [Fact]
    public async Task ConcurrentRefreshAllowsAtMostOneResponseAndRevokesFamily()
    {
        await using var factory = Factory();
        using var browser = Browser(factory);
        var (first, cookie) = await LoginAsync(browser);
        using var one = Browser(factory, cookie);
        using var two = Browser(factory, cookie);
        var responses = await Task.WhenAll(one.PostAsync("/v1/auth/refresh", null), two.PostAsync("/v1/auth/refresh", null));
        try
        {
            Assert.True(responses.Count(response => response.StatusCode == HttpStatusCode.OK) <= 1);
            Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
            Assert.True(Assert.Single(factory.Store.RefreshSessions).Revoked);
            Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(browser, first.SessionToken));
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task NativeExchangeRemainsOptInAndDoesNotIssueRefreshCookie()
    {
        await using var factory = Factory();
        using var native = factory.BrowserClient();
        var session = await LoginFlow.CompleteAsync(native);
        Assert.Null(session.RefreshExpiresAt);
        Assert.Empty(factory.Store.RefreshSessions);
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime.AddHours(8), session.ExpiresAt);
    }

    private static IdentityFactory Factory() => new() { WebClientOrigins = [Origin] };

    private static HttpClient Browser(IdentityFactory factory, string? explicitCookie = null)
    {
        var browser = factory.BrowserClient(cookies: explicitCookie is null);
        browser.DefaultRequestHeaders.Add("Origin", Origin);
        browser.DefaultRequestHeaders.Add(RefreshCookie.HeaderName, "1");
        if (explicitCookie is not null) browser.DefaultRequestHeaders.Add("Cookie", explicitCookie.Split(';')[0]);
        return browser;
    }

    private static async Task<(SessionResult Session, string Cookie)> LoginAsync(HttpClient browser)
    {
        var login = await LoginFlow.StartAsync(browser);
        using var callback = await browser.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        using var exchange = await browser.PostAsJsonAsync("/v1/auth/discord/exchange", new
        {
            challengeId = login.Challenge.ChallengeId, codeVerifier = login.Verifier, useRefreshCookie = true
        });
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var json = await exchange.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.TryGetProperty("refreshToken", out _));
        return (json.Deserialize<SessionResult>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!, Assert.Single(exchange.Headers.GetValues("Set-Cookie")));
    }

    private static string Token(string cookie) => cookie.Split(';')[0][(RefreshCookie.Name.Length + 1)..];

    private static async Task<HttpStatusCode> MeAsync(HttpClient browser, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/auth/me");
        request.Headers.Authorization = new("Bearer", token);
        using var result = await browser.SendAsync(request);
        return result.StatusCode;
    }
}
