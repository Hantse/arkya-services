using System.Net;
using System.Net.Http.Json;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class RealmManualRuntimeUpdateTests
{
    private const string PreviousImage = "ghcr.io/hantse/arkya-mmorpg-dedicated@sha256:cbb0cb0dd74d8da65024e0e2beb760747e356734217533fd5d36d706f0de4b1f";
    private const string NewImage = "ghcr.io/hantse/arkya-mmorpg-dedicated@sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Actor = "admin:operator";

    private static DeploymentProfile Profile() => new()
    {
        Id = "nova-native", DisplayName = "Nova native", Image = PreviousImage, ImagePullSecretName = "arkya-ghcr",
        TopologyTemplateServerId = "island", TopologyPublicEndpointTemplate = "wss://api.arkya.online/game/{realmId}/{serverId}/",
        Servers = [new() { Id = "island", Port = 17943, Transport = "WebSocket", PublicEndpoint = "wss://api.arkya.online/game/nova/island/",
            ProtocolVersion = 3, BuildId = "arkya-multi-area-v1" }]
    };
    private static readonly RealmTopology Topology = new([new("starter-island", 4, 1536, 1536, 0, 0, 2, 2)]);

    private static (RealmRecord Realm, DeploymentOptions Options) Configuration()
    {
        var previous = Profile();
        var next = Profile();
        next.Id = "nova-native-v5"; next.Image = NewImage; next.TopologyServerIdPrefix = "v5";
        next.Servers[0].ProtocolVersion = 5; next.Servers[0].BuildId = "arkya-town-npcs-v1";
        var realm = Fixtures.Realm(RealmStatus.Ready) with
            { RealmId = "nova", Profile = RealmTopologyBuilder.Snapshot(previous, "nova", Topology), Revision = 2 };
        var options = Fixtures.Options();
        options.Profiles = [previous, next];
        options.RuntimeReleases = [new() { RealmId = "nova", ProfileId = next.Id, Version = 1, Automatic = false,
            PreviousProfileSha256 = RealmRuntimeReleases.Fingerprint(realm.Profile), Topology = Topology,
            GameVersion = "0.1.49", SourceRevision = new string('a', 40), SourceRunId = "123456789" }];
        return (realm, options);
    }

    private static RealmService Service(MemoryRealmRepository repository, DeploymentOptions options) => new(repository, Options.Create(options), new TestClock());

    [Fact]
    public void HistoricalNovaFingerprintAndGeneratedIdsRemainUnchangedWithoutPrefix()
    {
        var snapshot = RealmTopologyBuilder.Snapshot(Profile(), "nova", Topology);
        Assert.Equal("c652962aa8072e2680ed4406fbcc3b3a6b373743560aa89ff50ef558f4ac1781", RealmRuntimeReleases.Fingerprint(snapshot));
        Assert.Equal(Enumerable.Range(1, 4).Select(index => $"island-starter-island-{index:00}"), snapshot.Servers.Select(server => server.Id));
    }

    [Fact]
    public void ProcessGenerationChangesIdsAndEndpointsWhileKeepingAreaGridBoundsAndCredentials()
    {
        var (realm, options) = Configuration();
        var profile = RealmTopologyBuilder.Snapshot(options.Profiles[1], "nova", Topology);
        Assert.True(options.IsValid());
        Assert.True(RealmRuntimeReleases.CompatibleTransition(realm.Profile, profile));
        Assert.Equal(Enumerable.Range(1, 4).Select(index => $"v5-starter-island-{index:00}"), profile.Servers.Select(server => server.Id));
        Assert.All(profile.Servers, server => Assert.Equal("starter-island", server.IslandId));
        Assert.Equal(realm.Profile.Servers.Select(server => (server.Bounds, server.PartitionIndex, server.PartitionCount)),
            profile.Servers.Select(server => (server.Bounds, server.PartitionIndex, server.PartitionCount)));
        Assert.Equal(realm.Profile.EnvironmentSecretName, profile.EnvironmentSecretName);
        Assert.Equal(realm.Profile.ImagePullSecretName, profile.ImagePullSecretName);
        Assert.All(profile.Servers, server => Assert.EndsWith("/nova/" + server.Id + "/", server.PublicEndpoint));
    }

    [Theory]
    [InlineData("")]
    [InlineData("-v5")]
    [InlineData("v5-")]
    [InlineData("verylong")]
    [InlineData("../v5")]
    [InlineData("V5")]
    public void InvalidPrefixesAreRejectedByConfiguration(string prefix)
    {
        var (_, options) = Configuration();
        options.Profiles[1].TopologyServerIdPrefix = prefix;
        Assert.False(options.IsValid());
    }

    [Fact]
    public void ApprovedBuildMetadataIsOptionalAndRejectsInvalidSourceReferences()
    {
        var (_, options) = Configuration();
        var release = options.RuntimeReleases[0];
        release.GameVersion = null; release.SourceRevision = null; release.SourceRunId = null;
        Assert.True(options.IsValid());
        release.GameVersion = "0.1.49\n";
        Assert.False(options.IsValid());
        release.GameVersion = "0.1.49"; release.SourceRevision = "../commit";
        Assert.False(options.IsValid());
        release.SourceRevision = new string('a', 40); release.SourceRunId = "run/123";
        Assert.False(options.IsValid());
    }

    [Fact]
    public async Task ApprovedManualMigrationIsOfferedAndAuditedThroughExistingUpdateContract()
    {
        var (realm, options) = Configuration();
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var service = Service(repository, options);
        var available = service.GetOperationsOptions(realm);
        Assert.True(available.CanUpdate);
        Assert.Equal("RuntimeMigration", available.UpdateKind);
        Assert.Equal(3, available.CurrentProtocolVersion);
        Assert.Equal(5, available.TargetProtocolVersion);
        Assert.Equal("arkya-town-npcs-v1", available.TargetBuildId);
        Assert.Equal("0.1.49", available.TargetGameVersion);
        Assert.Equal(options.RuntimeReleases[0].SourceRevision, available.TargetSourceRevision);
        Assert.Equal(options.RuntimeReleases[0].SourceRunId, available.TargetSourceRunId);
        var request = new UpdateRealmRequest(realm.Revision, "Install compatible Nova runtime", NewImage);
        var pending = (await service.RequestUpdateAsync("nova", request, Actor, default)).Realm!;
        Assert.Equal(RealmStatus.Pending, pending.Status);
        Assert.Equal(realm.Revision + 1, pending.Revision);
        Assert.Equal(realm.DeploymentIdentity, pending.DeploymentIdentity);
        Assert.Equal(realm.TargetId, pending.TargetId);
        Assert.Equal(realm.Namespace, pending.Namespace);
        Assert.Equal(realm.RuntimeConfiguration, pending.RuntimeConfiguration);
        Assert.Equal(realm.Profile, pending.RuntimeRelease!.PreviousProfile);
        Assert.All(pending.Servers, server => Assert.Equal(0, server.ReadyReplicas));
        Assert.True(RealmRuntimeReleases.Authorizes(options, pending));
        var operation = Assert.Single(pending.Operations);
        Assert.Equal("RuntimeMigration", operation.Kind);
        Assert.Equal(Actor, operation.RequestedBy);
        Assert.Equal(request.Reason, operation.Reason);
        Assert.Equal(pending, (await service.RequestUpdateAsync("nova", request, Actor, default)).Realm);
        Assert.Equal("revision_conflict", (await service.RequestUpdateAsync("nova", request with { Reason = "Changed request" }, Actor, default)).Error);
    }

    [Theory]
    [InlineData("fingerprint")]
    [InlineData("secret")]
    [InlineData("overlap")]
    [InlineData("image")]
    [InlineData("target")]
    [InlineData("protocol")]
    [InlineData("not-ready")]
    public async Task UnsafeManualMigrationsCannotBeRequested(string change)
    {
        var (realm, options) = Configuration();
        switch (change)
        {
            case "fingerprint": options.RuntimeReleases[0].PreviousProfileSha256 = new string('f', 64); break;
            case "secret": options.Profiles[1].EnvironmentSecretName = "different-secret"; break;
            case "overlap": options.Profiles[1].TopologyServerIdPrefix = null; break;
            case "image": options.Profiles[1].Image = PreviousImage; break;
            case "target": options.Targets[0].RealmId = "another-realm"; break;
            case "protocol": options.Profiles[1].Servers[0].ProtocolVersion = 3; break;
            case "not-ready": realm = realm with { Status = RealmStatus.Deploying }; break;
        }
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var service = Service(repository, options);
        Assert.False(service.GetOperationsOptions(realm).CanUpdate);
        Assert.Null((await service.RequestUpdateAsync("nova", new(realm.Revision, "Do not bypass approval", NewImage), Actor, default)).Realm);
        Assert.Equal(realm, await repository.FindAsync("nova", default));
    }

    [Fact]
    public async Task AutomaticIntentRemainsWorkerOwnedAndMissingOrChangedApprovalsCannotBeAppliedManually()
    {
        var (realm, options) = Configuration();
        options.RuntimeReleases[0].Automatic = true;
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        Assert.False(Service(repository, options).GetOperationsOptions(realm).CanUpdate);
        options.RuntimeReleases[0].Automatic = false;
        Assert.Equal("approved_image_changed", (await Service(repository, options).RequestUpdateAsync("nova",
            new(realm.Revision, "Stale image confirmation", PreviousImage), Actor, default)).Error);
        Assert.Equal("revision_conflict", (await Service(repository, options).RequestUpdateAsync("nova",
            new(realm.Revision - 1, "Stale revision confirmation", NewImage), Actor, default)).Error);
        options.RuntimeReleases = [];
        var unavailable = Service(repository, options).GetOperationsOptions(realm);
        Assert.False(unavailable.CanUpdate);
        Assert.Equal(3, unavailable.CurrentProtocolVersion);
        Assert.Equal("no_approved_update", unavailable.UnavailableReason);
    }

    [Fact]
    public async Task FailedManualTransitionRetriesOnlyTheSameApprovedRuntime()
    {
        var (realm, options) = Configuration();
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var service = Service(repository, options);
        var pending = (await service.RequestUpdateAsync("nova", new(realm.Revision, "Initial runtime upgrade", NewImage), Actor, default)).Realm!;
        var failed = pending with { Status = RealmStatus.Failed, Version = pending.Version + 1 };
        await repository.TryReplaceAsync(failed, pending.Version, default);
        Assert.True(service.GetOperationsOptions(failed).CanUpdate);
        var retried = (await service.RequestUpdateAsync("nova", new(failed.Revision, "Retry approved runtime", NewImage), Actor, default)).Realm!;
        Assert.Same(failed.RuntimeRelease, retried.RuntimeRelease);
        Assert.Equal(failed.Revision + 1, retried.Revision);
        Assert.Equal(2, retried.Operations.Length);
        Assert.Equal(realm.Profile, retried.RuntimeRelease!.PreviousProfile);
        options.RuntimeReleases[0].PreviousProfileSha256 = new string('f', 64);
        Assert.Null(RealmRuntimeReleases.ManualApproval(options, retried with { Status = RealmStatus.Failed }));
    }

    [Fact]
    public async Task ConcurrentManualRequestsCreateOnlyOneTransitionAndAuditEntry()
    {
        var (realm, options) = Configuration();
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var service = Service(repository, options);
        var request = new UpdateRealmRequest(realm.Revision, "Same operator retry", NewImage);
        var replies = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.RequestUpdateAsync("nova", request, Actor, default)));
        Assert.All(replies, reply => Assert.NotNull(reply.Realm));
        Assert.Single((await repository.FindAsync("nova", default))!.Operations);
    }

    [Fact]
    public async Task HttpUpdateKeepsAdminActorAuthorizationAndReturnsTheDurableRuntimeMigration()
    {
        var (realm, options) = Configuration();
        await using var baseFactory = new RealmFactory();
        await baseFactory.Repository.TryCreateAsync(realm, default);
        await using var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
        {
            services.RemoveAll<IOptions<DeploymentOptions>>();
            services.AddSingleton(Options.Create(options));
        }));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        const string path = "/admin/realms/nova";
        var request = new UpdateRealmRequest(realm.Revision, "Approved runtime via admin", NewImage);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(path + "/update", request)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", RealmFactory.AdminKey);
        var offered = (await client.GetFromJsonAsync<RealmOperationsOptions>(path + "/operations-options"))!;
        Assert.Equal("RuntimeMigration", offered.UpdateKind);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(path + "/update", request)).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Actor", Actor);
        var response = await client.PostAsJsonAsync(path + "/update", request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var pending = (await response.Content.ReadFromJsonAsync<RealmSummary>())!;
        Assert.Equal("RuntimeMigration", Assert.Single(pending.Operations).Kind);
        Assert.All(pending.Servers, server => Assert.StartsWith("v5-", server.Id));
    }
}
