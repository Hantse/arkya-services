using Arkya.Characters.Api.Application;

namespace Arkya.Characters.Tests;

public sealed class CharacterServiceTests
{
    [Fact]
    public async Task CreatesIdentityOwnershipAndInitialLevelOnTheServer()
    {
        var repository = new MemoryCharacterRepository();
        var service = new CharacterService(repository, new FixedTimeProvider());
        var accountId = Guid.NewGuid();

        var result = await service.CreateAsync(accountId, " Alice ", TestContext.CancellationToken);

        Assert.Equal(CreateCharacterStatus.Created, result.Status);
        Assert.NotEqual(Guid.Empty, result.Character!.Id);
        Assert.Equal(accountId, result.Character.AccountId);
        Assert.Equal(1, result.Character.Level);
        Assert.Equal(FixedTimeProvider.Now, result.Character.CreatedAt);
        Assert.Equal("Alice", result.Character.Name);
        Assert.Equal("ALICE", result.Character.NormalizedName);
    }

    [Fact]
    public async Task DuplicateNameAcrossAccountsReturnsNameTaken()
    {
        var service = new CharacterService(new MemoryCharacterRepository(), new FixedTimeProvider());
        await service.CreateAsync(Guid.NewGuid(), "Alice", TestContext.CancellationToken);

        var result = await service.CreateAsync(Guid.NewGuid(), " alice ", TestContext.CancellationToken);

        Assert.Equal(CreateCharacterStatus.NameTaken, result.Status);
        Assert.Null(result.Character);
    }
}
