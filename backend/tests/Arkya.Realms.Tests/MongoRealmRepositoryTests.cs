using Arkya.Realms.Api.Infrastructure;
using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Domain;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Arkya.Realms.Tests;

public sealed class MongoRealmRepositoryTests
{
    [Fact]
    public void RuntimeMigrationRoundTripsBothProfilesAndPreservesRealmIdentityAndPreviousTransitions()
    {
        var migration = MigrationRecord(Fixtures.Realm(RealmStatus.Ready));
        var restored = BsonSerializer.Deserialize<RealmDocument>(RealmDocument.From(migration).ToBsonDocument()).ToRecord();
        AssertMigration(migration, restored);
    }

    [MongoFact]
    public async Task RuntimeMigrationWithdrawalIsAtomicAndSurvivesAnotherRepositoryInstance()
    {
        var database = $"arkya_runtime_migration_test_{Guid.NewGuid():N}";
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        var options = Options.Create(new MongoOptions { DatabaseName = database });
        try
        {
            var first = new MongoRealmRepository(client, options);
            await first.InitializeAsync(default);
            var original = Fixtures.Realm(RealmStatus.Ready);
            Assert.True(await first.TryCreateAsync(original, default));
            var migration = MigrationRecord(original);
            Assert.True(await first.TryReplaceAsync(migration, original.Version, default));
            Assert.False(await first.TryReplaceAsync(original with { Version = original.Version + 1 }, original.Version, default));
            var second = new MongoRealmRepository(new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")), options);
            var restored = (await second.FindAsync(original.RealmId, default))!;
            AssertMigration(migration, restored);
            Assert.Equal(RealmStatus.Pending, restored.Status);
            Assert.All(restored.Servers, server => Assert.Equal(0, server.ReadyReplicas));
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    private static RealmRecord MigrationRecord(RealmRecord original)
    {
        var islands = new RealmTopology([new("starter-island", 4, 1536, 1536, GridColumns: 2, GridRows: 2)]);
        var servers = Enumerable.Range(0, 4).Select(index => new ServerDefinition($"island-starter-island-{index + 1:D2}", 17943, [])
        {
            ProtocolVersion = GameProtocol.HandoffVersion, BuildId = GameProtocol.HandoffBuildId,
            Transport = "WebSocket", Kind = DedicatedKind.Island, IslandId = "starter-island",
            PartitionIndex = index, PartitionCount = 4,
            Bounds = new(index % 2 * 768, index / 2 * 768, (index % 2 + 1) * 768, (index / 2 + 1) * 768),
            PublicEndpoint = $"wss://api.example/game/eu-one/island-starter-island-{index + 1:D2}/"
        }).ToArray();
        var profile = original.Profile with
        {
            Id = "island-v2", Image = "registry.example/arkya/dedicated@sha256:" + new string('a', 64),
            Servers = servers, Topology = islands, TopologyRuntimeSupported = true
        };
        var imageRelease = new RealmImageTransition(3, original.Profile.Image, "registry.example/arkya/dedicated@sha256:" + new string('b', 64));
        var restart = new RealmRestartTransition(original.Revision, 0) { Completed = true };
        return original with
        {
            Profile = profile, Revision = original.Revision + 1, Version = original.Version + 1, Status = RealmStatus.Pending,
            RuntimeRelease = new(4, original.Profile, imageRelease, restart),
            RuntimeConfiguration = new() { Revision = 7, XpMultiplier = 2m },
            Servers = servers.Select(server => new ServerObservation(server.Id, 0, null)).ToArray()
        };
    }

    private static void AssertMigration(RealmRecord expected, RealmRecord restored)
    {
        Assert.Equal(expected.RealmId, restored.RealmId);
        Assert.Equal(expected.Namespace, restored.Namespace);
        Assert.Equal(expected.DeploymentIdentity, restored.DeploymentIdentity);
        Assert.Equal(expected.Revision, restored.Revision);
        Assert.Equal(expected.Version, restored.Version);
        Assert.Equal(RealmRuntimeReleases.Fingerprint(expected.Profile), RealmRuntimeReleases.Fingerprint(restored.Profile));
        Assert.Equal(RealmRuntimeReleases.Fingerprint(expected.RuntimeRelease!.PreviousProfile),
            RealmRuntimeReleases.Fingerprint(restored.RuntimeRelease!.PreviousProfile));
        Assert.Equal(expected.RuntimeRelease.PreviousImageRelease, restored.RuntimeRelease.PreviousImageRelease);
        Assert.Equal(expected.RuntimeRelease.PreviousRestart, restored.RuntimeRelease.PreviousRestart);
        Assert.Equal(4, restored.RuntimeRelease.Version);
        Assert.False(restored.RuntimeRelease.Completed);
        Assert.Equal(expected.RuntimeConfiguration.Revision, restored.RuntimeConfiguration.Revision);
        Assert.Equal(expected.RuntimeConfiguration.XpMultiplier, restored.RuntimeConfiguration.XpMultiplier);
        Assert.Equal(expected.RuntimeConfiguration.HarvestMultiplier, restored.RuntimeConfiguration.HarvestMultiplier);
        Assert.Equal(expected.RuntimeConfiguration.History, restored.RuntimeConfiguration.History);
    }

    [Fact]
    public void RealmSnapshotRoundTripsBsonWithStableIdVersionsArgumentsAndDates()
    {
        var original = Fixtures.Realm() with { LastError = "cluster_unavailable", Version = 7,
            Servers = [new("area-west", 1, "192.0.2.42")] };
        var bson = RealmDocument.From(original).ToBsonDocument();
        Assert.Equal(original.RealmId, bson["_id"].AsString);
        Assert.Equal(1, bson["SchemaVersion"].AsInt32);
        Assert.Equal(7, bson["Version"].AsInt64);
        Assert.Equal(1, bson["Revision"].AsInt64);
        Assert.Equal(BsonType.DateTime, bson["UpdatedAt"].BsonType);
        var restored = BsonSerializer.Deserialize<RealmDocument>(bson).ToRecord();
        Assert.Equal(original.RealmId, restored.RealmId);
        Assert.Equal(original.DeploymentIdentity, restored.DeploymentIdentity);
        Assert.Equal(original.RequestedFromRevision, restored.RequestedFromRevision);
        Assert.Equal(original.UpdatedAt, restored.UpdatedAt);
        Assert.Equal(original.DeploymentStartedAt, restored.DeploymentStartedAt);
        Assert.Equal(original.Profile.Image, restored.Profile.Image);
        Assert.Equal(original.Profile.Servers[0].Args, restored.Profile.Servers[0].Args);
        Assert.Equal(original.LastError, restored.LastError);
        Assert.Equal(original.Servers, restored.Servers);
        bson["SchemaVersion"] = 999;
        Assert.Throws<InvalidOperationException>(() => BsonSerializer.Deserialize<RealmDocument>(bson).ToRecord());
    }

    [MongoFact]
    public async Task MongoUniquenessAndCompareAndSwapSurviveAnotherRepositoryInstance()
    {
        var database = $"arkya_realms_test_{Guid.NewGuid():N}";
        var settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        var client = new MongoClient(settings);
        var options = Options.Create(new MongoOptions { DatabaseName = database });
        try
        {
            var first = new MongoRealmRepository(client, options);
            var second = new MongoRealmRepository(client, options);
            await Task.WhenAll(first.InitializeAsync(default), second.InitializeAsync(default));
            var realm = Fixtures.Realm();
            Assert.True(await first.TryCreateAsync(realm, default));
            Assert.False(await second.TryCreateAsync(realm with { RealmId = "eu-two" }, default));
            var results = await Task.WhenAll(
                first.TryReplaceAsync(realm with { Version = 2, Status = "Ready" }, 1, default),
                second.TryReplaceAsync(realm with { Version = 2, Status = "Failed" }, 1, default));
            Assert.Single(results, result => result);
            var restored = (await new MongoRealmRepository(client, options).FindAsync(realm.RealmId, default))!;
            Assert.Equal(2, restored.Version);
            Assert.Equal(realm.DeploymentIdentity, restored.DeploymentIdentity);
            Assert.Equal(realm.Profile.Servers[0].Args, restored.Profile.Servers[0].Args);
        }
        finally { await client.DropDatabaseAsync(database); }
    }
}

public sealed class MongoFactAttribute : FactAttribute
{
    public MongoFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")))
            Skip = "Set ARKYA_TEST_MONGO to run against a disposable MongoDB database.";
    }
}
