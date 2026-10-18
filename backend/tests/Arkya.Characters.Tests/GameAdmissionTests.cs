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
using MongoDB.Driver;

namespace Arkya.Characters.Tests;

internal sealed class GameClock : TimeProvider
{
    public DateTimeOffset Now = FixedTimeProvider.Now;
    public override DateTimeOffset GetUtcNow() => Now;
}
internal sealed class MemoryGameLeaseStore : IGameLeaseStore
{
    public Action? AfterRead { get; set; }
    private readonly Dictionary<Guid, GameLeaseRecord> records = [];
    private readonly object gate = new();
    public Task<GameLeaseRecord?> FindCharacterAsync(Guid id, CancellationToken cancellationToken)
    { lock (gate) { var value = records.GetValueOrDefault(id); AfterRead?.Invoke(); return Task.FromResult(value); } }
    public Task<GameLeaseRecord?> FindTicketAsync(string hash, CancellationToken cancellationToken)
    { lock (gate) { var value = records.Values.SingleOrDefault(value => value.TicketHash == hash); AfterRead?.Invoke(); return Task.FromResult(value); } }
    public Task<bool> TryWriteAsync(GameLeaseRecord value, long? expectedVersion, CancellationToken cancellationToken, GameWriteCondition condition = GameWriteCondition.None)
    {
        lock (gate)
        {
            if (records.GetValueOrDefault(value.CharacterId)?.Version != expectedVersion) return Task.FromResult(false);
            records[value.CharacterId] = value;
            return Task.FromResult(true);
        }
    }
}
internal sealed class TestGameCatalog : IGameRealmCatalog
{
    public Task<GameRealmCatalog> ListAsync(CancellationToken cancellationToken) => Task.FromResult(new GameRealmCatalog([
        new("beta-web", "Web beta", true, "starter-island", "wss://game.example/", 1, GameProtocol.BuildId),
        new("other-realm", "Other", true, "starter-island", "wss://other.example/", 1, GameProtocol.BuildId)]));
}

internal sealed class TestSessionReferences(TimeProvider clock, Guid accountId) : ISessionReferenceValidator
{
    public SessionValidationStatus Status { get; set; } = SessionValidationStatus.Valid;
    public Guid AccountId { get; set; } = accountId;
    public DateTimeOffset? ExpiresAt { get; set; }
    public Action? AfterValidation { get; set; }
    public int Calls { get; private set; }
    public Task<SessionValidationResult> ValidateReferenceAsync(string sessionReference, CancellationToken cancellationToken)
    {
        Assert.True(SessionReference.IsValid(sessionReference));
        Calls++;
        var result = new SessionValidationResult(Status, Status == SessionValidationStatus.Valid
            ? new(AccountId, "Test player", ExpiresAt ?? clock.GetUtcNow().AddHours(1)) : null);
        AfterValidation?.Invoke();
        return Task.FromResult(result);
    }
}

public sealed class GameAdmissionTests
{
    private static readonly DedicatedServerIdentity Server = new() { RealmId = "beta-web", ServerId = "starter-island", Key = "dedicated-test-key-with-thirty-two-characters" };
    private static readonly Guid Account = Guid.NewGuid();
    private static readonly AdmissionRequest Request = new("beta-web", 1, GameProtocol.BuildId);
    private static readonly string Reference = SessionReference.Create("test-session-reference");
    private static DedicatedKeyValidator Keys() => new(Options.Create(new CharacterGameAccessOptions { DedicatedServers = [Server,
        new() { RealmId = "other-realm", ServerId = "starter-island", Key = "other-dedicated-key-with-thirty-two-characters" }] }));
    private static async Task<(GameAdmissionService Service, Character Character, GameClock Clock, IGameLeaseStore Store, TestSessionReferences Sessions)> Setup(IGameLeaseStore? store = null)
    {
        var characters = new MemoryCharacterRepository();
        var clock = new GameClock();
        var character = (await new CharacterService(characters, clock).CreateAsync(Account, "Aelys", default)).Character!;
        store ??= new MemoryGameLeaseStore();
        var sessions = new TestSessionReferences(clock, Account);
        return (new(characters, store, new TestGameCatalog(), Keys(), clock, sessions), character, clock, store, sessions);
    }

    [Fact]
    public async Task RevocationRejectsPendingTicketsRenewalAndSaveButStillAllowsRelease()
    {
        var f = await Setup();
        var ticket = (await f.Service.IssueAsync(f.Character.Id, Account, f.Clock.Now.AddHours(1), Reference, Request, default)).Value!;
        Assert.Equal(Reference, (await f.Store.FindCharacterAsync(f.Character.Id, default))!.SessionReference);
        f.Sessions.Status = SessionValidationStatus.Invalid;
        Assert.Equal("admission_rejected", (await f.Service.ConsumeAsync(Server, new(ticket.Ticket), default)).Error);
        f.Sessions.Status = SessionValidationStatus.Valid;
        var lease = (await f.Service.ConsumeAsync(Server, new(ticket.Ticket), default)).Value!;
        var save = new SaveWorldStateRequest(lease.LeaseId, lease.AuthorityEpoch, 0, new(1, 2, 3), new(0, 1));
        Assert.Null((await f.Service.SaveAsync(f.Character.Id, Server, save, default)).Error);
        var before = await f.Store.FindCharacterAsync(f.Character.Id, default);
        f.Sessions.Status = SessionValidationStatus.Invalid;
        Assert.Equal("lease_rejected", (await f.Service.HeartbeatAsync(f.Character.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch), default)).Error);
        Assert.Equal("lease_rejected", (await f.Service.SaveAsync(f.Character.Id, Server, save, default)).Error);
        Assert.Equal(before, await f.Store.FindCharacterAsync(f.Character.Id, default));
        var validationCalls = f.Sessions.Calls;
        Assert.True(await f.Service.ReleaseAsync(f.Character.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch), default));
        Assert.Equal(validationCalls, f.Sessions.Calls);
    }

    [Fact]
    public async Task IdentityFailureAndWrongOwnerCannotGrantOrExtendAuthority()
    {
        var f = await Setup();
        var ticket = (await f.Service.IssueAsync(f.Character.Id, Account, f.Clock.Now.AddHours(1), Reference, Request, default)).Value!;
        f.Sessions.Status = SessionValidationStatus.Unavailable;
        Assert.Equal(503, (await f.Service.ConsumeAsync(Server, new(ticket.Ticket), default)).Status);
        f.Sessions.Status = SessionValidationStatus.Valid;
        f.Sessions.AccountId = Guid.NewGuid();
        Assert.Equal("admission_rejected", (await f.Service.ConsumeAsync(Server, new(ticket.Ticket), default)).Error);
        f.Sessions.AccountId = Account;
        var lease = (await f.Service.ConsumeAsync(Server, new(ticket.Ticket), default)).Value!;
        var before = await f.Store.FindCharacterAsync(f.Character.Id, default);
        f.Sessions.Status = SessionValidationStatus.Unavailable;
        Assert.Equal(503, (await f.Service.HeartbeatAsync(f.Character.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch), default)).Status);
        Assert.Equal(503, (await f.Service.SaveAsync(f.Character.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch, 0, new(1, 2, 3), new(0, 1)), default)).Status);
        Assert.Equal(before, await f.Store.FindCharacterAsync(f.Character.Id, default));
        f.Sessions.Status = SessionValidationStatus.Valid;
        f.Sessions.AccountId = Guid.NewGuid();
        Assert.Equal("lease_rejected", (await f.Service.HeartbeatAsync(f.Character.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch), default)).Error);
    }

    [Fact]
    public async Task IdentityLatencyAndChangedExpiryCannotOutliveTheOriginalSession()
    {
        var f = await Setup();
        var originalExpiry = f.Clock.Now.AddSeconds(45);
        var ticket = (await f.Service.IssueAsync(f.Character.Id, Account, originalExpiry, Reference, Request, default)).Value!;
        f.Sessions.ExpiresAt = originalExpiry.AddHours(1);
        var lease = (await f.Service.ConsumeAsync(Server, new(ticket.Ticket), default)).Value!;
        Assert.Equal(originalExpiry, lease.SessionExpiresAt);
        Assert.Equal(originalExpiry, (await f.Service.HeartbeatAsync(f.Character.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch), default)).Value!.LeaseExpiresAt);
        f.Sessions.ExpiresAt = originalExpiry.AddSeconds(-10);
        Assert.Equal(f.Sessions.ExpiresAt, (await f.Service.HeartbeatAsync(f.Character.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch), default)).Value!.LeaseExpiresAt);
        var before = await f.Store.FindCharacterAsync(f.Character.Id, default);
        f.Sessions.AfterValidation = () => f.Clock.Now = originalExpiry;
        Assert.Equal("lease_rejected", (await f.Service.SaveAsync(f.Character.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch, 0, new(1, 2, 3), new(0, 1)), default)).Error);
        Assert.Equal(before, await f.Store.FindCharacterAsync(f.Character.Id, default));
    }

    [Fact]
    public async Task LegacyTicketWithoutSessionReferenceFailsClosed()
    {
        var f = await Setup();
        var ticket = (await f.Service.IssueAsync(f.Character.Id, Account, f.Clock.Now.AddHours(1), Reference, Request, default)).Value!;
        var before = (await f.Store.FindCharacterAsync(f.Character.Id, default))!;
        Assert.True(await f.Store.TryWriteAsync(before with { Version = before.Version + 1, SessionReference = null }, before.Version, default));
        Assert.Equal("admission_rejected", (await f.Service.ConsumeAsync(Server, new(ticket.Ticket), default)).Error);
        Assert.Equal(0, f.Sessions.Calls);
    }

    [Fact]
    public async Task ExpirationDuringReadCannotConsumeRenewSaveOrRelease()
    {
        var store = new MemoryGameLeaseStore();
        var f = await Setup(store);
        async Task<AdmissionResponse> Ticket() => (await f.Service.IssueAsync(f.Character.Id, Account, f.Clock.Now.AddHours(1), Reference, Request, default)).Value!;
        var first = await Ticket();
        store.AfterRead = () => f.Clock.Now = f.Clock.Now.AddSeconds(61);
        Assert.Equal("admission_rejected", (await f.Service.ConsumeAsync(Server, new(first.Ticket), default)).Error);
        store.AfterRead = null;
        var next = await Ticket();
        var lease = (await f.Service.ConsumeAsync(Server, new(next.Ticket), default)).Value!;
        var activeTime = f.Clock.Now;
        store.AfterRead = () => f.Clock.Now = activeTime.AddSeconds(91);
        Assert.Equal("lease_rejected", (await f.Service.HeartbeatAsync(f.Character.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch), default)).Error);
        f.Clock.Now = activeTime;
        Assert.Equal("lease_rejected", (await f.Service.SaveAsync(f.Character.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch, 0, new(1, 2, 3), new(0, 1)), default)).Error);
        f.Clock.Now = activeTime;
        Assert.False(await f.Service.ReleaseAsync(f.Character.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch), default));
    }

    [Fact]
    public async Task TicketsAreHashedSingleUseDestinationBoundAndAcquireOneLease()
    {
        var f = await Setup();
        var issue = await f.Service.IssueAsync(f.Character.Id, Account, f.Clock.Now.AddHours(1), Reference, Request, default);
        Assert.Null(issue.Error);
        var stored = await f.Store.FindCharacterAsync(f.Character.Id, default);
        Assert.NotEqual(issue.Value!.Ticket, stored!.TicketHash);
        Assert.Equal(GameAdmissionService.HashTicket(issue.Value.Ticket), stored.TicketHash);
        Assert.Equal("admission_rejected", (await f.Service.ConsumeAsync(new() { RealmId = "other-realm", ServerId = Server.ServerId }, new(issue.Value.Ticket), default)).Error);
        var consumes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Service.ConsumeAsync(Server, new(issue.Value.Ticket), default)));
        var accepted = Assert.Single(consumes, value => value.Error is null).Value!;
        Assert.Equal(1, accepted.AuthorityEpoch);
        Assert.Equal("beta-web", accepted.Character.RealmId);
        Assert.All(consumes.Where(value => value.Error is not null), value => Assert.Equal("admission_rejected", value.Error));
        Assert.Equal("character_in_use", (await f.Service.IssueAsync(f.Character.Id, Account, f.Clock.Now.AddHours(1), Reference, Request, default)).Error);
    }

    [Fact]
    public async Task OwnerRealmVersionExpiryAndReplacedTicketsAreRejected()
    {
        var f = await Setup();
        Assert.Equal("character_not_found", (await f.Service.IssueAsync(f.Character.Id, Guid.NewGuid(), f.Clock.Now.AddHours(1), Reference, Request, default)).Error);
        Assert.Equal("incompatible_client", (await f.Service.IssueAsync(f.Character.Id, Account, f.Clock.Now.AddHours(1), Reference, Request with { ProtocolVersion = 2 }, default)).Error);
        var first = (await f.Service.IssueAsync(f.Character.Id, Account, f.Clock.Now.AddHours(1), Reference, Request, default)).Value!;
        var second = (await f.Service.IssueAsync(f.Character.Id, Account, f.Clock.Now.AddHours(1), Reference, Request, default)).Value!;
        Assert.Equal("admission_rejected", (await f.Service.ConsumeAsync(Server, new(first.Ticket), default)).Error);
        Assert.Equal("realm_mismatch", (await f.Service.IssueAsync(f.Character.Id, Account, f.Clock.Now.AddHours(1), Reference, Request with { RealmId = "other-realm" }, default)).Error);
        f.Clock.Now = f.Clock.Now.AddSeconds(61);
        Assert.Equal("admission_rejected", (await f.Service.ConsumeAsync(Server, new(second.Ticket), default)).Error);
    }

    [Fact]
    public async Task StateSurvivesReleaseAndOldEpochCannotWriteOrRenew()
    {
        var f = await Setup();
        async Task<ConsumedAdmissionResponse> Enter()
        {
            var ticket = (await f.Service.IssueAsync(f.Character.Id, Account, f.Clock.Now.AddHours(1), Reference, Request, default)).Value!;
            return (await f.Service.ConsumeAsync(Server, new(ticket.Ticket), default)).Value!;
        }
        var first = await Enter();
        var save = new SaveWorldStateRequest(first.LeaseId, first.AuthorityEpoch, 0, new(10, 31, 20), new(0, 1));
        Assert.Equal(1, (await f.Service.SaveAsync(f.Character.Id, Server, save, default)).Value!.Revision);
        Assert.Equal(1, (await f.Service.SaveAsync(f.Character.Id, Server, save, default)).Value!.Revision);
        Assert.Equal("state_conflict", (await f.Service.SaveAsync(f.Character.Id, Server, save with { Position = new(15, 31, 20) }, default)).Error);
        Assert.Equal("invalid_request", (await f.Service.SaveAsync(f.Character.Id, Server, save with { Position = new(float.NaN, 1, 2) }, default)).Error);
        Assert.True(await f.Service.ReleaseAsync(f.Character.Id, Server, new(first.LeaseId, first.AuthorityEpoch), default));
        var next = await Enter();
        Assert.Equal(2, next.AuthorityEpoch);
        Assert.Equal(save.Position, next.State!.Position);
        Assert.Equal("lease_rejected", (await f.Service.SaveAsync(f.Character.Id, Server, save, default)).Error);
        Assert.Equal("lease_rejected", (await f.Service.HeartbeatAsync(f.Character.Id, Server, new(first.LeaseId, 1), default)).Error);
        f.Clock.Now = f.Clock.Now.AddSeconds(91);
        Assert.Equal("lease_rejected", (await f.Service.HeartbeatAsync(f.Character.Id, Server, new(next.LeaseId, 2), default)).Error);
    }

    [Fact]
    public async Task LeaseAndRenewalNeverOutliveTheAuthenticatedSession()
    {
        var f = await Setup();
        var expires = f.Clock.Now.AddSeconds(25);
        var ticket = (await f.Service.IssueAsync(f.Character.Id, Account, expires, Reference, Request, default)).Value!;
        Assert.Equal(expires, ticket.ExpiresAt);
        var lease = (await f.Service.ConsumeAsync(Server, new(ticket.Ticket), default)).Value!;
        Assert.Equal(expires, lease.LeaseExpiresAt);
        f.Clock.Now = f.Clock.Now.AddSeconds(10);
        Assert.Equal(expires, (await f.Service.HeartbeatAsync(f.Character.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch), default)).Value!.LeaseExpiresAt);
    }

    [Fact]
    public async Task HttpRoutesRequireSessionAndBoundDedicatedKeyAndExposeOnlyApprovedCors()
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
        using var client = factory.PlayerClient();
        var character = await (await client.PostAsJsonAsync("/v1/characters", new { name = "Aelys" })).Content.ReadFromJsonAsync<CharacterResponse>();
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/v1/realms");
        preflight.Headers.Add("Origin", "https://site.example"); preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "authorization");
        var cors = await client.SendAsync(preflight);
        Assert.Equal("https://site.example", cors.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.False(cors.Headers.Contains("Access-Control-Allow-Credentials"));
        var ticket = await (await client.PostAsJsonAsync($"/v1/characters/{character!.Id}/admissions", Request)).Content.ReadFromJsonAsync<AdmissionResponse>();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/internal/game/admissions/consume", new { ticket = ticket!.Ticket })).StatusCode);
        client.DefaultRequestHeaders.Add("X-Arkya-Dedicated-Key", Server.Key);
        var consume = await client.PostAsJsonAsync("/internal/game/admissions/consume", new { ticket = ticket!.Ticket });
        Assert.Equal(HttpStatusCode.OK, consume.StatusCode);
        Assert.Equal("no-store", consume.Headers.CacheControl!.ToString());
        using var anonymous = factory.PlayerClient(null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/v1/realms")).StatusCode);
    }

    [MongoFact]
    public async Task RealMongoPreservesStateAndAtomicTicketConsumptionAcrossInstances()
    {
        var database = "arkya_game_leases_test_" + Guid.NewGuid().ToString("N");
        var client = new MongoClient(Environment.GetEnvironmentVariable("ARKYA_TEST_MONGO"));
        try
        {
            var options = Options.Create(new MongoOptions { DatabaseName = database });
            var store = new MongoGameLeaseStore(client, options);
            await store.EnsureIndexesAsync(default);
            var f = await Setup(store);
            f.Clock.Now = DateTimeOffset.UtcNow;
            var issue = (await f.Service.IssueAsync(f.Character.Id, Account, f.Clock.Now.AddHours(1), Reference, Request, default)).Value!;
            var consumes = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => f.Service.ConsumeAsync(Server, new(issue.Ticket), default)));
            var lease = Assert.Single(consumes, value => value.Error is null).Value!;
            Assert.Null((await store.FindCharacterAsync(f.Character.Id, default))!.TicketHash);
            var save = await f.Service.SaveAsync(f.Character.Id, Server, new(lease.LeaseId, lease.AuthorityEpoch, 0, new(10, 32, 50), new(0, 1)), default);
            var restarted = await new MongoGameLeaseStore(client, options).FindCharacterAsync(f.Character.Id, default);
            Assert.Equal(save.Value, restarted!.State);
            var expired = restarted with { Version = restarted.Version + 1, LeaseExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1) };
            Assert.True(await store.TryWriteAsync(expired, restarted.Version, default));
            Assert.False(await store.TryWriteAsync(expired with { Version = expired.Version + 1, LeaseExpiresAt = DateTimeOffset.UtcNow.AddMinutes(1) },
                expired.Version, default, GameWriteCondition.ActiveLease));
        }
        finally { await client.DropDatabaseAsync(database); }
    }
}
