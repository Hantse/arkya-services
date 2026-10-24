using System.Text.Json.Nodes;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.Characters.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Tests;

public sealed class DivingCompatibilityTests
{
    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, -1)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    public void HistoricalAndDivingSnapshotsExposeTheSameValidatedGameplay(int version, int swimVertical)
    {
        var payload = Payload(version, swimVertical);
        var result = CharacterAdminGameplaySnapshot.Read(new(1, payload.ToJsonString()));
        Assert.Equal("available", result.Status);
        Assert.Equal(new(77, 100, 42, 100, 55, 100), result.Vitals);
        Assert.Equal("warrior", result.ActiveClass);
        Assert.Equal(new("warrior", 2, 25), result.Classes[0]);
        Assert.Equal(32768u, result.LearnedRankTwo);

        payload["gameplay"]!["value"]!["Health"] = 101;
        Assert.Equal("invalid", CharacterAdminGameplaySnapshot.Read(new(1, payload.ToJsonString())).Status);
        payload["gameplay"]!["version"] = 2;
        Assert.Equal("unsupported", CharacterAdminGameplaySnapshot.Read(new(1, payload.ToJsonString())).Status);
        payload.AsObject().Remove("gameplay");
        Assert.Equal("invalid", CharacterAdminGameplaySnapshot.Read(new(1, payload.ToJsonString())).Status);
    }

    [Theory]
    [InlineData(GameProtocol.MultiAreaVersion, GameProtocol.MultiAreaBuildId, GameProtocol.DivingVersion, GameProtocol.DivingBuildId, false)]
    [InlineData(GameProtocol.DivingVersion, GameProtocol.DivingBuildId, GameProtocol.MultiAreaVersion, GameProtocol.MultiAreaBuildId, false)]
    [InlineData(GameProtocol.DivingVersion, GameProtocol.DivingBuildId, GameProtocol.DivingVersion, GameProtocol.DivingBuildId, true)]
    [InlineData(GameProtocol.MultiAreaVersion, GameProtocol.MultiAreaBuildId, GameProtocol.MultiAreaVersion, GameProtocol.MultiAreaBuildId, true)]
    [InlineData(GameProtocol.TownNpcVersion, GameProtocol.TownNpcBuildId, GameProtocol.TownNpcVersion, GameProtocol.TownNpcBuildId, true)]
    [InlineData(GameProtocol.TownNpcVersion, GameProtocol.TownNpcBuildId, GameProtocol.DivingVersion, GameProtocol.DivingBuildId, false)]
    [InlineData(GameProtocol.DivingVersion, GameProtocol.DivingBuildId, GameProtocol.TownNpcVersion, GameProtocol.TownNpcBuildId, false)]
    [InlineData(GameProtocol.CraftingVersion, GameProtocol.CraftingBuildId, GameProtocol.CraftingVersion, GameProtocol.CraftingBuildId, true)]
    [InlineData(GameProtocol.CraftingVersion, GameProtocol.CraftingBuildId, GameProtocol.TownNpcVersion, GameProtocol.TownNpcBuildId, false)]
    [InlineData(GameProtocol.TownNpcVersion, GameProtocol.TownNpcBuildId, GameProtocol.CraftingVersion, GameProtocol.CraftingBuildId, false)]
    public async Task AdmissionRequiresAnExactPairAndPreservesTheAuthoritativeSnapshotOnReconnect(
        int serverVersion, string serverBuild, int clientVersion, string clientBuild, bool compatible)
    {
        var account = Guid.NewGuid();
        var clock = new GameClock();
        var characters = new MemoryCharacterRepository();
        var character = (await new CharacterService(characters, clock).CreateAsync(account, "Aelys", default)).Character!;
        var server = new DedicatedServerIdentity
        {
            RealmId = "beta-web", ServerId = "starter-island", Key = "diving-test-key-with-thirty-two-characters"
        };
        var options = Options.Create(new CharacterGameAccessOptions { DedicatedServers = [server] });
        var leases = new MemoryGameLeaseStore();
        var service = new GameAdmissionService(characters, leases, new Catalog(serverVersion, serverBuild),
            new DedicatedKeyValidator(options), clock, new TestSessionReferences(clock, account), options);
        var request = new AdmissionRequest(server.RealmId, clientVersion, clientBuild);
        var reference = SessionReference.Create("diving-compatibility-test");
        var issued = await service.IssueAsync(character.Id, account, clock.Now.AddHours(1), reference, request, default);
        if (!compatible)
        {
            Assert.Equal("incompatible_client", issued.Error);
            Assert.Null(await leases.FindCharacterAsync(character.Id, default));
            return;
        }
        Assert.Null(issued.Error);
        Assert.Equal(serverVersion, issued.Value!.ProtocolVersion);
        Assert.Equal(serverBuild, issued.Value.BuildId);
        var lease = (await service.ConsumeAsync(server, new(issued.Value.Ticket), default)).Value!;
        var snapshot = new ActorStateEnvelope(1, Payload(serverVersion >= GameProtocol.DivingVersion ? 3 : 2, -1).ToJsonString());
        var saved = await service.SaveAsync(character.Id, server,
            new(lease.LeaseId, lease.AuthorityEpoch, 0, new(10, 2, 20), new(0, 1)) { Simulation = snapshot }, default);
        Assert.Null(saved.Error);
        Assert.True(await service.ReleaseAsync(character.Id, server, new(lease.LeaseId, lease.AuthorityEpoch), default));
        var ticket = (await service.IssueAsync(character.Id, account, clock.Now.AddHours(1), reference, request, default)).Value!;
        var restored = (await service.ConsumeAsync(server, new(ticket.Ticket), default)).Value!;
        Assert.Equal(snapshot, restored.State!.Simulation);
        Assert.Equal("available", CharacterAdminGameplaySnapshot.Read(restored.State.Simulation).Status);
    }

    private static JsonNode Payload(int version, int swimVertical)
    {
        var payload = JsonNode.Parse(CharacterAdminDetailTests.GameplayPayload)!;
        payload["version"] = version;
        if (version == 3) payload["swimVertical"] = swimVertical;
        return payload;
    }

    private sealed class Catalog(int version, string buildId) : IGameRealmCatalog
    {
        public Task<GameRealmCatalog> ListAsync(CancellationToken cancellationToken) => Task.FromResult(new GameRealmCatalog([
            new("beta-web", "Diving test", true, "starter-island", "wss://game.example/", version, buildId)]));
    }
}
