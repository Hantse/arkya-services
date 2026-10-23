using Arkya.Admin.Authentication;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Admin.Tests;

public sealed class MongoAdminSessionStoreTests
{
    [AdminMongoFact]
    public async Task PersistedSessionHasTtlAtomicRotationAndDurableRevocation()
    {
        var settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")!);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        var client = new MongoClient(settings);
        var name = "arkya_admin_test_" + Guid.NewGuid().ToString("N");
        var options = Options.Create(new MongoOptions { DatabaseName = name });
        try
        {
            var first = new MongoAdminSessionStore(client, options);
            await first.InitializeAsync(default);
            var now = DateTime.UtcNow;
            var session = new AdminSession("session-id", "old-hash", "Discord", DiscordStub.AllowedId,
                "Administrateur Discord", "", now.AddDays(30), now, []);
            await first.CreateAsync(session, default);
            var replacement = new MongoAdminSessionStore(client, options);
            Assert.Equal(session.Id, (await replacement.GetAsync(session.Id, default))!.Id);
            var rotations = await Task.WhenAll(Enumerable.Range(0, 10).Select(index => replacement.RotateAsync(session.Id,
                "old-hash", "next-hash-" + index, now.AddHours(1), default)));
            Assert.Single(rotations, success => success);
            var persisted = (await replacement.GetAsync(session.Id, default))!;
            Assert.Equal(new[] { "old-hash" }, persisted.UsedTokenHashes);
            Assert.False(await first.RotateAsync(session.Id, persisted.RefreshTokenHash, "expired", session.ExpiresAt, default));
            var collection = client.GetDatabase(name).GetCollection<BsonDocument>("admin_sessions");
            var indexes = await (await collection.Indexes.ListAsync()).ToListAsync();
            Assert.Equal(0, Assert.Single(indexes, index => index["name"] == "admin_session_expiry")["expireAfterSeconds"].ToInt32());
            Assert.Equal(1, (await collection.Find(FilterDefinition<BsonDocument>.Empty).SingleAsync())["SchemaVersion"].AsInt32);
            await replacement.RevokeAsync(session.Id, default);
            Assert.Null(await first.GetAsync(session.Id, default));
            Assert.False(await first.RotateAsync(session.Id, persisted.RefreshTokenHash, "revoked", now.AddHours(2), default));
            await first.CreateAsync(session with { UsedTokenHashes = Enumerable.Repeat("consumed", AdminSession.MaximumRotations).ToArray() }, default);
            Assert.False(await first.RotateAsync(session.Id, "old-hash", "over-limit", now.AddHours(2), default));
            await first.RevokeAsync(session.Id, default);
            await first.CreateAsync(session with { SchemaVersion = 2 }, default);
            Assert.False(await first.RotateAsync(session.Id, "old-hash", "unknown-schema", now.AddHours(2), default));
        }
        finally { await client.DropDatabaseAsync(name); }
    }
}

internal sealed class AdminMongoFactAttribute : FactAttribute
{
    public AdminMongoFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")))
            Skip = "Set ARKYA_TEST_MONGO to run tests against a disposable MongoDB database.";
    }
}
