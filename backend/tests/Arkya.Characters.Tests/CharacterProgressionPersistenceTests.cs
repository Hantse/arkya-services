using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.ServiceDefaults;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Tests;

public sealed class CharacterProgressionPersistenceTests
{
    private sealed class StatsCatalog : IGameRealmCatalog
    {
        public Task<GameRealmCatalog> ListAsync(CancellationToken cancellationToken) => Task.FromResult(new GameRealmCatalog([
            new("beta-web", "Stats realm", true, "starter-island", "wss://game.example/",
                GameProtocol.HandoffVersion, GameProtocol.CharacterStatsBuildId)]));
    }

    [Fact]
    public async Task ServerProgressionSurvivesSaveReleaseAndReadmissionWhileOldAuthorityIsFenced()
    {
        var account = Guid.NewGuid();
        var clock = new GameClock();
        var characters = new MemoryCharacterRepository();
        var character = (await new CharacterService(characters, clock).CreateAsync(account, "Aelys", default)).Character!;
        var server = new DedicatedServerIdentity { RealmId = "beta-web", ServerId = "starter-island",
            Key = "progression-test-dedicated-key-1234567890" };
        var options = Options.Create(new CharacterGameAccessOptions { DedicatedServers = [server] });
        var service = new GameAdmissionService(characters, new MemoryGameLeaseStore(), new StatsCatalog(),
            new DedicatedKeyValidator(options), clock, new TestSessionReferences(clock, account), options);
        var reference = SessionReference.Create("progression-test-session");
        var request = new AdmissionRequest("beta-web", GameProtocol.HandoffVersion, GameProtocol.CharacterStatsBuildId);
        var firstTicket = await service.IssueAsync(character.Id, account, clock.Now.AddHours(1), reference, request, default);
        Assert.Null(firstTicket.Error);
        var first = (await service.ConsumeAsync(server, new(firstTicket.Value!.Ticket), default)).Value!;
        var simulation = new ActorStateEnvelope(1, """
            {"version":2,"motion":0,"gameplay":{"version":1,"value":{
              "Health":77,"MaxHealth":100,"Mana":42,"MaxMana":100,"Stamina":55,"MaxStamina":100,
              "ActiveClass":1,"WarriorLevel":2,"WarriorExperience":25,"ScoutLevel":0,"ScoutExperience":0,
              "MageLevel":0,"MageExperience":0,"ClericLevel":5,"ClericExperience":0,"DruidLevel":0,"DruidExperience":0,
              "LearnedRankOne":32771,"LearnedRankTwo":32768}}}
            """);
        var save = new SaveWorldStateRequest(first.LeaseId, first.AuthorityEpoch, 0, new(410, 32, 600), new(0, 1))
            { Simulation = simulation };
        var saved = await service.SaveAsync(character.Id, server, save, default);
        Assert.Null(saved.Error);
        Assert.Equal(simulation, saved.Value!.Simulation);
        Assert.True(await service.ReleaseAsync(character.Id, server, new(first.LeaseId, first.AuthorityEpoch), default));

        var nextTicket = await service.IssueAsync(character.Id, account, clock.Now.AddHours(1), reference, request, default);
        Assert.Null(nextTicket.Error);
        var restored = await service.ConsumeAsync(server, new(nextTicket.Value!.Ticket), default);
        Assert.Null(restored.Error);
        Assert.Equal(simulation, restored.Value!.State!.Simulation);
        Assert.Equal(saved.Value.Revision, restored.Value.State.Revision);
        Assert.True(restored.Value.AuthorityEpoch > first.AuthorityEpoch);
        Assert.Equal("lease_rejected", (await service.SaveAsync(character.Id, server, save, default)).Error);
    }

    [Fact]
    public async Task OldClientBuildCannotBeAdmittedToTheNewGhostSchema()
    {
        var account = Guid.NewGuid();
        var clock = new GameClock();
        var characters = new MemoryCharacterRepository();
        var character = (await new CharacterService(characters, clock).CreateAsync(account, "Aelys", default)).Character!;
        var server = new DedicatedServerIdentity { RealmId = "beta-web", ServerId = "starter-island",
            Key = "progression-test-dedicated-key-1234567890" };
        var options = Options.Create(new CharacterGameAccessOptions { DedicatedServers = [server] });
        var service = new GameAdmissionService(characters, new MemoryGameLeaseStore(), new StatsCatalog(),
            new DedicatedKeyValidator(options), clock, new TestSessionReferences(clock, account), options);
        var result = await service.IssueAsync(character.Id, account, clock.Now.AddHours(1),
            SessionReference.Create("progression-test-session"),
            new("beta-web", GameProtocol.HandoffVersion, GameProtocol.HandoffBuildId), default);
        Assert.Equal("incompatible_client", result.Error);
        Assert.Null(result.Value);
    }
}
