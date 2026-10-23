using Arkya.Inference.Api.Application;
using Arkya.Inference.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Inference;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Arkya.Inference.Tests;

public sealed class MongoInferenceRepositoryTests
{
    [Fact]
    public async Task SnapshotPreservesIdentityHashedCredentialWorkerStateVersionsAndDates()
    {
        var repository = new MemoryInferenceRepository();
        var service = new InferenceService(repository, Options.Create(new InferenceOptions()), new TestClock(), new MemoryEnrollmentGrantRepository());
        var created = (await service.CreateAsync(Fixtures.Create(), default)).Value!;
        await service.HeartbeatAsync(Fixtures.Heartbeat("cpu-one", Fixtures.Observe(created.Agent.DesiredWorkers[0])), created.AgentToken, default);
        await service.RemoveWorkerAsync("cpu-one", created.Agent.DesiredWorkers[0].WorkerId, default);
        var original = (await repository.FindAsync("cpu-one", default))!;
        var bson = InferenceAgentDocument.From(original).ToBsonDocument();
        Assert.Equal(original.AgentId, bson["_id"].AsString);
        Assert.Equal(1, bson["SchemaVersion"].AsInt32);
        Assert.Equal(original.TokenHash, bson["TokenHash"].AsString);
        Assert.DoesNotContain(created.AgentToken, bson.ToJson());
        Assert.Equal(BsonType.DateTime, bson["CreatedAt"].BsonType);
        Assert.Equal(BsonType.DateTime, bson["LastSeenAt"].BsonType);
        var restored = BsonSerializer.Deserialize<InferenceAgentDocument>(bson).ToRecord();
        Assert.Equal(original.AgentId, restored.AgentId);
        Assert.Equal(original.TokenHash, restored.TokenHash);
        Assert.Equal(original.CreatedAt, restored.CreatedAt);
        Assert.Equal(original.LastSeenAt, restored.LastSeenAt);
        Assert.Equal(original.Revision, restored.Revision);
        Assert.Equal(original.Version, restored.Version);
        Assert.Equal(original.ObservedRevision, restored.ObservedRevision);
        Assert.Equal(original.DesiredWorkers, restored.DesiredWorkers);
        Assert.Equal(original.ObservedWorkers, restored.ObservedWorkers);
        Assert.Equal(original.RetiredWorkers, restored.RetiredWorkers);
        bson["SchemaVersion"] = 99;
        Assert.Throws<InvalidOperationException>(() => BsonSerializer.Deserialize<InferenceAgentDocument>(bson).ToRecord());
    }

    [MongoFact]
    public async Task DurableStorageEnforcesUniquenessCasAndCredentialRemovalAcrossRepositoryInstances()
    {
        var database = $"arkya_inference_test_{Guid.NewGuid():N}";
        var settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        var client = new MongoClient(settings);
        var options = Options.Create(new MongoOptions { DatabaseName = database });
        try
        {
            var first = new MongoInferenceRepository(client, options);
            var second = new MongoInferenceRepository(client, options);
            await Task.WhenAll(first.InitializeAsync(default), second.InitializeAsync(default));
            var service = new InferenceService(first, Options.Create(new InferenceOptions()), new TestClock(), new MemoryEnrollmentGrantRepository());
            var created = (await service.CreateAsync(Fixtures.Create(), default)).Value!;
            // Existing v1 documents predate enrollment revocation metadata.
            await client.GetDatabase(database).GetCollection<BsonDocument>("agents").UpdateOneAsync(
                new BsonDocument("_id", "cpu-one"), new BsonDocument("$unset", new BsonDocument
                { { "Deleted", "" }, { "RevokedTokenHashes", "" } }));
            var stored = (await second.FindAsync("cpu-one", default))!;
            Assert.Equal(AgentCredentials.Hash(created.AgentToken), stored.TokenHash);
            Assert.False(await second.TryCreateAsync(stored, default));
            var results = await Task.WhenAll(
                first.TryReplaceAsync(stored with { Version = 2, MachineName = "FIRST" }, 1, default),
                second.TryReplaceAsync(stored with { Version = 2, MachineName = "SECOND" }, 1, default));
            Assert.Single(results, result => result);
            var restored = (await new MongoInferenceRepository(client, options).FindAsync("cpu-one", default))!;
            Assert.Equal(2, restored.Version);
            Assert.Equal(stored.DesiredWorkers, restored.DesiredWorkers);
            Assert.False(await second.TryDeleteAsync("cpu-one", 1, stored.TokenHash, default));
            Assert.True(await first.TryDeleteAsync("cpu-one", 2, stored.TokenHash, default));
            Assert.Null(await second.FindAsync("cpu-one", default));
            var replacement = (await service.CreateAsync(Fixtures.Create(), default)).Value!;
            Assert.NotEqual(created.AgentToken, replacement.AgentToken);
            Assert.False(await first.TryReplaceAsync(stored with { Version = 2 }, 1, default));
            Assert.False(await first.TryDeleteAsync("cpu-one", 1, stored.TokenHash, default));
            Assert.Equal(AgentCredentials.Hash(replacement.AgentToken), (await second.FindAsync("cpu-one", default))!.TokenHash);
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    [MongoFact]
    public async Task TombstoneAtomicallyRejectsDelayedEnrollmentAfterMultipleGenerationsAndRetainsBoundedHistory()
    {
        var database = $"arkya_inference_test_{Guid.NewGuid():N}";
        var settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        var client = new MongoClient(settings);
        var options = Options.Create(new MongoOptions { DatabaseName = database });
        try
        {
            var agents = new MongoInferenceRepository(client, options);
            var grants = new MongoEnrollmentGrantRepository(client, options);
            await Task.WhenAll(agents.InitializeAsync(default), grants.InitializeAsync(default));
            var clock = new TestClock { Now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()) };
            await EnrollmentTests.AssertDelayedCreationCannotResurrect(agents, grants, clock);
            var records = client.GetDatabase(database).GetCollection<BsonDocument>("agents");
            var stored = await records.Find(new BsonDocument("_id", "cpu-one")).SingleAsync();
            Assert.False(stored["Deleted"].AsBoolean);
            Assert.Equal(3, stored["RevokedTokenHashes"].AsBsonArray.Count);
            var originalHistory = stored["RevokedTokenHashes"].AsBsonArray.Select(value => value.AsString).ToArray();
            var service = new InferenceService(agents, Options.Create(new InferenceOptions()), clock, grants);
            Assert.Null(await service.DeleteAsync("cpu-one", default));
            stored = await records.Find(new BsonDocument("_id", "cpu-one")).SingleAsync();
            Assert.True(stored["Deleted"].AsBoolean);
            Assert.Equal(4, stored["RevokedTokenHashes"].AsBsonArray.Count);
            Assert.All(originalHistory, hash => Assert.Contains(hash, stored["RevokedTokenHashes"].AsBsonArray.Select(value => value.AsString)));
            var cappedHistory = Enumerable.Range(0, MongoInferenceRepository.MaxRetiredCredentials)
                .Select(_ => new BsonString(AgentCredentials.Hash(AgentCredentials.Create())));
            await records.UpdateOneAsync(new BsonDocument("_id", "cpu-one"),
                new BsonDocument("$set", new BsonDocument("RevokedTokenHashes", new BsonArray(cappedHistory))));
            Assert.Equal("agent_exists", (await service.CreateAsync(Fixtures.Create(workers: 0), default)).Error);
            Assert.Empty(await agents.ListAsync(default));
            Assert.Equal(MongoInferenceRepository.MaxRetiredCredentials,
                (await records.Find(new BsonDocument("_id", "cpu-one")).SingleAsync())["RevokedTokenHashes"].AsBsonArray.Count);
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task OldDocumentsWithoutModelOrReservationsKeepLfmAndNewConfigurationRoundTrips()
    {
        var repository = new MemoryInferenceRepository();
        var service = new InferenceService(repository, Options.Create(new InferenceOptions()), new TestClock(), new MemoryEnrollmentGrantRepository());
        var created = (await service.CreateAsync(Fixtures.Create(), default)).Value!;
        await service.HeartbeatAsync(Fixtures.Heartbeat("cpu-one", Fixtures.Observe(created.Agent.DesiredWorkers[0])), created.AgentToken, default);
        await service.RemoveWorkerAsync("cpu-one", created.Agent.DesiredWorkers[0].WorkerId, default);
        var original = (await repository.FindAsync("cpu-one", default))!;
        var old = InferenceAgentDocument.From(original).ToBsonDocument();
        old.Remove("ThreadReservations");
        foreach (var name in new[] { "DesiredWorkers", "ObservedWorkers", "RetiredWorkers" })
            foreach (var worker in old[name].AsBsonArray) worker.AsBsonDocument.Remove("ModelId");
        var restored = BsonSerializer.Deserialize<InferenceAgentDocument>(old).ToRecord();
        Assert.All(restored.DesiredWorkers, value => Assert.Equal("lfm", value.ModelId));
        Assert.All(restored.ObservedWorkers, value => Assert.Equal("lfm", value.ModelId));
        Assert.All(restored.RetiredWorkers, value => Assert.Equal("lfm", value.ModelId));
        Assert.Empty(restored.ThreadReservations);
        await service.UpdateWorkerAsync("cpu-one", original.DesiredWorkers[0].WorkerId, new(8, "qwen35-08b"), default);
        var changed = (await repository.FindAsync("cpu-one", default))!;
        var roundTrip = BsonSerializer.Deserialize<InferenceAgentDocument>(InferenceAgentDocument.From(changed).ToBsonDocument()).ToRecord();
        Assert.Equal(changed.DesiredWorkers, roundTrip.DesiredWorkers);
        Assert.Equal(changed.ThreadReservations, roundTrip.ThreadReservations);
        Assert.Equal("qwen35-08b", roundTrip.DesiredWorkers[0].ModelId);
        Assert.Equal(8, roundTrip.ThreadReservations.Single().Threads);
    }

    [MongoFact]
    public async Task EnrollmentBindingIsDurableAtomicExpiringAndStoresOnlyHashes()
    {
        var database = $"arkya_inference_test_{Guid.NewGuid():N}";
        var settings = MongoClientSettings.FromConnectionString(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);
        var client = new MongoClient(settings);
        var options = Options.Create(new MongoOptions { DatabaseName = database });
        try
        {
            var agents = new MongoInferenceRepository(client, options);
            var first = new MongoEnrollmentGrantRepository(client, options);
            var second = new MongoEnrollmentGrantRepository(client, options);
            await Task.WhenAll(agents.InitializeAsync(default), first.InitializeAsync(default), second.InitializeAsync(default));
            var clock = new TestClock { Now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()) };
            var inference = new InferenceService(agents, Options.Create(new InferenceOptions()), clock, first);
            var enrollment = new EnrollmentService(first, agents, inference, clock);
            var grant = await enrollment.CreateGrantAsync(default);
            var hash = AgentCredentials.Hash(grant.Token);
            var restored = (await second.FindAsync(hash, default))!;
            Assert.Equal(grant.ExpiresAt, restored.ExpiresAt);
            Assert.False(await second.TryCreateAsync(restored, default));
            var tokenA = AgentCredentials.Create();
            var tokenB = AgentCredentials.Create();
            var bindings = await Task.WhenAll(
                first.TryBindAsync(hash, "cpu-one", AgentCredentials.Hash(tokenA), clock.Now, default),
                second.TryBindAsync(hash, "cpu-two", AgentCredentials.Hash(tokenB), clock.Now, default));
            Assert.Single(bindings, success => success);
            restored = (await new MongoEnrollmentGrantRepository(client, options).FindAsync(hash, default))!;
            var winningToken = restored.BindingAgentId == "cpu-one" ? tokenA : tokenB;
            Assert.True(await second.TryBindAsync(hash, restored.BindingAgentId!, restored.BindingTokenHash!, clock.Now, default));
            Assert.False(await second.TryBindAsync(hash, restored.BindingAgentId!, AgentCredentials.Hash(AgentCredentials.Create()), clock.Now, default));
            Assert.False(await second.TryBindAsync(hash, restored.BindingAgentId!, restored.BindingTokenHash!, grant.ExpiresAt, default));
            var request = new RegisterInferenceAgentRequest(restored.BindingAgentId!, "Mongo CPU", winningToken, 0, 4, 6, "qwen35-08b");
            Assert.Null((await enrollment.RegisterAsync(request, grant.Token, default)).Error);
            Assert.Null((await enrollment.RegisterAsync(request, null, default)).Error);
            var raw = await client.GetDatabase(database).GetCollection<BsonDocument>("enrollment_grants")
                .Find(new BsonDocument("_id", hash)).SingleAsync();
            Assert.Equal(BsonType.DateTime, raw["ExpiresAt"].BsonType);
            Assert.Equal(1, raw["SchemaVersion"].AsInt32);
            Assert.DoesNotContain(grant.Token, raw.ToJson());
            Assert.DoesNotContain(tokenA, raw.ToJson());
            Assert.DoesNotContain(tokenB, raw.ToJson());
            var indexes = await (await client.GetDatabase(database).GetCollection<BsonDocument>("enrollment_grants").Indexes.ListAsync()).ToListAsync();
            Assert.Equal(0, Assert.Single(indexes, index => index["name"] == "ttl_enrollment_expiry")["expireAfterSeconds"].ToInt32());
            Assert.Null(await inference.DeleteAsync(request.AgentId, default));
            Assert.True((await second.FindAsync(hash, default))!.Revoked);
            Assert.Equal("unauthorized_enrollment", (await enrollment.RegisterAsync(request, grant.Token, default)).Error);
            Assert.Null(await agents.FindAsync(request.AgentId, default));

            var freshGrant = await enrollment.CreateGrantAsync(default);
            var freshRequest = new RegisterInferenceAgentRequest("cpu-fresh", "Fresh Mongo CPU", AgentCredentials.Create(), ModelId: "qwen35-08b");
            var created = (await enrollment.RegisterAsync(freshRequest, freshGrant.Token, default)).Value!;
            var worker = created.DesiredWorkers[0];
            Assert.Null((await inference.UpdateWorkerAsync("cpu-fresh", worker.WorkerId, new(8, "lfm"), default)).Error);
            var persisted = (await new MongoInferenceRepository(client, options).FindAsync("cpu-fresh", default))!;
            Assert.Equal(worker.WorkerId, persisted.DesiredWorkers[0].WorkerId);
            Assert.Equal("lfm", persisted.DesiredWorkers[0].ModelId);
            Assert.Equal(8, persisted.DesiredWorkers[0].Threads);
            Assert.Equal(8, persisted.ThreadReservations.Single().Threads);
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
