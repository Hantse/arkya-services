using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Contracts;
using Arkya.Identity.Api.Domain;
using Microsoft.AspNetCore.WebUtilities;

namespace Arkya.Identity.Tests;

public sealed class IdentityEndpointTests
{
    [Fact]
    public async Task DiscordLoginCreatesPrivateSessionThatCanBeIntrospectedAndRevoked()
    {
        await using var factory = new IdentityFactory();
        using var browser = factory.BrowserClient();
        var login = await LoginFlow.StartAsync(browser);
        var parameters = QueryHelpers.ParseQuery(login.DiscordUrl.Query);
        Assert.Equal("discord.com", login.DiscordUrl.Host);
        Assert.Equal("/oauth2/authorize", login.DiscordUrl.AbsolutePath);
        Assert.Equal("code", parameters["response_type"]);
        Assert.Equal("identify", parameters["scope"]);
        Assert.Equal("discord-test-client", parameters["client_id"]);
        Assert.Equal("https://localhost:7141/v1/auth/discord/callback", parameters["redirect_uri"]);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", login.State);
        Assert.NotEqual(login.Verifier, login.State);
        Assert.Contains("secure", login.Cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", login.Cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", login.Cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/v1/auth/discord/callback", login.Cookie);

        using var pending = await LoginFlow.ExchangeAsync(browser, login);
        Assert.Equal(HttpStatusCode.Accepted, pending.StatusCode);
        Assert.Equal(TimeSpan.FromSeconds(5), pending.Headers.RetryAfter!.Delta);
        using var callback = await browser.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        Assert.Equal("no-referrer", Assert.Single(callback.Headers.GetValues("Referrer-Policy")));
        using var exchanged = await LoginFlow.ExchangeAsync(browser, login);
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
        Assert.True(exchanged.Headers.CacheControl!.NoStore);
        var session = (await exchanged.Content.ReadFromJsonAsync<SessionResult>())!;
        Assert.True(Guid.TryParse(session.AccountId, out _));
        Assert.Equal("Discord Player", session.DisplayName);
        Assert.Equal(factory.Clock.GetUtcNow().UtcDateTime.AddHours(8), session.ExpiresAt);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", session.SessionToken);
        var persisted = Assert.Single(factory.Store.Sessions);
        Assert.Equal(IdentityService.Hash(session.SessionToken), persisted.TokenHash);
        Assert.NotEqual(session.SessionToken, persisted.TokenHash);
        var challenge = (await factory.Store.GetChallengeAsync(login.Challenge.ChallengeId, CancellationToken.None))!;
        Assert.Equal(IdentityService.Hash(login.State), challenge.StateHash);
        Assert.Equal(ChallengeStatus.Consumed, challenge.Status);

        browser.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionToken);
        using var me = await browser.GetAsync("/v1/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var profile = await me.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(session.AccountId, profile.GetProperty("accountId").GetString());
        Assert.False(profile.TryGetProperty("sessionToken", out _));
        Assert.False(profile.TryGetProperty("tokenHash", out _));
        Assert.False(profile.TryGetProperty("discordId", out _));

        using var internalClient = factory.BrowserClient();
        using var missingKey = await internalClient.PostAsJsonAsync("/internal/sessions/introspect", new { sessionToken = session.SessionToken });
        Assert.Equal(HttpStatusCode.Forbidden, missingKey.StatusCode);
        internalClient.DefaultRequestHeaders.Add("X-Arkya-Service-Key", "incorrect-service-key");
        using var wrongKey = await internalClient.PostAsJsonAsync("/internal/sessions/introspect", new { sessionToken = session.SessionToken });
        Assert.Equal(HttpStatusCode.Forbidden, wrongKey.StatusCode);
        internalClient.DefaultRequestHeaders.Remove("X-Arkya-Service-Key");
        internalClient.DefaultRequestHeaders.Add("X-Arkya-Service-Key", IdentityFactory.ServiceKey);
        using var valid = await internalClient.PostAsJsonAsync("/internal/sessions/introspect", new { sessionToken = session.SessionToken });
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(session.AccountId, (await valid.Content.ReadFromJsonAsync<AccountSessionResponse>())!.AccountId);

        using var logout = await browser.PostAsync("/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var revoked = await browser.GetAsync("/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        using var introspectedRevocation = await internalClient.PostAsJsonAsync("/internal/sessions/introspect", new { sessionToken = session.SessionToken });
        Assert.Equal(HttpStatusCode.Unauthorized, introspectedRevocation.StatusCode);
        Assert.Empty(factory.Store.Sessions);
    }

    [Fact]
    public async Task InvalidVerifierCannotConsumeCompletedChallenge()
    {
        await using var factory = new IdentityFactory();
        using var browser = factory.BrowserClient();
        var login = await LoginFlow.StartAsync(browser);
        using var callback = await browser.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        foreach (var verifier in new[] { "short", new string('x', 43), new string('x', 129), new string('!', 43), login.Verifier + "\n" })
        {
            using var invalid = await LoginFlow.ExchangeAsync(browser, login, verifier);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        Assert.Empty(factory.Store.Sessions);
        using var valid = await LoginFlow.ExchangeAsync(browser, login);
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Single(factory.Store.Sessions);
    }

    [Fact]
    public async Task ConcurrentExchangesProduceExactlyOneSession()
    {
        await using var factory = new IdentityFactory();
        using var browser = factory.BrowserClient();
        var login = await LoginFlow.StartAsync(browser);
        using var callback = await browser.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => LoginFlow.ExchangeAsync(browser, login)));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Equal(7, responses.Count(response => response.StatusCode == HttpStatusCode.Gone));
            Assert.Single(factory.Store.Sessions);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task DuplicateCallbacksExchangeDiscordCodeOnlyOnce()
    {
        await using var factory = new IdentityFactory();
        using var browser = factory.BrowserClient();
        var login = await LoginFlow.StartAsync(browser);
        var responses = await Task.WhenAll(browser.GetAsync(login.CallbackUrl()), browser.GetAsync(login.CallbackUrl()));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
            Assert.Equal(1, factory.Discord.Calls);
            Assert.Single(factory.Store.Accounts);
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task MissingOrMismatchedBrowserCookieDoesNotConsumeState()
    {
        await using var factory = new IdentityFactory();
        using var browser = factory.BrowserClient();
        using var stranger = factory.BrowserClient(cookies: false);
        var login = await LoginFlow.StartAsync(browser);
        using var missing = await stranger.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        stranger.DefaultRequestHeaders.Add("Cookie", $"{IdentityService.CookieName(login.Challenge.ChallengeId)}={new string('x', 43)}");
        using var mismatch = await stranger.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        Assert.Equal(0, factory.Discord.Calls);
        using var valid = await browser.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(1, factory.Discord.Calls);
    }

    [Fact]
    public async Task SecondBrowserStartCannotReplaceOriginalCallbackState()
    {
        await using var factory = new IdentityFactory();
        using var browser = factory.BrowserClient();
        var login = await LoginFlow.StartAsync(browser);
        using var secondStart = await browser.GetAsync(new Uri(login.Challenge.AuthorizationUrl).PathAndQuery);
        Assert.Equal(HttpStatusCode.BadRequest, secondStart.StatusCode);
        using var callback = await browser.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
    }

    [Fact]
    public async Task ExpiredChallengeRejectsCallbackEvenWhileDocumentStillExists()
    {
        await using var factory = new IdentityFactory();
        using var browser = factory.BrowserClient();
        var login = await LoginFlow.StartAsync(browser);
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        using var callback = await browser.GetAsync(login.CallbackUrl());
        Assert.Equal(HttpStatusCode.BadRequest, callback.StatusCode);
        using var exchange = await LoginFlow.ExchangeAsync(browser, login);
        Assert.Equal(HttpStatusCode.Gone, exchange.StatusCode);
        Assert.NotNull(await factory.Store.GetChallengeAsync(login.Challenge.ChallengeId, CancellationToken.None));
        Assert.Equal(0, factory.Discord.Calls);
        Assert.Empty(factory.Store.Sessions);
    }

    [Fact]
    public async Task ExpiredSessionIsInvalidWithoutTtlCleanup()
    {
        await using var factory = new IdentityFactory();
        using var browser = factory.BrowserClient();
        var session = await LoginFlow.CompleteAsync(browser);
        factory.Clock.Advance(TimeSpan.FromHours(8));
        browser.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionToken);
        browser.DefaultRequestHeaders.Add("X-Arkya-Service-Key", IdentityFactory.ServiceKey);
        using var me = await browser.GetAsync("/v1/auth/me");
        using var internalResponse = await browser.PostAsJsonAsync("/internal/sessions/introspect", new { sessionToken = session.SessionToken });
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, internalResponse.StatusCode);
        Assert.Single(factory.Store.Sessions);
    }

    [Theory]
    [InlineData("access_denied")]
    [InlineData("provider_unavailable")]
    [InlineData("provider_bad_response")]
    [InlineData("provider_timeout")]
    public async Task ProviderFailureTerminatesChallengeWithoutSession(string failure)
    {
        await using var factory = new IdentityFactory();
        factory.Discord.Failure = failure switch
        {
            "provider_unavailable" => new HttpRequestException("Provider unavailable"),
            "provider_bad_response" => new InvalidDataException("Invalid provider response"),
            "provider_timeout" => new TaskCanceledException("Provider timeout"),
            _ => null
        };
        using var browser = factory.BrowserClient();
        var login = await LoginFlow.StartAsync(browser);
        using var callback = await browser.GetAsync(login.CallbackUrl(failure == "access_denied" ? failure : null));
        Assert.Equal(HttpStatusCode.BadRequest, callback.StatusCode);
        using var exchange = await LoginFlow.ExchangeAsync(browser, login);
        Assert.Equal(HttpStatusCode.Gone, exchange.StatusCode);
        Assert.Equal(ChallengeStatus.Failed, (await factory.Store.GetChallengeAsync(login.Challenge.ChallengeId, CancellationToken.None))!.Status);
        Assert.Empty(factory.Store.Accounts);
        Assert.Empty(factory.Store.Sessions);
        Assert.Equal(failure == "access_denied" ? 0 : 1, factory.Discord.Calls);
    }

    [Fact]
    public async Task RepeatedDiscordLoginKeepsAccountIdAndRefreshesDisplayName()
    {
        await using var factory = new IdentityFactory();
        using var browser = factory.BrowserClient();
        var first = await LoginFlow.CompleteAsync(browser);
        factory.Discord.User = factory.Discord.User with { DisplayName = "Updated Discord Name" };
        var second = await LoginFlow.CompleteAsync(browser);
        Assert.Equal(first.AccountId, second.AccountId);
        Assert.NotEqual(first.SessionToken, second.SessionToken);
        Assert.Equal("Updated Discord Name", second.DisplayName);
        Assert.Single(factory.Store.Accounts);
        Assert.Equal(2, factory.Store.Sessions.Length);
    }

    [Theory]
    [InlineData("/v1/auth/discord/challenges", "{\"codeChallenge\":\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\",\"accountId\":\"injected\"}")]
    [InlineData("/v1/auth/discord/exchange", "{\"challengeId\":\"unknown\",\"codeVerifier\":\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\",\"displayName\":\"injected\"}")]
    [InlineData("/internal/sessions/introspect", "{\"sessionToken\":\"xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\",\"accountId\":\"injected\"}")]
    public async Task UnexpectedRequestFieldsAreRejected(string path, string body)
    {
        await using var factory = new IdentityFactory();
        using var client = factory.BrowserClient();
        client.DefaultRequestHeaders.Add("X-Arkya-Service-Key", IdentityFactory.ServiceKey);
        using var response = await client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.Store.Sessions);
        Assert.Empty(factory.Store.Accounts);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx+")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx\n")]
    public async Task InvalidChallengeIsRejected(string? challenge)
    {
        await using var factory = new IdentityFactory();
        using var client = factory.BrowserClient();
        using var response = await client.PostAsJsonAsync("/v1/auth/discord/challenges", new { codeChallenge = challenge });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
