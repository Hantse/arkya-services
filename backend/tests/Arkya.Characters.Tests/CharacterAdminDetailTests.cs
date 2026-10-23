using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.Characters.Api.Domain;
using Arkya.Characters.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Characters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arkya.Characters.Tests;

public sealed class CharacterAdminDetailTests
{
    private const string AdminKey = "character-detail-admin-key-with-thirty-two-characters";
    internal const string GameplayPayload = """
        {"version":2,"motion":0,"gameplay":{"version":1,"value":{
        "Health":77,"MaxHealth":100,"Mana":42,"MaxMana":100,"Stamina":55,"MaxStamina":100,
        "ActiveClass":1,"WarriorLevel":2,"WarriorExperience":25,"ScoutLevel":0,"ScoutExperience":0,
        "MageLevel":0,"MageExperience":0,"ClericLevel":5,"ClericExperience":0,"DruidLevel":0,"DruidExperience":0,
        "LearnedRankOne":32771,"LearnedRankTwo":32768}}}
        """;

    [Fact]
    public async Task DetailRequiresTheSeparateAdminKeyAndValidatesEveryScopeAndPageFilter()
    {
        var repository = new DetailRepository();
        await using var factory = Factory(repository);
        using var client = factory.PlayerClient();
        var path = "/admin/characters/" + repository.Character.Id.ToString("D");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        client.DefaultRequestHeaders.Add(ServiceKeyValidator.HeaderName, CharactersFactory.ServiceKey);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(0, repository.FindCalls);
        client.DefaultRequestHeaders.Add(CharacterAdminKeyValidator.HeaderName, AdminKey);
        foreach (var suffix in new[] { "?realmId=Other", "?realmId=../other", "?questPage=0", "?questPage=100001",
            "?questPageSize=0", "?questPageSize=101", "?questPage=abc" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(path + suffix)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/admin/characters/not-a-uuid")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/admin/characters/" + Guid.Empty)).StatusCode);
        Assert.Equal(0, repository.FindCalls);
        Assert.Equal(0, factory.Identity.Calls);
    }

    [Fact]
    public async Task DetailProjectsSavedStateQuestsAndAppearanceWithoutExposingAuthorityCredentials()
    {
        var repository = new DetailRepository();
        var lease = Lease(repository.Character);
        await using var factory = Factory(repository, lease);
        using var client = factory.PlayerClient(null);
        client.DefaultRequestHeaders.Add(CharacterAdminKeyValidator.HeaderName, AdminKey);
        var response = await client.GetAsync($"/admin/characters/{repository.Character.Id:D}?realmId=beta-web&questPage=2&questPageSize=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        var detail = (await response.Content.ReadFromJsonAsync<AdminCharacterDetail>())!;
        Assert.Equal(repository.Character.Id.ToString("D"), detail.Character.CharacterId);
        Assert.Equal(FixedTimeProvider.Now, detail.ObservedAt);
        Assert.Equal(lease.LastConnectedAt, detail.LastConnectedAt);
        Assert.Equal(175, detail.Appearance.HeightCm);
        Assert.Equal("available", detail.Gameplay.Status);
        Assert.Equal(new(77, 100, 42, 100, 55, 100), detail.Gameplay.Vitals);
        Assert.Equal("warrior", detail.Gameplay.ActiveClass);
        Assert.Equal(new("warrior", 2, 25), detail.Gameplay.Classes[0]);
        Assert.Equal(32768u, detail.Gameplay.LearnedRankTwo);
        Assert.Equal(12, detail.WorldState!.Revision);
        Assert.Equal("starter-island", detail.WorldState.AreaId);
        Assert.Equal("island-content", detail.WorldState.ContentId);
        Assert.Equal(410, detail.WorldState.X);
        Assert.Equal(2, detail.Quests.Page);
        Assert.Equal(3, detail.Quests.Total);
        Assert.Equal("hunt-two", Assert.Single(detail.Quests.Items).QuestId);
        Assert.True(detail.Quests.Items[0].Completed);
        Assert.Equal("not_saved", detail.Bags.Status);
        Assert.Equal("not_saved", detail.Bank.Status);
        Assert.Equal("not_saved", detail.Equipment.Status);
        var json = await response.Content.ReadAsStringAsync();
        foreach (var secret in new[] { "private-", "ticketHash", "leaseId", "sessionReference", "authorityEpoch", "payload" })
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(FixedTimeProvider.Now, repository.ObservedAt);
        Assert.Equal(0, factory.Identity.Calls);
    }

    [Fact]
    public async Task DetailReturnsTheSavedContentsAndToolWithoutExposingAuthorityCredentials()
    {
        var repository = new DetailRepository();
        var lease = Lease(repository.Character);
        var snapshot = Possessions();
        lease = lease with { State = lease.State! with { Possessions = snapshot } };
        await using var factory = Factory(repository, lease);
        using var client = factory.PlayerClient(null);
        client.DefaultRequestHeaders.Add(CharacterAdminKeyValidator.HeaderName, AdminKey);
        var response = await client.GetAsync($"/admin/characters/{repository.Character.Id:D}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var detail = (await response.Content.ReadFromJsonAsync<AdminCharacterDetail>())!;
        Assert.Equal("available", detail.Bags.Status);
        Assert.Equal(150, detail.Bags.SlotCapacity);
        Assert.Equal(new AdminCharacterStoredItem("0", "resource:wood", 27), detail.Bags.Items[0]);
        Assert.Equal(new AdminCharacterStoredItem("149", "travel-rations", 3), detail.Bags.Items[1]);
        Assert.Equal("available", detail.Bank.Status);
        Assert.Equal(80, detail.Bank.SlotCapacity);
        Assert.Equal(new AdminCharacterStoredItem("79", "old-copper-ring", 1), Assert.Single(detail.Bank.Items));
        Assert.Equal("available", detail.Equipment.Status);
        Assert.Null(detail.Equipment.SlotCapacity);
        Assert.Equal(new AdminCharacterStoredItem("chest", "traveller-tunic", 1), detail.Equipment.Items[0]);
        Assert.Equal(new AdminCharacterStoredItem("main-hand", "valley-sword", 1), detail.Equipment.Items[1]);
        var tool = detail.Equipment.HarvestTool!;
        Assert.NotNull(tool);
        Assert.Equal("ore", tool.Family);
        Assert.Equal(4, tool.Level);
        Assert.Equal(1250, tool.BaseYieldBonusBps);
        Assert.Equal(200, tool.YieldBonusPerLevelBps);
        Assert.Equal(2, tool.SocketCapacity);
        Assert.Equal(new AdminCharacterSocketedStone(2500, 5000), tool.Stones[0]);
        Assert.Equal(new AdminCharacterSocketedStone(500, 250), tool.Stones[1]);
        var json = await response.Content.ReadAsStringAsync();
        foreach (var secret in new[] { "private-", "ticketHash", "leaseId", "sessionReference", "authorityEpoch", "payload" })
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, factory.Identity.Calls);
    }

    [Fact]
    public void EmptySavedContainersAreAvailableAndDistinctFromAnAbsentSnapshot()
    {
        var empty = new CharacterPossessionsSnapshot(1, new(150, []), new(80, []), []);
        var saved = CharacterAdminPossessionsProjection.Read(empty);
        Assert.All(new[] { saved.Bags, saved.Bank, saved.Equipment }, section =>
        {
            Assert.Equal("available", section.Status);
            Assert.Empty(section.Items);
            Assert.Null(section.HarvestTool);
        });
        Assert.Equal(150, saved.Bags.SlotCapacity);
        Assert.Equal(80, saved.Bank.SlotCapacity);
        Assert.Null(saved.Equipment.SlotCapacity);
        var absent = CharacterAdminPossessionsProjection.Read(null);
        Assert.All(new[] { absent.Bags, absent.Bank, absent.Equipment }, section =>
        {
            Assert.Equal("not_saved", section.Status);
            Assert.Null(section.SlotCapacity);
            Assert.Empty(section.Items);
            Assert.Null(section.HarvestTool);
        });
    }

    [Fact]
    public void InvalidOrUnsupportedPersistedPossessionsDoNotExposePartialContents()
    {
        var valid = Possessions();
        var cases = new[]
        {
            (valid with { SchemaVersion = 99 }, "unsupported"),
            (valid with { Bank = new(0, []) }, "invalid"),
            (valid with { Bags = null! }, "invalid"),
            (valid with { Equipment = [new("head", "painted-cap", 1), new("head", "other-cap", 1)] }, "invalid"),
            (valid with { HarvestTool = valid.HarvestTool! with { Stones = [new(0, 10001)] } }, "invalid"),
            (JsonSerializer.Deserialize<CharacterPossessionsSnapshot>("""
                {"schemaVersion":1,"bags":{"slotCapacity":150,"slots":[]},"bank":{"slotCapacity":80,"slots":[]}}
                """, new JsonSerializerOptions(JsonSerializerDefaults.Web))!, "invalid")
        };
        foreach (var (snapshot, status) in cases)
        {
            var projection = CharacterAdminPossessionsProjection.Read(snapshot);
            Assert.All(new[] { projection.Bags, projection.Bank, projection.Equipment }, section =>
            {
                Assert.Equal(status, section.Status);
                Assert.Empty(section.Items);
                Assert.Null(section.SlotCapacity);
                Assert.Null(section.HarvestTool);
            });
        }
    }

    [Fact]
    public async Task MissingCharactersAndWrongRealmReturnNotFoundAndNeverFallBackToGlobalData()
    {
        var repository = new DetailRepository();
        await using var factory = Factory(repository);
        using var client = factory.PlayerClient(null);
        client.DefaultRequestHeaders.Add(CharacterAdminKeyValidator.HeaderName, AdminKey);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/admin/characters/{Guid.NewGuid():D}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/admin/characters/{repository.Character.Id:D}?realmId=other-realm")).StatusCode);
        Assert.Equal(0, repository.QuestCalls);
        var detail = (await client.GetFromJsonAsync<AdminCharacterDetail>($"/admin/characters/{repository.Character.Id:D}"))!;
        Assert.Null(detail.WorldState);
        Assert.Null(detail.LastConnectedAt);
        Assert.Equal("not_saved", detail.Gameplay.Status);
        Assert.Null(detail.Gameplay.Vitals);
        Assert.Empty(detail.Gameplay.Classes);
    }

    [Fact]
    public async Task SnapshotFromAnInconsistentLeaseIsNotExposed()
    {
        var repository = new DetailRepository();
        var lease = Lease(repository.Character);
        lease = lease with { AccountId = Guid.NewGuid(), State = lease.State! with { Possessions = Possessions() } };
        await using var factory = Factory(repository, lease);
        using var client = factory.PlayerClient(null);
        client.DefaultRequestHeaders.Add(CharacterAdminKeyValidator.HeaderName, AdminKey);
        var detail = (await client.GetFromJsonAsync<AdminCharacterDetail>($"/admin/characters/{repository.Character.Id:D}"))!;
        Assert.Null(detail.WorldState);
        Assert.Null(detail.LastConnectedAt);
        Assert.Equal("not_saved", detail.Gameplay.Status);
        Assert.All(new[] { detail.Bags, detail.Bank, detail.Equipment }, section =>
        {
            Assert.Equal("not_saved", section.Status);
            Assert.Empty(section.Items);
        });
    }

    [Theory]
    [InlineData("not-json", "invalid")]
    [InlineData("[]", "invalid")]
    [InlineData("{\"version\":1}", "not_saved")]
    [InlineData("{\"version\":4}", "unsupported")]
    [InlineData("{\"version\":3}", "invalid")]
    [InlineData("{\"version\":2}", "invalid")]
    [InlineData("{\"version\":2,\"gameplay\":{\"version\":2}}", "unsupported")]
    public void InvalidUnsupportedAndHistoricalPayloadsRemainDistinct(string payload, string expected)
    {
        var result = CharacterAdminGameplaySnapshot.Read(new(1, payload));
        Assert.Equal(expected, result.Status);
        Assert.Null(result.Vitals);
        Assert.Null(result.ActiveClass);
        Assert.Empty(result.Classes);
    }

    [Theory]
    [InlineData("Health", "-1")]
    [InlineData("Health", "101")]
    [InlineData("MaxHealth", "0")]
    [InlineData("Mana", "101")]
    [InlineData("MaxMana", "0")]
    [InlineData("Stamina", "-1")]
    [InlineData("ActiveClass", "0")]
    [InlineData("ActiveClass", "6")]
    [InlineData("WarriorLevel", "0")]
    [InlineData("WarriorLevel", "11")]
    [InlineData("WarriorExperience", "200")]
    [InlineData("WarriorLevel", "10")]
    [InlineData("ScoutExperience", "1")]
    [InlineData("ClericExperience", "-1")]
    [InlineData("LearnedRankOne", "33554432")]
    [InlineData("LearnedRankTwo", "32772")]
    [InlineData("Health", "1.5")]
    [InlineData("Health", "\"77\"")]
    public void MalformedStatsNeverProduceAnApparentlyValidSnapshot(string property, string rawValue)
    {
        var node = JsonNode.Parse(GameplayPayload)!;
        node["gameplay"]!["value"]![property] = JsonNode.Parse(rawValue);
        var result = CharacterAdminGameplaySnapshot.Read(new(1, node.ToJsonString()));
        Assert.Equal("invalid", result.Status);
        Assert.Null(result.Vitals);
    }

    [Fact]
    public void MissingFieldsAndOversizedPayloadsDoNotCreateDefaultStats()
    {
        var node = JsonNode.Parse(GameplayPayload)!;
        node["gameplay"]!["value"]!.AsObject().Remove("ScoutExperience");
        Assert.Equal("invalid", CharacterAdminGameplaySnapshot.Read(new(1, node.ToJsonString())).Status);
        Assert.Equal("invalid", CharacterAdminGameplaySnapshot.Read(new(1, new string(' ', 32769))).Status);
        Assert.Equal("unsupported", CharacterAdminGameplaySnapshot.Read(new(2, GameplayPayload)).Status);
        Assert.Equal("not_saved", CharacterAdminGameplaySnapshot.Read(null).Status);
    }

    private static CharacterPossessionsSnapshot Possessions() => new(1,
        new(150, [new(149, "travel-rations", 3), new(0, "resource:wood", 27)]),
        new(80, [new(79, "old-copper-ring", 1)]),
        [new("main-hand", "valley-sword", 1), new("chest", "traveller-tunic", 1)])
    { HarvestTool = new("ore", 4, 1250, 200, 2, [new(2500, 5000), new(500, 250)]) };

    private static GameLeaseRecord Lease(Character character) => new(character.Id, character.AccountId,
        "beta-web", "island-01", 5, "private-ticket-hash", FixedTimeProvider.Now.AddSeconds(60),
        Guid.NewGuid(), 7, FixedTimeProvider.Now.AddSeconds(90), FixedTimeProvider.Now.AddHours(1),
        new(12, new(410, 32, 600), new(0, 1)) { IslandId = "island-copy-01", Simulation = new(1, GameplayPayload) },
        "private-session-reference", FixedTimeProvider.Now.AddHours(-1)) { Area = new("starter-island", "island-content") };

    private static CharactersFactory Factory(DetailRepository repository, GameLeaseRecord? lease = null) => new()
    {
        AdditionalConfiguration = new Dictionary<string, string?> { ["AdminAuthentication:Key"] = AdminKey },
        ConfigureTestServices = services =>
        {
            var characters = new MemoryCharacterRepository();
            characters.TryAddAsync(repository.Character, default).GetAwaiter().GetResult();
            var leases = new MemoryGameLeaseStore();
            if (lease is not null) leases.TryWriteAsync(lease, null, default).GetAwaiter().GetResult();
            services.RemoveAll<ICharacterRepository>();
            services.AddSingleton<ICharacterRepository>(characters);
            services.RemoveAll<ICharacterAdminRepository>();
            services.AddSingleton<ICharacterAdminRepository>(repository);
            services.RemoveAll<IGameLeaseStore>();
            services.AddSingleton<IGameLeaseStore>(leases);
        }
    };

    private sealed class DetailRepository : ICharacterAdminRepository
    {
        public Character Character { get; } = new(Guid.NewGuid(), IdentityStub.AliceId, "Aelys", "AELYS", 1,
            FixedTimeProvider.Now.AddDays(-1)) { RealmId = "beta-web" };
        public int FindCalls { get; private set; }
        public int QuestCalls { get; private set; }
        public DateTimeOffset? ObservedAt { get; private set; }
        public Task<AdminCharacterSummary?> FindAsync(Guid characterId, string? realmId, DateTimeOffset now, CancellationToken cancellationToken)
        {
            FindCalls++;
            ObservedAt = now;
            return Task.FromResult<AdminCharacterSummary?>(characterId == Character.Id && (realmId is null || realmId == Character.RealmId)
                ? new(Character.Id.ToString("D"), Character.AccountId.ToString("D"), Character.Name,
                    Character.RealmId!, "human", "a", Character.CreatedAt, false, null) : null);
        }
        public Task<AdminCharacterQuestPage> ListQuestsAsync(Guid characterId, int page, int pageSize, CancellationToken cancellationToken)
        {
            QuestCalls++;
            AdminCharacterQuest[] quests = [new("hunt-one", Guid.NewGuid(), 3, 1, false, 2),
                new("hunt-two", Guid.NewGuid(), 5, 5, true, 6), new("hunt-three", Guid.NewGuid(), 10, 0, false, 1)];
            return Task.FromResult(new AdminCharacterQuestPage(quests.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
                quests.Length, page, pageSize));
        }
        public Task<AdminCharacterPage> ListAsync(CharacterAdminQuery query, DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AdminCharacterOverview> OverviewAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
