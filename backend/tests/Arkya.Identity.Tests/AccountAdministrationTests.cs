using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Contracts;
using Arkya.ServiceDefaults.Accounts;
using Microsoft.AspNetCore.Http;

namespace Arkya.Identity.Tests;

public sealed class AccountAdministrationTests
{
    private const string AdminKey = "identity-administration-test-key-123456789";

    [Fact]
    public async Task AccountAdministrationIsDisabledByDefaultAndRejectsUntrustedKeys()
    {
        await using var disabled = new IdentityFactory();
        using var disabledClient = disabled.BrowserClient();
        Assert.Equal(HttpStatusCode.NotFound, (await disabledClient.GetAsync("/admin/accounts")).StatusCode);

        await using var factory = Configured();
        using var client = factory.BrowserClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/accounts")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", IdentityFactory.ServiceKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/accounts")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Arkya-Admin-Key");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Arkya-Admin-Key", new[] { AdminKey, AdminKey });
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/accounts")).StatusCode);
    }

    [Fact]
    public async Task AccountsCanBeSearchedPagedAndReadWithoutExposingSessionSecrets()
    {
        await using var factory = Configured();
        var first = await factory.Store.UpsertAccountAsync("111", "Alpha player", factory.Clock.GetUtcNow().UtcDateTime, default);
        await factory.Store.UpsertAccountAsync("222", "Beta player", factory.Clock.GetUtcNow().UtcDateTime, default);
        using var client = AdminClient(factory);
        var page = (await client.GetFromJsonAsync<AdminAccountPage>("/admin/accounts?page=2&pageSize=1"))!;
        Assert.Equal(2, page.Total);
        Assert.Equal(2, page.Page);
        Assert.Equal("Beta player", Assert.Single(page.Items).DisplayName);
        var matches = (await client.GetFromJsonAsync<AdminAccountPage>("/admin/accounts?search=Alpha"))!;
        Assert.Equal(first.Id, Assert.Single(matches.Items).AccountId);
        Assert.Equal(first.Id, Assert.Single((await client.GetFromJsonAsync<AdminAccountPage>("/admin/accounts?search=111"))!.Items).AccountId);
        var account = (await client.GetFromJsonAsync<AdminAccountSummary>($"/admin/accounts/{first.Id}"))!;
        Assert.False(account.HasBetaAccess);
        Assert.False(account.IsBanned);
        Assert.Equal(0, account.Revision);
        Assert.Equal(factory.Clock.GetUtcNow(), account.CreatedAt);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/accounts/{Guid.NewGuid()}")).StatusCode);
        foreach (var query in new[] { "page=0", "pageSize=101", "page=-1", "page=100001", "search=" + new string('x', 101) })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/admin/accounts?" + query)).StatusCode);
        using var raw = await client.GetAsync($"/admin/accounts/{first.Id}");
        var json = await raw.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(json.TryGetProperty("sessionVersion", out _));
        Assert.False(json.TryGetProperty("sessionToken", out _));
    }

    [Fact]
    public async Task BetaEntitlementIsReadLiveForBothIntrospectionPaths()
    {
        await using var factory = Configured();
        using var browser = factory.BrowserClient();
        var session = await LoginFlow.CompleteAsync(browser);
        using var admin = AdminClient(factory);
        browser.DefaultRequestHeaders.Add("X-Arkya-Service-Key", IdentityFactory.ServiceKey);

        async Task AssertEntitlement(bool expected)
        {
            using var tokenResponse = await browser.PostAsJsonAsync("/internal/sessions/introspect", new { sessionToken = session.SessionToken });
            using var referenceResponse = await browser.PostAsJsonAsync("/internal/sessions/introspect-reference", new { sessionReference = IdentityService.Hash(session.SessionToken) });
            Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
            Assert.Equal(HttpStatusCode.OK, referenceResponse.StatusCode);
            Assert.Equal(expected, (await tokenResponse.Content.ReadFromJsonAsync<AccountSessionResponse>())!.HasBetaAccess);
            Assert.Equal(expected, (await referenceResponse.Content.ReadFromJsonAsync<AccountSessionResponse>())!.HasBetaAccess);
        }

        await AssertEntitlement(false);
        using var grant = await admin.PostAsJsonAsync($"/admin/accounts/{session.AccountId}/beta-access", new AccountBetaAccessRequest(0, true, "Approved beta application"));
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        Assert.Equal(1, (await grant.Content.ReadFromJsonAsync<AdminAccountSummary>())!.Revision);
        await AssertEntitlement(true);
        using var revoke = await admin.PostAsJsonAsync($"/admin/accounts/{session.AccountId}/beta-access", new AccountBetaAccessRequest(1, false, "Beta access revoked"));
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        await AssertEntitlement(false);
    }

    [Fact]
    public async Task BanRejectsSessionsAndLoginAndUnbanNeverRevivesPriorTokens()
    {
        await using var factory = Configured();
        using var browser = factory.BrowserClient();
        var first = await LoginFlow.CompleteAsync(browser);
        var second = await LoginFlow.CompleteAsync(browser);
        var racedSession = factory.Store.Sessions.First();
        using var admin = AdminClient(factory);
        using var ban = await admin.PostAsJsonAsync($"/admin/accounts/{first.AccountId}/ban", new AccountBanRequest(0, "Repeated abuse"));
        Assert.Equal(HttpStatusCode.OK, ban.StatusCode);
        var banned = (await ban.Content.ReadFromJsonAsync<AdminAccountSummary>())!;
        Assert.True(banned.IsBanned);
        Assert.Equal("Repeated abuse", banned.BanReason);
        Assert.Empty(factory.Store.Sessions);
        browser.DefaultRequestHeaders.Add("X-Arkya-Service-Key", IdentityFactory.ServiceKey);
        foreach (var token in new[] { first.SessionToken, second.SessionToken })
        {
            browser.DefaultRequestHeaders.Authorization = new("Bearer", token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/v1/auth/me")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/internal/sessions/introspect", new { sessionToken = token })).StatusCode);
        }
        var deniedLogin = await LoginFlow.StartAsync(browser);
        Assert.Equal(HttpStatusCode.BadRequest, (await browser.GetAsync(deniedLogin.CallbackUrl())).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await LoginFlow.ExchangeAsync(browser, deniedLogin)).StatusCode);

        using var unban = await admin.PostAsJsonAsync($"/admin/accounts/{first.AccountId}/unban", new AccountUnbanRequest(1, "Appeal accepted"));
        Assert.Equal(HttpStatusCode.OK, unban.StatusCode);
        var unbanned = (await unban.Content.ReadFromJsonAsync<AdminAccountSummary>())!;
        Assert.False(unbanned.IsBanned);
        Assert.Null(unbanned.BanReason);
        // Simulate an insertion that raced the ban's cleanup; the durable epoch still rejects it.
        await factory.Store.CreateSessionAsync(racedSession, default);
        browser.DefaultRequestHeaders.Authorization = new("Bearer", first.SessionToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/v1/auth/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/internal/sessions/introspect-reference",
            new { sessionReference = racedSession.TokenHash })).StatusCode);
        Assert.Equal(first.AccountId, (await LoginFlow.CompleteAsync(browser)).AccountId);
    }

    [Fact]
    public async Task BanInvalidatesAlreadyCompletedLoginChallengesAfterUnban()
    {
        await using var factory = Configured();
        using var browser = factory.BrowserClient();
        var session = await LoginFlow.CompleteAsync(browser);
        var pendingExchange = await LoginFlow.StartAsync(browser);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync(pendingExchange.CallbackUrl())).StatusCode);
        using var admin = AdminClient(factory);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/accounts/{session.AccountId}/ban", new AccountBanRequest(0, "Ban"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"/admin/accounts/{session.AccountId}/unban", new AccountUnbanRequest(1, "Unban"))).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await LoginFlow.ExchangeAsync(browser, pendingExchange)).StatusCode);
        Assert.Empty(factory.Store.Sessions);
    }

    [Fact]
    public async Task BanDuringDiscordSocialExchangeCannotReleaseSocialTokensAfterUnban()
    {
        await using var factory = Configured();
        using var browser = factory.BrowserClient();
        var session = await LoginFlow.CompleteAsync(browser);
        var racedSession = Assert.Single(factory.Store.Sessions);
        browser.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionToken);
        factory.Social.BeforeReturn = async () =>
        {
            await factory.Store.AdministerAccountAsync(session.AccountId, 0, "ban", null, "Confirmed abuse", "admin:test", factory.Clock.GetUtcNow().UtcDateTime, default);
            await factory.Store.AdministerAccountAsync(session.AccountId, 1, "unban", null, "Appeal", "admin:test", factory.Clock.GetUtcNow().UtcDateTime, default);
            await factory.Store.CreateSessionAsync(racedSession, default);
        };
        using var response = await browser.PostAsJsonAsync("/v1/auth/discord/social/exchange", new
        {
            code = "social-authorization-code", codeVerifier = new string('v', 43), redirectUri = "http://127.0.0.1/callback"
        });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain("social-access-token", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ConcurrentChangesRequireRevisionAndReasonAndAnAuditableActor()
    {
        await using var factory = Configured();
        var account = await factory.Store.UpsertAccountAsync("111", "Player", factory.Clock.GetUtcNow().UtcDateTime, default);
        using var admin = AdminClient(factory);
        foreach (var reason in new[] { "", "   ", "Invalid\nreason", new string('x', 501) })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/accounts/{account.Id}/ban", new AccountBanRequest(0, reason))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/accounts/{account.Id}/ban", new { reason = "Missing revision" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/accounts/{account.Id}/beta-access", new { expectedRevision = 0, reason = "Missing entitlement" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/accounts/{account.Id}/ban", new { expectedRevision = 0, reason = "Ban", isBanned = false })).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-Arkya-Admin-Actor");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/accounts/{account.Id}/ban", new AccountBanRequest(0, "Missing actor"))).StatusCode);
        admin.DefaultRequestHeaders.Add("X-Arkya-Admin-Actor", "admin:discord:123456789");
        var concurrent = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => admin.PostAsJsonAsync($"/admin/accounts/{account.Id}/beta-access", new AccountBetaAccessRequest(0, true, "Access approved"))));
        try
        {
            Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Equal(7, concurrent.Count(response => response.StatusCode == HttpStatusCode.Conflict));
        }
        finally { foreach (var response in concurrent) response.Dispose(); }
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/admin/accounts/{Guid.NewGuid()}/ban", new AccountBanRequest(0, "Unknown account"))).StatusCode);
    }

    [Theory]
    [InlineData("10.42.1.9", "/admin/accounts", true)]
    [InlineData("203.0.113.9", "/admin/accounts", false)]
    [InlineData("10.42.1.9", "/admin/beta/registrations", true)]
    [InlineData("203.0.113.9", "/admin/beta/registrations", false)]
    public async Task ClusterHttpAdministrationRequiresExplicitOptInAndAllowlistedDirectPeer(string peer, string path, bool allowed)
    {
        await using var factory = Configured(production: true, clusterHttp: true);
        var response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost");
            context.Request.Method = "GET";
            context.Request.Path = path;
            context.Request.Headers["X-Forwarded-For"] = "10.42.1.9";
            context.Request.Headers["X-Forwarded-Proto"] = "https";
            context.Request.Headers["X-Arkya-Admin-Key"] = AdminKey;
        });
        Assert.Equal(allowed ? 200 : 400, response.Response.StatusCode);
    }

    [Fact]
    public async Task ClusterHttpAdministrationRemainsDisabledWithoutExplicitOptIn()
    {
        await using var factory = Configured(production: true);
        var response = await factory.Server.SendAsync(context =>
        {
            context.Connection.RemoteIpAddress = IPAddress.Parse("10.42.1.9");
            context.Request.Scheme = "http";
            context.Request.Host = new HostString("localhost");
            context.Request.Method = "GET";
            context.Request.Path = "/admin/accounts";
            context.Request.Headers["X-Arkya-Admin-Key"] = AdminKey;
        });
        Assert.Equal(400, response.Response.StatusCode);
    }

    private static IdentityFactory Configured(bool production = false, bool clusterHttp = false) => new()
    {
        EnvironmentName = production ? "Production" : "Development",
        AdditionalConfiguration = new Dictionary<string, string?>
        {
            ["Administration:Enabled"] = "true", ["Administration:AdminKey"] = AdminKey,
            ["Administration:AllowClusterHttp"] = clusterHttp.ToString(), ["InternalHttp:AllowedNetworks:0"] = "10.42.0.0/16"
        }
    };

    private static HttpClient AdminClient(IdentityFactory factory)
    {
        var client = factory.BrowserClient();
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", AdminKey);
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Actor", "admin:discord:123456789");
        return client;
    }
}
