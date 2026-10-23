using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.Characters.Api.Domain;
using Arkya.Characters.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Arkya.Characters.Tests;

public sealed class AreaTransitionTests
{
    private static readonly ActorStateEnvelope SourceSimulation = new(1,
        "{\"lastSequence\":4294967295,\"jumpSequence\":41,\"verticalVelocity\":-4.25,\"phase\":3,\"colliderSupportHeight\":32,\"gameplay\":{\"schemaVersion\":2,\"health\":77,\"mana\":31,\"xp\":808,\"skills\":[3,5]}}");
    private static readonly ActorStateEnvelope ArrivalSimulation = new(1,
        "{\"lastSequence\":4294967295,\"jumpSequence\":41,\"verticalVelocity\":-4.25,\"phase\":3,\"colliderSupportHeight\":43,\"gameplay\":{\"schemaVersion\":2,\"health\":77,\"mana\":31,\"xp\":808,\"skills\":[3,5]}}");
    private static readonly GameAreaTransition Transition = new("starter-to-forest", "starter-island", "forest-area",
        new(400, 200, 450, 300), new(750, 44, 250), new(0, 1));

    private sealed class AreaCatalog : IGameRealmCatalog
    {
        public static readonly DedicatedServerIdentity[] Servers = new[] { "source-west", "source-east", "target-west", "target-east" }
            .Select(id => new DedicatedServerIdentity { RealmId = "beta-web", ServerId = id, Key = "test-area-key-12345678901234567890-" + id }).ToArray();
        public GameRealmEntry Entry { get; set; } = new("beta-web", "Two independent maps", true, Servers[0].ServerId,
            "wss://play.example/source-west", GameProtocol.MultiAreaVersion, GameProtocol.MultiAreaBuildId)
        {
            TopologyVersion = "two-areas-v1",
            Areas = [new("starter-island", "starter-island"), new("forest-area", "forest-content")],
            AreaTransitions = [Transition],
            Partitions = Servers.Select((server, index) => new GameRealmPartition(server.ServerId, "wss://play.example/" + server.ServerId,
                index < 2 ? "starter-island" : "forest-area", index % 2, new(index % 2 * 500, 0, index % 2 * 500 + 500, 1000))).ToArray()
        };
        public Task<GameRealmCatalog> ListAsync(CancellationToken cancellationToken) => Task.FromResult(new GameRealmCatalog([Entry]));
    }

    private sealed class Scenario(IGameLeaseStore? store = null)
    {
        public Guid Account { get; } = Guid.NewGuid();
        public Character Character { get; private set; } = null!;
        public GameClock Clock { get; } = new();
        public AreaCatalog Catalog { get; } = new();
        public MemoryCharacterRepository Characters { get; } = new();
        public IGameLeaseStore Store { get; set; } = store ?? new MemoryGameLeaseStore();
        public ConsumedAdmissionResponse Lease { get; private set; } = null!;
        public AdmissionResponse Ticket { get; private set; } = null!;
        public DedicatedServerIdentity Source => AreaCatalog.Servers[0];
        public DedicatedServerIdentity Target => AreaCatalog.Servers[3];
        public GameAdmissionService Service => new(Characters, Store, Catalog,
            new DedicatedKeyValidator(Options.Create(new CharacterGameAccessOptions { DedicatedServers = AreaCatalog.Servers })),
            Clock, new TestSessionReferences(Clock, Account), Options.Create(new CharacterGameAccessOptions()));
        public async Task Initialize()
        {
            Character = (await new CharacterService(Characters, Clock).CreateAsync(Account, "Aelys", default)).Character!;
            Ticket = await Issue();
            Lease = (await Service.ConsumeAsync(Source, new(Ticket.Ticket), default)).Value!;
        }
        public async Task<AdmissionResponse> Issue() => (await Service.IssueAsync(Character.Id, Account, Clock.Now.AddHours(1),
            SessionReference.Create("multi-area-session"), new("beta-web", GameProtocol.MultiAreaVersion, GameProtocol.MultiAreaBuildId), default)).Value!;
        public PrepareTransferRequest Prepare() => new(Guid.NewGuid(), Lease.LeaseId, Lease.AuthorityEpoch,
            Target.ServerId, Catalog.Entry.TopologyVersion!, new(425, 32, 250), new(1, 0), SourceSimulation) { TransitionId = Transition.Id };
        public SealTransferRequest Seal() => new(Lease.LeaseId, Lease.AuthorityEpoch, 0, new(440, 33, 250), new(1, 0), SourceSimulation)
            { ArrivalSimulation = ArrivalSimulation };
        public async Task<(PrepareTransferRequest Request, PreparedTransferResponse Prepared, ClaimedTransferResponse Claim)> Claim()
        {
            var request = Prepare();
            var prepared = (await Service.PrepareTransferAsync(Character.Id, Source, request, default)).Value!;
            var claim = (await Service.ClaimTransferAsync(Target, new(prepared.Ticket), default)).Value!;
            return (request, prepared, claim);
        }
    }

    [Fact]
    public async Task ExplicitTransitionCommitsTheConfiguredArrivalAndCompleteStateAndReadmitsToTheTargetArea()
    {
        var scenario = new Scenario(); await scenario.Initialize();
        await AssertCommittedArrivalAndReadmission(scenario);
    }

    private static async Task AssertCommittedArrivalAndReadmission(Scenario f, Func<IGameLeaseStore>? restart = null)
    {
        Assert.Equal("starter-island", f.Ticket.AreaId);
        Assert.Equal("starter-island", f.Lease.ContentId);
        var transfer = await f.Claim();
        Assert.Equal(Guid.Empty, transfer.Claim.Admission.LeaseId);
        Assert.Equal("forest-area", transfer.Claim.Admission.AreaId);
        Assert.Equal("forest-content", transfer.Claim.Admission.ContentId);
        Assert.Equal(TransferStatus.Sealed, (await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default)).Value!.Status);
        var sealedRecord = (await f.Store.FindCharacterAsync(f.Character.Id, default))!;
        Assert.Equal(f.Source.ServerId, sealedRecord.ServerId);
        Assert.Equal("starter-island", sealedRecord.State!.IslandId);
        Assert.Equal(f.Seal().Position, sealedRecord.State.Position);
        Assert.Equal(SourceSimulation, sealedRecord.State.Simulation);
        Assert.Equal("forest-area", sealedRecord.Transfer!.State.IslandId);
        Assert.Equal(new GamePosition(750, 44, 250), sealedRecord.Transfer.State.Position);
        Assert.Equal(ArrivalSimulation, sealedRecord.Transfer.State.Simulation);
        Assert.Equal(sealedRecord.State, sealedRecord.Transfer.SourceState);
        if (restart is not null)
        {
            f.Store = restart();
            var restored = (await f.Store.FindCharacterAsync(f.Character.Id, default))!;
            Assert.Equal(sealedRecord.State, restored.State);
            Assert.Equal(sealedRecord.Transfer, restored.Transfer);
        }
        var commits = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Service.CommitTransferAsync(f.Character.Id,
            transfer.Request.TransferId, f.Target, new(transfer.Claim.ClaimId), default)));
        Assert.All(commits, result => Assert.Null(result.Error));
        Assert.Single(commits.Select(result => result.Value!.LeaseId).Distinct());
        var committed = commits[0].Value!;
        Assert.Equal(f.Lease.AuthorityEpoch + 1, committed.AuthorityEpoch);
        Assert.Equal("forest-area", committed.AreaId);
        Assert.Equal("forest-content", committed.ContentId);
        Assert.Equal(new GameForward(0, 1), committed.State!.Forward);
        Assert.Equal(sealedRecord.Transfer.State, committed.State);
        Assert.Equal("lease_rejected", (await f.Service.SaveAsync(f.Character.Id, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch, 1, f.Seal().Position, f.Seal().Forward) { Simulation = SourceSimulation }, default)).Error);
        Assert.False(await f.Service.ReleaseAsync(f.Character.Id, f.Source, new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default));
        Assert.Equal(TransferStatus.Committed, (await f.Service.AbortTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default)).Value!.Status);
        Assert.True(await f.Service.ReleaseAsync(f.Character.Id, f.Target, new(committed.LeaseId, committed.AuthorityEpoch), default));
        var ticket = await f.Issue();
        Assert.Equal(f.Target.ServerId, ticket.ServerId);
        Assert.Equal("forest-area", ticket.AreaId);
        Assert.Equal("forest-content", ticket.ContentId);
        var readmitted = (await f.Service.ConsumeAsync(f.Target, new(ticket.Ticket), default)).Value!;
        Assert.Equal(committed.AuthorityEpoch + 1, readmitted.AuthorityEpoch);
        Assert.Equal(committed.State, readmitted.State);
        Assert.Equal(committed.AreaId, readmitted.AreaId);
        Assert.Equal(committed.ContentId, readmitted.ContentId);
    }

    [Fact]
    public async Task AbortRestoresTheFinalSourceStateInsteadOfPersistingTheTargetArrival()
    {
        var f = new Scenario(); await f.Initialize();
        var transfer = await f.Claim();
        await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default);
        var aborted = await f.Service.AbortTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default);
        Assert.Equal(TransferStatus.Aborted, aborted.Value!.Status);
        var restored = (await f.Store.FindCharacterAsync(f.Character.Id, default))!;
        Assert.Equal(f.Source.ServerId, restored.ServerId);
        Assert.Equal("starter-island", restored.Area!.AreaId);
        Assert.Equal("starter-island", restored.State!.IslandId);
        Assert.Equal(f.Seal().Position, restored.State.Position);
        Assert.Equal(SourceSimulation, restored.State.Simulation);
        Assert.Equal("transfer_rejected", (await f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target,
            new(transfer.Claim.ClaimId), default)).Error);
        Assert.Equal(2, (await f.Service.SaveAsync(f.Character.Id, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch, 1, new(430, 33, 250), new(1, 0)) { Simulation = SourceSimulation }, default)).Value!.Revision);
    }

    [Fact]
    public async Task TriggerSourceOwnershipTransitionAndDestinationCannotBeForged()
    {
        var f = new Scenario(); await f.Initialize();
        var request = f.Prepare();
        var invalid = new[]
        {
            request with { TransitionId = null },
            request with { TransitionId = "unknown-transition" },
            request with { Position = new(451, 32, 250) },
            request with { TargetServerId = AreaCatalog.Servers[2].ServerId },
            request with { TopologyVersion = "stale-version" }
        };
        foreach (var value in invalid)
            Assert.Equal("invalid_transfer_destination", (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, value, default)).Error);
        Assert.Equal("invalid_transfer_destination", (await f.Service.PrepareTransferAsync(f.Character.Id, AreaCatalog.Servers[1], request, default)).Error);
        var transfer = await f.Claim();
        Assert.Equal("invalid_transfer_destination", (await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source,
            f.Seal() with { Position = new(750, 44, 250) }, default)).Error);
        Assert.Equal("invalid_transfer", (await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source,
            f.Seal() with { ArrivalSimulation = null }, default)).Error);
        Assert.Equal("invalid_transfer", (await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source,
            f.Seal() with { ArrivalSimulation = new(0, "") }, default)).Error);
        Assert.Equal(f.Lease.AuthorityEpoch, (await f.Store.FindCharacterAsync(f.Character.Id, default))!.AuthorityEpoch);
    }

    [Fact]
    public async Task RetriesKeepBothSourceAndArrivalSnapshotsFrozenAndRejectDifferentArrivalState()
    {
        var f = new Scenario(); await f.Initialize();
        var transfer = await f.Claim();
        Assert.Equal(transfer.Prepared, (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, transfer.Request, default)).Value);
        await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default);
        Assert.Null((await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default)).Error);
        Assert.Equal("transfer_conflict", (await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source,
            f.Seal() with { ArrivalSimulation = SourceSimulation }, default)).Error);
        Assert.Equal("transfer_conflict", (await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source,
            f.Seal() with { Position = new(441, 33, 250) }, default)).Error);
    }

    [Fact]
    public async Task SourceCrashBeforeCommitRecoversInTheSourceAreaAfterLeaseExpiry()
    {
        var f = new Scenario(); await f.Initialize();
        var transfer = await f.Claim();
        await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default);
        f.Clock.Now = f.Clock.Now.AddSeconds(GameAdmissionService.LeaseSeconds + 1);
        var ticket = await f.Issue();
        Assert.Equal(f.Source.ServerId, ticket.ServerId);
        Assert.Equal("starter-island", ticket.AreaId);
        var readmitted = (await f.Service.ConsumeAsync(f.Source, new(ticket.Ticket), default)).Value!;
        Assert.Equal(f.Seal().Position, readmitted.State!.Position);
        Assert.Equal(SourceSimulation, readmitted.State.Simulation);
    }

    [Fact]
    public async Task AChangedTransitionCannotReplaceTheFrozenArrivalAndAnOmittedForwardPreservesTheSourceHeading()
    {
        var f = new Scenario(); await f.Initialize();
        f.Catalog.Entry = f.Catalog.Entry with { AreaTransitions = [Transition with { TargetForward = null }] };
        var transfer = await f.Claim();
        var frozen = f.Catalog.Entry;
        f.Catalog.Entry = frozen with { AreaTransitions = [Transition with { TargetForward = null, TargetPosition = new(800, 44, 250) }] };
        Assert.Equal("invalid_transfer_destination", (await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default)).Error);
        f.Catalog.Entry = frozen;
        await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default);
        var committed = (await f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target,
            new(transfer.Claim.ClaimId), default)).Value!;
        Assert.Equal(f.Seal().Forward, committed.State!.Forward);
        Assert.Equal(new GamePosition(750, 44, 250), committed.State.Position);
    }

    [Fact]
    public async Task AbortAndCommitChooseExactlyOneAreaState()
    {
        var f = new Scenario(); await f.Initialize();
        await AssertAbortCommitRace(f);
    }

    private static async Task AssertAbortCommitRace(Scenario f)
    {
        var transfer = await f.Claim();
        await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default);
        var commit = Task.Run(() => f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target,
            new(transfer.Claim.ClaimId), default));
        var abort = Task.Run(() => f.Service.AbortTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default));
        await Task.WhenAll(commit, abort);
        var record = (await f.Store.FindCharacterAsync(f.Character.Id, default))!;
        if (record.Transfer!.Status == TransferStatus.Committed)
        {
            Assert.Equal(f.Target.ServerId, record.ServerId);
            Assert.Equal("forest-area", record.Area!.AreaId);
            Assert.Equal("forest-area", record.State!.IslandId);
            Assert.Equal(ArrivalSimulation, record.State.Simulation);
            Assert.Equal(TransferStatus.Committed, abort.Result.Value!.Status);
            Assert.Null(commit.Result.Error);
        }
        else
        {
            Assert.Equal(TransferStatus.Aborted, record.Transfer.Status);
            Assert.Equal(f.Source.ServerId, record.ServerId);
            Assert.Equal("starter-island", record.Area!.AreaId);
            Assert.Equal("starter-island", record.State!.IslandId);
            Assert.Equal(SourceSimulation, record.State.Simulation);
            Assert.Equal("transfer_rejected", commit.Result.Error);
        }
    }

    [MongoFact]
    public async Task MongoInterAreaTransferPreservesBothSnapshotsAcrossARepositoryRestart()
    {
        var database = "arkya_multi_area_test_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        try
        {
            var options = Options.Create(new MongoOptions { DatabaseName = database });
            var store = new MongoGameLeaseStore(client, options);
            await store.EnsureIndexesAsync(default);
            var f = new Scenario(store); f.Clock.Now = DateTimeOffset.UtcNow; await f.Initialize();
            await AssertCommittedArrivalAndReadmission(f,
                () => new MongoGameLeaseStore(new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")), options));
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    [MongoFact]
    public async Task MongoAbortAndCommitCannotMixSourceAndArrivalStates()
    {
        var database = "arkya_multi_area_race_test_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        try
        {
            var store = new MongoGameLeaseStore(client, Options.Create(new MongoOptions { DatabaseName = database }));
            await store.EnsureIndexesAsync(default);
            var f = new Scenario(store); f.Clock.Now = DateTimeOffset.UtcNow; await f.Initialize();
            await AssertAbortCommitRace(f);
        }
        finally { await client.DropDatabaseAsync(database); }
    }
}
