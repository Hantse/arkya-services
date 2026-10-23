using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.Characters.Api.Domain;
using Arkya.Characters.Api.Infrastructure.Persistence;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;

namespace Arkya.Characters.Tests;

public sealed class GameTransferTests
{
    private static readonly ActorStateEnvelope Simulation = new(1,
        "{\"motion\":2,\"lastSequence\":4294967295,\"jumpSequence\":41,\"verticalVelocity\":-4.25,\"consumedJumpSequence\":40,\"phase\":3,\"health\":77}");

    private sealed class PartitionCatalog : IGameRealmCatalog
    {
        public bool Ready { get; set; } = true;
        public string Version { get; set; } = "starter-island-4-v1";
        public static readonly DedicatedServerIdentity[] Servers = Enumerable.Range(0, 4).Select(index => new DedicatedServerIdentity
        {
            RealmId = "beta-web", ServerId = $"island-starter-island-{index + 1:D2}", Key = $"test-partition-{index + 1}-dedicated-key-1234567890"
        }).ToArray();
        public Task<GameRealmCatalog> ListAsync(CancellationToken cancellationToken) => Task.FromResult(new GameRealmCatalog([
            new("beta-web", "Four areas", Ready, Servers[0].ServerId, "wss://play.example/area-1", GameProtocol.HandoffVersion, GameProtocol.HandoffBuildId)
            {
                TopologyVersion = Version,
                Partitions = Servers.Select((server, index) => new GameRealmPartition(server.ServerId, $"wss://play.example/area-{index + 1}",
                    "starter-island", index, new(index % 2 * 500, index / 2 * 500, index % 2 * 500 + 500, index / 2 * 500 + 500))).ToArray()
            }]));
    }

    private sealed class Scenario
    {
        public Guid Account { get; } = Guid.NewGuid();
        public Character Character { get; private set; } = null!;
        public GameClock Clock { get; } = new();
        public MemoryCharacterRepository Characters { get; } = new();
        public IGameLeaseStore Store { get; }
        public PartitionCatalog Catalog { get; } = new();
        public TestSessionReferences Sessions { get; }
        public GameAdmissionService Service => new(Characters, Store, Catalog,
            new DedicatedKeyValidator(Options.Create(new CharacterGameAccessOptions { DedicatedServers = PartitionCatalog.Servers })),
            Clock, Sessions, Options.Create(new CharacterGameAccessOptions()));
        public ConsumedAdmissionResponse Lease { get; private set; } = null!;
        public DedicatedServerIdentity Source => PartitionCatalog.Servers[0];
        public DedicatedServerIdentity Target => PartitionCatalog.Servers[1];
        public Scenario(IGameLeaseStore? store = null) { Store = store ?? new MemoryGameLeaseStore(); Sessions = new(Clock, Account); }
        public async Task Initialize()
        {
            Character = (await new CharacterService(Characters, Clock).CreateAsync(Account, "Aelys", default)).Character!;
            var admission = await Issue();
            Lease = (await Service.ConsumeAsync(Source, new(admission.Ticket), default)).Value!;
        }
        public async Task<AdmissionResponse> Issue() => (await Service.IssueAsync(Character.Id, Account, Clock.Now.AddHours(1),
            SessionReference.Create("transfer-test-session"), new("beta-web", GameProtocol.HandoffVersion, GameProtocol.HandoffBuildId), default)).Value!;
        public PrepareTransferRequest Prepare(int target = 1) => new(Guid.NewGuid(), Lease.LeaseId, Lease.AuthorityEpoch,
            PartitionCatalog.Servers[target].ServerId, Catalog.Version, new(499, 32, 250), new(1, 0), Simulation);
        public async Task<(PrepareTransferRequest Request, PreparedTransferResponse Prepared, ClaimedTransferResponse Claim)> Claim(int target = 1)
        {
            var request = Prepare(target);
            var prepared = (await Service.PrepareTransferAsync(Character.Id, Source, request, default)).Value!;
            var claim = (await Service.ClaimTransferAsync(PartitionCatalog.Servers[target], new(prepared.Ticket), default)).Value!;
            return (request, prepared, claim);
        }
        public SealTransferRequest Seal(long revision = 0, GamePosition? position = null) =>
            new(Lease.LeaseId, Lease.AuthorityEpoch, revision, position ?? new(500, 33, 250), new(1, 0), Simulation);
    }

    [Fact]
    public async Task PreconnectedClaimHasNoAuthorityAndSealCommitsCompleteActorStateExactlyOnce()
    {
        var f = new Scenario(); await f.Initialize();
        var transfer = await f.Claim();
        Assert.Equal(Guid.Empty, transfer.Claim.Admission.LeaseId);
        Assert.Equal(f.Lease.AuthorityEpoch, (await f.Store.FindCharacterAsync(f.Character.Id, default))!.AuthorityEpoch);
        var moving = await f.Service.SaveAsync(f.Character.Id, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch, 0, new(499.5f, 32, 250), new(1, 0)) { Simulation = Simulation }, default);
        Assert.Equal(1, moving.Value!.Revision);
        var sealedState = await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(1), default);
        Assert.Equal(TransferStatus.Sealed, sealedState.Value!.Status);
        Assert.Equal(2, sealedState.Value.StateRevision);
        Assert.Equal("transfer_sealed", (await f.Service.SaveAsync(f.Character.Id, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch, 2, new(1, 1, 1), new(1, 0)), default)).Error);
        Assert.False(await f.Service.ReleaseAsync(f.Character.Id, f.Source, new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default));
        var committed = (await f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target, new(transfer.Claim.ClaimId), default)).Value!;
        Assert.Equal(f.Target.ServerId, committed.ServerId);
        Assert.Equal(f.Lease.AuthorityEpoch + 1, committed.AuthorityEpoch);
        Assert.NotEqual(f.Lease.LeaseId, committed.LeaseId);
        Assert.Equal(Simulation, committed.State!.Simulation);
        Assert.Equal(f.Seal().Position, committed.State.Position);
        Assert.Equal("starter-island", committed.State.IslandId);
        Assert.Equal("lease_rejected", (await f.Service.HeartbeatAsync(f.Character.Id, f.Source, new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default)).Error);
        Assert.Equal("lease_rejected", (await f.Service.SaveAsync(f.Character.Id, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch, 2, new(1, 1, 1), new(1, 0)), default)).Error);
        Assert.Equal(TransferStatus.Committed, (await f.Service.AbortTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default)).Value!.Status);
    }

    [Fact]
    public async Task IntraAreaPrepareAcceptsJsonUtilityEmptyOptionalTransitionAndNullRetries()
    {
        var f = new Scenario(); await f.Initialize();
        var request = f.Prepare() with { TransitionId = "" };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(request, options);
        Assert.Contains("\"transitionId\":\"\"", json);
        var wireRequest = JsonSerializer.Deserialize<PrepareTransferRequest>(json, options)!;
        Assert.Equal("", wireRequest.TransitionId);

        var prepared = await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, wireRequest, default);
        Assert.Null(prepared.Error);
        Assert.Equal(prepared.Value, (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source,
            wireRequest with { TransitionId = null }, default)).Value);
        Assert.Equal(prepared.Value, (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, wireRequest, default)).Value);
        Assert.Null((await f.Store.FindCharacterAsync(f.Character.Id, default))!.Transfer!.Transition);
        Assert.Equal("invalid_transfer", (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source,
            wireRequest with { TransitionId = " " }, default)).Error);

        var claimed = (await f.Service.ClaimTransferAsync(f.Target, new(prepared.Value!.Ticket), default)).Value!;
        Assert.Null((await f.Service.SealTransferAsync(f.Character.Id, request.TransferId, f.Source, f.Seal(), default)).Error);
        var committed = await f.Service.CommitTransferAsync(f.Character.Id, request.TransferId, f.Target, new(claimed.ClaimId), default);
        Assert.Null(committed.Error);
        Assert.Equal(f.Target.ServerId, committed.Value!.ServerId);
        Assert.Equal(Simulation, committed.Value.State!.Simulation);
    }

    [Fact]
    public async Task IntraAreaSealAcceptsJsonUtilityEmptyOptionalArrivalButNotASubmittedArrivalPayload()
    {
        var f = new Scenario(); await f.Initialize();
        var transfer = await f.Claim();
        Assert.Equal("invalid_transfer", (await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source,
            f.Seal() with { ArrivalSimulation = new(0, "{}") }, default)).Error);
        var seal = f.Seal() with { ArrivalSimulation = new(0, "") };
        Assert.Null((await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, seal, default)).Error);
        Assert.Null((await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, seal, default)).Error);
        Assert.Equal(Simulation, (await f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target,
            new(transfer.Claim.ClaimId), default)).Value!.State!.Simulation);
    }

    [Fact]
    public async Task PrepareClaimSealCommitRetriesSurviveAServiceRestartWithoutChangingLeaseOrEpoch()
    {
        var f = new Scenario(); await f.Initialize();
        var transfer = await f.Claim();
        Assert.Equal(transfer.Prepared, (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, transfer.Request, default)).Value);
        var claimedAgain = (await f.Service.ClaimTransferAsync(f.Target, new(transfer.Prepared.Ticket), default)).Value!;
        Assert.Equal(transfer.Claim.ClaimId, claimedAgain.ClaimId);
        await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default);
        Assert.Equal(TransferStatus.Sealed, (await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default)).Value!.Status);
        var first = (await f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target, new(transfer.Claim.ClaimId), default)).Value!;
        var retry = (await f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target, new(transfer.Claim.ClaimId), default)).Value!;
        Assert.Equal(first.LeaseId, retry.LeaseId);
        Assert.Equal(first.AuthorityEpoch, retry.AuthorityEpoch);
        Assert.Equal(first.State, retry.State);
        Assert.True(await f.Service.ReleaseAsync(f.Character.Id, f.Target, new(first.LeaseId, first.AuthorityEpoch), default));
        Assert.Equal("admission_rejected", (await f.Service.ConsumeAsync(f.Target, new(transfer.Prepared.Ticket), default)).Error);
        Assert.Equal("lease_rejected", (await f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target, new(transfer.Claim.ClaimId), default)).Error);
        var ticket = await f.Issue();
        Assert.Equal(f.Target.ServerId, ticket.ServerId);
        var reconnect = (await f.Service.ConsumeAsync(f.Target, new(ticket.Ticket), default)).Value!;
        Assert.Equal(first.AuthorityEpoch + 1, reconnect.AuthorityEpoch);
        Assert.Equal(Simulation, reconnect.State!.Simulation);
    }

    [Fact]
    public async Task ExpiredTargetClaimCanBeAbortedAndSourceResumesOnlyAfterTheAbortWins()
    {
        var f = new Scenario(); await f.Initialize();
        var transfer = await f.Claim();
        await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default);
        f.Clock.Now = f.Clock.Now.AddSeconds(GameAdmissionService.TransferSeconds + 1);
        Assert.Equal("transfer_rejected", (await f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target, new(transfer.Claim.ClaimId), default)).Error);
        Assert.Equal(TransferStatus.Aborted, (await f.Service.AbortTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default)).Value!.Status);
        Assert.Equal(2, (await f.Service.SaveAsync(f.Character.Id, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch, 1, new(499, 32, 250), new(1, 0)) { Simulation = Simulation }, default)).Value!.Revision);
        Assert.Equal("transfer_rejected", (await f.Service.ClaimTransferAsync(f.Target, new(transfer.Prepared.Ticket), default)).Error);
    }

    [Fact]
    public async Task SourceCrashAfterSealRecoversFinalStateOnTheCorrectPartitionAfterLeaseExpiry()
    {
        var f = new Scenario(); await f.Initialize();
        var transfer = await f.Claim();
        await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default);
        f.Clock.Now = f.Clock.Now.AddSeconds(GameAdmissionService.LeaseSeconds + 1);
        Assert.Equal("lease_rejected", (await f.Service.AbortTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default)).Error);
        var admission = await f.Issue();
        Assert.Equal(f.Target.ServerId, admission.ServerId);
        var restored = (await f.Service.ConsumeAsync(f.Target, new(admission.Ticket), default)).Value!;
        Assert.Equal(f.Lease.AuthorityEpoch + 1, restored.AuthorityEpoch);
        Assert.Equal(Simulation, restored.State!.Simulation);
        Assert.Equal(f.Seal().Position, restored.State.Position);
    }

    [Fact]
    public async Task ACommitAndAbortRaceHasExactlyOneAuthorityWinner()
    {
        var f = new Scenario(); await f.Initialize();
        var transfer = await f.Claim();
        await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default);
        var commit = Task.Run(() => f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target, new(transfer.Claim.ClaimId), default));
        var abort = Task.Run(() => f.Service.AbortTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default));
        await Task.WhenAll(commit, abort);
        var commitResult = await commit;
        var abortResult = await abort;
        var state = (await f.Store.FindCharacterAsync(f.Character.Id, default))!;
        Assert.Contains(state.Transfer!.Status, new[] { TransferStatus.Committed, TransferStatus.Aborted });
        if (state.Transfer.Status == TransferStatus.Committed)
        {
            Assert.Equal(f.Target.ServerId, state.ServerId);
            Assert.Equal(f.Lease.AuthorityEpoch + 1, state.AuthorityEpoch);
            Assert.Equal(TransferStatus.Committed, abortResult.Value!.Status);
        }
        else
        {
            Assert.Equal(f.Source.ServerId, state.ServerId);
            Assert.Equal(f.Lease.AuthorityEpoch, state.AuthorityEpoch);
            Assert.NotNull(commitResult.Error);
        }
    }

    [Fact]
    public async Task ForgedDestinationsEpochsClaimsAndOutOfPartitionFinalPositionsCannotTransferAuthority()
    {
        var f = new Scenario(); await f.Initialize();
        var request = f.Prepare();
        Assert.Equal("invalid_transfer_destination", (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, request with { TopologyVersion = "stale" }, default)).Error);
        Assert.Equal("invalid_transfer_destination", (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, request with { TargetServerId = "unknown-server" }, default)).Error);
        Assert.Equal("lease_rejected", (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, request with { AuthorityEpoch = 2 }, default)).Error);
        var transfer = await f.Claim();
        Assert.Equal("transfer_rejected", (await f.Service.ClaimTransferAsync(PartitionCatalog.Servers[2], new(transfer.Prepared.Ticket), default)).Error);
        Assert.Equal("invalid_transfer_destination", (await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(position: new(499, 32, 250)), default)).Error);
        await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default);
        Assert.Equal("transfer_rejected", (await f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target, new(Guid.NewGuid()), default)).Error);
        f.Catalog.Version = "changed-topology";
        Assert.Equal("topology_unavailable", (await f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target, new(transfer.Claim.ClaimId), default)).Error);
        Assert.Equal(f.Source.ServerId, (await f.Store.FindCharacterAsync(f.Character.Id, default))!.ServerId);
    }

    [Fact]
    public async Task CornerCrossingSelectsTheDiagonalOwnerWithoutDoubleAuthority()
    {
        var f = new Scenario(); await f.Initialize();
        var transfer = await f.Claim(3);
        Assert.Equal(TransferStatus.Sealed, (await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source,
            f.Seal(position: new(500, 35, 500)), default)).Value!.Status);
        var committed = (await f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, PartitionCatalog.Servers[3], new(transfer.Claim.ClaimId), default)).Value!;
        Assert.Equal(PartitionCatalog.Servers[3].ServerId, committed.ServerId);
    }

    [Fact]
    public async Task RevokedSessionOrExpiryDuringValidationCannotCommit()
    {
        var f = new Scenario(); await f.Initialize();
        var transfer = await f.Claim();
        await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default);
        f.Sessions.Status = SessionValidationStatus.Invalid;
        Assert.Equal("lease_rejected", (await f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target, new(transfer.Claim.ClaimId), default)).Error);
        f.Sessions.Status = SessionValidationStatus.Valid;
        f.Sessions.AfterValidation = () => f.Clock.Now = f.Clock.Now.AddSeconds(GameAdmissionService.TransferSeconds + 1);
        Assert.Equal("transfer_rejected", (await f.Service.CommitTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Target, new(transfer.Claim.ClaimId), default)).Error);
    }

    [Fact]
    public async Task TopologyIsReadableBeforeReadinessButAdmissionAndPrepareRemainClosed()
    {
        var f = new Scenario(); await f.Initialize(); f.Catalog.Ready = false;
        Assert.Equal(4, (await f.Service.TopologyAsync(f.Target, default)).Value!.Partitions.Length);
        Assert.Equal("topology_unavailable", (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, f.Prepare(), default)).Error);
    }

    [Fact]
    public async Task LegacyClientCannotAcquireAHandoffRealmTicket()
    {
        var f = new Scenario();
        var character = (await new CharacterService(f.Characters, f.Clock).CreateAsync(f.Account, "Aelys", default)).Character!;
        var result = await f.Service.IssueAsync(character.Id, f.Account, f.Clock.Now.AddHours(1), SessionReference.Create("transfer-test-session"),
            new("beta-web", GameProtocol.Version, GameProtocol.BuildId), default);
        Assert.Equal("incompatible_client", result.Error);
        Assert.Null(await f.Store.FindCharacterAsync(character.Id, default));
        Assert.Null((await f.Characters.FindOwnedAsync(character.Id, f.Account, default))!.RealmId);
    }

    [Fact]
    public async Task InvalidSimulationEnvelopesCannotReplaceAuthoritativeState()
    {
        var f = new Scenario(); await f.Initialize();
        foreach (var simulation in new[] { new ActorStateEnvelope(99, "{}"), new(1, "[]"), new(1, "invalid"), new(1, new string('x', 32769)) })
            Assert.Equal("invalid_transfer", (await f.Service.PrepareTransferAsync(f.Character.Id, f.Source, f.Prepare() with { Simulation = simulation }, default)).Error);
        Assert.Null((await f.Store.FindCharacterAsync(f.Character.Id, default))!.Transfer);
    }

    [Fact]
    public async Task HttpTransferRoutesEnforceDedicatedIdentityAndSerializeTheFullProtocol()
    {
        var settings = new Dictionary<string, string?>();
        for (var index = 0; index < PartitionCatalog.Servers.Length; index++)
        {
            var server = PartitionCatalog.Servers[index];
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
                services.RemoveAll<IGameRealmCatalog>(); services.AddSingleton<IGameRealmCatalog, PartitionCatalog>();
            }
        };
        using var player = factory.PlayerClient();
        var character = (await (await player.PostAsJsonAsync("/v1/characters", new { name = "Aelys" })).Content.ReadFromJsonAsync<CharacterResponse>())!;
        var ticket = (await (await player.PostAsJsonAsync($"/v1/characters/{character.Id}/admissions", new AdmissionRequest("beta-web", GameProtocol.HandoffVersion, GameProtocol.HandoffBuildId))).Content.ReadFromJsonAsync<AdmissionResponse>())!;
        using var source = factory.PlayerClient(null);
        Assert.Equal(HttpStatusCode.Forbidden, (await source.GetAsync("/internal/game/topology")).StatusCode);
        source.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", PartitionCatalog.Servers[0].Key);
        using var target = factory.PlayerClient(null);
        target.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", PartitionCatalog.Servers[1].Key);
        var topology = (await source.GetFromJsonAsync<GameRealmEntry>("/internal/game/topology"))!;
        Assert.Equal(4, topology.Partitions.Length);
        var lease = (await (await source.PostAsJsonAsync("/internal/game/admissions/consume", new ConsumeAdmissionRequest(ticket.Ticket))).Content.ReadFromJsonAsync<ConsumedAdmissionResponse>())!;
        var id = Guid.NewGuid();
        var path = $"/internal/game/leases/{character.Id}/transfers/{id}";
        var prepare = new PrepareTransferRequest(id, lease.LeaseId, lease.AuthorityEpoch, PartitionCatalog.Servers[1].ServerId,
            topology.TopologyVersion!, new(499, 32, 250), new(1, 0), Simulation);
        var prepared = (await (await source.PostAsJsonAsync($"/internal/game/leases/{character.Id}/transfers/prepare", prepare)).Content.ReadFromJsonAsync<PreparedTransferResponse>())!;
        var claimed = (await (await target.PostAsJsonAsync("/internal/game/transfers/claim", new ClaimTransferRequest(prepared.Ticket))).Content.ReadFromJsonAsync<ClaimedTransferResponse>())!;
        Assert.Equal(TransferStatus.Claimed, (await source.GetFromJsonAsync<TransferStatusResponse>(path))!.Status);
        var seal = await source.PostAsJsonAsync(path + "/seal", new SealTransferRequest(lease.LeaseId, lease.AuthorityEpoch, 0,
            new(500, 33, 250), new(1, 0), Simulation));
        Assert.Equal(HttpStatusCode.OK, seal.StatusCode);
        var commit = await target.PostAsJsonAsync(path + "/commit", new CommitTransferRequest(claimed.ClaimId));
        Assert.Equal(HttpStatusCode.OK, commit.StatusCode);
        Assert.Equal(Simulation, (await commit.Content.ReadFromJsonAsync<ConsumedAdmissionResponse>())!.State!.Simulation);
        Assert.Equal(TransferStatus.Committed, (await (await source.PostAsJsonAsync(path + "/abort", new LeaseRequest(lease.LeaseId, lease.AuthorityEpoch))).Content.ReadFromJsonAsync<TransferStatusResponse>())!.Status);
    }

    [MongoFact]
    public async Task MongoTransferStateAndCommitAreDurableAcrossRepositoryInstances()
    {
        var database = $"arkya_transfer_test_{Guid.NewGuid():N}";
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        var options = Options.Create(new MongoOptions { DatabaseName = database });
        try
        {
            var first = new MongoGameLeaseStore(client, options);
            await first.EnsureIndexesAsync(default);
            var f = new Scenario(first); f.Clock.Now = DateTimeOffset.UtcNow; await f.Initialize();
            var transfer = await f.Claim();
            Assert.Equal(TransferStatus.Sealed, (await f.Service.SealTransferAsync(f.Character.Id, transfer.Request.TransferId, f.Source, f.Seal(), default)).Value!.Status);
            var restoredStore = new MongoGameLeaseStore(new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO")), options);
            var restored = (await restoredStore.FindCharacterAsync(f.Character.Id, default))!;
            Assert.Equal(Simulation, restored.State!.Simulation);
            Assert.Equal(TransferStatus.Sealed, restored.Transfer!.Status);
            var service = new GameAdmissionService(f.Characters, restoredStore, f.Catalog,
                new DedicatedKeyValidator(Options.Create(new CharacterGameAccessOptions { DedicatedServers = PartitionCatalog.Servers })),
                f.Clock, f.Sessions, Options.Create(new CharacterGameAccessOptions()));
            var commits = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.CommitTransferAsync(f.Character.Id,
                transfer.Request.TransferId, f.Target, new(transfer.Claim.ClaimId), default)));
            Assert.All(commits, result => Assert.Null(result.Error));
            Assert.Single(commits.Select(result => result.Value!.LeaseId).Distinct());
            Assert.All(commits, result => Assert.Equal(2, result.Value!.AuthorityEpoch));
            Assert.Equal("lease_rejected", (await f.Service.SaveAsync(f.Character.Id, f.Source,
                new(f.Lease.LeaseId, f.Lease.AuthorityEpoch, 1, new(1, 1, 1), new(1, 0)), default)).Error);
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task RapidReturnFencesAnOldSourceEvenWhenThePreviousTransferStatusWasReplaced()
    {
        var scenario = new Scenario();
        await scenario.Initialize();
        await AssertRapidReturn(scenario);
    }

    [MongoFact]
    public async Task MongoRapidReturnFencesTheOldSourceAndPreservesStateAcrossBothTransfers()
    {
        var database = $"arkya_rapid_transfer_test_{Guid.NewGuid():N}";
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        try
        {
            var store = new MongoGameLeaseStore(client, Options.Create(new MongoOptions { DatabaseName = database }));
            await store.EnsureIndexesAsync(default);
            var scenario = new Scenario(store);
            scenario.Clock.Now = DateTimeOffset.UtcNow;
            await scenario.Initialize();
            await AssertRapidReturn(scenario);
        }
        finally { await client.DropDatabaseAsync(database); }
    }

    private static async Task AssertRapidReturn(Scenario f)
    {
        var outbound = await f.Claim();
        await f.Service.SealTransferAsync(f.Character.Id, outbound.Request.TransferId, f.Source, f.Seal(), default);
        Assert.Equal(409, (await f.Service.TransferStatusAsync(f.Character.Id, Guid.NewGuid(), f.Source, default)).Status);
        var sealedHeartbeat = await f.Service.HeartbeatAsync(f.Character.Id, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default);
        Assert.Null(sealedHeartbeat.Error);
        Assert.NotNull(sealedHeartbeat.Value);
        var ownerB = (await f.Service.CommitTransferAsync(f.Character.Id, outbound.Request.TransferId, f.Target,
            new(outbound.Claim.ClaimId), default)).Value!;
        Assert.Equal(2, ownerB.AuthorityEpoch);

        var returning = new PrepareTransferRequest(Guid.NewGuid(), ownerB.LeaseId, ownerB.AuthorityEpoch,
            f.Source.ServerId, f.Catalog.Version, new(500.2f, 33, 250), new(-1, 0), Simulation);
        var preparedReturn = (await f.Service.PrepareTransferAsync(f.Character.Id, f.Target, returning, default)).Value!;
        var oldStatus = await f.Service.TransferStatusAsync(f.Character.Id, outbound.Request.TransferId, f.Source, default);
        Assert.Equal("transfer_rejected", oldStatus.Error);
        Assert.Equal(409, oldStatus.Status);
        var fencedHeartbeat = await f.Service.HeartbeatAsync(f.Character.Id, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default);
        Assert.Equal("lease_rejected", fencedHeartbeat.Error);
        Assert.Equal(409, fencedHeartbeat.Status);
        Assert.Equal("lease_rejected", (await f.Service.SaveAsync(f.Character.Id, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch, 1, new(499, 32, 250), new(1, 0)), default)).Error);
        Assert.False(await f.Service.ReleaseAsync(f.Character.Id, f.Source, new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default));

        var claimedReturn = (await f.Service.ClaimTransferAsync(f.Source, new(preparedReturn.Ticket), default)).Value!;
        Assert.Equal(Guid.Empty, claimedReturn.Admission.LeaseId);
        Assert.Equal(f.Lease.AuthorityEpoch + 2, claimedReturn.Admission.AuthorityEpoch);
        Assert.Equal(f.Character.Id, claimedReturn.CharacterId);
        var finalSimulation = new ActorStateEnvelope(1,
            "{\"motion\":1,\"lastSequence\":2,\"jumpSequence\":41,\"verticalVelocity\":-2.5,\"consumedJumpSequence\":41,\"phase\":3,\"health\":77}");
        var finalPosition = new GamePosition(499.8f, 32.5f, 250);
        var sealedReturn = await f.Service.SealTransferAsync(f.Character.Id, returning.TransferId, f.Target,
            new(ownerB.LeaseId, ownerB.AuthorityEpoch, ownerB.State!.Revision, finalPosition, new(-1, 0), finalSimulation), default);
        Assert.Equal(TransferStatus.Sealed, sealedReturn.Value!.Status);
        Assert.Null((await f.Service.HeartbeatAsync(f.Character.Id, f.Target,
            new(ownerB.LeaseId, ownerB.AuthorityEpoch), default)).Error);
        var returned = (await f.Service.CommitTransferAsync(f.Character.Id, returning.TransferId, f.Source,
            new(claimedReturn.ClaimId), default)).Value!;
        Assert.Equal(f.Source.ServerId, returned.ServerId);
        Assert.Equal(3, returned.AuthorityEpoch);
        Assert.NotEqual(f.Lease.LeaseId, returned.LeaseId);
        Assert.Equal(2, returned.State!.Revision);
        Assert.Equal(finalPosition, returned.State.Position);
        Assert.Equal(finalSimulation, returned.State.Simulation);
        Assert.Equal("lease_rejected", (await f.Service.HeartbeatAsync(f.Character.Id, f.Source,
            new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default)).Error);
        Assert.False(await f.Service.ReleaseAsync(f.Character.Id, f.Source, new(f.Lease.LeaseId, f.Lease.AuthorityEpoch), default));
        Assert.Equal("lease_rejected", (await f.Service.HeartbeatAsync(f.Character.Id, f.Target,
            new(ownerB.LeaseId, ownerB.AuthorityEpoch), default)).Error);
    }
}
