using Arkya.Characters.Api.Domain;

namespace Arkya.Characters.Api.Application;

public enum CreateCharacterStatus { Created, InvalidName, NameTaken, InvalidAppearance }
public sealed record CreateCharacterResult(CreateCharacterStatus Status, Character? Character = null,
    Dictionary<string, string[]>? Errors = null);

public sealed class CharacterService(ICharacterRepository repository, TimeProvider timeProvider)
{
    public Task<IReadOnlyList<Character>> ListOwnedAsync(Guid accountId, CancellationToken cancellationToken) =>
        repository.ListOwnedAsync(accountId, cancellationToken);

    public Task<Character?> FindOwnedAsync(Guid characterId, Guid accountId, CancellationToken cancellationToken) =>
        repository.FindOwnedAsync(characterId, accountId, cancellationToken);

    public Task<CreateCharacterResult> CreateAsync(Guid accountId, string? name, CancellationToken cancellationToken) =>
        CreateAsync(accountId, name, null, cancellationToken);

    public async Task<CreateCharacterResult> CreateAsync(Guid accountId, string? name, CharacterAppearance? appearance,
        CancellationToken cancellationToken, string? realmId = null)
    {
        if (!CharacterName.TryCreate(name, out var characterName))
            return new(CreateCharacterStatus.InvalidName);

        appearance ??= CharacterAppearance.Default;
        var errors = appearance.Validate();
        if (errors.Count > 0)
            return new(CreateCharacterStatus.InvalidAppearance, Errors: errors);

        var character = new Character(Guid.NewGuid(), accountId, characterName!.Value,
            characterName.NormalizedValue, 1, timeProvider.GetUtcNow()) { Appearance = appearance, RealmId = realmId };
        return await repository.TryAddAsync(character, cancellationToken)
            ? new(CreateCharacterStatus.Created, character)
            : new(CreateCharacterStatus.NameTaken);
    }
}
