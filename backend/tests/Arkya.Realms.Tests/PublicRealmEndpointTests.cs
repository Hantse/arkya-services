using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;

namespace Arkya.Realms.Tests;

public sealed class PublicRealmEndpointTests
{
    [Fact]
    public async Task AnonymousCatalogAndDetailExposeOnlyPublicSummaryAndHideDrafts()
    {
        using var factory = new RealmFactory();
        var now = new TestClock().Now;
        var ready = Fixtures.Realm(RealmStatus.Ready) with
        {
            LastObservedAt = now,
            LastError = "private-error-detail",
            Servers = [new("area-west", 1, "192.0.2.42")],
            Operations = [new("Restart", 1, 0, "private-admin", "private-reason", now, now,
                RealmStatus.Ready, null, Fixtures.Image, Fixtures.Image)]
        };
        Assert.True(await factory.Repository.TryCreateAsync(ready, default));
        Assert.True(await factory.Repository.TryCreateAsync(Fixtures.Realm(RealmStatus.Draft) with
            { RealmId = "draft-one", TargetId = "draft-target", Namespace = "private-draft" }, default));
        using var client = factory.Client(key: null);

        var response = await client.GetAsync("/v1/public/realms");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var summary = Assert.Single((await response.Content.ReadFromJsonAsync<PublicRealmSummary[]>())!);
        Assert.Equal("eu-one", summary.RealmId);
        Assert.Equal("Europe one", summary.DisplayName);
        Assert.Equal(RealmStatus.Ready, summary.Status);
        Assert.Equal(1, summary.ReadyServerCount);
        Assert.Equal(1, summary.ServerCount);
        Assert.Equal(now, summary.LastObservedAt);
        Assert.Null(summary.PlayerCount);

        var detail = await client.GetAsync("/v1/public/realms/eu-one");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(summary, await detail.Content.ReadFromJsonAsync<PublicRealmSummary>());
        using var document = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(["displayName", "lastObservedAt", "playerCount", "readyServerCount", "realmId", "serverCount", "status", "updatedAt"],
            document.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/public/realms/draft-one")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/public/realms/unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/public/realms/INVALID")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/realms")).StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-31)]
    [InlineData(1)]
    public async Task StaleMissingAndFutureObservationsNeverReportReady(int? seconds)
    {
        using var factory = new RealmFactory();
        var observed = seconds is { } value ? new TestClock().Now.AddSeconds(value) : (DateTimeOffset?)null;
        Assert.True(await factory.Repository.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready) with
            { LastObservedAt = observed, Servers = [new("area-west", 1, "192.0.2.42")] }, default));
        using var client = factory.Client(key: null);
        var summary = (await client.GetFromJsonAsync<PublicRealmSummary>("/v1/public/realms/eu-one"))!;
        Assert.Equal("Unknown", summary.Status);
        Assert.Equal(0, summary.ReadyServerCount);
        Assert.Null(summary.PlayerCount);
        Assert.Equal(seconds is > 0 ? null : observed, summary.LastObservedAt);
    }

    [Fact]
    public async Task FreshCountsRequireKnownServerReadinessAndCurrentImageAndRestart()
    {
        using var factory = new RealmFactory();
        var realm = Fixtures.Realm(RealmStatus.Deploying) with
        {
            LastObservedAt = new TestClock().Now.AddSeconds(-30),
            Profile = Fixtures.Realm().Profile with
            {
                Servers = [new("server-one", 17843, []), new("server-two", 17844, []),
                    new("server-three", 17845, []), new("server-four", 17846, [])]
            },
            ImageRelease = new(1, "previous-image", Fixtures.Image),
            Restart = new(1, 0),
            Servers = [new("server-one", 1, "192.0.2.1") { ReadyImage = Fixtures.Image, ReadyRestartRevision = 1 },
                new("server-two", 1, null) { ReadyImage = Fixtures.Image, ReadyRestartRevision = 1 },
                new("server-three", 1, "192.0.2.3") { ReadyImage = "previous-image", ReadyRestartRevision = 1 },
                new("server-four", 1, "192.0.2.4") { ReadyImage = Fixtures.Image, ReadyRestartRevision = 0 },
                new("unknown-server", 1, "192.0.2.5") { ReadyImage = Fixtures.Image, ReadyRestartRevision = 1 }]
        };
        Assert.True(await factory.Repository.TryCreateAsync(realm, default));
        using var client = factory.Client(key: null);
        var summary = (await client.GetFromJsonAsync<PublicRealmSummary>("/v1/public/realms/eu-one"))!;
        Assert.Equal(RealmStatus.Deploying, summary.Status);
        Assert.Equal(1, summary.ReadyServerCount);
        Assert.Equal(4, summary.ServerCount);
    }

    [Fact]
    public async Task CatalogReflectsNewObservationsAndOrdersRealmsWithoutExposingDrafts()
    {
        using var factory = new RealmFactory();
        var failed = Fixtures.Realm(RealmStatus.Failed);
        Assert.True(await factory.Repository.TryCreateAsync(failed, default));
        Assert.True(await factory.Repository.TryCreateAsync(Fixtures.Realm(RealmStatus.Pending) with
            { RealmId = "alpha-one", TargetId = "alpha-target", Namespace = "alpha-namespace" }, default));
        using var client = factory.Client(key: null);
        var first = (await client.GetFromJsonAsync<PublicRealmSummary[]>("/v1/public/realms"))!;
        Assert.Equal(["alpha-one", "eu-one"], first.Select(realm => realm.RealmId));
        Assert.Equal(RealmStatus.Pending, first[0].Status);
        Assert.Equal(RealmStatus.Failed, first[1].Status);
        Assert.True(await factory.Repository.TryReplaceAsync(failed with
        {
            Status = RealmStatus.Ready, Version = failed.Version + 1,
            LastObservedAt = new TestClock().Now, Servers = [new("area-west", 1, "192.0.2.42")]
        }, failed.Version, default));
        var next = (await client.GetFromJsonAsync<PublicRealmSummary>("/v1/public/realms/eu-one"))!;
        Assert.Equal(RealmStatus.Ready, next.Status);
        Assert.Equal(1, next.ReadyServerCount);
    }

    [Fact]
    public async Task InvalidAndReservedLegacyIdsStayPrivateWithoutBreakingValidPublicCatalog()
    {
        using var factory = new RealmFactory();
        Assert.True(await factory.Repository.TryCreateAsync(Fixtures.Realm(RealmStatus.Pending), default));
        foreach (var id in new[] { "api", "admin", "www", "backoffice", "beta-game", "INVALID" })
            Assert.True(await factory.Repository.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready) with
                { RealmId = id, TargetId = "target-" + id, Namespace = "namespace-" + id }, default));
        using var client = factory.Client(key: null);
        var catalog = (await client.GetFromJsonAsync<PublicRealmSummary[]>("/v1/public/realms"))!;
        Assert.Equal("eu-one", Assert.Single(catalog).RealmId);
        foreach (var id in new[] { "api", "admin", "www", "backoffice", "beta-game", "INVALID" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/v1/public/realms/" + id)).StatusCode);
        Assert.Equal(7, (await factory.Repository.ListAsync(default)).Count);
    }

    [Fact]
    public async Task OnlyPublicReadsOfferAnonymousCorsAndNeverAllowCredentials()
    {
        using var factory = new RealmFactory();
        using var client = factory.Client(key: null);
        client.DefaultRequestHeaders.Add("Origin", "https://beta-eu-one.arkya.online");
        var catalog = await client.GetAsync("/v1/public/realms");
        Assert.Equal("*", Assert.Single(catalog.Headers.GetValues("Access-Control-Allow-Origin")));
        Assert.False(catalog.Headers.Contains("Access-Control-Allow-Credentials"));
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/v1/public/realms");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        var allowed = await client.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Equal("GET", Assert.Single(allowed.Headers.GetValues("Access-Control-Allow-Methods")));
        var admin = await client.GetAsync("/admin/realms");
        Assert.False(admin.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.Equal(HttpStatusCode.MethodNotAllowed,
            (await client.PostAsJsonAsync("/v1/public/realms", new { })).StatusCode);
    }
}
