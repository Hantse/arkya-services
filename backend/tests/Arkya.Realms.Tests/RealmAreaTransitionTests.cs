using System.Text.Json;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Arkya.Realms.Tests;

public sealed class RealmAreaTransitionTests
{
    private static readonly GameAreaTransition Transition = new("starter-to-forest", "starter-island", "forest-area",
        new(400, 200, 450, 300), new(750, 44, 250), new(0, 1));

    private static RealmTopology Plan() => new([new("starter-island", 4), new("forest-area", 2) { ContentId = "forest-content" }])
        { AreaTransitions = [Transition] };

    private static DeploymentProfile Profile(int version = GameProtocol.MultiAreaVersion, string buildId = GameProtocol.MultiAreaBuildId)
    {
        var profile = Fixtures.Options().Profiles[0];
        profile.TopologyTemplateServerId = profile.Servers[0].Id;
        profile.TopologyPublicEndpointTemplate = "wss://play.example/{realmId}/{serverId}";
        profile.Servers[0].Transport = "WebSocket";
        profile.Servers[0].PublicEndpoint = "wss://play.example/legacy";
        profile.Servers[0].ProtocolVersion = version;
        profile.Servers[0].BuildId = buildId;
        return profile;
    }

    [Theory]
    [InlineData(GameProtocol.MultiAreaVersion, GameProtocol.MultiAreaBuildId)]
    [InlineData(GameProtocol.DivingVersion, GameProtocol.DivingBuildId)]
    [InlineData(GameProtocol.NpcVitalsVersion, GameProtocol.NpcVitalsBuildId)]
    public async Task ApprovedMultiAreaPlanPublishesContentAndExplicitTransitionsWithoutConfusingLocalCoordinates(int version, string buildId)
    {
        var plan = Plan();
        var snapshot = RealmTopologyBuilder.Snapshot(Profile(version, buildId), "eu-one", plan);
        Assert.True(snapshot.TopologyRuntimeSupported);
        Assert.True(RealmTopologyBuilder.SnapshotSupportsRuntime(snapshot));
        var repository = new MemoryRealmRepository();
        await repository.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready) with { Profile = snapshot }, default);
        var entry = Assert.Single((await new GameEntryService(repository, Options.Create(new GameEntryOptions())).ListAsync(default)).Realms);
        Assert.True(GameTopology.IsValid(entry));
        Assert.Contains(new GameRealmArea("starter-island", "starter-island"), entry.Areas!);
        Assert.Contains(new GameRealmArea("forest-area", "forest-content"), entry.Areas!);
        Assert.Equal(Transition, Assert.Single(entry.AreaTransitions!));
        Assert.Equal("island-starter-island-02", GameTopology.Resolve(entry, "starter-island", 750, 250)!.ServerId);
        Assert.Equal("island-forest-area-02", GameTopology.Resolve(entry, "forest-area", 750, 250)!.ServerId);
        plan.AreaTransitions![0] = Transition with { Id = "changed-transition" };
        Assert.Equal(Transition, Assert.Single(snapshot.Topology!.AreaTransitions!));
    }

    [Fact]
    public void NewContentAndTransitionsRequireTheMultiAreaRuntimeWhileLegacySnapshotsKeepTheirJsonShape()
    {
        var legacy = RealmTopologyBuilder.Snapshot(Profile(GameProtocol.HandoffVersion, GameProtocol.CharacterStatsBuildId), "eu-one",
            new([new("starter-island", 4)]));
        Assert.True(RealmTopologyBuilder.SnapshotSupportsRuntime(legacy));
        var json = JsonSerializer.Serialize(legacy);
        Assert.DoesNotContain("ContentId", json);
        Assert.DoesNotContain("AreaTransitions", json);
        var restored = BsonSerializer.Deserialize<ProfileSnapshot>(legacy.ToBsonDocument());
        Assert.Equal(RealmRuntimeReleases.Fingerprint(legacy), RealmRuntimeReleases.Fingerprint(restored));
        var unsupported = RealmTopologyBuilder.Snapshot(Profile(GameProtocol.HandoffVersion, GameProtocol.CharacterStatsBuildId), "eu-one", Plan());
        Assert.False(unsupported.TopologyRuntimeSupported);
        Assert.False(RealmTopologyBuilder.SnapshotSupportsRuntime(unsupported with { TopologyRuntimeSupported = true }));
        var multi = RealmTopologyBuilder.Snapshot(Profile(), "eu-one", Plan());
        Assert.NotEqual(RealmRuntimeReleases.Fingerprint(legacy), RealmRuntimeReleases.Fingerprint(multi));
        var roundTrip = BsonSerializer.Deserialize<ProfileSnapshot>(multi.ToBsonDocument());
        Assert.Equal(RealmRuntimeReleases.Fingerprint(multi), RealmRuntimeReleases.Fingerprint(roundTrip));
    }

    [Fact]
    public void InvalidTransitionsCannotEnterAFrozenPlan()
    {
        var invalid = new[]
        {
            Transition with { Id = "../unsafe" },
            Transition with { SourceAreaId = "unknown-area" },
            Transition with { TargetAreaId = "unknown-area" },
            Transition with { TargetAreaId = "starter-island" },
            Transition with { SourceBounds = new(-1, 200, 450, 300) },
            Transition with { SourceBounds = new(450, 200, 450, 300) },
            Transition with { TargetPosition = new(1001, 44, 250) },
            Transition with { TargetPosition = new(float.NaN, 44, 250) },
            Transition with { TargetForward = new(0, 0) }
        };
        Assert.True(RealmTopologyBuilder.IsValid(Plan()));
        Assert.All(invalid, value => Assert.False(RealmTopologyBuilder.IsValid(Plan() with { AreaTransitions = [value] })));
        Assert.False(RealmTopologyBuilder.IsValid(Plan() with { AreaTransitions = [Transition, Transition] }));
        Assert.False(RealmTopologyBuilder.IsValid(Plan() with { Islands = [new("starter-island", 1) { ContentId = "scene/path" }] }));
    }

    [Fact]
    public void LegacyCatalogCannotAdvertiseMultiAreaContentOrTransitions()
    {
        var entry = new GameRealmEntry("eu-one", "Test", true, "island-starter-01", "wss://play.example/one",
            GameProtocol.HandoffVersion, GameProtocol.CharacterStatsBuildId)
        { TopologyVersion = "v1", Partitions = [new("island-starter-01", "wss://play.example/one", "starter-island", 0, new(0, 0, 1000, 1000))] };
        Assert.True(GameTopology.IsValid(entry));
        Assert.False(GameTopology.IsValid(entry with { Areas = [new("starter-island", "starter-island")] }));
        Assert.False(GameTopology.IsValid(entry with { AreaTransitions = [Transition] }));
    }
}
