using System.Net;
using System.Net.Http.Json;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;

namespace Arkya.Realms.Tests;

public sealed class RealmEndpointTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("bad-key")]
    [InlineData("realm-administrator-test-key-123456780")]
    public async Task AdminRoutesRejectMissingAndIncorrectKeys(string? key)
    {
        using var factory = new RealmFactory();
        using var client = factory.Client(key);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/realms")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/admin/realms/options")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/admin/realms", Request())).StatusCode);
    }

    [Fact]
    public async Task CreateDeployAndRetryPersistOneOperationAndPreventNamespaceReuse()
    {
        using var factory = new RealmFactory();
        using var client = factory.Client();
        var created = await client.PostAsJsonAsync("/admin/realms", Request());
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draft = (await created.Content.ReadFromJsonAsync<RealmSummary>())!;
        Assert.Equal(0, draft.Revision);
        Assert.Equal("Draft", draft.Status);
        var stored = (await factory.Repository.FindAsync(draft.RealmId, default))!;
        Assert.Equal(RealmRuntimeReleases.Fingerprint(stored.Profile), draft.ProfileSha256);
        var requested = await client.PostAsJsonAsync("/admin/realms/eu-one/deploy", new DeployRealmRequest(0));
        Assert.Equal(HttpStatusCode.Accepted, requested.StatusCode);
        var pending = (await requested.Content.ReadFromJsonAsync<RealmSummary>())!;
        Assert.Equal(1, pending.Revision);
        Assert.Equal("Pending", pending.Status);
        Assert.Equal(draft.ProfileSha256, pending.ProfileSha256);
        var duplicate = await client.PostAsJsonAsync("/admin/realms/eu-one/deploy", new DeployRealmRequest(0));
        Assert.Equal(HttpStatusCode.Accepted, duplicate.StatusCode);
        Assert.Equal(1, (await duplicate.Content.ReadFromJsonAsync<RealmSummary>())!.Revision);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/admin/realms/eu-one/deploy", new DeployRealmRequest(1))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/admin/realms", Request() with { RealmId = "eu-two" })).StatusCode);
        Assert.Single((await client.GetFromJsonAsync<RealmSummary[]>("/admin/realms"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/admin/realms/unknown")).StatusCode);
    }

    [Fact]
    public async Task DisabledDeploymentStillAllowsCatalogAndDrafts()
    {
        using var factory = new RealmFactory(enabled: false);
        using var client = factory.Client();
        Assert.False((await client.GetFromJsonAsync<DeploymentOptionsResponse>("/admin/realms/options"))!.DeploymentEnabled);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/admin/realms", Request())).StatusCode);
        var result = await client.PostAsJsonAsync("/admin/realms/eu-one/deploy", new DeployRealmRequest(0));
        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.Equal("deployment_disabled", (await result.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
        Assert.Equal("Draft", (await factory.Repository.FindAsync("eu-one", default))!.Status);
    }

    [Fact]
    public async Task RejectsUntrustedImagesNamespacesAndInvalidIds()
    {
        using var factory = new RealmFactory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/realms", new
        { realmId = "eu-one", displayName = "EU", targetId = "realm-eu-one", profileId = "island-v1", image = "attacker/image:latest" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/realms", Request() with { TargetId = "kube-system" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/realms", Request() with { RealmId = "../../pods" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/realms", Request() with { RealmId = "eu-one\n" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/realms/eu-one/deploy", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/admin/realms", Request() with { DisplayName = "bad\nname" })).StatusCode);
        Assert.Empty(await factory.Repository.ListAsync(default));
    }

    [Fact]
    public async Task ProductionRejectsHttpAndSpoofedForwardedProto()
    {
        using var factory = new RealmFactory(production: true);
        using var client = factory.Client(scheme: "http");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/admin/realms")).StatusCode);
        using var secure = factory.Client();
        Assert.Equal(HttpStatusCode.OK, (await secure.GetAsync("/admin/realms")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await secure.GetAsync("/health/ready")).StatusCode);
    }

    private static CreateRealmRequest Request() => new("eu-one", "Europe one", "realm-eu-one", "island-v1");
}
