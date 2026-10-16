using Arkya.Characters.Api.Domain;

namespace Arkya.Characters.Api.Application;

public interface ICharacterQuestRepository
{
    Task<IReadOnlyList<CharacterQuest>> ListAsync(Guid characterId, CancellationToken cancellationToken);
    Task<CharacterQuest?> FindAsync(Guid characterId, string questId, CancellationToken cancellationToken);
    Task<bool> TryWriteAsync(CharacterQuest quest, long expectedRevision, CancellationToken cancellationToken);
}
