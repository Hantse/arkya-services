using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.Characters.Api.Domain;
using Arkya.Characters.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Characters;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Characters.Tests;

public sealed class RealmStorageMigrationTests
{
    [MongoFact]
    public async Task PlanReportsTheCopyWithoutWritingTargetsDirectoryOrActivationMarkers()
    {
        await using var fixture = new Fixture();
        await fixture.Seed("realm-a");
        await fixture.Seed("realm-b");
        var before = await fixture.SourceSnapshot();

        var report = await fixture.Migration.RunAsync(false, false, null, default);

        Assert.False(report.Applied);
        Assert.Equal(2, report.Characters);
        Assert.Equal(2, report.Leases);
        Assert.Equal(2, report.Quests);
        Assert.Equal(64, report.SourceFingerprint.Length);
        Assert.Equal(before, await fixture.SourceSnapshot());
        Assert.Equal(0, await fixture.Count(fixture.Global, "character_directory"));
        Assert.Equal(0, await fixture.Count(fixture.Global, "realm_storage_migrations"));
        foreach (var realm in fixture.Routes)
        foreach (var collection in Fixture.GameplayCollections)
            Assert.Equal(0, await fixture.Count(fixture.Provider.GetRealmDatabase(realm.RealmId), collection));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateProvider(true).EnsureActivatedAsync(default));
    }

    [MongoFact]
    public async Task ApplyCopiesExactBsonAndPreservesSourceIdsRevisionsPossessionsAndTransfers()
    {
        await using var fixture = new Fixture();
        var first = await fixture.Seed("realm-a");
        var second = await fixture.Seed("realm-b");
        var before = await fixture.SourceSnapshot();

        var report = await fixture.Migration.RunAsync(true, true, null, default);

        Assert.True(report.Applied);
        Assert.Equal(2, report.Characters);
        Assert.Equal(2, report.Leases);
        Assert.Equal(2, report.Quests);
        Assert.Equal(before, await fixture.SourceSnapshot());
        foreach (var seeded in new[] { first, second })
        {
            var target = fixture.Provider.GetRealmDatabase(seeded.Character["RealmId"].AsString);
            Assert.Equal(seeded.Character, await fixture.Read(target, "characters", seeded.Character["_id"]));
            Assert.Equal(seeded.Lease, await fixture.Read(target, "game_leases", seeded.Character["_id"]));
            Assert.Equal(seeded.Quest, await fixture.Read(target, "character_quests", seeded.Quest["_id"]));
            var directory = await fixture.Read(fixture.Global, "character_directory", seeded.Character["_id"]);
            Assert.NotNull(directory);
            Assert.Equal(new BsonDocument
            {
                ["_id"] = seeded.Character["_id"], ["AccountId"] = seeded.Character["AccountId"],
                ["RealmId"] = seeded.Character["RealmId"], ["NormalizedName"] = seeded.Character["NormalizedName"], ["Status"] = "Ready"
            }, directory);
            Assert.Equal(1, await fixture.Count(target, "characters"));
            Assert.Equal(1, await fixture.Count(target, "game_leases"));
            Assert.Equal(1, await fixture.Count(target, "character_quests"));
        }
        await fixture.CreateProvider(true).EnsureActivatedAsync(default);

        // Retrying migration after cutover must not replace newer realm-owned progress with the old backup.
        var firstTarget = fixture.Provider.GetRealmDatabase("realm-a");
        await firstTarget.GetCollection<BsonDocument>("game_leases").UpdateOneAsync(new BsonDocument("_id", first.Character["_id"]),
            Builders<BsonDocument>.Update.Set("Version", 124L).Set("State.Revision", 43L));
        var repeated = await fixture.Migration.RunAsync(true, true, null, default);
        Assert.Equal(report, repeated);
        Assert.Equal(124L, (await fixture.Read(firstTarget, "game_leases", first.Character["_id"]))!["Version"].AsInt64);
        Assert.Equal(before, await fixture.SourceSnapshot());
        Assert.Equal(2, await fixture.Count(fixture.Global, "character_directory"));
    }

    [MongoFact]
    public async Task UnboundLegacyCharactersRequireAnExplicitDestinationAndDoNotChangeTheBackup()
    {
        await using var fixture = new Fixture();
        var seeded = await fixture.Seed(null, includeLease: false);
        var before = await fixture.SourceSnapshot();
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Migration.RunAsync(false, false, null, default));
        await Assert.ThrowsAsync<RealmStorageUnavailableException>(() => fixture.Migration.RunAsync(false, false, "unknown-realm", default));

        var report = await fixture.Migration.RunAsync(true, true, "realm-b", default);

        Assert.Equal(1, report.Characters);
        Assert.Equal(0, report.Leases);
        Assert.Equal(1, report.Quests);
        var expected = seeded.Character.DeepClone().AsBsonDocument;
        expected["RealmId"] = "realm-b";
        Assert.Equal(expected, await fixture.Read(fixture.Provider.GetRealmDatabase("realm-b"), "characters", seeded.Character["_id"]));
        Assert.Equal(before, await fixture.SourceSnapshot());
        await fixture.CreateProvider(true).EnsureActivatedAsync(default);
    }

    [MongoFact]
    public async Task MigrationRequiresStoppedWritersAndRejectsLiveLeaseTicketOrTransferWindows()
    {
        await using var fixture = new Fixture();
        var seeded = await fixture.Seed("realm-a");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Migration.RunAsync(true, false, null, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new RealmStorageMigration(fixture.CreateProvider(true))
            .RunAsync(true, true, null, default));

        foreach (var field in new[] { "LeaseExpiresAt", "TicketExpiresAt", "TransferExpiresAt" })
        {
            await fixture.Global.GetCollection<BsonDocument>("game_leases").UpdateOneAsync(new BsonDocument("_id", seeded.Character["_id"]),
                Builders<BsonDocument>.Update.Set(field, DateTime.UtcNow.AddMinutes(5)));
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Migration.RunAsync(true, true, null, default));
            Assert.Contains("expire", error.Message, StringComparison.OrdinalIgnoreCase);
            await fixture.Global.GetCollection<BsonDocument>("game_leases").ReplaceOneAsync(new BsonDocument("_id", seeded.Character["_id"]), seeded.Lease!);
        }
        Assert.Equal(0, await fixture.Count(fixture.Global, "realm_storage_migrations"));
        Assert.Equal(0, await fixture.Count(fixture.Global, "character_directory"));
    }

    [MongoFact]
    public async Task DivergentTargetDocumentsAreNeverOverwrittenAndCannotActivateTheRealm()
    {
        await using var fixture = new Fixture();
        var seeded = await fixture.Seed("realm-a");
        var divergent = seeded.Character.DeepClone().AsBsonDocument;
        divergent["Level"] = 99;
        var target = fixture.Provider.GetRealmDatabase("realm-a");
        await target.GetCollection<BsonDocument>("characters").InsertOneAsync(divergent);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Migration.RunAsync(false, false, null, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Migration.RunAsync(true, true, null, default));
        Assert.Equal(divergent, await fixture.Read(target, "characters", seeded.Character["_id"]));
        Assert.Equal(seeded.Character, await fixture.Read(fixture.Global, "characters", seeded.Character["_id"]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateProvider(true).EnsureActivatedAsync(default));
    }

    [MongoFact]
    public async Task ExtraTargetGameplayOrDirectoryDocumentsPreventCompletionWithoutDeletingThem()
    {
        foreach (var collection in Fixture.GameplayCollections.Append("character_directory"))
        {
            await using var fixture = new Fixture();
            var seeded = await fixture.Seed("realm-a");
            var extraId = Guid.NewGuid().ToString("D");
            var extra = collection switch
            {
                "characters" => seeded.Character.DeepClone().AsBsonDocument,
                "game_leases" => seeded.Lease!.DeepClone().AsBsonDocument,
                "character_quests" => seeded.Quest.DeepClone().AsBsonDocument,
                _ => new BsonDocument { ["_id"] = extraId, ["AccountId"] = seeded.Character["AccountId"], ["RealmId"] = "realm-a", ["Status"] = "Ready" }
            };
            extra["_id"] = extraId;
            if (collection is "characters" or "character_directory") extra["NormalizedName"] = "UNEXPECTED";
            var target = collection == "character_directory" ? fixture.Global : fixture.Provider.GetRealmDatabase("realm-a");
            await target.GetCollection<BsonDocument>(collection).InsertOneAsync(extra);
            var sourceBefore = await fixture.SourceSnapshot();

            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Migration.RunAsync(false, false, null, default));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Migration.RunAsync(true, true, null, default));
            Assert.Equal(extra, await fixture.Read(target, collection, extraId));
            Assert.Equal(sourceBefore, await fixture.SourceSnapshot());
            Assert.Equal(0, await fixture.Count(fixture.Global, "realm_storage_migrations"));
            Assert.Equal(collection == "character_directory" ? 1 : 0, await fixture.Count(fixture.Global, "character_directory"));
            foreach (var name in Fixture.GameplayCollections)
                Assert.Equal(collection == name ? 1 : 0, await fixture.Count(fixture.Provider.GetRealmDatabase("realm-a"), name));
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.CreateProvider(true).EnsureActivatedAsync(default));
        }
    }

    [MongoFact]
    public async Task ChangedLegacySourceCannotResumeAPartialMigrationOrRepeatACompletedOne()
    {
        foreach (var completed in new[] { false, true })
        {
            await using var fixture = new Fixture();
            var seeded = await fixture.Seed("realm-a");
            if (completed) await fixture.Migration.RunAsync(true, true, null, default);
            else
            {
                var plan = await fixture.Migration.RunAsync(false, false, null, default);
                var route = fixture.Routes.Single(value => value.RealmId == "realm-a");
                // Model interruption after one exact copy, before the remaining gameplay was copied.
                await fixture.Provider.GetRealmDatabase("realm-a").GetCollection<BsonDocument>("characters").InsertOneAsync(seeded.Character);
                await fixture.Global.GetCollection<BsonDocument>("realm_storage_migrations").InsertOneAsync(new BsonDocument
                {
                    ["_id"] = route.RealmId, ["DatabaseName"] = route.DatabaseName, ["Status"] = "Copying",
                    ["RouteFingerprint"] = RealmMongoDatabaseProvider.RouteFingerprint(route), ["SourceFingerprint"] = plan.SourceFingerprint
                });
            }

            await fixture.Global.GetCollection<BsonDocument>("character_quests").UpdateOneAsync(new BsonDocument("_id", seeded.Quest["_id"]),
                Builders<BsonDocument>.Update.Set("Kills", 8).Set("Revision", 62L));
            var sourceBeforeRetry = await fixture.SourceSnapshot();
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Migration.RunAsync(true, true, null, default));
            Assert.Equal(sourceBeforeRetry, await fixture.SourceSnapshot());
            Assert.Equal(seeded.Character, await fixture.Read(fixture.Provider.GetRealmDatabase("realm-a"), "characters", seeded.Character["_id"]));
            var targetQuest = await fixture.Read(fixture.Provider.GetRealmDatabase("realm-a"), "character_quests", seeded.Quest["_id"]);
            if (completed) Assert.Equal(seeded.Quest, targetQuest);
            else Assert.Null(targetQuest);
        }
    }

    [MongoFact]
    public async Task ActivationRequiresAllCompletionMarkersAndTheExactConfiguredDestination()
    {
        await using var fixture = new Fixture();
        await fixture.Seed("realm-a");
        var enabled = fixture.CreateProvider(true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => enabled.EnsureActivatedAsync(default));
        await fixture.Migration.RunAsync(true, true, null, default);
        await enabled.EnsureActivatedAsync(default);
        var markers = fixture.Global.GetCollection<BsonDocument>("realm_storage_migrations");
        await markers.UpdateOneAsync(new BsonDocument("_id", "realm-b"), Builders<BsonDocument>.Update.Set("Status", "Copying"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => enabled.EnsureActivatedAsync(default));
        await markers.UpdateOneAsync(new BsonDocument("_id", "realm-b"), Builders<BsonDocument>.Update.Set("Status", "Complete"));
        await markers.UpdateOneAsync(new BsonDocument("_id", "realm-a"), Builders<BsonDocument>.Update.Set("RouteFingerprint", "different-destination"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => enabled.EnsureActivatedAsync(default));
    }

    private sealed record Seeded(BsonDocument Character, BsonDocument? Lease, BsonDocument Quest);

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly string[] GameplayCollections = ["characters", "game_leases", "character_quests"];
        private readonly MongoClient client;
        private readonly MongoOptions mongo;
        public RealmStorageRoute[] Routes { get; }
        public RealmMongoDatabaseProvider Provider { get; }
        public RealmStorageMigration Migration => new(Provider);
        public IMongoDatabase Global => Provider.DirectoryDatabase;

        public Fixture()
        {
            var connection = Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")!;
            var prefix = "arkya_migration_test_" + Guid.NewGuid().ToString("N");
            mongo = new() { DatabaseName = prefix };
            Routes = new[] { "realm-a", "realm-b" }.Select((realm, index) => new RealmStorageRoute
            {
                RealmId = realm, DatabaseName = prefix + "_" + index, ConnectionString = connection
            }).ToArray();
            var settings = MongoClientSettings.FromConnectionString(connection);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
            client = new MongoClient(settings);
            Provider = CreateProvider(false);
        }

        public RealmMongoDatabaseProvider CreateProvider(bool enabled) => new(client, Options.Create(mongo),
            Options.Create(new RealmStorageOptions { Enabled = enabled, Realms = Routes }));

        public async Task<Seeded> Seed(string? realm, bool includeLease = true)
        {
            var id = Guid.NewGuid();
            var account = Guid.NewGuid();
            var expired = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeMilliseconds());
            var character = CharacterDocument.From(new(id, account, "Aeria", id.ToString("N").ToUpperInvariant(), 1, expired)
                { RealmId = realm }).ToBsonDocument();
            await Global.GetCollection<BsonDocument>("characters").InsertOneAsync(character);
            BsonDocument? lease = null;
            if (includeLease)
            {
                var state = new SavedWorldState(42, new(410, 32, 600), new(0, 1))
                {
                    IslandId = "starter-island", Simulation = new(1, "{\"version\":3,\"health\":77}"),
                    Possessions = new(1, new(150, [new(149, "resource:wood", 7)]),
                        new(80, [new(79, "old-copper-ring", 1)]), [new("main-hand", "valley-sword", 1)])
                };
                var record = new GameLeaseRecord(id, account, realm!, "source", 123, null, expired,
                    Guid.Empty, 98, expired, expired, state, "migration-session-reference", expired)
                {
                    Area = new("starter-island", "starter-island"),
                    Transfer = new(Guid.NewGuid().ToString("D"), "source", Guid.NewGuid().ToString("D"), 97,
                        "target", "wss://game.example/", "topology-v1", TransferStatus.Aborted, Guid.NewGuid().ToString("D"), expired, state)
                };
                Assert.True(await new MongoGameLeaseStore(client, Options.Create(mongo)).TryWriteAsync(record, null, default));
                lease = await Read(Global, "game_leases", id.ToString("D"));
            }
            var quest = CharacterQuestDocument.From(new(id, "hunt-wolves", Guid.NewGuid(), 12, 7, 61)).ToBsonDocument();
            await Global.GetCollection<BsonDocument>("character_quests").InsertOneAsync(quest);
            return new(character, lease, quest);
        }

        public async Task<string[]> SourceSnapshot()
        {
            var result = new List<string>();
            foreach (var name in GameplayCollections)
            {
                var rows = await Global.GetCollection<BsonDocument>(name).Find(FilterDefinition<BsonDocument>.Empty)
                    .Sort(new BsonDocument("_id", 1)).ToListAsync();
                result.AddRange(rows.Select(row => name + ":" + Convert.ToBase64String(row.ToBson())));
            }
            return result.ToArray();
        }
        public Task<BsonDocument?> Read(IMongoDatabase database, string collection, BsonValue id) =>
            database.GetCollection<BsonDocument>(collection).Find(new BsonDocument("_id", id)).FirstOrDefaultAsync()!;
        public Task<long> Count(IMongoDatabase database, string collection) =>
            database.GetCollection<BsonDocument>(collection).CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty);
        public async ValueTask DisposeAsync()
        {
            foreach (var route in Routes) await client.DropDatabaseAsync(route.DatabaseName);
            await client.DropDatabaseAsync(mongo.DatabaseName);
        }
    }
}
