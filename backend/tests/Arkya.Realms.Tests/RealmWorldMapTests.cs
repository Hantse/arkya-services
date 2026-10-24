using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.Realms.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Arkya.Realms.Tests;

public sealed class RealmWorldMapTests
{
    private const string RealmId = "eu-one";
    private const string ServerId = "area-west";
    private static readonly string Instance = Guid.NewGuid().ToString("D");
    private static RealmWorldMapEntity Player() => new(Guid.NewGuid().ToString("D"), "player", "Ariane", 12, 3, 24, 1);
    private static RealmWorldMapReport Report(params RealmWorldMapEntity[] entities) => new(Instance, 1, entities, TotalEntities: entities.Length);

    private static async Task<(RealmWorldMapService Service, MemoryRealmRepository Realms, MemoryRealmWorldMapRepository Store, TestClock Clock)> Fixture()
    {
        var realms = new MemoryRealmRepository();
        await realms.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready), default);
        var store = new MemoryRealmWorldMapRepository();
        var clock = new TestClock();
        return (new(realms, store, clock), realms, store, clock);
    }

    [Fact]
    public async Task SnapshotsReplacePositionsAndEmptyReportDiffersFromUnsupportedRuntime()
    {
        var (service, _, store, clock) = await Fixture();
        var missing = Assert.Single((await service.GetAsync(RealmId, default))!.Servers);
        Assert.Equal("unsupported", missing.Status);
        Assert.Null(missing.UpdatedAt);
        var player = Player();
        Assert.Null(await service.ReportAsync(RealmId, ServerId, Report(player, new("master", "npc", "Master", 3, 0, 4)), default));
        var first = Assert.Single((await service.GetAsync(RealmId, default))!.Servers);
        Assert.Equal("live", first.Status);
        Assert.Equal(clock.Now, first.UpdatedAt);
        Assert.Equal(2, first.TotalEntities);
        Assert.Equal(2, first.Entities.Length);
        Assert.Equal(Instance, Assert.Single(await store.FindAsync(RealmId, [ServerId], default)).InstanceId);
        clock.Now += TimeSpan.FromSeconds(2);
        Assert.Null(await service.ReportAsync(RealmId, ServerId, Report(player with { X = 42, AuthorityEpoch = 2 }), default));
        Assert.Equal(42, Assert.Single(Assert.Single((await service.GetAsync(RealmId, default))!.Servers).Entities).X);
        Assert.Null(await service.ReportAsync(RealmId, ServerId, Report(), default));
        var empty = Assert.Single((await service.GetAsync(RealmId, default))!.Servers);
        Assert.Equal("live", empty.Status);
        Assert.Empty(empty.Entities);
        Assert.Equal(0, empty.TotalEntities);
        Assert.False(empty.Truncated);
    }

    [Fact]
    public async Task ExpiredAndFutureObservationsNeverExposeOldPositions()
    {
        var (service, _, _, clock) = await Fixture();
        await service.ReportAsync(RealmId, ServerId, Report(Player()), default);
        clock.Now += TimeSpan.FromSeconds(10);
        Assert.Equal("live", Assert.Single((await service.GetAsync(RealmId, default))!.Servers).Status);
        clock.Now += TimeSpan.FromMilliseconds(1);
        var stale = Assert.Single((await service.GetAsync(RealmId, default))!.Servers);
        Assert.Equal("stale", stale.Status);
        Assert.Empty(stale.Entities);
        Assert.Equal(0, stale.TotalEntities);
        clock.Now -= TimeSpan.FromSeconds(11);
        Assert.Equal("stale", Assert.Single((await service.GetAsync(RealmId, default))!.Servers).Status);
    }

    [Theory]
    [InlineData(RealmStatus.Pending)]
    [InlineData(RealmStatus.Deploying)]
    [InlineData(RealmStatus.Failed)]
    [InlineData(RealmStatus.Deleting)]
    [InlineData(RealmStatus.DeleteFailed)]
    public async Task UnavailableRealmStatesHidePositions(string status)
    {
        var (service, realms, _, _) = await Fixture();
        await service.ReportAsync(RealmId, ServerId, Report(Player()), default);
        var realm = (await realms.FindAsync(RealmId, default))!;
        await realms.TryReplaceAsync(realm with { Status = status, Version = 2 }, 1, default);
        var unavailable = Assert.Single((await service.GetAsync(RealmId, default))!.Servers);
        Assert.Equal("unavailable", unavailable.Status);
        Assert.Empty(unavailable.Entities);
    }

    [Fact]
    public async Task RevisionAndRealmBindingPreventOldOrOtherRealmLocations()
    {
        var (service, realms, _, _) = await Fixture();
        await service.ReportAsync(RealmId, ServerId, Report(Player()), default);
        var realm = (await realms.FindAsync(RealmId, default))!;
        await realms.TryCreateAsync(realm with { RealmId = "eu-two", TargetId = "realm-eu-two", Namespace = "arkya-realm-eu-two" }, default);
        Assert.Equal("unsupported", Assert.Single((await service.GetAsync("eu-two", default))!.Servers).Status);
        await realms.TryReplaceAsync(realm with { Revision = 2, Version = 2 }, 1, default);
        var stale = Assert.Single((await service.GetAsync(RealmId, default))!.Servers);
        Assert.Equal("stale", stale.Status);
        Assert.Empty(stale.Entities);
        await service.ReportAsync(RealmId, ServerId, Report(), default);
        Assert.Equal("live", Assert.Single((await service.GetAsync(RealmId, default))!.Servers).Status);
        Assert.Null(await service.GetAsync("absent", default));
        Assert.Equal("invalid_server", await service.ReportAsync(RealmId, "other", Report(), default));
        Assert.Equal("realm_not_found", await service.ReportAsync("absent", ServerId, Report(), default));
        var current = (await realms.FindAsync(RealmId, default))!;
        await realms.TryReplaceAsync(current with { Status = RealmStatus.Deleted, Version = 3 }, 2, default);
        Assert.Null(await service.GetAsync(RealmId, default));
        Assert.Equal("realm_not_found", await service.ReportAsync(RealmId, ServerId, Report(), default));
    }

    [Fact]
    public async Task ReportValidationBoundsVersionIdentityCountsLabelsAndCoordinates()
    {
        var (service, _, _, _) = await Fixture();
        var player = Player();
        var invalid = new[]
        {
            Report(player) with { Version = 2 }, Report(player) with { InstanceId = Guid.Empty.ToString("D") },
            Report(player) with { InstanceId = "invalid" }, Report(player) with { Entities = null! },
            Report(player) with { TotalEntities = 0 }, Report(player) with { Truncated = true },
            Report(player) with { TotalEntities = 2 }, Report(player, player),
            Report(player with { Id = "invalid" }), Report(player with { AuthorityEpoch = 0 }),
            Report(player with { Kind = "Player" }), Report(player with { Kind = "object" }),
            Report(player with { Name = "" }), Report(player with { Name = new string('a', 129) }),
            Report(player with { Name = "bad\nname" }), Report(player with { X = double.NaN }),
            Report(player with { Y = double.PositiveInfinity }), Report(player with { Z = 11_000_001 }),
            Report(new RealmWorldMapEntity("master", "npc", "Master", 0, 0, 0, 1)),
            Report(new RealmWorldMapEntity(" master", "npc", "Master", 0, 0, 0)), Report([null!]),
            Report(Enumerable.Range(0, RealmWorldMapService.MaxEntities + 1).Select(index => new RealmWorldMapEntity(index.ToString(), "npc", "Master", 0, 0, 0)).ToArray())
        };
        foreach (var report in invalid)
            Assert.Equal("invalid_world_map", await service.ReportAsync(RealmId, ServerId, report, default));
        Assert.Null(await service.ReportAsync(RealmId, ServerId, Report(player, new("master", "npc", "Master", 3, 0, 4),
            new("master", "creature", "Wolf", 5, 0, 6)) with { TotalEntities = 8, Truncated = true }, default));
        var partial = Assert.Single((await service.GetAsync(RealmId, default))!.Servers);
        Assert.True(partial.Truncated);
        Assert.Equal(8, partial.TotalEntities);
        Assert.Equal(3, partial.Entities.Length);
    }

    [Fact]
    public async Task CompleteAreaBoundsAllowHandoffAcrossPartitionButRejectUnrelatedCoordinates()
    {
        var (service, realms, _, _) = await Fixture();
        var realm = (await realms.FindAsync(RealmId, default))!;
        var server = realm.Profile.Servers[0] with { IslandId = "starter", Bounds = new(0, 0, 500, 1000) };
        await realms.TryReplaceAsync(realm with { Profile = realm.Profile with
        {
            Servers = [server], Topology = new([new("starter", 2)])
        }, Version = 2 }, 1, default);
        Assert.Null(await service.ReportAsync(RealmId, ServerId, Report(Player() with { X = 600 }), default));
        Assert.Null(await service.ReportAsync(RealmId, ServerId, Report(Player() with { X = -20 }), default));
        Assert.Equal("invalid_world_map", await service.ReportAsync(RealmId, ServerId, Report(Player() with { X = 2000 }), default));
        Assert.Equal("starter", Assert.Single((await service.GetAsync(RealmId, default))!.Servers).IslandId);
    }

    [Fact]
    public async Task HttpOnlyDedicatedKeyCanSubmitAndOnlyAdminCanRead()
    {
        await using var factory = new WorldMapFactory();
        await factory.Realms.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready), default);
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        const string read = "/admin/realms/eu-one/world-map";
        const string write = "/internal/realms/eu-one/servers/area-west/world-map";
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(read)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "player-session");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(write, Report())).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Service-Key", RealmFactory.AdminKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(write, Report())).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Admin-Key", RealmFactory.AdminKey);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(read)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(write, Report())).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", RealmRuntimeKeys.DeriveDedicatedKey(WorldMapFactory.Seed, RealmId, ServerId));
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(write, Report(Player()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/internal/realms/eu-two/servers/area-west/world-map", Report())).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/internal/realms/eu-one/servers/area-east/world-map", Report())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(write, Report() with { Version = 99 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(write, new { InstanceId = Instance })).StatusCode);
        var json = await client.GetStringAsync(read);
        Assert.DoesNotContain(Instance, json);
        Assert.DoesNotContain(WorldMapFactory.Seed, json);
        client.DefaultRequestHeaders.Remove("X-Arkya-Admin-Key");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(read)).StatusCode);
    }

    [Fact]
    public async Task TelemetryBodyLimitIsLargerButScopedAndStillBounded()
    {
        await using var factory = new WorldMapFactory();
        await factory.Realms.TryCreateAsync(Fixtures.Realm(RealmStatus.Ready), default);
        using var client = factory.CreateClient(new() { BaseAddress = new("https://localhost") });
        client.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", RealmRuntimeKeys.DeriveDedicatedKey(WorldMapFactory.Seed, RealmId, ServerId));
        var entities = Enumerable.Range(0, RealmWorldMapService.MaxEntities).Select(index =>
            new RealmWorldMapEntity(index.ToString(), "npc", new string('a', 128), 0, 0, 0)).ToArray();
        const string path = "/internal/realms/eu-one/servers/area-west/world-map";
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(path, Report(entities))).StatusCode);
        var accepted = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var excessive = new StringContent(new string('a', (int)RealmWorldMapService.MaxRequestBodyBytes + 1), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync(path, excessive)).StatusCode);
        using var ordinary = new StringContent(new string('a', 32769), Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, (await client.PostAsync("/internal/realms/eu-one/servers/area-west/commands/poll", ordinary)).StatusCode);
    }

    [Fact]
    public void SnapshotDocumentRoundTripsWithoutLeakingInternalMetadata()
    {
        var snapshot = new RealmWorldMapSnapshot(RealmId, ServerId, 4, Instance, new TestClock().Now, false, 1, [Player()]);
        var document = new RealmWorldMapDocument { Id = RealmId + "/" + ServerId, ObservedAtUtc = snapshot.UpdatedAt.UtcDateTime, State = snapshot };
        var restored = BsonSerializer.Deserialize<RealmWorldMapDocument>(document.ToBson());
        Assert.Equal(document.Id, restored.Id);
        Assert.Equal(snapshot.UpdatedAt, restored.State.UpdatedAt);
        Assert.Equal(snapshot.InstanceId, restored.State.InstanceId);
        Assert.Equal(snapshot.RealmRevision, restored.State.RealmRevision);
        Assert.Equal(snapshot.Entities, restored.State.Entities);
    }

    [MongoFact]
    public async Task MongoKeepsOnlyLatestSnapshotPerRealmAndServer()
    {
        var database = "arkya_world_map_test_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        try
        {
            var store = new MongoRealmWorldMapRepository(client, Options.Create(new MongoOptions { DatabaseName = database }));
            var initial = new RealmWorldMapSnapshot(RealmId, ServerId, 1, Instance, new TestClock().Now, false, 1, [Player()]);
            await store.ReplaceAsync(initial, default);
            var latest = initial with { UpdatedAt = initial.UpdatedAt.AddSeconds(2), Entities = [], TotalEntities = 0 };
            await store.ReplaceAsync(latest, default);
            await store.ReplaceAsync(initial, default);
            var restored = Assert.Single(await store.FindAsync(RealmId, [ServerId], default));
            Assert.Equal(latest.UpdatedAt, restored.UpdatedAt);
            Assert.Empty(restored.Entities);
            Assert.Empty(await store.FindAsync("eu-two", [ServerId], default));
            Assert.Equal(1, await client.GetDatabase(database).GetCollection<BsonDocument>("realm_world_maps").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        }
        finally { await client.DropDatabaseAsync(database); }
    }
}

internal sealed class MemoryRealmWorldMapRepository : IRealmWorldMapRepository
{
    private readonly Dictionary<string, RealmWorldMapSnapshot> snapshots = [];
    private readonly object gate = new();
    public Task<RealmWorldMapSnapshot[]> FindAsync(string realmId, string[] serverIds, CancellationToken ct)
    {
        lock (gate) return Task.FromResult(snapshots.Values.Where(value => value.RealmId == realmId && serverIds.Contains(value.ServerId)).ToArray());
    }
    public Task ReplaceAsync(RealmWorldMapSnapshot snapshot, CancellationToken ct)
    {
        lock (gate)
        {
            var id = snapshot.RealmId + "/" + snapshot.ServerId;
            if (!snapshots.TryGetValue(id, out var previous) || previous.UpdatedAt <= snapshot.UpdatedAt) snapshots[id] = snapshot;
            return Task.CompletedTask;
        }
    }
}

internal sealed class WorldMapFactory : WebApplicationFactory<Arkya.Realms.Api.Program>
{
    public const string Seed = "dedicated-world-map-test-seed-1234567890";
    public MemoryRealmRepository Realms { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RealmsAdmin:Key"] = RealmFactory.AdminKey,
            ["Mongo:DatabaseName"] = "arkya_world_map_http_tests",
            ["RealmRuntime:DedicatedKeySeed"] = Seed,
            ["RealmRuntime:CharactersBaseUrl"] = "https://characters.example/"
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IRealmRepository>();
            services.AddSingleton<IRealmRepository>(Realms);
            services.RemoveAll<IRealmCommandRepository>();
            services.AddSingleton<IRealmCommandRepository, MemoryRealmCommandRepository>();
            services.RemoveAll<IRealmWorldMapRepository>();
            services.AddSingleton<IRealmWorldMapRepository, MemoryRealmWorldMapRepository>();
            services.Remove(services.Single(descriptor => descriptor.ImplementationType == typeof(RealmDeploymentWorker)));
            services.RemoveAll<IOptions<DeploymentOptions>>();
            services.AddSingleton(Options.Create(Fixtures.Options()));
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider, TestClock>();
        });
    }
}
