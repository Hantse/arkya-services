using Arkya.Realms.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Arkya.Realms.Tests;

public sealed class MongoRealmRepositoryTests
{
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
