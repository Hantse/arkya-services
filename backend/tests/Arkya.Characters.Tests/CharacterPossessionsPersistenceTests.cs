using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.Characters.Api.Domain;
using Arkya.Characters.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Characters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Arkya.Characters.Tests;

public sealed class CharacterPossessionsPersistenceTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly ActorStateEnvelope Simulation = new(1, "{\"version\":2,\"health\":77}");
    private static readonly ActorStateEnvelope ArrivalSimulation = new(1, "{\"version\":2,\"health\":77,\"supportHeight\":44}");
    private static readonly GameAreaTransition Transition = new("starter-to-forest", "starter-island", "forest-area",
        new(400, 200, 450, 300), new(750, 44, 250), new(0, 1));

    private sealed class PossessionsCatalog : IGameRealmCatalog
    {
        public static readonly DedicatedServerIdentity[] Servers = new[] { "source-west", "source-east", "target-west", "target-east" }
            .Select(id => new DedicatedServerIdentity
            {
                RealmId = "beta-web", ServerId = id, Key = "possessions-dedicated-test-key-1234567890-" + id
            }).ToArray();

        public GameRealmEntry Entry { get; } = new("beta-web", "Possessions realm", true, Servers[0].ServerId,
            "wss://play.example/source-west", GameProtocol.MultiAreaVersion, GameProtocol.MultiAreaBuildId)
        {
            TopologyVersion = "possessions-two-areas-v1",
            Areas = [new("starter-island", "starter-island"), new("forest-area", "forest-content")],
            AreaTransitions = [Transition],
            Partitions = Servers.Select((server, index) => new GameRealmPartition(server.ServerId,
                "wss://play.example/" + server.ServerId, index < 2 ? "starter-island" : "forest-area", index % 2,
                new(index % 2 * 500, 0, index % 2 * 500 + 500, 1000))).ToArray()
        };

        public Task<GameRealmCatalog> ListAsync(CancellationToken cancellationToken) => Task.FromResult(new GameRealmCatalog([Entry]));
    }

    private sealed class Scenario(IGameLeaseStore? store = null)
    {
        public Guid Account { get; } = Guid.NewGuid();
        public Character Character { get; private set; } = null!;
        public GameClock Clock { get; } = new();
        public MemoryCharacterRepository Characters { get; } = new();
        public IGameLeaseStore Store { get; set; } = store ?? new MemoryGameLeaseStore();
        public PossessionsCatalog Catalog { get; } = new();
        public ConsumedAdmissionResponse Lease { get; private set; } = null!;
        public DedicatedServerIdentity Source => PossessionsCatalog.Servers[0];
        public DedicatedServerIdentity PartitionTarget => PossessionsCatalog.Servers[1];
        public DedicatedServerIdentity AreaTarget => PossessionsCatalog.Servers[3];
        public GameAdmissionService Service => new(Characters, Store, Catalog,
            new DedicatedKeyValidator(Options.Create(new CharacterGameAccessOptions { DedicatedServers = PossessionsCatalog.Servers })),
            Clock, new TestSessionReferences(Clock, Account), Options.Create(new CharacterGameAccessOptions()));

        public async Task Initialize()
        {
            Character = (await new CharacterService(Characters, Clock).CreateAsync(Account, "Aelys", default)).Character!;
            Lease = await Enter(Source);
        }

        public async Task<ConsumedAdmissionResponse> Enter(DedicatedServerIdentity server)
        {
            var issued = await Service.IssueAsync(Character.Id, Account, Clock.Now.AddHours(1), SessionReference.Create("possessions-test-session"),
                new("beta-web", GameProtocol.MultiAreaVersion, GameProtocol.MultiAreaBuildId), default);
            Assert.Null(issued.Error);
            Assert.Equal(server.ServerId, issued.Value!.ServerId);
            var consumed = await Service.ConsumeAsync(server, new(issued.Value.Ticket), default);
            Assert.Null(consumed.Error);
            return consumed.Value!;
        }

        public SaveWorldStateRequest Save(long revision = 0, CharacterPossessionsSnapshot? possessions = null) =>
            new(Lease.LeaseId, Lease.AuthorityEpoch, revision, new(425, 32, 250), new(1, 0))
            { Simulation = Simulation, Possessions = possessions };

        public PrepareTransferRequest Prepare(bool interArea, CharacterPossessionsSnapshot? possessions = null) =>
            new(Guid.NewGuid(), Lease.LeaseId, Lease.AuthorityEpoch,
                interArea ? AreaTarget.ServerId : PartitionTarget.ServerId, Catalog.Entry.TopologyVersion!,
                interArea ? new(425, 32, 250) : new(499, 32, 250), new(1, 0), Simulation)
            { TransitionId = interArea ? Transition.Id : null, Possessions = possessions };

        public SealTransferRequest Seal(bool interArea, long revision, CharacterPossessionsSnapshot? possessions = null) =>
            new(Lease.LeaseId, Lease.AuthorityEpoch, revision, interArea ? new(440, 33, 250) : new(500, 33, 250), new(1, 0), Simulation)
            { ArrivalSimulation = interArea ? ArrivalSimulation : null, Possessions = possessions };
    }

    private static CharacterPossessionsSnapshot Snapshot(int woodQuantity = 7) => new(1,
        new(150, [new(0, "resource:wood", woodQuantity), new(149, "painted-travel-rations", 3)]),
        new(80, [new(79, "resource:enchanted-crystal", 11), new(4, "old-copper-ring", 1)]),
        [new("main-hand", "valley-sword", 1), new("chest", "traveller-tunic", 1)])
    {
        HarvestTool = new("ore", 4, 1250, 200, 2, [new(2500, 5000), new(500, 250)])
    };

    private static T WireClone<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Json), Json)!;

    private static void AssertSnapshot(CharacterPossessionsSnapshot expected, CharacterPossessionsSnapshot? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(JsonSerializer.Serialize(expected, Json), JsonSerializer.Serialize(actual, Json));
    }

    [Fact]
    public async Task NonemptyBagsBankEquipmentAndSocketedToolSurviveSaveReleaseAndReadmission()
    {
        var f = new Scenario(); await f.Initialize();
        Assert.Null(f.Lease.State?.Possessions);
        var expected = Snapshot();
        var save = f.Save(possessions: expected);
        var saved = await f.Service.SaveAsync(f.Character.Id, f.Source, save, default);
        Assert.Null(saved.Error);
        Assert.Equal(1, saved.Value!.Revision);
        AssertSnapshot(expected, saved.Value.Possessions);
        AssertSnapshot(expected, (await f.Store.FindCharacterAsync(f.Character.Id, default))!.State!.Possessions);

        var retry = await f.Service.SaveAsync(f.Character.Id, f.Source, WireClone(save), default);
        Assert.Null(retry.Error);
        Assert.Equal(saved.Value.Revision, retry.Value!.Revision);
        AssertSnapshot(expected, retry.Value.Possessions);
        var reordered = expected with
        {
            Bags = expected.Bags with { Slots = expected.Bags.Slots.Reverse().ToArray() },
            Bank = expected.Bank with { Slots = expected.Bank.Slots.Reverse().ToArray() },
            Equipment = expected.Equipment.Reverse().ToArray()
        };
        var reorderedRetry = await f.Service.SaveAsync(f.Character.Id, f.Source, save with { Possessions = reordered }, default);
        Assert.Null(reorderedRetry.Error);
        Assert.Equal(1, reorderedRetry.Value!.Revision);
        Assert.Equal("state_conflict", (await f.Service.SaveAsync(f.Character.Id, f.Source,
            save with { Possessions = Snapshot(99) }, default)).Error);

        var positionOnly = await f.Service.SaveAsync(f.Character.Id, f.Source, f.Save(1) with { Position = new(430, 32, 250) }, default);
        Assert.Null(positionOnly.Error);
        AssertSnapshot(expected, positionOnly.Value!.Possessions);
        Assert.True(await f.Service.ReleaseAsync(f.Character.Id, f.Source, new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default));
        var restored = await f.Enter(f.Source);
        Assert.Equal(f.Lease.AuthorityEpoch + 1, restored.AuthorityEpoch);
        Assert.Equal(2, restored.State!.Revision);
        Assert.Equal(positionOnly.Value.Position, restored.State.Position);
        AssertSnapshot(expected, restored.State.Possessions);
        Assert.Equal("lease_rejected", (await f.Service.SaveAsync(f.Character.Id, f.Source, save with { Possessions = Snapshot(100) }, default)).Error);
        AssertSnapshot(expected, (await f.Store.FindCharacterAsync(f.Character.Id, default))!.State!.Possessions);
    }

    [Fact]
    public async Task ExplicitEmptySnapshotClearsPersistedItemsWhileLegacyOmissionKeepsThem()
    {
        var f = new Scenario(); await f.Initialize();
        Assert.Null((await f.Service.SaveAsync(f.Character.Id, f.Source, f.Save(), default)).Value!.Possessions);
        Assert.Null((await f.Service.SaveAsync(f.Character.Id, f.Source, f.Save(1, Snapshot()), default)).Error);
        var empty = new CharacterPossessionsSnapshot(1, new(150, []), new(80, []), []);
        var cleared = await f.Service.SaveAsync(f.Character.Id, f.Source, f.Save(2, empty), default);
        Assert.Null(cleared.Error);
        AssertSnapshot(empty, cleared.Value!.Possessions);
        Assert.True(await f.Service.ReleaseAsync(f.Character.Id, f.Source, new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default));
        AssertSnapshot(empty, (await f.Enter(f.Source)).State!.Possessions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandoffKeepsPreparedAndFinalPossessionsAtomicAndRetriesIdempotent(bool interArea)
    {
        var f = new Scenario(); await f.Initialize();
        await AssertTransfer(f, interArea);
    }

    private static async Task AssertTransfer(Scenario f, bool interArea, Func<IGameLeaseStore>? restart = null)
    {
        var preparedItems = Snapshot();
        var finalItems = Snapshot(21);
        var target = interArea ? f.AreaTarget : f.PartitionTarget;
        Assert.Null((await f.Service.SaveAsync(f.Character.Id, f.Source, f.Save(possessions: preparedItems), default)).Error);
        var prepare = f.Prepare(interArea, preparedItems);
        var prepared = await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, prepare, default);
        Assert.Null(prepared.Error);
        Assert.Equal(prepared.Value, (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, WireClone(prepare), default)).Value);
        var prepareRetry = await f.Service.PrepareTransferAsync(f.Character.Id, f.Source,
            prepare with { Possessions = Snapshot(99) }, default);
        Assert.Null(prepareRetry.Error);
        Assert.Equal(prepared.Value, prepareRetry.Value);
        var claim = await f.Service.ClaimTransferAsync(target, new(prepared.Value!.Ticket), default);
        Assert.Null(claim.Error);
        Assert.Equal(Guid.Empty, claim.Value!.Admission.LeaseId);
        AssertSnapshot(preparedItems, claim.Value.Admission.State!.Possessions);
        var claimRetry = await f.Service.ClaimTransferAsync(target, new(prepared.Value.Ticket), default);
        Assert.Equal(claim.Value.ClaimId, claimRetry.Value!.ClaimId);
        AssertSnapshot(preparedItems, claimRetry.Value.Admission.State!.Possessions);

        var seal = f.Seal(interArea, 1, finalItems);
        var sealedState = await f.Service.SealTransferAsync(f.Character.Id, prepare.TransferId, f.Source, seal, default);
        Assert.Null(sealedState.Error);
        Assert.Equal(2, sealedState.Value!.StateRevision);
        Assert.Null((await f.Service.SealTransferAsync(f.Character.Id, prepare.TransferId, f.Source, WireClone(seal), default)).Error);
        Assert.Null((await f.Service.SealTransferAsync(f.Character.Id, prepare.TransferId, f.Source,
            seal with { Possessions = finalItems with { Equipment = finalItems.Equipment.Reverse().ToArray() } }, default)).Error);
        Assert.Equal("transfer_conflict", (await f.Service.SealTransferAsync(f.Character.Id, prepare.TransferId, f.Source,
            seal with { Possessions = Snapshot(100) }, default)).Error);
        var sealedRecord = (await f.Store.FindCharacterAsync(f.Character.Id, default))!;
        AssertSnapshot(finalItems, sealedRecord.State!.Possessions);
        AssertSnapshot(finalItems, sealedRecord.Transfer!.State.Possessions);
        if (interArea)
        {
            AssertSnapshot(finalItems, sealedRecord.Transfer.SourceState!.Possessions);
            Assert.Equal("starter-island", sealedRecord.State.IslandId);
            Assert.Equal("forest-area", sealedRecord.Transfer.State.IslandId);
            Assert.Equal(ArrivalSimulation, sealedRecord.Transfer.State.Simulation);
        }
        Assert.Equal("transfer_sealed", (await f.Service.SaveAsync(f.Character.Id, f.Source, f.Save(2, Snapshot(100)), default)).Error);
        if (restart is not null)
        {
            f.Store = restart();
            var durable = (await f.Store.FindCharacterAsync(f.Character.Id, default))!;
            AssertSnapshot(finalItems, durable.State!.Possessions);
            AssertSnapshot(finalItems, durable.Transfer!.State.Possessions);
            if (interArea) AssertSnapshot(finalItems, durable.Transfer.SourceState!.Possessions);
        }
        var commit = await f.Service.CommitTransferAsync(f.Character.Id, prepare.TransferId, target, new(claim.Value.ClaimId), default);
        Assert.Null(commit.Error);
        var retry = await f.Service.CommitTransferAsync(f.Character.Id, prepare.TransferId, target, new(claim.Value.ClaimId), default);
        Assert.Null(retry.Error);
        Assert.Equal(commit.Value!.LeaseId, retry.Value!.LeaseId);
        Assert.Equal(commit.Value.AuthorityEpoch, retry.Value.AuthorityEpoch);
        Assert.Equal(f.Lease.AuthorityEpoch + 1, commit.Value.AuthorityEpoch);
        Assert.Equal(2, commit.Value.State!.Revision);
        AssertSnapshot(finalItems, commit.Value.State.Possessions);
        AssertSnapshot(finalItems, retry.Value.State!.Possessions);
        Assert.Equal("lease_rejected", (await f.Service.SaveAsync(f.Character.Id, f.Source, f.Save(2, Snapshot(100)), default)).Error);
        Assert.True(await f.Service.ReleaseAsync(f.Character.Id, target, new(commit.Value.LeaseId, commit.Value.AuthorityEpoch), default));
        var restored = await f.Enter(target);
        Assert.Equal(commit.Value.AuthorityEpoch + 1, restored.AuthorityEpoch);
        AssertSnapshot(finalItems, restored.State!.Possessions);
        Assert.Equal(interArea ? "forest-area" : "starter-island", restored.State.IslandId);
    }

    [Fact]
    public async Task OmittedTransferSnapshotPreservesSavedItemsAndAbortKeepsFinalSourceItems()
    {
        var f = new Scenario(); await f.Initialize();
        Assert.Null((await f.Service.SaveAsync(f.Character.Id, f.Source, f.Save(possessions: Snapshot()), default)).Error);
        var prepare = f.Prepare(true);
        var prepared = (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, prepare, default)).Value!;
        var claim = (await f.Service.ClaimTransferAsync(f.AreaTarget, new(prepared.Ticket), default)).Value!;
        AssertSnapshot(Snapshot(), claim.Admission.State!.Possessions);
        var finalItems = Snapshot(31);
        Assert.Null((await f.Service.SealTransferAsync(f.Character.Id, prepare.TransferId, f.Source, f.Seal(true, 1, finalItems), default)).Error);
        Assert.Equal(TransferStatus.Aborted, (await f.Service.AbortTransferAsync(f.Character.Id, prepare.TransferId, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default)).Value!.Status);
        var source = (await f.Store.FindCharacterAsync(f.Character.Id, default))!;
        Assert.Equal(f.Source.ServerId, source.ServerId);
        Assert.Equal("starter-island", source.State!.IslandId);
        AssertSnapshot(finalItems, source.State.Possessions);
        AssertSnapshot(finalItems, (await f.Service.SaveAsync(f.Character.Id, f.Source, f.Save(2), default)).Value!.Possessions);
    }

    [Fact]
    public async Task HttpRejectsPlayerWritesAndMalformedSnapshotsWithoutReplacingSavedItems()
    {
        var settings = new Dictionary<string, string?>();
        for (var index = 0; index < PossessionsCatalog.Servers.Length; index++)
        {
            var server = PossessionsCatalog.Servers[index];
            settings[$"GameAccess:DedicatedServers:{index}:RealmId"] = server.RealmId;
            settings[$"GameAccess:DedicatedServers:{index}:ServerId"] = server.ServerId;
            settings[$"GameAccess:DedicatedServers:{index}:Key"] = server.Key;
        }
        await using var factory = new CharactersFactory
        {
            AdditionalConfiguration = settings,
            ConfigureTestServices = services =>
            {
                services.RemoveAll<IGameLeaseStore>(); services.AddSingleton<IGameLeaseStore, MemoryGameLeaseStore>();
                services.RemoveAll<IGameRealmCatalog>(); services.AddSingleton<IGameRealmCatalog, PossessionsCatalog>();
            }
        };
        using var player = factory.PlayerClient();
        var created = await player.PostAsJsonAsync("/v1/characters", new { name = "Aelys" });
        var character = (await created.Content.ReadFromJsonAsync<CharacterResponse>())!;
        var admission = await player.PostAsJsonAsync($"/v1/characters/{character.Id}/admissions",
            new AdmissionRequest("beta-web", GameProtocol.MultiAreaVersion, GameProtocol.MultiAreaBuildId));
        var ticket = (await admission.Content.ReadFromJsonAsync<AdmissionResponse>())!;
        using var source = factory.PlayerClient(null);
        source.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", PossessionsCatalog.Servers[0].Key);
        var consumed = await source.PostAsJsonAsync("/internal/game/admissions/consume", new ConsumeAdmissionRequest(ticket.Ticket));
        var lease = (await consumed.Content.ReadFromJsonAsync<ConsumedAdmissionResponse>())!;
        var save = new SaveWorldStateRequest(lease.LeaseId, lease.AuthorityEpoch, 0, new(425, 32, 250), new(1, 0))
            { Simulation = Simulation, Possessions = Snapshot() };
        var path = $"/internal/game/leases/{character.Id}/state";
        Assert.Equal(HttpStatusCode.Forbidden, (await player.PutAsJsonAsync(path, save)).StatusCode);
        var saved = await source.PutAsJsonAsync(path, save);
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        AssertSnapshot(Snapshot(), (await saved.Content.ReadFromJsonAsync<SavedWorldState>())!.Possessions);

        var store = factory.Services.GetRequiredService<IGameLeaseStore>();
        var before = (await store.FindCharacterAsync(character.Id, default))!;
        foreach (var invalid in InvalidSnapshots())
        {
            var rejected = await source.PutAsJsonAsync(path, save with { ExpectedRevision = 1, Possessions = invalid });
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal("invalid_request", (await rejected.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        }
        var after = (await store.FindCharacterAsync(character.Id, default))!;
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.State!.Revision, after.State!.Revision);
        AssertSnapshot(Snapshot(), after.State.Possessions);
    }

    private static IEnumerable<CharacterPossessionsSnapshot> InvalidSnapshots()
    {
        var valid = Snapshot();
        yield return valid with { SchemaVersion = 99 };
        yield return valid with { Bags = null! };
        yield return valid with { Bank = null! };
        yield return valid with { Equipment = null! };
        yield return valid with { Bags = new(0, []) };
        yield return valid with { Bank = new(151, []) };
        yield return valid with { Bags = new(150, null!) };
        yield return valid with { Bags = new(150, [null!]) };
        yield return valid with { Bags = new(150, [new(0, "resource:wood", 1), new(0, "resource:stone", 2)]) };
        yield return valid with { Bank = new(150, [new(150, "valley-sword", 1)]) };
        yield return valid with { Bags = new(150, [new(-1, "resource:wood", 1)]) };
        yield return valid with { Bags = new(150, [new(0, "resource:wood", 0)]) };
        yield return valid with { Bags = new(150, [new(0, "resource:wood", 1_000_001)]) };
        yield return valid with { Bags = new(150, [new(0, "resource:unknown", 1)]) };
        yield return valid with { Bags = new(150, [new(0, "../not-an-item", 1)]) };
        yield return valid with { Equipment = [new("head", "painted-cap", 1), new("head", "other-cap", 1)] };
        yield return valid with { Equipment = [new("unrecognized-slot", "valley-sword", 1)] };
        yield return valid with { Equipment = [new("main-hand", "valley-sword", 2)] };
        yield return valid with { Equipment = [null!] };
        yield return valid with { HarvestTool = valid.HarvestTool! with { SocketCapacity = 1 } };
        yield return valid with { HarvestTool = valid.HarvestTool! with { Level = 0 } };
        yield return valid with { HarvestTool = valid.HarvestTool! with { Family = "unknown" } };
        yield return valid with { HarvestTool = valid.HarvestTool! with { Stones = [new(0, 10_001)] } };
        yield return valid with { HarvestTool = valid.HarvestTool! with { Stones = [null!] } };
    }

    [Fact]
    public async Task InvalidTransferSnapshotsDoNotPrepareOrSealAuthoritativeState()
    {
        var f = new Scenario(); await f.Initialize();
        foreach (var invalid in InvalidSnapshots())
        {
            var rejected = await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, f.Prepare(true, invalid), default);
            Assert.Equal(400, rejected.Status);
            Assert.Equal("invalid_transfer", rejected.Error);
        }
        Assert.Null((await f.Store.FindCharacterAsync(f.Character.Id, default))!.Transfer);
        var prepare = f.Prepare(true, Snapshot());
        var prepared = (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, prepare, default)).Value!;
        Assert.Null((await f.Service.ClaimTransferAsync(f.AreaTarget, new(prepared.Ticket), default)).Error);
        foreach (var invalid in InvalidSnapshots())
        {
            var rejected = await f.Service.SealTransferAsync(f.Character.Id, prepare.TransferId, f.Source, f.Seal(true, 0, invalid), default);
            Assert.Equal(400, rejected.Status);
            Assert.Equal("invalid_transfer", rejected.Error);
        }
        var unchanged = (await f.Store.FindCharacterAsync(f.Character.Id, default))!;
        Assert.Equal(TransferStatus.Claimed, unchanged.Transfer!.Status);
        Assert.Null(unchanged.State);
        AssertSnapshot(Snapshot(), unchanged.Transfer.State.Possessions);
    }

    [MongoFact]
    public async Task MongoPersistsNonemptySourceAndArrivalPossessionsAcrossRepositoryRestarts()
    {
        var database = "arkya_possessions_test_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        var options = Options.Create(new MongoOptions { DatabaseName = database });
        try
        {
            var store = new MongoGameLeaseStore(client, options);
            await store.EnsureIndexesAsync(default);
            var f = new Scenario(store); f.Clock.Now = DateTimeOffset.UtcNow; await f.Initialize();
            await AssertTransfer(f, true,
                () => new MongoGameLeaseStore(new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")), options));
            var restored = await new MongoGameLeaseStore(client, options).FindCharacterAsync(f.Character.Id, default);
            AssertSnapshot(Snapshot(21), restored!.State!.Possessions);
            Assert.Equal("forest-area", restored.State.IslandId);
        }
        finally { await client.DropDatabaseAsync(database); }
    }
}
