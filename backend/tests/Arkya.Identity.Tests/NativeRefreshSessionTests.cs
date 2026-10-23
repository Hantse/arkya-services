using System.Net;
using System.Net.Http.Json;
using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Contracts;
using Arkya.Identity.Api.Endpoints;

namespace Arkya.Identity.Tests;

public sealed class NativeRefreshSessionTests
{
    [Fact]
    public async Task NativeOptInReturnsRotatableSecretWithoutCookiesAndSurvivesAccessExpiry()
    {
        await using var factory = new IdentityFactory();
        using var native = factory.BrowserClient(cookies: false);
        var first = await LoginAsync(factory, native);
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime.AddDays(30), first.RefreshExpiresAt);
        Assert.Equal(IdentityService.Hash(first.RefreshToken), Assert.Single(factory.Store.RefreshSessions).TokenHash);
        factory.Clock.Advance(TimeSpan.FromHours(8));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(native, first.SessionToken));
        using var refresh = await native.PostAsJsonAsync("/v1/auth/refresh/native", new NativeRefreshRequest(first.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.True(refresh.Headers.CacheControl!.NoStore);
        Assert.False(refresh.Headers.Contains("Set-Cookie"));
        var next = (await refresh.Content.ReadFromJsonAsync<NativeSessionResponse>())!;
        Assert.NotEqual(first.RefreshToken, next.RefreshToken);
        Assert.NotEqual(first.SessionToken, next.SessionToken);
        Assert.Equal(first.AccountId, next.AccountId);
        Assert.Equal(first.RefreshExpiresAt, next.RefreshExpiresAt);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(native, next.SessionToken));
    }

    [Fact]
    public async Task NativeReplayRevokesSuccessorAndAllFamilyAccess()
    {
        await using var factory = new IdentityFactory();
        using var native = factory.BrowserClient(cookies: false);
        var first = await LoginAsync(factory, native);
        using var refresh = await native.PostAsJsonAsync("/v1/auth/refresh/native", new NativeRefreshRequest(first.RefreshToken));
        var next = (await refresh.Content.ReadFromJsonAsync<NativeSessionResponse>())!;
        using var replay = await native.PostAsJsonAsync("/v1/auth/refresh/native", new NativeRefreshRequest(first.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        using var successor = await native.PostAsJsonAsync("/v1/auth/refresh/native", new NativeRefreshRequest(next.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, successor.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(native, first.SessionToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(native, next.SessionToken));
    }

    [Fact]
    public async Task NativeLogoutRevokesFamilyEvenAfterAccessHasExpired()
    {
        await using var factory = new IdentityFactory();
        using var native = factory.BrowserClient(cookies: false);
        var first = await LoginAsync(factory, native);
        factory.Clock.Advance(TimeSpan.FromHours(9));
        using var logout = await native.PostAsJsonAsync("/v1/auth/logout/native", new NativeRefreshRequest(first.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.True(logout.Headers.CacheControl!.NoStore);
        Assert.False(logout.Headers.Contains("Set-Cookie"));
        using var refresh = await native.PostAsJsonAsync("/v1/auth/refresh/native", new NativeRefreshRequest(first.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        using var repeat = await native.PostAsJsonAsync("/v1/auth/logout/native", new NativeRefreshRequest(first.RefreshToken));
        Assert.Equal(HttpStatusCode.NoContent, repeat.StatusCode);
    }

    [Fact]
    public async Task NativeAbsoluteExpiryAndBanUnbanNeverRestoreTheFamily()
    {
        await using var factory = new IdentityFactory();
        using var native = factory.BrowserClient(cookies: false);
        var first = await LoginAsync(factory, native);
        var now = factory.Clock.GetUtcNow().UtcDateTime;
        await factory.Store.AdministerAccountAsync(first.AccountId, 0, "ban", null, "Abuse", "admin", now, default);
        await factory.Store.AdministerAccountAsync(first.AccountId, 1, "unban", null, "Appeal", "admin", now, default);
        using var banned = await native.PostAsJsonAsync("/v1/auth/refresh/native", new NativeRefreshRequest(first.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, banned.StatusCode);

        var fresh = await LoginAsync(factory, native);
        factory.Clock.Advance(TimeSpan.FromDays(30));
        using var expired = await native.PostAsJsonAsync("/v1/auth/refresh/native", new NativeRefreshRequest(fresh.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeAsync(native, fresh.SessionToken));
    }

    [Theory]
    [InlineData("Origin", "https://arkya.online")]
    [InlineData("Origin", "https://evil.test")]
    [InlineData("Cookie", "arkya.refresh=test")]
    [InlineData("Sec-Fetch-Site", "same-site")]
    [InlineData(RefreshCookie.HeaderName, "1")]
    public async Task BrowserRequestsCannotUseTheNativeRefreshTokenMode(string header, string value)
    {
        await using var factory = new IdentityFactory { WebClientOrigins = ["https://arkya.online"] };
        using var native = factory.BrowserClient(cookies: false);
        var first = await LoginAsync(factory, native);
        native.DefaultRequestHeaders.Add(header, value);
        foreach (var path in new[] { "/v1/auth/refresh/native", "/v1/auth/logout/native" })
        {
            using var denied = await native.PostAsJsonAsync(path, new NativeRefreshRequest(first.RefreshToken));
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.False(denied.Headers.Contains("Set-Cookie"));
        }
        using var exchange = await native.PostAsJsonAsync("/v1/auth/discord/exchange", new
        { challengeId = "x", codeVerifier = "x", useRefreshToken = true });
        Assert.Equal(HttpStatusCode.Forbidden, exchange.StatusCode);
        Assert.Empty(Assert.Single(factory.Store.RefreshSessions).UsedTokenHashes);
        Assert.False(Assert.Single(factory.Store.RefreshSessions).Revoked);
    }

    [Fact]
    public async Task ConflictingRefreshModesAreRejectedBeforeRedeemingTheChallenge()
    {
        await using var factory = new IdentityFactory();
        using var browser = factory.BrowserClient();
        var login = await LoginFlow.StartAsync(browser);
        using var callback = await browser.GetAsync(login.CallbackUrl());
        using var native = factory.BrowserClient(cookies: false);
        using var conflict = await native.PostAsJsonAsync("/v1/auth/discord/exchange", new ExchangeRequest(
            login.Challenge.ChallengeId, login.Verifier, UseRefreshCookie: true, UseRefreshToken: true));
        Assert.Equal(HttpStatusCode.BadRequest, conflict.StatusCode);
        Assert.Empty(factory.Store.RefreshSessions);
        using var valid = await native.PostAsJsonAsync("/v1/auth/discord/exchange", new ExchangeRequest(
            login.Challenge.ChallengeId, login.Verifier, UseRefreshToken: true));
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Single(factory.Store.RefreshSessions);
    }

    [Fact]
    public async Task GuessedNativeSecretCannotRevokeARealFamily()
    {
        await using var factory = new IdentityFactory();
        using var native = factory.BrowserClient(cookies: false);
        var first = await LoginAsync(factory, native);
        var forged = first.RefreshToken[..33] + new string('z', 43);
        using var refresh = await native.PostAsJsonAsync("/v1/auth/refresh/native", new NativeRefreshRequest(forged));
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        using var logout = await native.PostAsJsonAsync("/v1/auth/logout/native", new NativeRefreshRequest(forged));
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.False(Assert.Single(factory.Store.RefreshSessions).Revoked);
        Assert.Equal(HttpStatusCode.OK, await MeAsync(native, first.SessionToken));
    }

    private static async Task<NativeSessionResponse> LoginAsync(IdentityFactory factory, HttpClient native)
    {
        using var browser = factory.BrowserClient();
        var login = await LoginFlow.StartAsync(browser);
        using var callback = await browser.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        using var exchange = await native.PostAsJsonAsync("/v1/auth/discord/exchange", new ExchangeRequest(
            login.Challenge.ChallengeId, login.Verifier, UseRefreshToken: true));
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        Assert.True(exchange.Headers.CacheControl!.NoStore);
        Assert.False(exchange.Headers.Contains("Set-Cookie"));
        var session = (await exchange.Content.ReadFromJsonAsync<NativeSessionResponse>())!;
        Assert.Matches("^[a-f0-9]{32}\\.[A-Za-z0-9_-]{43}$", session.RefreshToken);
        return session;
    }

    private static async Task<HttpStatusCode> MeAsync(HttpClient native, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/v1/auth/me");
        request.Headers.Authorization = new("Bearer", token);
        using var response = await native.SendAsync(request);
        return response.StatusCode;
    }
}
