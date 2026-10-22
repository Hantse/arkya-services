using System.Net;
using System.Net.Http.Json;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Arkya.Realms.Tests;

public sealed class RealmTopologyTests
{
    private const string NextImage = "registry.example/arkya/dedicated@sha256:abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789";

    [Fact]
    public void ExpandsDistinctDedicatedOwnersForEveryIslandAndPool()
    {
        var profile = Fixtures.Options().Profiles[0];
        profile.TopologyTemplateServerId = "area-west";
        var input = new RealmTopology([new("starter-island", 4), new("future-island", 2)], 2, 1);
        var snapshot = RealmTopologyBuilder.Snapshot(profile, "eu-one", input);
        Assert.True(snapshot.TopologyRuntimeSupported);
        Assert.Equal(9, snapshot.Servers.Length);
        Assert.Equal(9, snapshot.Servers.Select(server => server.Id).Distinct().Count());
        Assert.All(snapshot.Servers, server => Assert.True(DeploymentOptions.ValidId(server.Id)));
        var starter = snapshot.Servers.Where(server => server.IslandId == "starter-island").ToArray();
        Assert.Equal(["island-starter-island-01", "island-starter-island-02", "island-starter-island-03", "island-starter-island-04"], starter.Select(server => server.Id));
        Assert.Equal([0, 1, 2, 3], starter.Select(server => server.PartitionIndex!.Value));
        Assert.All(starter, server => { Assert.Equal(DedicatedKind.Island, server.Kind); Assert.Equal(4, server.PartitionCount); });
        Assert.Equal(new AreaBounds(0, 0, 500, 500), starter[0].Bounds);
        Assert.Equal(new AreaBounds(500, 500, 1000, 1000), starter[3].Bounds);
        Assert.Equal(2, snapshot.Servers.Count(server => server.Kind == DedicatedKind.Instance && server.IslandId is null && server.Bounds is null));
        Assert.Single(snapshot.Servers, server => server.Kind == DedicatedKind.Raid && server.IslandId is null);
        Assert.All(snapshot.Servers, server => Assert.Equal(profile.Servers[0].Port, server.Port));
        input.Islands[0] = new("changed-island", 1);
        profile.Servers[0].Args[0] = "changed";
        Assert.Contains(snapshot.Topology!.Islands, island => island.IslandId == "starter-island");
        Assert.All(snapshot.Servers, server => Assert.Equal("-batchmode", server.Args[0]));
    }

    [Fact]
    public void ExpansionIsStableAcrossRequestOrderAndSeparatesOrigins()
    {
        var profile = Fixtures.Options().Profiles[0];
        var first = new RealmTopology([new("starter-island", 4, 2000, 1000, -500, 100), new("future-island", 1)], 1, 1);
        var second = first with { Islands = first.Islands.Reverse().ToArray() };
        var left = RealmTopologyBuilder.Snapshot(profile, "eu-one", first);
        var right = RealmTopologyBuilder.Snapshot(profile, "eu-one", second);
        Assert.Equal(left.Topology!.Islands, right.Topology!.Islands);
        Assert.Equal(left.Servers.Select(server => (server.Id, server.Bounds)), right.Servers.Select(server => (server.Id, server.Bounds)));
        Assert.Contains(left.Servers, server => server.IslandId == "starter-island" && server.Bounds!.MinX == -500 && server.Bounds.MinZ == 100);
    }

    [Fact]
    public void RejectsDuplicateIslandsOversizedOrEmptyPlansAndUnsafeIds()
    {
        Assert.False(RealmTopologyBuilder.IsValid(new([new("starter-island", 1), new("starter-island", 1)])));
        Assert.False(RealmTopologyBuilder.IsValid(new([new("../unsafe", 4)])));
        Assert.False(RealmTopologyBuilder.IsValid(new([new("abcdefghijklmnopqrstu", 1)])));
        Assert.False(RealmTopologyBuilder.IsValid(new([new("starter-island", 0)])));
        Assert.False(RealmTopologyBuilder.IsValid(new([new("starter-island", 65)])));
        Assert.False(RealmTopologyBuilder.IsValid(new([], -1, 0)));
        Assert.False(RealmTopologyBuilder.IsValid(new([], 0, 65)));
        Assert.False(RealmTopologyBuilder.IsValid(new([])));
        Assert.False(RealmTopologyBuilder.IsValid(new(null!)));
        Assert.False(RealmTopologyBuilder.IsValid(new([null!])));
        Assert.False(RealmTopologyBuilder.IsValid(new([new("starter-island", 64), new("island-two", 64), new("island-three", 64)], 64, 1)));
        Assert.False(RealmTopologyBuilder.IsValid(new(Enumerable.Range(0, 33).Select(index => new RealmIslandTopology($"island-{index:D2}", 1)).ToArray())));
        Assert.False(RealmTopologyBuilder.IsValid(new([new("starter-island", 1, double.NaN)])));
        Assert.True(RealmTopologyBuilder.IsValid(new([new("starter-island", 64), new("island-two", 64)], 64, 64)));
        Assert.True(RealmTopologyBuilder.IsValid(new([], 1, 1)));
    }

    [Fact]
    public void WebSocketTopologyRequiresExplicitUniqueEndpointTemplate()
    {
        var profile = Fixtures.Options().Profiles[0];
        profile.Servers[0].Transport = "WebSocket";
        profile.Servers[0].PublicEndpoint = "wss://play.example/realms/eu-one/starter-island";
        profile.TopologyTemplateServerId = "area-west";
        Assert.False(profile.IsValid());
        profile.TopologyPublicEndpointTemplate = "wss://play.example/realms/{realmId}/{serverId}";
        Assert.True(profile.IsValid());
        var servers = RealmTopologyBuilder.Snapshot(profile, "eu-one", new([new("starter-island", 4)], 1, 1)).Servers;
        Assert.Equal(servers.Length, servers.Select(server => server.PublicEndpoint).Distinct().Count());
        Assert.All(servers, server => Assert.Equal($"wss://play.example/realms/eu-one/{server.Id}", server.PublicEndpoint));
        profile.TopologyPublicEndpointTemplate = "wss://play.example/{unknown}/{serverId}";
        Assert.False(profile.IsValid());
        profile.TopologyPublicEndpointTemplate = "wss://play.example/static";
        Assert.False(profile.IsValid());
        profile.TopologyPublicEndpointTemplate = "https://play.example/{serverId}";
        Assert.False(profile.IsValid());
        profile.TopologyTemplateServerId = "unknown-server";
        Assert.False(profile.IsValid());
    }

    [Fact]
    public void WebSocketTemplatesCannotCollapseDestinationsThroughUriNormalizationOrExceedTransportPathBudget()
    {
        var profile = Fixtures.Options().Profiles[0];
        profile.Servers[0].Transport = "WebSocket";
        profile.Servers[0].PublicEndpoint = "wss://play.example/realms/eu-one/starter-island";
        profile.TopologyTemplateServerId = "area-west";
        profile.TopologyPublicEndpointTemplate = "wss://play.example/{serverId}/../shared";
        Assert.False(profile.IsValid());
        profile.TopologyPublicEndpointTemplate = "wss://play.example/" + new string('a', 90) + "/{realmId}/{serverId}";
        Assert.False(profile.IsValid());
        profile.TopologyPublicEndpointTemplate = "wss://play.example/realms/{realmId}/{serverId}";
        Assert.True(profile.IsValid());
        var servers = RealmTopologyBuilder.Snapshot(profile, new string('r', 31), new([new(new string('i', 20), 64)], 64, 64)).Servers;
        Assert.Equal(192, servers.Select(server => new Uri(server.PublicEndpoint!).AbsoluteUri).Distinct().Count());
        Assert.All(servers, server => Assert.True(Arkya.ServiceDefaults.GameProtocol.ValidEndpoint(server.PublicEndpoint)));
    }

    [Fact]
    public async Task OmittedGeometryUsesAThousandUnitIsland()
    {
        using var factory = new RealmFactory();
        using var client = factory.Client();
        var response = await client.PostAsJsonAsync("/admin/realms", new
        {
            realmId = "eu-one", displayName = "EU", targetId = "realm-eu-one", profileId = "island-v1",
            topology = new { islands = new[] { new { islandId = "starter-island", dedicatedCount = 4 } } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var summary = (await response.Content.ReadFromJsonAsync<RealmSummary>())!;
        Assert.Equal(1000, Assert.Single(summary.Topology!.Islands).Width);
        Assert.Equal(new AreaBounds(500, 500, 1000, 1000), summary.Servers[3].Bounds);
    }

    [Fact]
    public async Task UnsupportedProfileStillSavesThePlanButCannotDeploy()
    {
        using var factory = new RealmFactory();
        using var client = factory.Client();
        var request = new CreateRealmRequest("eu-one", "EU one", "realm-eu-one", "island-v1", new([new("starter-island", 4)], 2, 1));
        var result = await client.PostAsJsonAsync("/admin/realms", request);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        var summary = (await result.Content.ReadFromJsonAsync<RealmSummary>())!;
        Assert.Equal(7, summary.Servers.Length);
        Assert.False(summary.TopologyRuntimeSupported);
        Assert.Equal(4, Assert.Single(summary.Topology!.Islands).DedicatedCount);
        Assert.All(summary.Servers, server => Assert.Equal(0, server.DesiredReplicas));
        Assert.False(Assert.Single((await client.GetFromJsonAsync<DeploymentOptionsResponse>("/admin/realms/options"))!.Profiles).TopologySupported);
        var deployment = await client.PostAsJsonAsync("/admin/realms/eu-one/deploy", new DeployRealmRequest(0));
        Assert.Equal(HttpStatusCode.Conflict, deployment.StatusCode);
        Assert.Equal("topology_runtime_unsupported", (await deployment.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
        Assert.Equal(RealmStatus.Draft, (await factory.Repository.FindAsync("eu-one", default))!.Status);
    }

    [Fact]
    public async Task SupportedSnapshotCanBeRequestedAndLegacySnapshotsStayExact()
    {
        var options = Fixtures.Options();
        options.Profiles[0].TopologyTemplateServerId = "area-west";
        var repository = new MemoryRealmRepository();
        var service = new RealmService(repository, Options.Create(options), new TestClock());
        var created = await service.CreateAsync(new("eu-one", "EU", "realm-eu-one", "island-v1", new([new("starter-island", 4)])), default);
        Assert.True(created.Realm!.Profile.TopologyRuntimeSupported);
        Assert.True(Assert.Single(service.GetOptions().Profiles).TopologySupported);
        Assert.Equal(RealmStatus.Pending, (await service.RequestDeploymentAsync("eu-one", 0, default)).Realm!.Status);
        var legacy = RealmTopologyBuilder.Snapshot(options.Profiles[0], "legacy", null);
        Assert.Null(legacy.Topology);
        Assert.False(legacy.TopologyRuntimeSupported);
        Assert.Equal("area-west", Assert.Single(legacy.Servers).Id);
    }

    [Fact]
    public async Task InvalidTopologyReturnsBadRequestWithoutPersisting()
    {
        using var factory = new RealmFactory();
        using var client = factory.Client();
        var response = await client.PostAsJsonAsync("/admin/realms", new CreateRealmRequest("eu-one", "EU", "realm-eu-one", "island-v1", new([new("starter-island", 0)])));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_topology", (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
        Assert.Empty(await factory.Repository.ListAsync(default));
    }

    [Fact]
    public async Task DraftTopologyEditsUseRevisionAndCanReturnToLegacyBeforeDeployment()
    {
        using var factory = new RealmFactory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/admin/realms", new CreateRealmRequest(
            "eu-one", "EU", "realm-eu-one", "island-v1", new([new("starter-island", 4)])))).StatusCode);
        var edit = new UpdateRealmTopologyRequest(0, "island-v1", new([new("starter-island", 4), new("future-island", 2, 2000, 1500, 100, 200)], 1, 1));
        var changed = await client.PutAsJsonAsync("/admin/realms/eu-one/topology", edit);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        var summary = (await changed.Content.ReadFromJsonAsync<RealmSummary>())!;
        Assert.Equal(1, summary.Revision);
        Assert.Equal(8, summary.Servers.Length);
        Assert.Equal(2, summary.Topology!.Islands.Length);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync("/admin/realms/eu-one/topology", edit)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/admin/realms/eu-one/topology", new UpdateRealmTopologyRequest(1, "unknown", null))).StatusCode);
        var legacy = await client.PutAsJsonAsync("/admin/realms/eu-one/topology", new UpdateRealmTopologyRequest(1, "island-v1", null));
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        var restored = (await legacy.Content.ReadFromJsonAsync<RealmSummary>())!;
        Assert.Null(restored.Topology);
        Assert.Equal(2, restored.Revision);
        Assert.Equal("area-west", Assert.Single(restored.Servers).Id);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/admin/realms/eu-one/deploy", new DeployRealmRequest(2))).StatusCode);
        var active = await client.PutAsJsonAsync("/admin/realms/eu-one/topology", new UpdateRealmTopologyRequest(3, "island-v1", null));
        Assert.Equal(HttpStatusCode.Conflict, active.StatusCode);
        Assert.Equal("topology_edit_unavailable", (await active.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
    }

    [Fact]
    public async Task CompatibleProfileCanReplaceUnsupportedDraftButCannotChangeRunningTopology()
    {
        var options = Fixtures.Options();
        var repository = new MemoryRealmRepository();
        var service = new RealmService(repository, Options.Create(options), new TestClock());
        var topology = new RealmTopology([new("starter-island", 4)]);
        var created = (await service.CreateAsync(new("eu-one", "EU", "realm-eu-one", "island-v1", topology), default)).Realm!;
        Assert.False(created.Profile.TopologyRuntimeSupported);
        options.Profiles[0].TopologyTemplateServerId = "area-west";
        var changed = (await service.UpdateTopologyAsync("eu-one", new(0, "island-v1", topology), default)).Realm!;
        Assert.True(changed.Profile.TopologyRuntimeSupported);
        Assert.Equal(RealmStatus.Pending, (await service.RequestDeploymentAsync("eu-one", changed.Revision, default)).Realm!.Status);
        Assert.Equal("topology_edit_unavailable", (await service.UpdateTopologyAsync("eu-one", new(2, "island-v1", topology), default)).Error);
    }

    [Fact]
    public async Task ConcurrentDraftEditsPermitExactlyOneRevisionWinner()
    {
        var options = Fixtures.Options();
        var repository = new MemoryRealmRepository();
        var service = new RealmService(repository, Options.Create(options), new TestClock());
        await service.CreateAsync(new("eu-one", "EU", "realm-eu-one", "island-v1"), default);
        var changes = await Task.WhenAll(service.UpdateTopologyAsync("eu-one", new(0, "island-v1", new([new("starter-island", 4)])), default),
            service.UpdateTopologyAsync("eu-one", new(0, "island-v1", new([new("starter-island", 2)])), default));
        Assert.Single(changes, change => change.Realm is not null);
        Assert.Single(changes, change => change.Error == "revision_conflict");
        Assert.Equal(1, (await repository.FindAsync("eu-one", default))!.Revision);
    }

    [Fact]
    public void FrozenTopologyRoundTripsBsonAndLegacyDocumentsUseSafeDefaults()
    {
        var profile = Fixtures.Options().Profiles[0];
        profile.TopologyTemplateServerId = "area-west";
        var realm = Fixtures.Realm() with { Profile = RealmTopologyBuilder.Snapshot(profile, "eu-one", new([new("starter-island", 4, 1000, 1000, -200, 500)], 1, 1)) };
        var bson = RealmDocument.From(realm).ToBsonDocument();
        Assert.Equal(2, bson["SchemaVersion"].AsInt32);
        var restored = BsonSerializer.Deserialize<RealmDocument>(bson).ToRecord();
        Assert.True(restored.Profile.TopologyRuntimeSupported);
        Assert.Equal(realm.Profile.Topology!.Islands, restored.Profile.Topology!.Islands);
        Assert.Equal(realm.Profile.Servers.Select(server => (server.Id, server.Kind, server.Bounds)), restored.Profile.Servers.Select(server => (server.Id, server.Kind, server.Bounds)));
        var legacy = RealmDocument.From(Fixtures.Realm()).ToBsonDocument();
        legacy["Profile"].AsBsonDocument.Remove("Topology");
        legacy["Profile"].AsBsonDocument.Remove("TopologyRuntimeSupported");
        var server = legacy["Profile"]["Servers"].AsBsonArray[0].AsBsonDocument;
        foreach (var name in new[] { "Kind", "IslandId", "PartitionIndex", "PartitionCount", "Bounds" }) server.Remove(name);
        var old = BsonSerializer.Deserialize<RealmDocument>(legacy).ToRecord();
        Assert.Null(old.Profile.Topology);
        Assert.False(old.Profile.TopologyRuntimeSupported);
        Assert.Equal(DedicatedKind.Dedicated, Assert.Single(old.Profile.Servers).Kind);
    }

    [Fact]
    public void ImageReleasePreservesTopologyAndCannotEnableRuntimeSupportOrResizeAnIsland()
    {
        var options = Fixtures.Options();
        var profile = options.Profiles[0];
        profile.TopologyTemplateServerId = "area-west";
        options.Targets[0].RealmId = "eu-one";
        var realm = Fixtures.Realm() with { Profile = RealmTopologyBuilder.Snapshot(profile, "eu-one", new([new("starter-island", 4)])) };
        profile.Image = NextImage;
        var release = new RealmImageRelease { RealmId = "eu-one", ProfileId = profile.Id, Version = 1, PreviousImage = Fixtures.Image };
        options.ImageReleases = [release];
        Assert.Same(profile, RealmImageReleases.AllowedProfile(options, realm, release));
        var resized = realm with { Profile = realm.Profile with { Topology = new([new("starter-island", 4, 2000)]) } };
        Assert.Null(RealmImageReleases.AllowedProfile(options, resized, release));
        var unapproved = realm with { Profile = realm.Profile with { TopologyRuntimeSupported = false } };
        Assert.Null(RealmImageReleases.AllowedProfile(options, unapproved, release));
        var moved = realm with { Profile = realm.Profile with { Servers = realm.Profile.Servers.Select(server => server with { Bounds = new(1, 2, 3, 4) }).ToArray() } };
        Assert.Null(RealmImageReleases.AllowedProfile(options, moved, release));
    }
}
