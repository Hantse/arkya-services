using Arkya.Characters.Api.Domain;

namespace Arkya.Characters.Api.Application;

public interface ICharacterRepository
{
    Task<bool> ExistsAsync(Guid characterId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Character>> ListOwnedAsync(Guid accountId, CancellationToken cancellationToken);
    Task<Character?> FindOwnedAsync(Guid characterId, Guid accountId, CancellationToken cancellationToken);
    Task<bool> TryAddAsync(Character character, CancellationToken cancellationToken);
}
