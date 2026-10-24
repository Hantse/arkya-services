using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Identity.Api.Application;
using Arkya.ServiceDefaults.Accounts;

namespace Arkya.Identity.Tests;

public sealed class BetaAccessTests
{
    private const string AdminKey = "identity-beta-administration-test-key-123456789";

    [Fact]
    public async Task SignupRequiresLiveSessionIsIdempotentAndNeverAcceptsIdentityOrEntitlementFromClient()
    {
        await using var factory = Configured();
        using var browser = factory.BrowserClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/v1/beta/registration")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/v1/beta/registration", new { })).StatusCode);
        var session = await LoginFlow.CompleteAsync(browser);
        browser.DefaultRequestHeaders.Authorization = new("Bearer", session.SessionToken);
        var initial = (await browser.GetFromJsonAsync<BetaRegistrationResponse>("/v1/beta/registration"))!;
        Assert.False(initial.Registered);
        Assert.Null(initial.RegisteredAt);
        Assert.False(initial.HasBetaAccess);
        foreach (var forged in new object[] { new { accountId = Guid.NewGuid() }, new { hasBetaAccess = true }, new { registeredAt = "2000-01-01" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await browser.PostAsJsonAsync("/v1/beta/registration", forged)).StatusCode);
        Assert.Null(Assert.Single(factory.Store.Accounts).BetaRegisteredAt);

        using var response = await browser.PostAsJsonAsync("/v1/beta/registration", new { });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var registered = (await response.Content.ReadFromJsonAsync<BetaRegistrationResponse>())!;
        Assert.True(registered.Registered);
        Assert.Equal(factory.Clock.GetUtcNow(), registered.RegisteredAt);
        Assert.False(registered.HasBetaAccess);
        factory.Clock.Advance(TimeSpan.FromMinutes(5));
        using var duplicate = await browser.PostAsJsonAsync("/v1/beta/registration", new { });
        Assert.Equal(registered, await duplicate.Content.ReadFromJsonAsync<BetaRegistrationResponse>());
        Assert.Equal(0, Assert.Single(factory.Store.Accounts).Revision);
        var raw = await browser.GetFromJsonAsync<JsonElement>("/v1/beta/registration");
        Assert.Equal(new[] { "registered", "registeredAt", "hasBetaAccess" }, raw.EnumerateObject().Select(x => x.Name));

        using var admin = AdminClient(factory);
        using var grant = await admin.PostAsJsonAsync($"/admin/beta/registrations/{session.AccountId}/grant", new BetaGrantRequest(0, "Beta approved"));
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        Assert.True((await browser.GetFromJsonAsync<BetaRegistrationResponse>("/v1/beta/registration"))!.HasBetaAccess);
        await factory.Store.AdministerAccountAsync(session.AccountId, 1, "ban", null, "Abuse", "admin:test", factory.Clock.GetUtcNow().UtcDateTime, default);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.GetAsync("/v1/beta/registration")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.PostAsJsonAsync("/v1/beta/registration", new { })).StatusCode);
    }

    [Fact]
    public async Task AdminSignupListIsPrivateBoundedSearchableAndGrantUsesCurrentAccountRevision()
    {
        await using var disabled = new IdentityFactory();
        using var disabledClient = disabled.BrowserClient();
        Assert.Equal(HttpStatusCode.NotFound, (await disabledClient.GetAsync("/admin/beta/registrations")).StatusCode);
        await using var factory = Configured();
        using var anonymous = factory.BrowserClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await anonymous.GetAsync("/admin/beta/registrations")).StatusCode);
        using var admin = AdminClient(factory);
        var first = await CreateApplicantAsync(factory, "111", "Alpha [player]");
        var second = await CreateApplicantAsync(factory, "222", "Beta player");
        var banned = await CreateApplicantAsync(factory, "333", "Banned player");
        await factory.Store.AdministerAccountAsync(banned.Id, 0, "ban", null, "Abuse", "admin:test", factory.Clock.GetUtcNow().UtcDateTime, default);
        var unregistered = await factory.Store.UpsertAccountAsync("444", "Not registered", factory.Clock.GetUtcNow().UtcDateTime, default);
        var page = (await admin.GetFromJsonAsync<BetaRegistrationPage>("/admin/beta/registrations?page=2&pageSize=1"))!;
        Assert.Equal(3, page.Total);
        Assert.Single(page.Items);
        Assert.Equal(first.Id, Assert.Single((await admin.GetFromJsonAsync<BetaRegistrationPage>("/admin/beta/registrations?search=%5Bplayer%5D"))!.Items).AccountId);
        Assert.Empty((await admin.GetFromJsonAsync<BetaRegistrationPage>("/admin/beta/registrations?search=.*"))!.Items);
        Assert.Equal(2, (await admin.GetFromJsonAsync<BetaRegistrationPage>("/admin/beta/registrations?status=pending"))!.Total);
        Assert.Equal(banned.Id, Assert.Single((await admin.GetFromJsonAsync<BetaRegistrationPage>("/admin/beta/registrations?status=banned"))!.Items).AccountId);
        foreach (var query in new[] { "page=0", "page=100001", "pageSize=101", "pageSize=0", "status=wrong", "search=" + new string('x', 101) })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync("/admin/beta/registrations?" + query)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"/admin/beta/registrations/{unregistered.Id}/grant", new BetaGrantRequest(0, "Approve"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/admin/beta/registrations/{banned.Id}/grant", new BetaGrantRequest(1, "Approve"))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/admin/beta/registrations/{first.Id}/grant", new BetaGrantRequest(1, "Stale"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/beta/registrations/{first.Id}/grant", new { reason = "Missing revision" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/beta/registrations/{first.Id}/grant", new BetaGrantRequest(0, "\n"))).StatusCode);
        admin.DefaultRequestHeaders.Remove("X-Arkya-Admin-Actor");
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync($"/admin/beta/registrations/{first.Id}/grant", new BetaGrantRequest(0, "Missing actor"))).StatusCode);
        admin.DefaultRequestHeaders.Add("X-Arkya-Admin-Actor", "admin:test");
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => admin.PostAsJsonAsync($"/admin/beta/registrations/{first.Id}/grant", new BetaGrantRequest(0, "Approved"))));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
            Assert.Equal(7, responses.Count(x => x.StatusCode == HttpStatusCode.Conflict));
        }
        finally { foreach (var response in responses) response.Dispose(); }
        Assert.Equal(first.Id, Assert.Single((await admin.GetFromJsonAsync<BetaRegistrationPage>("/admin/beta/registrations?status=granted"))!.Items).AccountId);
        Assert.False((await factory.Store.GetAccountAsync(second.Id, default))!.HasBetaAccess);
    }

    [Fact]
    public async Task RandomGrantsExcludeIneligibleAccountsAndReplayNeverRedrawsOrRegrantsRevokedAccess()
    {
        await using var factory = Configured();
        using var admin = AdminClient(factory);
        var first = await CreateApplicantAsync(factory, "111", "First");
        var second = await CreateApplicantAsync(factory, "222", "Second");
        var banned = await CreateApplicantAsync(factory, "333", "Banned");
        var alreadyGranted = await CreateApplicantAsync(factory, "444", "Already granted");
        var unregistered = await factory.Store.UpsertAccountAsync("555", "Unregistered", factory.Clock.GetUtcNow().UtcDateTime, default);
        await factory.Store.AdministerAccountAsync(banned.Id, 0, "ban", null, "Abuse", "admin:test", factory.Clock.GetUtcNow().UtcDateTime, default);
        await factory.Store.AdministerAccountAsync(alreadyGranted.Id, 0, "beta-access", true, "Earlier access", "admin:test", factory.Clock.GetUtcNow().UtcDateTime, default);
        var request = new BetaRandomGrantRequest(Guid.NewGuid(), 10, "First beta wave");
        using var response = await admin.PostAsJsonAsync("/admin/beta/grants/random", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<BetaRandomGrantResponse>())!;
        Assert.Equal(10, result.RequestedCount);
        Assert.Equal(2, result.GrantedCount);
        Assert.Equal(new[] { first.Id, second.Id }.Order(), result.GrantedAccountIds.Order());
        await factory.Store.AdministerAccountAsync(first.Id, 1, "beta-access", false, "Revoked", "admin:test", factory.Clock.GetUtcNow().UtcDateTime, default);
        await CreateApplicantAsync(factory, "666", "Later signup");
        using var retry = await admin.PostAsJsonAsync("/admin/beta/grants/random", request);
        var replayed = (await retry.Content.ReadFromJsonAsync<BetaRandomGrantResponse>())!;
        Assert.Equal(result.GrantedAccountIds, replayed.GrantedAccountIds);
        Assert.Equal(result.CreatedAt, replayed.CreatedAt);
        Assert.False((await factory.Store.GetAccountAsync(first.Id, default))!.HasBetaAccess);
        Assert.False((await factory.Store.GetAccountAsync(unregistered.Id, default))!.HasBetaAccess);
        Assert.False((await factory.Store.GetAccountAsync(banned.Id, default))!.HasBetaAccess);
        Assert.Equal(1, (await factory.Store.GetAccountAsync(alreadyGranted.Id, default))!.Revision);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/admin/beta/grants/random", request with { Count = 11 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/admin/beta/grants/random", request with { Reason = "Different" })).StatusCode);
    }

    [Fact]
    public async Task ConcurrentRandomRetriesGrantOnlyThePersistedDrawAndValidateInput()
    {
        await using var factory = Configured();
        using var admin = AdminClient(factory);
        for (var i = 0; i < 12; i++) await CreateApplicantAsync(factory, i.ToString(), "Player " + i);
        var request = new BetaRandomGrantRequest(Guid.NewGuid(), 3, "Concurrent wave");
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => admin.PostAsJsonAsync("/admin/beta/grants/random", request)));
        try
        {
            var outcomes = new List<BetaRandomGrantResponse>();
            foreach (var response in responses)
            {
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
                outcomes.Add((await response.Content.ReadFromJsonAsync<BetaRandomGrantResponse>())!);
            }
            Assert.All(outcomes, x => Assert.Equal(outcomes[0].GrantedAccountIds, x.GrantedAccountIds));
            Assert.Equal(3, factory.Store.Accounts.Count(x => x.HasBetaAccess));
            Assert.All(factory.Store.Accounts.Where(x => x.HasBetaAccess), x => Assert.Equal(1, x.Revision));
        }
        finally { foreach (var response in responses) response.Dispose(); }
        foreach (var invalid in new[] { request with { RequestId = Guid.Empty }, request with { Count = 0 }, request with { Count = 101 }, request with { Reason = "" } })
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/admin/beta/grants/random", invalid)).StatusCode);
        using var fill = await admin.PostAsJsonAsync("/admin/beta/grants/random", request with { RequestId = Guid.NewGuid(), Count = 100 });
        Assert.Equal(9, (await fill.Content.ReadFromJsonAsync<BetaRandomGrantResponse>())!.GrantedCount);
        using var empty = await admin.PostAsJsonAsync("/admin/beta/grants/random", request with { RequestId = Guid.NewGuid() });
        Assert.Equal(0, (await empty.Content.ReadFromJsonAsync<BetaRandomGrantResponse>())!.GrantedCount);
    }

    [Theory]
    [InlineData("https://arkya.online", true)]
    [InlineData("https://attacker.example", false)]
    public async Task RegistrationHasOnlyConfiguredWebClientCors(string origin, bool allowed)
    {
        await using var factory = Configured();
        using var browser = factory.BrowserClient();
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/v1/beta/registration");
        preflight.Headers.Add("Origin", origin);
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        using var response = await browser.SendAsync(preflight);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed) Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    private static IdentityFactory Configured() => new()
    {
        WebClientOrigins = ["https://arkya.online"],
        AdditionalConfiguration = new Dictionary<string, string?>
        {
            ["Administration:Enabled"] = "true", ["Administration:AdminKey"] = AdminKey
        }
    };

    private static HttpClient AdminClient(IdentityFactory factory)
    {
        var client = factory.BrowserClient();
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", AdminKey);
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Actor", "admin:test");
        return client;
    }

    private static async Task<Arkya.Identity.Api.Domain.Account> CreateApplicantAsync(IdentityFactory factory, string discordId, string displayName)
    {
        var account = await factory.Store.UpsertAccountAsync(discordId, displayName, factory.Clock.GetUtcNow().UtcDateTime, default);
        return (await factory.Store.RegisterBetaAsync(account.Id, factory.Clock.GetUtcNow().UtcDateTime, default))!;
    }
}
