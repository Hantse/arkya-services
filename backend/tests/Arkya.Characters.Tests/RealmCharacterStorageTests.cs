using System.Net;
using System.Net.Http.Json;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.Characters.Api.Domain;
using Arkya.Characters.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Arkya.Characters.Tests;

public sealed class RealmCharacterStorageTests
{
    [Fact]
    public void RoutesRejectAmbiguousDestinationsAndUnsafeNames()
    {
        var options = new RealmStorageOptions { Enabled = true };
        Assert.False(options.IsValid("directory"));
        options.Realms = [Route("realm-a", "alpha"), Route("realm-b", "bravo")];
        Assert.True(options.IsValid("directory"));
        options.Realms[1].DatabaseName = "ALPHA";
        Assert.False(options.IsValid("directory"));
        options.Realms[1] = Route("realm-a", "bravo");
        Assert.False(options.IsValid("directory"));
        options.Realms = [Route("../realm", "alpha")];
        Assert.False(options.IsValid("directory"));
        options.Realms = [Route("realm-a", "directory")];
        Assert.False(options.IsValid("directory"));
        options.Realms = [Route("realm-a", "db.$other")];
        Assert.False(options.IsValid("directory"));
        options.Realms = [Route("realm-a", "alpha")];
        options.Realms[0].ConnectionString = "https://example.com";
        Assert.False(options.IsValid("directory"));
    }

    [MongoFact]
    public async Task CreationReservesNamesGloballyButKeepsCanonicalCharactersInTheirRealm()
    {
        await using var fixture = new Fixture();
        var provider = fixture.Provider();
        var repository = new MongoCharacterRepository(provider);
        await repository.EnsureIndexesAsync(default);
        var service = new CharacterService(repository, new FixedTimeProvider());
        var owner = Guid.NewGuid();
        var first = (await service.CreateAsync(owner, "Aeria", null, default, "beta-web")).Character!;
        var second = (await service.CreateAsync(owner, "Brina", null, default, "other-realm")).Character!;
        var race = await Task.WhenAll(service.CreateAsync(owner, "Eloria", null, default, "beta-web"),
            service.CreateAsync(Guid.NewGuid(), " eloria ", null, default, "other-realm"));
        Assert.Single(race, value => value.Status == CreateCharacterStatus.Created);
        Assert.Single(race, value => value.Status == CreateCharacterStatus.NameTaken);
        Assert.Equal(first, await repository.FindOwnedAsync(first.Id, owner, default));
        Assert.Null(await repository.FindOwnedAsync(first.Id, Guid.NewGuid(), default));
        Assert.False(await repository.TryBindRealmAsync(first.Id, owner, "other-realm", default));
        Assert.True(await repository.TryBindRealmAsync(first.Id, owner, "beta-web", default));
        Assert.True(await repository.ExistsAsync(second.Id, default));
        Assert.True(await repository.TryAddAsync(first, default));
        Assert.False(await repository.TryAddAsync(first with { Level = 2 }, default));
        Assert.Equal(0, await provider.DirectoryDatabase.GetCollection<BsonDocument>("characters").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        var entries = await provider.DirectoryDatabase.GetCollection<BsonDocument>("character_directory")
            .Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
        Assert.Equal(3, entries.Count);
        Assert.All(entries, value =>
        {
            Assert.Equal("Ready", value["Status"].AsString);
            Assert.False(value.Contains("CreationIntent"));
            Assert.False(value.Contains("Appearance"));
            Assert.False(value.Contains("Level"));
        });
        var restarted = new MongoCharacterRepository(fixture.Provider());
        Assert.Contains(first, await restarted.ListOwnedAsync(owner, default));
        Assert.Contains(second, await restarted.ListOwnedAsync(owner, default));
        Assert.Null(await provider.GetRealmDatabase("other-realm").GetCollection<BsonDocument>("characters")
            .Find(new BsonDocument("_id", first.Id.ToString("D"))).FirstOrDefaultAsync());
    }

    [MongoFact]
    public async Task PendingCreationCanResumeAfterEitherSideOfTheRealmInsertWithoutOverwritingState()
    {
        await using var fixture = new Fixture();
        var provider = fixture.Provider();
        var character = new Character(Guid.NewGuid(), Guid.NewGuid(), "Aeria", "AERIA", 1, FixedTimeProvider.Now) { RealmId = "beta-web" };
        var intent = CharacterDocument.From(character).ToBsonDocument();
        var directory = provider.DirectoryDatabase.GetCollection<BsonDocument>("character_directory");
        var entry = new BsonDocument
        {
            ["_id"] = character.Id.ToString("D"), ["AccountId"] = character.AccountId.ToString("D"),
            ["RealmId"] = character.RealmId, ["NormalizedName"] = character.NormalizedName,
            ["Status"] = "Pending", ["CreationIntent"] = intent
        };
        await directory.InsertOneAsync(entry);
        var repository = new MongoCharacterRepository(provider);
        Assert.Equal(character, Assert.Single(await repository.ListOwnedAsync(character.AccountId, default)));
        var ready = await directory.Find(new BsonDocument("_id", character.Id.ToString("D"))).SingleAsync();
        Assert.Equal("Ready", ready["Status"].AsString);
        Assert.False(ready.Contains("CreationIntent"));
        await directory.ReplaceOneAsync(new BsonDocument("_id", character.Id.ToString("D")), entry);
        var canonical = provider.GetRealmDatabase("beta-web").GetCollection<BsonDocument>("characters");
        await canonical.UpdateOneAsync(new BsonDocument("_id", character.Id.ToString("D")), Builders<BsonDocument>.Update.Set("Level", 7));
        Assert.Equal(7, (await new MongoCharacterRepository(fixture.Provider()).FindOwnedAsync(character.Id, character.AccountId, default))!.Level);
        await directory.UpdateOneAsync(new BsonDocument("_id", character.Id.ToString("D")), Builders<BsonDocument>.Update.Set("RealmId", "missing-realm"));
        await Assert.ThrowsAsync<RealmStorageUnavailableException>(() => repository.FindOwnedAsync(character.Id, character.AccountId, default));
    }

    [MongoFact]
    public async Task PublicAndDedicatedHttpFlowsUseRealmDatabasesAndRejectCrossRealmTickets()
    {
        await using var fixture = new Fixture();
        var provider = fixture.Provider();
        await new MongoCharacterRepository(provider).EnsureIndexesAsync(default);
        await new MongoGameLeaseStore(provider).EnsureIndexesAsync(default);
        await new MongoCharacterQuestRepository(provider).EnsureIndexesAsync(default);
        await using var factory = new CharactersFactory
        {
            AdditionalConfiguration = fixture.Configuration(),
            ConfigureTestServices = services =>
            {
                services.RemoveAll<ICharacterRepository>();
                services.AddSingleton<ICharacterRepository>(sp => sp.GetRequiredService<MongoCharacterRepository>());
                services.RemoveAll<ICharacterQuestRepository>();
                services.AddSingleton<ICharacterQuestRepository>(sp => sp.GetRequiredService<MongoCharacterQuestRepository>());
                services.RemoveAll<TimeProvider>();
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton<ISessionValidator, CurrentSessions>();
                services.AddSingleton<ISessionReferenceValidator, CurrentSessions>();
                services.AddSingleton<IGameRealmCatalog, TestGameCatalog>();
            }
        };
        using var player = factory.PlayerClient();
        using var firstResponse = await player.PostAsJsonAsync("/v1/characters", new { name = "Aeria", realmId = "beta-web" });
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var first = (await firstResponse.Content.ReadFromJsonAsync<CharacterResponse>())!;
        using var secondResponse = await player.PostAsJsonAsync("/v1/characters", new { name = "Brina", realmId = "other-realm" });
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.Equal(2, (await player.GetFromJsonAsync<CharacterResponse[]>("/v1/characters"))!.Length);
        Assert.Equal(HttpStatusCode.BadRequest, (await player.PostAsJsonAsync("/v1/characters", new { name = "Norealm" })).StatusCode);
        using var issued = await player.PostAsJsonAsync($"/v1/characters/{first.Id:D}/admissions", new AdmissionRequest("beta-web", 1, GameProtocol.BuildId));
        Assert.Equal(HttpStatusCode.OK, issued.StatusCode);
        var ticket = (await issued.Content.ReadFromJsonAsync<AdmissionResponse>())!;
        using var wrongServer = factory.PlayerClient(null);
        wrongServer.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", OtherKey);
        Assert.Equal(HttpStatusCode.Conflict, (await wrongServer.PostAsJsonAsync("/internal/game/admissions/consume", new ConsumeAdmissionRequest(ticket.Ticket))).StatusCode);
        using var server = factory.PlayerClient(null);
        server.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", FirstKey);
        using var consumedResponse = await server.PostAsJsonAsync("/internal/game/admissions/consume", new ConsumeAdmissionRequest(ticket.Ticket));
        Assert.Equal(HttpStatusCode.OK, consumedResponse.StatusCode);
        var admission = (await consumedResponse.Content.ReadFromJsonAsync<ConsumedAdmissionResponse>())!;
        var save = new SaveWorldStateRequest(admission.LeaseId, admission.AuthorityEpoch, 0, new(10, 32, 20), new(0, 1));
        Assert.Equal(HttpStatusCode.OK, (await server.PutAsJsonAsync($"/internal/game/leases/{first.Id:D}/state", save)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await wrongServer.PutAsJsonAsync($"/internal/game/leases/{first.Id:D}/state", save)).StatusCode);
        Assert.Equal(1, (await new MongoGameLeaseStore(fixture.Provider()).FindCharacterAsync(first.Id, default))!.State!.Revision);
        Assert.Equal(0, await provider.DirectoryDatabase.GetCollection<BsonDocument>("game_leases").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(1, await provider.GetRealmDatabase("beta-web").GetCollection<BsonDocument>("game_leases").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(0, await provider.GetRealmDatabase("other-realm").GetCollection<BsonDocument>("game_leases").CountDocumentsAsync(FilterDefinition<BsonDocument>.Empty));
        Assert.Equal(HttpStatusCode.NoContent, (await server.PostAsJsonAsync($"/internal/game/leases/{first.Id:D}/release", new LeaseRequest(admission.LeaseId, admission.AuthorityEpoch))).StatusCode);
        var stored = await provider.GetRealmDatabase("beta-web").GetCollection<BsonDocument>("characters")
            .Find(new BsonDocument("_id", first.Id.ToString("D"))).SingleAsync();
        await provider.LegacyDatabase.GetCollection<BsonDocument>("characters").InsertOneAsync(stored);
        await provider.DirectoryDatabase.GetCollection<BsonDocument>("character_directory").UpdateOneAsync(
            new BsonDocument("_id", first.Id.ToString("D")), Builders<BsonDocument>.Update.Set("RealmId", "missing-realm"));
        using var unavailable = await player.GetAsync($"/v1/characters/{first.Id:D}");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, unavailable.StatusCode);
        Assert.DoesNotContain("mongodb://", await unavailable.Content.ReadAsStringAsync());
    }

    private const string FirstKey = "first-realm-dedicated-key-at-least-32-characters";
    private const string OtherKey = "other-realm-dedicated-key-at-least-32-characters";
    private static RealmStorageRoute Route(string realm, string database) => new()
    { RealmId = realm, DatabaseName = database, ConnectionString = "mongodb://localhost:27017" };

    private sealed class CurrentSessions : ISessionValidator, ISessionReferenceValidator
    {
        public Task<SessionValidationResult> ValidateAsync(string token, CancellationToken ct) =>
            Task.FromResult(new SessionValidationResult(SessionValidationStatus.Valid,
                new(token == "bob" ? IdentityStub.BobId : IdentityStub.AliceId, "Player", DateTimeOffset.UtcNow.AddHours(1))));
        public Task<SessionValidationResult> ValidateReferenceAsync(string reference, CancellationToken ct) => ValidateAsync("alice", ct);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string connection = Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")!;
        private readonly string prefix = "arkya_storage_" + Guid.NewGuid().ToString("N");
        private readonly MongoClient client;
        public Fixture() => client = new MongoClient(connection);
        public RealmMongoDatabaseProvider Provider() => new(client,
            Options.Create(new MongoOptions { DatabaseName = prefix, ConnectionString = connection }),
            Options.Create(new RealmStorageOptions { Enabled = true, Realms =
            [
                new() { RealmId = "beta-web", DatabaseName = prefix + "_a", ConnectionString = connection },
                new() { RealmId = "other-realm", DatabaseName = prefix + "_b", ConnectionString = connection }
            ] }));
        public Dictionary<string, string?> Configuration() => new()
        {
            ["Mongo:ConnectionString"] = connection, ["Mongo:DatabaseName"] = prefix,
            ["RealmStorage:Enabled"] = "true",
            ["RealmStorage:Realms:0:RealmId"] = "beta-web", ["RealmStorage:Realms:0:DatabaseName"] = prefix + "_a",
            ["RealmStorage:Realms:0:ConnectionString"] = connection,
            ["RealmStorage:Realms:1:RealmId"] = "other-realm", ["RealmStorage:Realms:1:DatabaseName"] = prefix + "_b",
            ["RealmStorage:Realms:1:ConnectionString"] = connection,
            ["GameAccess:DedicatedServers:0:RealmId"] = "beta-web", ["GameAccess:DedicatedServers:0:ServerId"] = "starter-island",
            ["GameAccess:DedicatedServers:0:Key"] = FirstKey,
            ["GameAccess:DedicatedServers:1:RealmId"] = "other-realm", ["GameAccess:DedicatedServers:1:ServerId"] = "starter-island",
            ["GameAccess:DedicatedServers:1:Key"] = OtherKey
        };
        public async ValueTask DisposeAsync()
        {
            foreach (var name in new[] { prefix, prefix + "_a", prefix + "_b" }) await client.DropDatabaseAsync(name);
        }
    }
}
