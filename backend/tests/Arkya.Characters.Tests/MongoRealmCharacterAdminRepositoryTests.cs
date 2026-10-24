using System.Text.Json;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Domain;
using Arkya.Characters.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Characters.Tests;

public sealed class MongoRealmCharacterAdminRepositoryTests
{
    [MongoFact]
    public async Task GlobalPagesMergeBothRealmsWithStableOrderingAndApplyPresenceBeforePagination()
    {
        await using var store = new RealmStore();
        var now = FixedTimeProvider.Now;
        var older = await store.AddAsync(RealmStore.Alpha, "Older", now.AddMinutes(-1), online: true);
        var first = await store.AddAsync(RealmStore.Bravo, "First", now, online: true,
            id: Guid.Parse("10000000-0000-0000-0000-000000000001"));
        var second = await store.AddAsync(RealmStore.Alpha, "Second", now,
            id: Guid.Parse("20000000-0000-0000-0000-000000000002"));
        var third = await store.AddAsync(RealmStore.Bravo, "Third", now, online: true,
            id: Guid.Parse("30000000-0000-0000-0000-000000000003"));

        var pageOne = await store.Admin.ListAsync(new(null, "", false, 1, 2), now, default);
        var pageTwo = await store.Admin.ListAsync(new(null, "", false, 2, 2), now, default);
        Assert.Equal(4, pageOne.Total);
        Assert.Equal(4, pageTwo.Total);
        Assert.Equal(new[] { first.Id, second.Id, third.Id, older.Id },
            pageOne.Items.Concat(pageTwo.Items).Select(value => Guid.Parse(value.CharacterId)));
        var online = await store.Admin.ListAsync(new(null, "", true, 2, 1), now, default);
        Assert.Equal(3, online.Total);
        Assert.Equal(third.Id.ToString("D"), Assert.Single(online.Items).CharacterId);
        var beyond = await store.Admin.ListAsync(new(null, "", true, 100000, 100), now, default);
        Assert.Equal(3, beyond.Total);
        Assert.Empty(beyond.Items);

        var overview = await store.Admin.OverviewAsync(now, default);
        Assert.Equal(4, overview.TotalCharacters);
        Assert.Equal(3, overview.OnlineCharacters);
        Assert.Collection(overview.Realms,
            realm => { Assert.Equal(RealmStore.Alpha, realm.RealmId); Assert.Equal(1, realm.OnlineCharacters); },
            realm => { Assert.Equal(RealmStore.Bravo, realm.RealmId); Assert.Equal(2, realm.OnlineCharacters); });
    }

    [MongoFact]
    public async Task ScopedQueriesCombineOwnerLiteralSearchAndPresenceWithoutFallingBackToLegacyData()
    {
        await using var store = new RealmStore();
        var now = FixedTimeProvider.Now;
        var owner = Guid.NewGuid();
        var selected = await store.AddAsync(RealmStore.Alpha, "AelysAlpha", now, owner: owner, online: true);
        await store.AddAsync(RealmStore.Alpha, "AelysOffline", now.AddSeconds(1), owner: owner);
        await store.AddAsync(RealmStore.Alpha, "AelysGuest", now.AddSeconds(2), online: true);
        await store.AddAsync(RealmStore.Bravo, "AelysBravo", now.AddSeconds(3), owner: owner, online: true);
        var legacy = CharacterDocument.From(new Character(Guid.NewGuid(), owner, "AelysLegacy", "AELYSLEGACY", 1, now)
            { RealmId = RealmStore.Alpha });
        await store.Provider.LegacyDatabase.GetCollection<CharacterDocument>("characters").InsertOneAsync(legacy);

        var page = await store.Admin.ListAsync(new(owner, "aElYs", true, 1, 1, RealmStore.Alpha), now, default);
        Assert.Equal(1, page.Total);
        Assert.Equal(selected.Id.ToString("D"), Assert.Single(page.Items).CharacterId);
        Assert.Empty((await store.Admin.ListAsync(new(null, ".*", false, 1, 25, RealmStore.Alpha), now, default)).Items);
        var unknown = await store.Admin.ListAsync(new(null, "", false, 1, 25, "unknown-realm"), now, default);
        Assert.Equal(0, unknown.Total);
        Assert.Empty(unknown.Items);
        Assert.Null(await store.Admin.FindAsync(selected.Id, RealmStore.Bravo, now, default));
        Assert.NotNull(await store.Admin.FindAsync(selected.Id, null, now, default));
        Assert.DoesNotContain((await store.Admin.ListAsync(new(null, "", false, 1, 25), now, default)).Items,
            value => value.DisplayName == "AelysLegacy");
    }

    [MongoFact]
    public async Task PendingOrphanAndMisplacedRowsCannotLeakIntoPagesCountsDetailsOrQuests()
    {
        await using var store = new RealmStore();
        var now = FixedTimeProvider.Now;
        var canonical = await store.AddAsync(RealmStore.Alpha, "Canonical", now, online: true);
        var pending = await store.AddAsync(RealmStore.Bravo, "Pending", now, online: true, status: "Pending");
        var orphan = await store.AddAsync(RealmStore.Bravo, "Orphan", now, online: true, directory: false);
        var wrongOwner = await store.AddAsync(RealmStore.Bravo, "WrongOwner", now, online: true);
        await store.Directory.UpdateOneAsync(new BsonDocument("_id", wrongOwner.Id.ToString("D")),
            new BsonDocument("$set", new BsonDocument("AccountId", Guid.NewGuid().ToString("D"))));
        var misplaced = canonical with { Name = "PrivateOtherRealm", RealmId = RealmStore.Bravo };
        await store.Database(RealmStore.Bravo).GetCollection<CharacterDocument>("characters")
            .InsertOneAsync(CharacterDocument.From(misplaced));
        await store.InsertLeaseAsync(misplaced);
        foreach (var value in new[] { pending, orphan, wrongOwner, misplaced })
            await store.InsertQuestAsync(value, "private-quest");

        var page = await store.Admin.ListAsync(new(null, "", false, 1, 25), now, default);
        Assert.Equal(1, page.Total);
        Assert.Equal(canonical.Id.ToString("D"), Assert.Single(page.Items).CharacterId);
        Assert.Equal(1, (await store.Admin.OverviewAsync(now, default)).TotalCharacters);
        Assert.Null(await store.Admin.FindAsync(pending.Id, null, now, default));
        Assert.Null(await store.Admin.FindAsync(orphan.Id, null, now, default));
        Assert.Null(await store.Admin.FindAsync(wrongOwner.Id, null, now, default));
        Assert.Null(await store.Admin.FindAsync(canonical.Id, RealmStore.Bravo, now, default));
        foreach (var value in new[] { pending, orphan, wrongOwner, canonical })
            Assert.Empty((await store.Admin.ListQuestsAsync(value.Id, 1, 25, default)).Items);
        var json = JsonSerializer.Serialize(page);
        Assert.DoesNotContain("private-", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("LeaseId", json, StringComparison.OrdinalIgnoreCase);
    }

    [MongoFact]
    public async Task QuestPagesResolveTheCanonicalRealmAndConcurrentQueriesKeepTheirScopes()
    {
        await using var store = new RealmStore();
        var now = FixedTimeProvider.Now;
        var alpha = await store.AddAsync(RealmStore.Alpha, "Alpha", now);
        var bravo = await store.AddAsync(RealmStore.Bravo, "Bravo", now);
        foreach (var quest in new[] { "quest-c", "quest-a", "quest-b" }) await store.InsertQuestAsync(alpha, quest);
        await store.InsertQuestAsync(bravo, "bravo-only");
        await store.Database(RealmStore.Bravo).GetCollection<BsonDocument>("character_quests")
            .InsertOneAsync(new BsonDocument { ["_id"] = "foreign-quest", ["CharacterId"] = alpha.Id.ToString("D"),
                ["QuestId"] = "private-foreign", ["CatalogVersion"] = Guid.NewGuid().ToString("D"),
                ["RequiredKills"] = 2, ["Kills"] = 0, ["Revision"] = 1L });

        var reads = await Task.WhenAll(Enumerable.Range(0, 12).Select(async index =>
        {
            var realm = index % 2 == 0 ? RealmStore.Alpha : RealmStore.Bravo;
            return (Realm: realm, Page: await store.Admin.ListAsync(new(null, "", false, 1, 25, realm), now, default));
        }));
        Assert.All(reads, read => Assert.Equal(read.Realm, Assert.Single(read.Page.Items).RealmId));
        var quests = await store.Admin.ListQuestsAsync(alpha.Id, 2, 2, default);
        Assert.Equal(3, quests.Total);
        Assert.Equal("quest-c", Assert.Single(quests.Items).QuestId);
        Assert.Equal("bravo-only", Assert.Single((await store.Admin.ListQuestsAsync(bravo.Id, 1, 25, default)).Items).QuestId);
        var recreated = new MongoCharacterAdminRepository(store.Provider);
        Assert.Equal(alpha.Id.ToString("D"), (await recreated.FindAsync(alpha.Id, RealmStore.Alpha, now, default))!.CharacterId);
    }

    [MongoFact]
    public async Task LeasesMustMatchBothOwnerAndRealmToCountAsOnline()
    {
        await using var store = new RealmStore();
        var now = FixedTimeProvider.Now;
        var wrongOwner = await store.AddAsync(RealmStore.Alpha, "WrongOwner", now);
        var wrongRealm = await store.AddAsync(RealmStore.Alpha, "WrongRealm", now);
        await store.InsertLeaseAsync(wrongOwner with { AccountId = Guid.NewGuid() });
        await store.InsertLeaseAsync(wrongRealm with { RealmId = RealmStore.Bravo }, RealmStore.Alpha);
        var page = await store.Admin.ListAsync(new(null, "", false, 1, 25), now, default);
        Assert.Equal(2, page.Total);
        Assert.All(page.Items, value => { Assert.False(value.IsOnline); Assert.Null(value.LeaseExpiresAt); });
        Assert.Empty((await store.Admin.ListAsync(new(null, "", true, 1, 25), now, default)).Items);
        Assert.Equal(0, (await store.Admin.OverviewAsync(now, default)).OnlineCharacters);
    }

    [MongoFact]
    public async Task CursorBatchesPreserveExactGlobalTotalsAndDoNotExposeRowsWithoutReadyDirectoryEntries()
    {
        await using var store = new RealmStore();
        var now = FixedTimeProvider.Now;
        for (var index = 0; index < 270; index++)
            await store.AddAsync(index % 2 == 0 ? RealmStore.Alpha : RealmStore.Bravo,
                "Character" + index.ToString("D3"), now.AddSeconds(index), online: true,
                directory: index % 7 != 0);
        var expected = Enumerable.Range(0, 270).Where(index => index % 7 != 0)
            .OrderByDescending(index => index).ToArray();
        var page = await store.Admin.ListAsync(new(null, "", true, 3, 10), now, default);
        Assert.Equal(expected.Length, page.Total);
        Assert.Equal(expected.Skip(20).Take(10).Select(index => "Character" + index.ToString("D3")),
            page.Items.Select(value => value.DisplayName));
    }

    private sealed class RealmStore : IAsyncDisposable
    {
        public const string Alpha = "realm-alpha";
        public const string Bravo = "realm-bravo";
        private readonly MongoClient client;
        private readonly string[] databaseNames;
        public RealmMongoDatabaseProvider Provider { get; }
        public MongoCharacterAdminRepository Admin { get; }
        public IMongoCollection<BsonDocument> Directory => Provider.DirectoryDatabase.GetCollection<BsonDocument>("character_directory");

        public RealmStore()
        {
            var connection = Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")!;
            client = new MongoClient(connection);
            var prefix = "arkya_admin_realms_" + Guid.NewGuid().ToString("N");
            databaseNames = [prefix, prefix + "_a", prefix + "_b"];
            Provider = new(client, Options.Create(new MongoOptions { ConnectionString = connection, DatabaseName = prefix }),
                Options.Create(new RealmStorageOptions
                {
                    Enabled = true,
                    Realms = [new() { RealmId = Alpha, DatabaseName = databaseNames[1], ConnectionString = connection },
                        new() { RealmId = Bravo, DatabaseName = databaseNames[2], ConnectionString = connection }]
                }));
            Admin = new(Provider);
        }

        public IMongoDatabase Database(string realmId) => Provider.GetRealmDatabase(realmId);

        public async Task<Character> AddAsync(string realm, string name, DateTimeOffset createdAt,
            bool online = false, Guid? id = null, Guid? owner = null, string status = "Ready", bool directory = true)
        {
            var value = new Character(id ?? Guid.NewGuid(), owner ?? Guid.NewGuid(), name, name.ToUpperInvariant(), 1, createdAt)
                { RealmId = realm };
            await Database(realm).GetCollection<CharacterDocument>("characters").InsertOneAsync(CharacterDocument.From(value));
            if (directory) await Directory.InsertOneAsync(new BsonDocument
            {
                ["_id"] = value.Id.ToString("D"), ["AccountId"] = value.AccountId.ToString("D"), ["RealmId"] = realm,
                ["NormalizedName"] = value.NormalizedName, ["Status"] = status
            });
            if (online) await InsertLeaseAsync(value);
            return value;
        }

        public Task InsertLeaseAsync(Character value, string? storageRealm = null) =>
            Database(storageRealm ?? value.RealmId!).GetCollection<BsonDocument>("game_leases").InsertOneAsync(new BsonDocument
            {
                ["_id"] = value.Id.ToString("D"), ["AccountId"] = value.AccountId.ToString("D"), ["RealmId"] = value.RealmId,
                ["ServerId"] = "island", ["LeaseId"] = Guid.NewGuid().ToString("D"), ["AuthorityEpoch"] = 1L,
                ["LeaseExpiresAt"] = new BsonDateTime(FixedTimeProvider.Now.AddSeconds(90).UtcDateTime),
                ["SessionExpiresAt"] = new BsonDateTime(FixedTimeProvider.Now.AddHours(1).UtcDateTime),
                ["SessionReference"] = "private-session-reference", ["TicketHash"] = "private-ticket-hash"
            });

        public Task InsertQuestAsync(Character value, string questId) =>
            Database(value.RealmId!).GetCollection<BsonDocument>("character_quests").InsertOneAsync(new BsonDocument
            {
                ["_id"] = value.Id.ToString("D") + ":" + questId, ["CharacterId"] = value.Id.ToString("D"),
                ["QuestId"] = questId, ["CatalogVersion"] = Guid.NewGuid().ToString("D"),
                ["RequiredKills"] = 2, ["Kills"] = 0, ["Revision"] = 1L
            });

        public async ValueTask DisposeAsync()
        {
            foreach (var database in databaseNames) await client.DropDatabaseAsync(database);
        }
    }
}
