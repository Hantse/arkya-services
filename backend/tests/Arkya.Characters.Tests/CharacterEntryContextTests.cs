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

public sealed class CharacterEntryContextTests
{
    private static readonly Guid Account = IdentityStub.AliceId;
    private static readonly DedicatedServerIdentity Server = new()
        { RealmId = "beta-web", ServerId = "starter-island", Key = "dedicated-entry-test-key-with-thirty-two-characters" };
    private static readonly DedicatedServerIdentity OtherServer = new()
        { RealmId = "other-realm", ServerId = "starter-island", Key = "other-entry-test-key-with-thirty-two-characters" };
    private static readonly string Reference = SessionReference.Create("alice");

    private sealed class Scenario
    {
        public MemoryCharacterRepository Characters { get; } = new();
        public GameClock Clock { get; } = new();
        public IGameLeaseStore Store { get; }
        public GameAdmissionService Service { get; }
        public Scenario(IGameLeaseStore? store = null)
        {
            Store = store ?? new MemoryGameLeaseStore();
            var options = Options.Create(new CharacterGameAccessOptions { DedicatedServers = [Server, OtherServer] });
            Service = new(Characters, Store, new TestGameCatalog(), new(options), Clock, new TestSessionReferences(Clock, Account), options);
        }
        public async Task<Character> Create(string name, Guid? accountId = null) =>
            (await new CharacterService(Characters, Clock).CreateAsync(accountId ?? Account, name, default)).Character!;
        public async Task<AdmissionResponse> Issue(Character character, DedicatedServerIdentity? server = null) =>
            (await Service.IssueAsync(character.Id, Account, Clock.Now.AddHours(1), Reference,
                new((server ?? Server).RealmId, GameProtocol.Version, GameProtocol.BuildId), default)).Value!;
        public async Task<ConsumedAdmissionResponse> Enter(Character character, DedicatedServerIdentity? server = null)
        {
            var ticket = await Issue(character, server);
            return (await Service.ConsumeAsync(server ?? Server, new(ticket.Ticket), default)).Value!;
        }
    }

    [Fact]
    public async Task EmptyAccountAndHistoricalCharactersHaveNoInventedLastRealm()
    {
        var f = new Scenario();
        Assert.Equal(new(false, null, null, null), await f.Service.EntryContextAsync(Account, default));
        var character = await f.Create("Aelys");
        await f.Characters.TryBindRealmAsync(character.Id, Account, Server.RealmId, default);
        Assert.Equal(new(true, null, null, null), await f.Service.EntryContextAsync(Account, default));
        Assert.Equal(new(false, null, null, null), await f.Service.EntryContextAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task OnlySuccessfulConsumeRecordsConnectionAndLeaseOperationsPreserveIt()
    {
        var f = new Scenario();
        var character = await f.Create("Aelys");
        var ticket = await f.Issue(character);
        Assert.Null((await f.Service.EntryContextAsync(Account, default)).LastRealmId);
        Assert.Equal("admission_rejected", (await f.Service.ConsumeAsync(OtherServer, new(ticket.Ticket), default)).Error);
        Assert.Null((await f.Service.EntryContextAsync(Account, default)).LastRealmId);

        var enteredAt = f.Clock.Now;
        var entered = (await f.Service.ConsumeAsync(Server, new(ticket.Ticket), default)).Value!;
        var expected = new CharacterEntryContextResponse(true, Server.RealmId, character.Id, enteredAt);
        Assert.Equal(expected, await f.Service.EntryContextAsync(Account, default));
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        Assert.Equal("admission_rejected", (await f.Service.ConsumeAsync(Server, new(ticket.Ticket), default)).Error);
        Assert.Null((await f.Service.HeartbeatAsync(character.Id, Server, new(entered.LeaseId, entered.AuthorityEpoch), default)).Error);
        Assert.Null((await f.Service.SaveAsync(character.Id, Server,
            new(entered.LeaseId, entered.AuthorityEpoch, 0, new(1, 2, 3), new(0, 1)), default)).Error);
        Assert.True(await f.Service.ReleaseAsync(character.Id, Server, new(entered.LeaseId, entered.AuthorityEpoch), default));
        var replacement = await f.Issue(character);
        Assert.Equal(expected, await f.Service.EntryContextAsync(Account, default));
        f.Clock.Now = f.Clock.Now.AddSeconds(61);
        Assert.Equal("admission_rejected", (await f.Service.ConsumeAsync(Server, new(replacement.Ticket), default)).Error);
        Assert.Equal(expected, await f.Service.EntryContextAsync(Account, default));
        await f.Enter(character);
        Assert.Equal(expected with { LastConnectedAt = f.Clock.Now }, await f.Service.EntryContextAsync(Account, default));
    }

    [Fact]
    public async Task LatestOwnedCharacterWinsAndDeletingItFallsBackToPreviousConnection()
    {
        var f = new Scenario();
        var first = await f.Create("Aelys");
        var second = await f.Create("Eloria");
        await f.Enter(first);
        var firstContext = await f.Service.EntryContextAsync(Account, default);
        f.Clock.Now = f.Clock.Now.AddSeconds(1);
        await f.Enter(second, OtherServer);
        Assert.Equal(new(true, OtherServer.RealmId, second.Id, f.Clock.Now), await f.Service.EntryContextAsync(Account, default));
        Assert.Equal(new(false, null, null, null), await f.Service.EntryContextAsync(IdentityStub.BobId, default));
        await f.Create("Bobara", IdentityStub.BobId);
        Assert.Equal(new(true, null, null, null), await f.Service.EntryContextAsync(IdentityStub.BobId, default));
        f.Characters.Remove(second.Id);
        Assert.Equal(firstContext, await f.Service.EntryContextAsync(Account, default));
        f.Characters.Remove(first.Id);
        Assert.Equal(new(false, null, null, null), await f.Service.EntryContextAsync(Account, default));
    }

    [Fact]
    public async Task EqualConnectionTimesUseStableCharacterIdOrdering()
    {
        var f = new Scenario();
        var first = await f.Create("Aelys");
        var second = await f.Create("Eloria");
        await f.Enter(first);
        await f.Enter(second, OtherServer);
        var expected = new[] { first.Id, second.Id }.OrderBy(value => value.ToString("D"), StringComparer.Ordinal).First();
        Assert.Equal(expected, (await f.Service.EntryContextAsync(Account, default)).LastCharacterId);
    }

    [Fact]
    public async Task EntryContextIsSessionProtectedReadOnlyAndChangesOnlyAfterDedicatedAdmission()
    {
        await using var factory = new CharactersFactory
        {
            AdditionalConfiguration = new Dictionary<string, string?>
            {
                ["WebClient:AllowedOrigins:0"] = "https://site.example",
                ["GameAccess:DedicatedServers:0:RealmId"] = Server.RealmId,
                ["GameAccess:DedicatedServers:0:ServerId"] = Server.ServerId,
                ["GameAccess:DedicatedServers:0:Key"] = Server.Key
            },
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IGameLeaseStore>(); services.AddSingleton<IGameLeaseStore, MemoryGameLeaseStore>();
                services.RemoveAll<IGameRealmCatalog>(); services.AddSingleton<IGameRealmCatalog, TestGameCatalog>();
            }
        };
        const string route = "/v1/characters/entry-context";
        using var anonymous = factory.PlayerClient(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(route)).StatusCode);
        using var alice = factory.PlayerClient();
        using var bob = factory.PlayerClient("bob");
        using var empty = await alice.GetAsync(route);
        Assert.Equal("no-store", empty.Headers.CacheControl!.ToString());
        Assert.Equal(new(false, null, null, null), await empty.Content.ReadFromJsonAsync<CharacterEntryContextResponse>());
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await alice.PostAsJsonAsync(route, new { lastRealmId = "forged" })).StatusCode);
        var character = (await (await alice.PostAsJsonAsync("/v1/characters", new { name = "Aelys" }))
            .Content.ReadFromJsonAsync<CharacterResponse>())!;
        var ticket = (await (await alice.PostAsJsonAsync($"/v1/characters/{character.Id}/admissions",
            new AdmissionRequest(Server.RealmId, GameProtocol.Version, GameProtocol.BuildId))).Content.ReadFromJsonAsync<AdmissionResponse>())!;
        Assert.Equal(new(true, null, null, null), await alice.GetFromJsonAsync<CharacterEntryContextResponse>(route));
        anonymous.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", Server.Key);
        Assert.Equal(HttpStatusCode.OK, (await anonymous.PostAsJsonAsync("/internal/game/admissions/consume", new ConsumeAdmissionRequest(ticket.Ticket))).StatusCode);
        Assert.Equal(new(true, Server.RealmId, character.Id, FixedTimeProvider.Now), await alice.GetFromJsonAsync<CharacterEntryContextResponse>(route));
        Assert.Equal(new(false, null, null, null), await bob.GetFromJsonAsync<CharacterEntryContextResponse>(route));
        using var preflight = new HttpRequestMessage(HttpMethod.Options, route);
        preflight.Headers.Add("Origin", "https://site.example");
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");
        using var cors = await anonymous.SendAsync(preflight);
        Assert.Equal("https://site.example", cors.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [MongoFact]
    public async Task MongoRetainsConnectionHistoryAcrossRestartAndReadsLegacyLeasesWithoutHistory()
    {
        var database = "arkya_entry_context_test_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        try
        {
            var options = Options.Create(new MongoOptions { DatabaseName = database });
            var store = new MongoGameLeaseStore(client, options);
            await store.EnsureIndexesAsync(default);
            var f = new Scenario(store);
            f.Clock.Now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            var first = await f.Create("Aelys");
            var second = await f.Create("Eloria");
            var lease = await f.Enter(first);
            var firstContext = await f.Service.EntryContextAsync(Account, default);
            Assert.True(await f.Service.ReleaseAsync(first.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch), default));
            await f.Issue(first);
            Assert.Equal(firstContext, await f.Service.EntryContextAsync(Account, default));
            await f.Enter(second, OtherServer);
            var restarted = new MongoGameLeaseStore(client, options);
            var last = await restarted.FindLastConnectedAsync(Account, [first.Id, second.Id], default);
            var expectedId = new[] { first.Id, second.Id }.OrderBy(value => value.ToString("D"), StringComparer.Ordinal).First();
            Assert.Equal(expectedId, last!.CharacterId);
            Assert.Equal(f.Clock.Now, last.LastConnectedAt);
            Assert.Null(await restarted.FindLastConnectedAsync(IdentityStub.BobId, [first.Id, second.Id], default));
            Assert.Null(await restarted.FindLastConnectedAsync(Account, [], default));
            Assert.Equal(first.Id, (await restarted.FindLastConnectedAsync(Account, [first.Id], default))!.CharacterId);

            var collection = client.GetDatabase(database).GetCollection<BsonDocument>("game_leases");
            var documents = await collection.Find(FilterDefinition<BsonDocument>.Empty).ToListAsync();
            Assert.All(documents, value => Assert.Equal(3, value["SchemaVersion"].AsInt32));
            using var indexCursor = await collection.Indexes.ListAsync();
            var indexes = await indexCursor.ToListAsync();
            Assert.Contains(indexes, value => value["name"] == "ix_account_last_connection");
            Assert.DoesNotContain(indexes, value => value.Contains("expireAfterSeconds"));
            await collection.UpdateManyAsync(FilterDefinition<BsonDocument>.Empty,
                Builders<BsonDocument>.Update.Unset("LastConnectedAt").Set("SchemaVersion", 2));
            Assert.Null((await restarted.FindCharacterAsync(first.Id, default))!.LastConnectedAt);
            Assert.Null(await restarted.FindLastConnectedAsync(Account, [first.Id, second.Id], default));
        }
        finally { await client.DropDatabaseAsync(database); }
    }
}
