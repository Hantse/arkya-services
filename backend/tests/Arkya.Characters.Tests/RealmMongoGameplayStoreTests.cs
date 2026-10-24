using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.Characters.Api.Domain;
using Arkya.Characters.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Characters.Tests;

public sealed class RealmMongoGameplayStoreTests
{
    [MongoFact]
    public async Task LeasesUseOnlyTheirDirectoryRealmAndTicketsRequireTheAuthenticatedRealm()
    {
        await using var fixture = new Fixture();
        var account = Guid.NewGuid();
        var first = Lease(Guid.NewGuid(), account, "realm-a");
        var second = Lease(Guid.NewGuid(), account, "realm-b");
        await fixture.Register(first);
        await fixture.Register(second);
        var store = new MongoGameLeaseStore(fixture.Provider);
        await store.EnsureIndexesAsync(default);

        Assert.True(await store.TryWriteAsync(first, null, default));
        Assert.True(await store.TryWriteAsync(second, null, default));
        Assert.Equal(first, await store.FindCharacterAsync(first.CharacterId, default));
        Assert.Equal(second, await store.FindCharacterAsync(second.CharacterId, default));
        Assert.Equal(first, await store.FindTicketAsync(first.TicketHash!, "realm-a", default));
        Assert.Equal(second, await store.FindTicketAsync(second.TicketHash!, "realm-b", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindTicketAsync(first.TicketHash!, default));
        await Assert.ThrowsAsync<RealmStorageUnavailableException>(() => store.FindTicketAsync(first.TicketHash!, "missing-realm", default));

        Assert.False(await store.TryWriteAsync(first with { RealmId = "realm-b", Version = 2 }, 1, default));
        Assert.False(await store.TryWriteAsync(first with { AccountId = Guid.NewGuid(), Version = 2 }, 1, default));
        Assert.False(await store.TryWriteAsync(first with { CharacterId = Guid.NewGuid() }, null, default));
        Assert.Null(await store.FindCharacterAsync(Guid.NewGuid(), default));
        Assert.Equal(0, await fixture.Count(fixture.GlobalDatabase, "game_leases"));
        Assert.Equal(1, await fixture.Count(fixture.Provider.GetRealmDatabase("realm-a"), "game_leases"));
        Assert.Equal(1, await fixture.Count(fixture.Provider.GetRealmDatabase("realm-b"), "game_leases"));

        await AssertIndexes(fixture.Provider.GetRealmDatabase("realm-a"), "game_leases", "ux_admission_ticket", "ix_account_last_connection");
        await AssertIndexes(fixture.Provider.GetRealmDatabase("realm-b"), "game_leases", "ux_admission_ticket", "ix_account_last_connection");
    }

    [MongoFact]
    public async Task RealmLeaseRoutingPreservesAtomicVersionsTransferStateAndMongoExpiryFencing()
    {
        await using var fixture = new Fixture();
        var lease = Lease(Guid.NewGuid(), Guid.NewGuid(), "realm-a");
        var transfer = new GameTransferRecord(Guid.NewGuid().ToString("D"), lease.ServerId, lease.LeaseId.ToString("D"),
            lease.AuthorityEpoch, "target", "wss://game.example/", "topology-v1", TransferStatus.Sealed,
            Guid.NewGuid().ToString("D"), lease.LeaseExpiresAt, lease.State!);
        lease = lease with { Transfer = transfer, Area = new("starter-island", "starter-island") };
        await fixture.Register(lease);
        var store = new MongoGameLeaseStore(fixture.Provider);
        await store.EnsureIndexesAsync(default);
        Assert.True(await store.TryWriteAsync(lease, null, default));
        Assert.False(await store.TryWriteAsync(lease, null, default));

        var candidates = new[]
        {
            lease with { Version = 2, State = lease.State! with { Revision = 8, Position = new(11, 32, 20) } },
            lease with { Version = 2, State = lease.State! with { Revision = 8, Position = new(12, 32, 20) } }
        };
        var results = await Task.WhenAll(candidates.Select(value => store.TryWriteAsync(value, 1, default, GameWriteCondition.TransferPending)));
        Assert.Single(results, value => value);
        Assert.Single(results, value => !value);
        var expected = candidates[Array.IndexOf(results, true)];
        var restarted = new MongoGameLeaseStore(fixture.CreateProvider());
        Assert.Equal(expected, await restarted.FindCharacterAsync(lease.CharacterId, default));

        var expired = expected with { Version = 3, LeaseExpiresAt = fixture.Now.AddMinutes(-1) };
        Assert.True(await restarted.TryWriteAsync(expired, 2, default));
        Assert.False(await restarted.TryWriteAsync(expired with { Version = 4, LeaseExpiresAt = fixture.Now.AddMinutes(5) },
            3, default, GameWriteCondition.ActiveLease));
        Assert.Equal(expired, await restarted.FindCharacterAsync(lease.CharacterId, default));
    }

    [MongoFact]
    public async Task LastConnectedUsesOwnedDirectoryEntriesAcrossRealmsAndDeterministicTies()
    {
        await using var fixture = new Fixture();
        var account = Guid.NewGuid();
        var first = Lease(Guid.Parse("00000000-0000-0000-0000-000000000001"), account, "realm-a") with { LastConnectedAt = fixture.Now };
        var second = Lease(Guid.Parse("00000000-0000-0000-0000-000000000002"), account, "realm-b") with { LastConnectedAt = fixture.Now };
        var stranger = Lease(Guid.NewGuid(), Guid.NewGuid(), "realm-b") with { TicketHash = "stranger-ticket", LastConnectedAt = fixture.Now.AddMinutes(1) };
        await fixture.Register(first);
        await fixture.Register(second);
        await fixture.Register(stranger);
        var store = new MongoGameLeaseStore(fixture.Provider);
        await store.EnsureIndexesAsync(default);
        Assert.True(await store.TryWriteAsync(first, null, default));
        Assert.True(await store.TryWriteAsync(second, null, default));
        Assert.True(await store.TryWriteAsync(stranger, null, default));
        Guid[] ids = [second.CharacterId, first.CharacterId, stranger.CharacterId];
        Assert.Equal(first, await store.FindLastConnectedAsync(account, ids, default));
        Assert.Null(await store.FindLastConnectedAsync(Guid.NewGuid(), ids, default));
        Assert.Null(await store.FindLastConnectedAsync(account, [], default));
        var newer = second with { Version = 2, LastConnectedAt = fixture.Now.AddSeconds(1) };
        Assert.True(await store.TryWriteAsync(newer, 1, default));
        Assert.Equal(newer, await store.FindLastConnectedAsync(account, ids, default));
    }

    [MongoFact]
    public async Task QuestRoutingKeepsRevisionsIsolatedAndNeverFallsBackToLegacyData()
    {
        await using var fixture = new Fixture();
        var first = Lease(Guid.NewGuid(), Guid.NewGuid(), "realm-a");
        var second = Lease(Guid.NewGuid(), first.AccountId, "realm-b");
        await fixture.Register(first);
        await fixture.Register(second);
        var catalog = Guid.NewGuid();
        var firstQuest = new CharacterQuest(first.CharacterId, "hunt-wolves", catalog, 10, 1, 1);
        var secondQuest = new CharacterQuest(second.CharacterId, "hunt-wolves", catalog, 10, 3, 1);
        var store = new MongoCharacterQuestRepository(fixture.Provider);
        await store.EnsureIndexesAsync(default);
        Assert.True(await store.TryWriteAsync(firstQuest, 0, default));
        Assert.True(await store.TryWriteAsync(secondQuest, 0, default));
        Assert.False(await store.TryWriteAsync(firstQuest, 0, default));
        var race = await Task.WhenAll(new[] { firstQuest with { Kills = 2, Revision = 2 }, firstQuest with { Kills = 4, Revision = 2 } }
            .Select(value => store.TryWriteAsync(value, 1, default)));
        Assert.Single(race, value => value);
        Assert.Single(race, value => !value);

        var restarted = new MongoCharacterQuestRepository(fixture.CreateProvider());
        var restored = Assert.Single(await restarted.ListAsync(first.CharacterId, default));
        Assert.Equal(2, restored.Revision);
        Assert.Equal(restored, await restarted.FindAsync(first.CharacterId, firstQuest.QuestId, default));
        Assert.Equal(secondQuest, Assert.Single(await restarted.ListAsync(second.CharacterId, default)));
        var missing = Guid.NewGuid();
        await fixture.GlobalDatabase.GetCollection<CharacterQuestDocument>("character_quests")
            .InsertOneAsync(CharacterQuestDocument.From(firstQuest with { CharacterId = missing }));
        Assert.Empty(await restarted.ListAsync(missing, default));
        Assert.Null(await restarted.FindAsync(missing, firstQuest.QuestId, default));
        Assert.False(await restarted.TryWriteAsync(firstQuest with { CharacterId = missing }, 0, default));

        await fixture.Directory.UpdateOneAsync(new BsonDocument("_id", first.CharacterId.ToString("D")),
            Builders<BsonDocument>.Update.Set("RealmId", "missing-realm"));
        await Assert.ThrowsAsync<RealmStorageUnavailableException>(() => restarted.ListAsync(first.CharacterId, default));
        await Assert.ThrowsAsync<RealmStorageUnavailableException>(() => restarted.TryWriteAsync(firstQuest, 1, default));
        await AssertIndexes(fixture.Provider.GetRealmDatabase("realm-a"), "character_quests", "ux_character_quest");
        await AssertIndexes(fixture.Provider.GetRealmDatabase("realm-b"), "character_quests", "ux_character_quest");
        Assert.Equal(1, await fixture.Count(fixture.Provider.GetRealmDatabase("realm-a"), "character_quests"));
        Assert.Equal(1, await fixture.Count(fixture.Provider.GetRealmDatabase("realm-b"), "character_quests"));
    }

    private static GameLeaseRecord Lease(Guid id, Guid account, string realm)
    {
        var now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        return new(id, account, realm, "source", 1, "shared-ticket-hash", now.AddMinutes(5), Guid.NewGuid(), 7,
            now.AddMinutes(5), now.AddHours(1), new(7, new(10, 32, 20), new(0, 1))
            { Simulation = new(1, "{\"version\":2}"), IslandId = "starter-island" }, "session-reference", now);
    }

    private static async Task AssertIndexes(IMongoDatabase database, string collection, params string[] names)
    {
        using var cursor = await database.GetCollection<BsonDocument>(collection).Indexes.ListAsync();
        var indexes = await cursor.ToListAsync();
        foreach (var name in names) Assert.Contains(indexes, value => value["name"] == name);
        Assert.True(indexes.Single(value => value["name"] == names[0])["unique"].AsBoolean);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string connection = Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")!;
        private readonly MongoClient client;
        private readonly MongoOptions mongo;
        private readonly RealmStorageOptions storage;
        public DateTimeOffset Now { get; } = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        public RealmMongoDatabaseProvider Provider { get; }
        public IMongoDatabase GlobalDatabase => Provider.DirectoryDatabase;
        public IMongoCollection<BsonDocument> Directory => GlobalDatabase.GetCollection<BsonDocument>("character_directory");

        public Fixture()
        {
            var prefix = "arkya_realm_gameplay_test_" + Guid.NewGuid().ToString("N");
            mongo = new() { DatabaseName = prefix };
            storage = new() { Enabled = true, Realms =
            [
                new() { RealmId = "realm-a", DatabaseName = prefix + "_a", ConnectionString = connection },
                new() { RealmId = "realm-b", DatabaseName = prefix + "_b", ConnectionString = connection }
            ] };
            var settings = MongoClientSettings.FromConnectionString(connection);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            client = new MongoClient(settings);
            Provider = CreateProvider();
        }

        public RealmMongoDatabaseProvider CreateProvider() => new(client, Options.Create(mongo), Options.Create(storage));
        public Task Register(GameLeaseRecord lease) => Directory.InsertOneAsync(new BsonDocument
        {
            ["_id"] = lease.CharacterId.ToString("D"), ["AccountId"] = lease.AccountId.ToString("D"),
            ["RealmId"] = lease.RealmId, ["NormalizedName"] = lease.CharacterId.ToString("N"), ["Status"] = "Ready"
        });
        public Task<long> Count(IMongoDatabase database, string collection) =>
            database.GetCollection<BsonDocument>(collection).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        public async ValueTask DisposeAsync()
        {
            foreach (var route in storage.Realms) await client.DropDatabaseAsync(route.DatabaseName);
            await client.DropDatabaseAsync(mongo.DatabaseName);
        }
    }
}
