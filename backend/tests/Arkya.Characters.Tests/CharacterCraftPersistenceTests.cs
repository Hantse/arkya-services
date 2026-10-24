using System.Text.Json;
using System.Text.Json.Nodes;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Characters;
using Microsoft.Extensions.Options;

namespace Arkya.Characters.Tests;

public sealed class CharacterCraftPersistenceTests
{
    private sealed class Catalog : IGameRealmCatalog
    {
        public static readonly DedicatedServerIdentity[] Servers = [
            new() { RealmId = "beta-web", ServerId = "craft-west", Key = "craft-west-test-key-with-thirty-two-characters" },
            new() { RealmId = "beta-web", ServerId = "craft-east", Key = "craft-east-test-key-with-thirty-two-characters" },
            new() { RealmId = "beta-web", ServerId = "craft-valley", Key = "craft-valley-test-key-with-thirty-two-characters" }];
        public Task<GameRealmCatalog> ListAsync(CancellationToken cancellationToken) => Task.FromResult(new GameRealmCatalog([
            new("beta-web", "Craft persistence", true, Servers[0].ServerId, "wss://game.example/west",
                GameProtocol.CraftSchoolsVersion, GameProtocol.CraftSchoolsBuildId)
            {
                TopologyVersion = "craft-two-areas-v1",
                Areas = [new("starter-island", "starter-island"), new("valley", "handoff-valley")],
                Partitions = [new(Servers[0].ServerId, "wss://game.example/west", "starter-island", 0, new(0, 0, 500, 1000)),
                    new(Servers[1].ServerId, "wss://game.example/east", "starter-island", 1, new(500, 0, 1000, 1000)),
                    new(Servers[2].ServerId, "wss://game.example/valley", "valley", 0, new(0, 0, 1000, 1000))],
                AreaTransitions = [new("starter-to-valley", "starter-island", "valley", new(400, 200, 450, 300), new(100, 30, 250))]
            }]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CraftProgressAndMaterialsSurvivePeriodicSaveHandoffAndReconnect(bool interArea)
    {
        var account = Guid.NewGuid();
        var clock = new GameClock();
        var characters = new MemoryCharacterRepository();
        var character = (await new CharacterService(characters, clock).CreateAsync(account, "Aelys", default)).Character!;
        var options = Options.Create(new CharacterGameAccessOptions { DedicatedServers = Catalog.Servers });
        var service = new GameAdmissionService(characters, new MemoryGameLeaseStore(), new Catalog(),
            new DedicatedKeyValidator(options), clock, new TestSessionReferences(clock, account), options);
        var reference = SessionReference.Create("craft-persistence-test");
        var request = new AdmissionRequest("beta-web", GameProtocol.CraftSchoolsVersion, GameProtocol.CraftSchoolsBuildId);
        var ticket = await service.IssueAsync(character.Id, account, clock.Now.AddHours(1), reference, request, default);
        Assert.Null(ticket.Error);
        var source = Catalog.Servers[0];
        var lease = (await service.ConsumeAsync(source, new(ticket.Value!.Ticket), default)).Value!;
        var possessions = Materials();
        var simulation = Snapshot(37);
        var position = interArea ? new GamePosition(425, 22, 250) : new GamePosition(499, 22, 250);
        var save = new SaveWorldStateRequest(lease.LeaseId, lease.AuthorityEpoch, 0, position, new(1, 0))
            { Simulation = simulation, Possessions = possessions };
        var saved = await service.SaveAsync(character.Id, source, save, default);
        Assert.Null(saved.Error);
        Assert.Equal(simulation, saved.Value!.Simulation);

        var target = Catalog.Servers[interArea ? 2 : 1];
        var transfer = new PrepareTransferRequest(Guid.NewGuid(), lease.LeaseId, lease.AuthorityEpoch, target.ServerId,
            "craft-two-areas-v1", position, new(1, 0), simulation)
            { TransitionId = interArea ? "starter-to-valley" : null, Possessions = possessions };
        var prepared = await service.PrepareTransferAsync(character.Id, source, transfer, default);
        Assert.Null(prepared.Error);
        var claim = await service.ClaimTransferAsync(target, new(prepared.Value!.Ticket), default);
        Assert.Null(claim.Error);
        var final = Snapshot(47);
        var seal = new SealTransferRequest(lease.LeaseId, lease.AuthorityEpoch, saved.Value.Revision,
            interArea ? position : new(500, 22, 250), new(1, 0), final)
            { ArrivalSimulation = interArea ? Snapshot(47, 30) : null, Possessions = possessions };
        Assert.Null((await service.SealTransferAsync(character.Id, transfer.TransferId, source, seal, default)).Error);
        var committed = await service.CommitTransferAsync(character.Id, transfer.TransferId, target, new(claim.Value!.ClaimId), default);
        Assert.Null(committed.Error);
        Assert.Equal(seal.ArrivalSimulation ?? final, committed.Value!.State!.Simulation);
        Assert.True(CharacterPossessionRules.Equivalent(possessions, committed.Value.State.Possessions));

        var transferred = committed.Value;
        Assert.True(await service.ReleaseAsync(character.Id, target, new(transferred.LeaseId, transferred.AuthorityEpoch), default));
        var nextTicket = await service.IssueAsync(character.Id, account, clock.Now.AddHours(1), reference, request, default);
        Assert.Null(nextTicket.Error);
        Assert.Equal(target.ServerId, nextTicket.Value!.ServerId);
        var restored = await service.ConsumeAsync(target, new(nextTicket.Value.Ticket), default);
        Assert.Null(restored.Error);
        Assert.Equal(transferred.State, restored.Value!.State);
        using var payload = JsonDocument.Parse(restored.Value.State!.Simulation!.Payload);
        var craft = payload.RootElement.GetProperty("craft").GetProperty("value");
        Assert.Equal(47, craft.GetProperty("MinerExperience").GetInt32());
        Assert.Equal(231, craft.GetProperty("LoggerExperience").GetInt32());
        Assert.Equal(48, craft.GetProperty("CarpenterExperience").GetInt32());
        Assert.Equal(2, payload.RootElement.GetProperty("craft").GetProperty("version").GetInt32());
        Assert.Equal(3, craft.GetProperty("LearnedRecipes").GetInt32());
        Assert.Equal(7, craft.GetProperty("StarterToolsClaimed").GetInt32());
        Assert.Equal("available", CharacterAdminGameplaySnapshot.Read(restored.Value.State.Simulation).Status);
        Assert.Equal("lease_rejected", (await service.SaveAsync(character.Id, source, save, default)).Error);
    }

    [Theory]
    [InlineData("resource:sandstone", true)]
    [InlineData("resource:stone-brick", true)]
    [InlineData("resource:sandstone-brick", true)]
    [InlineData("resource:unknown-brick", false)]
    [InlineData("resource:sandstone/../../stone", false)]
    public void OnlyDeclaredCraftMaterialIdsExtendTheResourceContract(string itemId, bool valid) =>
        Assert.Equal(valid, CharacterPossessionRules.IsItemId(itemId));

    private static CharacterPossessionsSnapshot Materials() => new(1,
        new(150, [new(0, "resource:stone", 4), new(1, "resource:sandstone", 6), new(2, "resource:stone-brick", 2)]),
        new(150, [new(149, "resource:sandstone-brick", 3)]), []);

    private static ActorStateEnvelope Snapshot(int miningExperience, int supportHeight = 22)
    {
        var payload = JsonNode.Parse(CharacterAdminDetailTests.GameplayPayload)!;
        payload["version"] = 3;
        payload["colliderSupportHeight"] = supportHeight;
        payload["craft"] = JsonNode.Parse($$$"""
            {"version":2,"value":{"ActiveSchool":4,"MinerLevel":4,"MinerExperience":{{{miningExperience}}},
            "BlacksmithLevel":3,"BlacksmithExperience":19,"LoggerLevel":10,"LoggerExperience":231,
            "CarpenterLevel":2,"CarpenterExperience":48,"LearnedRecipes":3,"StarterToolsClaimed":7}}
            """);
        return new(1, payload.ToJsonString());
    }
}
