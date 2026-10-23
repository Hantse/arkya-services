using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Realms.Tests;

public sealed class GameTopologyCatalogTests
{
    [Fact]
    public async Task ApprovedHandoffSnapshotKeepsIndependentAreasAndAStableTopologyVersion()
    {
        var profile = Fixtures.Options().Profiles[0];
        profile.TopologyTemplateServerId = "area-west";
        profile.TopologyPublicEndpointTemplate = "wss://play.example/realms/{realmId}/{serverId}";
        profile.Servers[0].Transport = "WebSocket";
        profile.Servers[0].PublicEndpoint = "wss://play.example/legacy";
        profile.Servers[0].ProtocolVersion = GameProtocol.HandoffVersion;
        profile.Servers[0].BuildId = GameProtocol.HandoffBuildId;
        var realm = Fixtures.Realm(RealmStatus.Ready) with
        {
            Profile = RealmTopologyBuilder.Snapshot(profile, "eu-one", new([new("starter-island", 4), new("future-island", 2)]))
        };
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(realm, default);
        var service = new GameEntryService(repository, Options.Create(new GameEntryOptions()));
        var entry = Assert.Single((await service.ListAsync(default)).Realms);
        Assert.True(entry.Ready);
        Assert.Equal(GameProtocol.HandoffVersion, entry.ProtocolVersion);
        Assert.Equal(GameProtocol.HandoffBuildId, entry.BuildId);
        Assert.Equal(realm.DeploymentIdentity, entry.TopologyVersion);
        Assert.Equal("island-starter-island-01", entry.ServerId);
        Assert.Equal(6, entry.Partitions.Length);
        var starterArea = entry.Partitions.Where(partition => partition.IslandId == "starter-island").ToArray();
        var futureArea = entry.Partitions.Where(partition => partition.IslandId == "future-island").ToArray();
        Assert.Equal(4, starterArea.Length);
        Assert.Equal(2, futureArea.Length);
        Assert.DoesNotContain(entry.Partitions, partition => partition.ServerId.StartsWith("instance-", StringComparison.Ordinal) || partition.ServerId.StartsWith("raid-", StringComparison.Ordinal));
        Assert.Equal("island-starter-island-02", GameTopology.Resolve(entry, "starter-island", 500, 250)!.ServerId);
        Assert.Equal("island-starter-island-04", GameTopology.Resolve(entry, "starter-island", 500, 500)!.ServerId);
        Assert.Equal("island-future-island-02", GameTopology.Resolve(entry, "future-island", 500, 500)!.ServerId);
        Assert.Equal("island-starter-island-03", GameTopology.Resolve(entry, "starter-island", 250, 750)!.ServerId);
        Assert.Equal("island-future-island-01", GameTopology.Resolve(entry, "future-island", 250, 750)!.ServerId);
        Assert.Equal("island-starter-island-04", GameTopology.Resolve(entry, "starter-island", 1000, 1000)!.ServerId);
        Assert.Equal("island-future-island-02", GameTopology.Resolve(entry, "future-island", 1000, 1000)!.ServerId);
        Assert.Null(GameTopology.Resolve(entry, "unknown-area", 250, 750));
        Assert.All(starterArea, left => Assert.DoesNotContain(futureArea, right => GameTopology.AreNeighbors(left, right)));
        Assert.True(GameTopology.IsValid(entry));
        await repository.TryReplaceAsync(realm with { Status = RealmStatus.Deploying, Version = realm.Version + 1 }, realm.Version, default);
        var starting = Assert.Single((await service.ListAsync(default)).Realms);
        Assert.False(starting.Ready);
        Assert.Equal(entry.TopologyVersion, starting.TopologyVersion);
        Assert.Equal(entry.Partitions, starting.Partitions);
    }

    [Fact]
    public void StandaloneTopologyRejectsLegacyWireProtocolAndAliasedDestinations()
    {
        var entry = new StandaloneGameEntry
        {
            RealmId = "test-realm", DisplayName = "Test", ServerId = "area-west", Endpoint = "ws://127.0.0.1:17900/",
            TopologyVersion = "test-layout-v1",
            Partitions = [new("area-west", "ws://127.0.0.1:17900/", "starter-island", 0, new(0, 0, 500, 1000)),
                new("area-east", "ws://127.0.0.1:17901/", "starter-island", 1, new(500, 0, 1000, 1000))]
        };
        Assert.False(entry.IsValid(true));
        entry.ProtocolVersion = GameProtocol.HandoffVersion;
        entry.BuildId = GameProtocol.HandoffBuildId;
        Assert.True(entry.IsValid(true));
        Assert.False(entry.IsValid(false));
        entry.Partitions[1] = entry.Partitions[1] with { Endpoint = "ws://127.0.0.1:17900/shared/../" };
        Assert.False(entry.IsValid(true));
    }
}
