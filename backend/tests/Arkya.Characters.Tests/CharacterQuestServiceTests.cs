using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Domain;

namespace Arkya.Characters.Tests;

public sealed class CharacterQuestServiceTests
{
    private static readonly Guid Version = Guid.Parse("95f1b50c-85ba-49a6-9b75-e12745472cf4");

    [Fact]
    public async Task AcceptanceProgressAndCompletionPreserveTheAcceptedDefinition()
    {
        var (service, characterId) = await CreateServiceAsync();
        var accepted = await Save(service, characterId, 0, 0);
        Assert.Equal(SaveCharacterQuestStatus.Saved, accepted.Status);
        Assert.Equal(1, accepted.Quest!.Revision);
        Assert.False(accepted.Quest.Completed);
        Assert.Equal(Version, accepted.Quest.CatalogVersion);

        var progressed = await Save(service, characterId, 2, 1);
        Assert.Equal(2, progressed.Quest!.Revision);
        var completed = await Save(service, characterId, 3, 2);
        Assert.True(completed.Quest!.Completed);
        Assert.Equal(completed.Quest, Assert.Single((await service.ListAsync(characterId, CancellationToken.None))!));
    }

    [Fact]
    public async Task LostResponseRetriesAndNoOpSavesDoNotIncrementRevision()
    {
        var (service, characterId) = await CreateServiceAsync();
        var accepted = await Save(service, characterId, 0, 0);
        Assert.Equal(accepted, await Save(service, characterId, 0, 0));
        Assert.Equal(accepted, await Save(service, characterId, 0, 1));

        var progressed = await Save(service, characterId, 1, 1);
        Assert.Equal(progressed, await Save(service, characterId, 1, 1));
        Assert.Equal(progressed, await Save(service, characterId, 1, 2));
        Assert.Equal(2, progressed.Quest!.Revision);
    }

    [Fact]
    public async Task MonotonicProgressCannotChangeTheVersionOrTargetAndCannotBeOverwrittenByAStaleServer()
    {
        var (service, characterId) = await CreateServiceAsync();
        await Save(service, characterId, 0, 0);
        await Save(service, characterId, 2, 1);

        Assert.Equal(SaveCharacterQuestStatus.Conflict, (await Save(service, characterId, 1, 2)).Status);
        Assert.Equal(SaveCharacterQuestStatus.Conflict, (await Save(service, characterId, 3, 1)).Status);
        Assert.Equal(SaveCharacterQuestStatus.Conflict, (await service.SaveAsync(characterId, "hunt-wolves",
            Guid.NewGuid().ToString("D"), 3, 3, 2, CancellationToken.None)).Status);
        Assert.Equal(SaveCharacterQuestStatus.Conflict, (await service.SaveAsync(characterId, "hunt-wolves",
            Version.ToString("D"), 4, 3, 2, CancellationToken.None)).Status);

        var stored = Assert.Single((await service.ListAsync(characterId, CancellationToken.None))!);
        Assert.Equal(2, stored.Kills);
        Assert.Equal(2, stored.Revision);
        Assert.Equal(3, stored.RequiredKills);
        Assert.Equal(Version, stored.CatalogVersion);
    }

    [Fact]
    public async Task ConcurrentDifferentUpdatesHaveOneWinner()
    {
        var (service, characterId) = await CreateServiceAsync();
        await Save(service, characterId, 0, 0);

        var updates = await Task.WhenAll(Save(service, characterId, 1, 1), Save(service, characterId, 2, 1));

        Assert.Single(updates, result => result.Status == SaveCharacterQuestStatus.Saved);
        Assert.Single(updates, result => result.Status == SaveCharacterQuestStatus.Conflict);
        Assert.Equal(2, Assert.Single((await service.ListAsync(characterId, CancellationToken.None))!).Revision);
    }

    [Fact]
    public async Task UnknownCharactersCannotAcquireProgressAndNonexistentQuestsRequireRevisionZero()
    {
        var (service, characterId) = await CreateServiceAsync();
        var unknown = Guid.NewGuid();

        Assert.Null(await service.ListAsync(unknown, CancellationToken.None));
        Assert.Equal(SaveCharacterQuestStatus.CharacterNotFound, (await Save(service, unknown, 0, 0)).Status);
        Assert.Equal(SaveCharacterQuestStatus.Conflict, (await Save(service, characterId, 0, 1)).Status);
        Assert.Empty((await service.ListAsync(characterId, CancellationToken.None))!);
    }

    [Theory]
    [InlineData("hunt--wolves", "95f1b50c-85ba-49a6-9b75-e12745472cf4", 3, 0, 0)]
    [InlineData("hunt-wolves-", "95f1b50c-85ba-49a6-9b75-e12745472cf4", 3, 0, 0)]
    [InlineData("Hunt-wolves", "95f1b50c-85ba-49a6-9b75-e12745472cf4", 3, 0, 0)]
    [InlineData("hunt-wolves", "not-a-version", 3, 0, 0)]
    [InlineData("hunt-wolves", "00000000-0000-0000-0000-000000000000", 3, 0, 0)]
    [InlineData("hunt-wolves", "95f1b50c85ba49a69b75e12745472cf4", 3, 0, 0)]
    [InlineData("hunt-wolves", "95f1b50c-85ba-49a6-9b75-e12745472cf4", 0, 0, 0)]
    [InlineData("hunt-wolves", "95f1b50c-85ba-49a6-9b75-e12745472cf4", 10001, 0, 0)]
    [InlineData("hunt-wolves", "95f1b50c-85ba-49a6-9b75-e12745472cf4", 3, -1, 0)]
    [InlineData("hunt-wolves", "95f1b50c-85ba-49a6-9b75-e12745472cf4", 3, 4, 0)]
    [InlineData("hunt-wolves", "95f1b50c-85ba-49a6-9b75-e12745472cf4", 3, 0, -1)]
    [InlineData("hunt-wolves", "95f1b50c-85ba-49a6-9b75-e12745472cf4", 3, 0, long.MaxValue)]
    public async Task InvalidInputsNeverWrite(string questId, string version, int requiredKills, int kills, long revision)
    {
        var (service, characterId) = await CreateServiceAsync();
        var result = await service.SaveAsync(characterId, questId, version, requiredKills, kills, revision, CancellationToken.None);
        Assert.Equal(SaveCharacterQuestStatus.Invalid, result.Status);
        Assert.Empty((await service.ListAsync(characterId, CancellationToken.None))!);
    }

    [Fact]
    public async Task ProgressIsSeparateForEveryCharacterAndQuest()
    {
        var characters = new MemoryCharacterRepository();
        var creation = new CharacterService(characters, new FixedTimeProvider());
        var alice = (await creation.CreateAsync(Guid.NewGuid(), "Aeria", CancellationToken.None)).Character!;
        var bob = (await creation.CreateAsync(Guid.NewGuid(), "Borea", CancellationToken.None)).Character!;
        var service = new CharacterQuestService(characters, new MemoryCharacterQuestRepository());
        await Save(service, alice.Id, 2, 0);
        await Save(service, bob.Id, 0, 0);
        await service.SaveAsync(alice.Id, "hunt-bandits", Version.ToString("D"), 3, 1, 0, CancellationToken.None);

        Assert.Equal(0, Assert.Single((await service.ListAsync(bob.Id, CancellationToken.None))!).Kills);
        var progress = (await service.ListAsync(alice.Id, CancellationToken.None))!;
        Assert.Equal(2, progress.Count);
        Assert.Equal(1, progress.Single(quest => quest.QuestId == "hunt-bandits").Kills);
        Assert.Equal(2, progress.Single(quest => quest.QuestId == "hunt-wolves").Kills);
    }

    private static Task<SaveCharacterQuestResult> Save(CharacterQuestService service, Guid characterId, int kills, long revision) =>
        service.SaveAsync(characterId, "hunt-wolves", Version.ToString("D"), 3, kills, revision, CancellationToken.None);

    private static async Task<(CharacterQuestService, Guid)> CreateServiceAsync()
    {
        var characters = new MemoryCharacterRepository();
        var created = await new CharacterService(characters, new FixedTimeProvider())
            .CreateAsync(Guid.NewGuid(), "Aeria", CancellationToken.None);
        return (new CharacterQuestService(characters, new MemoryCharacterQuestRepository()), created.Character!.Id);
    }
}
