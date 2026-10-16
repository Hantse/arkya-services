using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Contracts;

namespace Arkya.Identity.Tests;

internal sealed class FakeDiscordSocialClient : IDiscordSocialClient
{
    public int Calls { get; private set; }
    public string? RefreshedToken { get; private set; }
    public DiscordSocialGrant Grant { get; set; } = new("social-access-token", "private-provider-refresh", 604800,
        DiscordSocialService.RequiredScopes, "123456789012345678");
    public DiscordSocialException? Failure { get; set; }
    public Func<Task>? BeforeReturn { get; set; }

    public Task<DiscordSocialGrant> ExchangeAsync(string code, string codeVerifier, string redirectUri, CancellationToken cancellationToken) => GetGrantAsync();

    public Task<DiscordSocialGrant> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        RefreshedToken = refreshToken;
        return GetGrantAsync();
    }

    private async Task<DiscordSocialGrant> GetGrantAsync()
    {
        Calls++;
        if (Failure is not null) throw Failure;
        if (BeforeReturn is not null) await BeforeReturn();
        return Grant;
    }
}

public sealed class DiscordSocialEndpointTests
{
    private static readonly DiscordSocialExchangeRequest ValidExchange = new("sdk-code", new string('v', 43), "http://127.0.0.1:6463/callback");

    [Fact]
    public async Task ExchangeAndRefreshRequireSameAccountAndNeverReturnProviderRefreshToken()
    {
        await using var factory = new IdentityFactory();
        using var client = factory.BrowserClient();
        var session = await LoginFlow.CompleteAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionToken);
        var configuration = await client.GetFromJsonAsync<DiscordSocialConfiguration>("/v1/auth/discord/social/config");
        Assert.Equal("discord-test-client", configuration!.ApplicationId);
        Assert.Equal(DiscordSocialService.RequiredScopes, configuration.Scopes);
        Assert.Equal("http://127.0.0.1/callback", configuration.RedirectUri);

        using var exchanged = await client.PostAsJsonAsync("/v1/auth/discord/social/exchange", ValidExchange);
        Assert.Equal(HttpStatusCode.OK, exchanged.StatusCode);
        Assert.True(exchanged.Headers.CacheControl!.NoStore);
        var body = await exchanged.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-provider-refresh", body);
        Assert.DoesNotContain("discord-test-secret", body);
        var token = (await exchanged.Content.ReadFromJsonAsync<DiscordSocialTokenResponse>())!;
        Assert.Equal("social-access-token", token.AccessToken);
        Assert.Equal("123456789012345678", token.DiscordUserId);
        Assert.Equal(604800, token.ExpiresIn);
        Assert.Equal("Bearer", token.TokenType);

        factory.Social.Grant = factory.Social.Grant with { AccessToken = "new-access", RefreshToken = "rotated-refresh" };
        using var refreshed = await client.PostAsJsonAsync("/v1/auth/discord/social/refresh", new DiscordSocialRefreshRequest(token.RefreshTicket));
        Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
        Assert.True(refreshed.Headers.CacheControl!.NoStore);
        Assert.Equal("private-provider-refresh", factory.Social.RefreshedToken);
        var rotated = (await refreshed.Content.ReadFromJsonAsync<DiscordSocialTokenResponse>())!;
        Assert.Equal("new-access", rotated.AccessToken);
        Assert.NotEqual(token.RefreshTicket, rotated.RefreshTicket);
        using var rotatedRefresh = await client.PostAsJsonAsync("/v1/auth/discord/social/refresh", new DiscordSocialRefreshRequest(rotated.RefreshTicket));
        Assert.Equal(HttpStatusCode.OK, rotatedRefresh.StatusCode);
        Assert.Equal("rotated-refresh", factory.Social.RefreshedToken);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscordAccountMismatchCannotExposeTokens(bool refresh)
    {
        await using var factory = new IdentityFactory();
        using var client = factory.BrowserClient();
        var session = await LoginFlow.CompleteAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionToken);
        string? refreshTicket = null;
        if (refresh)
        {
            using var initial = await client.PostAsJsonAsync("/v1/auth/discord/social/exchange", ValidExchange);
            refreshTicket = (await initial.Content.ReadFromJsonAsync<DiscordSocialTokenResponse>())!.RefreshTicket;
        }
        factory.Social.Grant = factory.Social.Grant with { DiscordUserId = "999999999" };
        using var response = refresh
            ? await client.PostAsJsonAsync("/v1/auth/discord/social/refresh", new DiscordSocialRefreshRequest(refreshTicket))
            : await client.PostAsJsonAsync("/v1/auth/discord/social/exchange", ValidExchange);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("discord_account_mismatch", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.DoesNotContain("social-access-token", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task RefreshTicketIsBoundToOriginalSessionAndRejectsTampering()
    {
        await using var factory = new IdentityFactory();
        using var client = factory.BrowserClient();
        var first = await LoginFlow.CompleteAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", first.SessionToken);
        using var initial = await client.PostAsJsonAsync("/v1/auth/discord/social/exchange", ValidExchange);
        var token = (await initial.Content.ReadFromJsonAsync<DiscordSocialTokenResponse>())!;
        using var tampered = await client.PostAsJsonAsync("/v1/auth/discord/social/refresh", new DiscordSocialRefreshRequest("tampered" + token.RefreshTicket));
        Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
        var second = await LoginFlow.CompleteAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", second.SessionToken);
        using var wrongSession = await client.PostAsJsonAsync("/v1/auth/discord/social/refresh", new DiscordSocialRefreshRequest(token.RefreshTicket));
        Assert.Equal(HttpStatusCode.BadRequest, wrongSession.StatusCode);
        Assert.Equal(1, factory.Social.Calls);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("revoked")]
    public async Task UnauthenticatedRequestsCannotReachDiscord(string condition)
    {
        await using var factory = new IdentityFactory();
        using var client = factory.BrowserClient();
        if (condition != "missing")
        {
            var session = await LoginFlow.CompleteAsync(client);
            client.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionToken);
            if (condition == "expired") factory.Clock.Advance(TimeSpan.FromHours(8));
            else await factory.Store.RevokeSessionAsync(IdentityService.Hash(session.SessionToken), CancellationToken.None);
        }
        using var configuration = await client.GetAsync("/v1/auth/discord/social/config");
        using var exchange = await client.PostAsJsonAsync("/v1/auth/discord/social/exchange", ValidExchange);
        using var refresh = await client.PostAsJsonAsync("/v1/auth/discord/social/refresh", new DiscordSocialRefreshRequest("ticket"));
        Assert.Equal(HttpStatusCode.Unauthorized, configuration.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, exchange.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal(0, factory.Social.Calls);
    }

    [Fact]
    public async Task SessionRevokedDuringDiscordRequestCannotReceiveToken()
    {
        await using var factory = new IdentityFactory();
        using var client = factory.BrowserClient();
        var session = await LoginFlow.CompleteAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionToken);
        factory.Social.BeforeReturn = () => factory.Store.RevokeSessionAsync(IdentityService.Hash(session.SessionToken), CancellationToken.None);
        using var response = await client.PostAsJsonAsync("/v1/auth/discord/social/exchange", ValidExchange);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("social-access-token", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("discord_authorization_rejected", 400)]
    [InlineData("discord_social_scope_required", 403)]
    [InlineData("invalid_discord_response", 502)]
    [InlineData("discord_unavailable", 503)]
    public async Task ProviderFailureReturnsOnlyStableErrorCode(string code, int status)
    {
        await using var factory = new IdentityFactory();
        using var client = factory.BrowserClient();
        var session = await LoginFlow.CompleteAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionToken);
        factory.Social.Failure = new DiscordSocialException(code, status);
        using var response = await client.PostAsJsonAsync("/v1/auth/discord/social/exchange", ValidExchange);
        Assert.Equal(status, (int)response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(code, error.GetProperty("code").GetString());
        Assert.Single(error.EnumerateObject());
    }

    [Theory]
    [InlineData("https://attacker.example/callback")]
    [InlineData("http://localhost/callback")]
    [InlineData("http://127.0.0.1/callback?next=anything")]
    [InlineData("http://user@127.0.0.1/callback")]
    [InlineData("http://127.0.0.1/other")]
    public async Task NonSdkRedirectCannotReachDiscord(string redirectUri)
    {
        await using var factory = new IdentityFactory();
        using var client = factory.BrowserClient();
        var session = await LoginFlow.CompleteAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionToken);
        using var response = await client.PostAsJsonAsync("/v1/auth/discord/social/exchange", ValidExchange with { RedirectUri = redirectUri });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Social.Calls);
    }

    [Fact]
    public async Task InvalidPkceOrUnexpectedFieldsCannotReachDiscord()
    {
        await using var factory = new IdentityFactory();
        using var client = factory.BrowserClient();
        var session = await LoginFlow.CompleteAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionToken);
        foreach (var verifier in new[] { "short", new string('v', 129), new string('!', 43), new string('v', 43) + "\n" })
        {
            using var response = await client.PostAsJsonAsync("/v1/auth/discord/social/exchange", ValidExchange with { CodeVerifier = verifier });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var injected = await client.PostAsJsonAsync("/v1/auth/discord/social/exchange", new
        {
            ValidExchange.Code, ValidExchange.CodeVerifier, ValidExchange.RedirectUri, accountId = "other-account"
        });
        Assert.Equal(HttpStatusCode.BadRequest, injected.StatusCode);
        Assert.Equal(0, factory.Social.Calls);
    }

    [Fact]
    public async Task PlainHttpCannotReturnConfigurationOrTokensEvenInDevelopment()
    {
        await using var factory = new IdentityFactory();
        using var client = factory.BrowserClient();
        var session = await LoginFlow.CompleteAsync(client);
        client.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionToken);
        using var response = await client.GetAsync("http://localhost:7141/v1/auth/discord/social/config");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("https_required", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(0, factory.Social.Calls);
    }
}
