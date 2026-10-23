using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Arkya.Realms.Tests;

public sealed class RealmIslandGridTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static RealmIslandTopology Grid(int columns = 2, int rows = 3) =>
        new("starter-island", columns * rows, 1200, 600, -300, 50, columns, rows);

    [Fact]
    public void ExplicitGridOverridesAspectRatioAndAssignsColumnsBeforeRows()
    {
        var island = Grid();
        var expected = new AreaBounds[]
        {
            new(-300, 50, 300, 250), new(300, 50, 900, 250),
            new(-300, 250, 300, 450), new(300, 250, 900, 450),
            new(-300, 450, 300, 650), new(300, 450, 900, 650)
        };
        Assert.Equal(expected, IslandPartitioning.Create(island));
        Assert.NotEqual(expected[0], IslandPartitioning.Create(island with { GridColumns = null, GridRows = null })[0]);
        Assert.Equal(1, IslandPartitioning.FindPartition(island, 300, 50));
        Assert.Equal(2, IslandPartitioning.FindPartition(island, -300, 250));
        Assert.Equal(3, IslandPartitioning.FindPartition(island, 300, 250));
        Assert.Equal(5, IslandPartitioning.FindPartition(island, 900, 650));
        Assert.True(IslandPartitioning.AreNeighbors(expected[0], expected[1]));
        Assert.True(IslandPartitioning.AreNeighbors(expected[0], expected[2]));
        Assert.False(IslandPartitioning.AreNeighbors(expected[0], expected[3]));
    }

    [Fact]
    public void EverySupportedGridCoversTheIslandWithUniqueOwnersIncludingFractionalEdges()
    {
        for (var columns = 1; columns <= 64; columns++)
        for (var rows = 1; columns * rows <= 64; rows++)
        {
            var island = Grid(columns, rows) with { Width = 1000.1, Depth = 987.6, OriginX = 0.3, OriginZ = -0.7 };
            Assert.True(RealmTopologyBuilder.IsValid(new([island])));
            var bounds = IslandPartitioning.Create(island);
            Assert.Equal(columns * rows, bounds.Length);
            Assert.Equal(island.Width * island.Depth, bounds.Sum(cell => (cell.MaxX - cell.MinX) * (cell.MaxZ - cell.MinZ)), 5);
            for (var index = 0; index < bounds.Length; index++)
            {
                var cell = bounds[index];
                Assert.True(cell.MinX < cell.MaxX && cell.MinZ < cell.MaxZ);
                Assert.Equal(index, IslandPartitioning.FindPartition(island, cell.MinX, cell.MinZ));
                Assert.Equal(index, IslandPartitioning.FindPartition(island, (cell.MinX + cell.MaxX) / 2, (cell.MinZ + cell.MaxZ) / 2));
                foreach (var other in bounds.Skip(index + 1))
                    Assert.False(Math.Max(cell.MinX, other.MinX) < Math.Min(cell.MaxX, other.MaxX) &&
                        Math.Max(cell.MinZ, other.MinZ) < Math.Min(cell.MaxZ, other.MaxZ));
            }
            Assert.Equal(bounds.Length - 1, IslandPartitioning.FindPartition(island, island.OriginX + island.Width, island.OriginZ + island.Depth));
        }
    }

    [Theory]
    [InlineData(null, 2, 4)]
    [InlineData(2, null, 4)]
    [InlineData(0, 2, 4)]
    [InlineData(2, 0, 4)]
    [InlineData(-1, 4, 4)]
    [InlineData(4, -1, 4)]
    [InlineData(65, 1, 64)]
    [InlineData(1, 65, 64)]
    [InlineData(64, 64, 64)]
    [InlineData(int.MaxValue, int.MaxValue, 1)]
    [InlineData(2, 3, 4)]
    [InlineData(5, 13, 65)]
    public void InvalidGridPairsCannotProduceAPlan(int? columns, int? rows, int count)
    {
        var island = Grid() with { GridColumns = columns, GridRows = rows, DedicatedCount = count };
        Assert.False(IslandPartitioning.IsValid(island));
        Assert.False(RealmTopologyBuilder.IsValid(new([island])));
        Assert.Throws<ArgumentOutOfRangeException>(() => IslandPartitioning.Create(island));
    }

    [Fact]
    public void SnapshotFreezesGridAndKeepsTheExistingPartitionIds()
    {
        var topology = new RealmTopology([Grid()]);
        var snapshot = RealmTopologyBuilder.Snapshot(Fixtures.Options().Profiles[0], "eu-one", topology);
        topology.Islands[0] = Grid(3, 2);
        Assert.Equal(Grid(), Assert.Single(snapshot.Topology!.Islands));
        Assert.Equal(IslandPartitioning.Create(Grid()), snapshot.Servers.Select(server => server.Bounds));
        Assert.Equal(Enumerable.Range(1, 6).Select(slot => $"island-starter-island-{slot:D2}"), snapshot.Servers.Select(server => server.Id));
        Assert.Equal(Enumerable.Range(0, 6), snapshot.Servers.Select(server => server.PartitionIndex!.Value));
    }

    [Theory]
    [InlineData(2, null)]
    [InlineData(null, 2)]
    [InlineData(2, 3)]
    [InlineData(0, 4)]
    public async Task InvalidGridReturnsBadRequestWithoutPersisting(int? columns, int? rows)
    {
        using var factory = new RealmFactory();
        using var client = factory.Client();
        var island = new RealmIslandTopology("starter-island", 4, GridColumns: columns, GridRows: rows);
        var response = await client.PostAsJsonAsync("/admin/realms", Request(island));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_topology", (await response.Content.ReadFromJsonAsync<ErrorResponse>())!.Code);
        Assert.Empty(await factory.Repository.ListAsync(default));
    }

    [Fact]
    public async Task CreateAndDraftEditRoundTripTheGridAndRejectStaleOrientationChanges()
    {
        using var factory = new RealmFactory();
        using var client = factory.Client();
        var created = await client.PostAsJsonAsync("/admin/realms", Request(Grid()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var initial = (await created.Content.ReadFromJsonAsync<RealmSummary>())!;
        Assert.Equal(Grid(), Assert.Single(initial.Topology!.Islands));
        var edited = await client.PutAsJsonAsync("/admin/realms/eu-one/topology", new UpdateRealmTopologyRequest(0, "island-v1", new([Grid(3, 2)])));
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        var changed = (await edited.Content.ReadFromJsonAsync<RealmSummary>())!;
        Assert.Equal(1, changed.Revision);
        Assert.Equal(Grid(3, 2), Assert.Single(changed.Topology!.Islands));
        Assert.Equal(IslandPartitioning.Create(Grid(3, 2)), changed.Servers.Select(server => server.Bounds));
        var stale = await client.PutAsJsonAsync("/admin/realms/eu-one/topology", new UpdateRealmTopologyRequest(0, "island-v1", new([Grid()])));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var invalid = await client.PutAsJsonAsync("/admin/realms/eu-one/topology", new UpdateRealmTopologyRequest(1, "island-v1", new([Grid() with { GridRows = null }])));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var persisted = (await client.GetFromJsonAsync<RealmSummary>("/admin/realms/eu-one"))!;
        Assert.Equal(1, persisted.Revision);
        Assert.Equal(Grid(3, 2), Assert.Single(persisted.Topology!.Islands));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyJsonKeepsAutomaticPartitioningAndExactlyTheSameManifestPayload(bool explicitNulls)
    {
        const string oldJson = "{\"islands\":[{\"islandId\":\"starter-island\",\"dedicatedCount\":6,\"width\":1000,\"depth\":1000,\"originX\":0,\"originZ\":0}],\"instanceDedicatedCount\":0,\"raidDedicatedCount\":0}";
        var input = explicitNulls ? oldJson.Replace("\"originZ\":0", "\"originZ\":0,\"gridColumns\":null,\"gridRows\":null") : oldJson;
        var topology = JsonSerializer.Deserialize<RealmTopology>(input, JsonOptions)!;
        var island = Assert.Single(topology.Islands);
        Assert.Null(island.GridColumns);
        Assert.Null(island.GridRows);
        Assert.True(RealmTopologyBuilder.IsValid(topology));
        var cells = IslandPartitioning.Create(island);
        Assert.Equal(new AreaBounds(0, 0, 1000d / 3, 500), cells[0]);
        Assert.Equal(new AreaBounds(0, 500, 1000d / 3, 1000), cells[3]);
        Assert.Equal(oldJson, JsonSerializer.Serialize(topology, JsonOptions));
    }

    [Fact]
    public void GridPersistsInMongoWhileOlderTopologyDocumentsKeepNullDimensions()
    {
        var profile = Fixtures.Options().Profiles[0];
        var realm = Fixtures.Realm() with { Profile = RealmTopologyBuilder.Snapshot(profile, "eu-one", new([Grid()])) };
        var bson = RealmDocument.From(realm).ToBsonDocument();
        var restored = BsonSerializer.Deserialize<RealmDocument>(bson).ToRecord();
        Assert.Equal(Grid(), Assert.Single(restored.Profile.Topology!.Islands));
        Assert.Equal(realm.Profile.Servers.Select(server => server.Bounds), restored.Profile.Servers.Select(server => server.Bounds));
        var oldRealm = realm with { Profile = RealmTopologyBuilder.Snapshot(profile, "eu-one", new([Grid() with { GridColumns = null, GridRows = null }])) };
        var oldBson = RealmDocument.From(oldRealm).ToBsonDocument();
        var oldIsland = oldBson["Profile"]["Topology"]["Islands"][0].AsBsonDocument;
        oldIsland.Remove("GridColumns");
        oldIsland.Remove("GridRows");
        var oldRestored = BsonSerializer.Deserialize<RealmDocument>(oldBson).ToRecord();
        Assert.Equal(oldRealm.Profile.Topology!.Islands, oldRestored.Profile.Topology!.Islands);
        Assert.Equal(oldRealm.Profile.Servers.Select(server => server.Bounds), oldRestored.Profile.Servers.Select(server => server.Bounds));
        Assert.Equal(JsonSerializer.Serialize(oldRealm.Profile.Topology, JsonOptions), JsonSerializer.Serialize(oldRestored.Profile.Topology, JsonOptions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImageReleaseRetainsTheFrozenGridAndRejectsRepartitionedMetadata(bool explicitGrid)
    {
        var options = Fixtures.Options();
        options.Targets[0].RealmId = "eu-one";
        var profile = options.Profiles[0];
        var island = explicitGrid ? Grid() : Grid() with { GridColumns = null, GridRows = null };
        var realm = Fixtures.Realm() with { Profile = RealmTopologyBuilder.Snapshot(profile, "eu-one", new([island])) };
        profile.Image = "registry.example/arkya/dedicated@sha256:" + new string('a', 64);
        var release = new RealmImageRelease { RealmId = "eu-one", ProfileId = profile.Id, Version = 1, PreviousImage = Fixtures.Image };
        Assert.Same(profile, RealmImageReleases.AllowedProfile(options, realm, release));
        var changedIsland = explicitGrid ? Grid(3, 2) : Grid();
        var changed = realm with { Profile = realm.Profile with { Topology = new([changedIsland]) } };
        Assert.Null(RealmImageReleases.AllowedProfile(options, changed, release));
    }

    private static CreateRealmRequest Request(RealmIslandTopology island) =>
        new("eu-one", "Europe one", "realm-eu-one", "island-v1", new([island]));
}
