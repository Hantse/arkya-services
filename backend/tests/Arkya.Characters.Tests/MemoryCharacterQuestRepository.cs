using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Domain;

namespace Arkya.Characters.Tests;

internal sealed class MemoryCharacterQuestRepository : ICharacterQuestRepository
{
    private readonly Dictionary<(Guid Character, string Quest), CharacterQuest> quests = [];
    private readonly object gate = new();

    public Task<IReadOnlyList<CharacterQuest>> ListAsync(Guid characterId, CancellationToken cancellationToken)
    {
        lock (gate)
            return Task.FromResult<IReadOnlyList<CharacterQuest>>(quests.Values.Where(quest => quest.CharacterId == characterId)
                .OrderBy(quest => quest.QuestId, StringComparer.Ordinal).ToArray());
    }

    public Task<CharacterQuest?> FindAsync(Guid characterId, string questId, CancellationToken cancellationToken)
    {
        lock (gate)
            return Task.FromResult(quests.GetValueOrDefault((characterId, questId)));
    }

    public Task<bool> TryWriteAsync(CharacterQuest quest, long expectedRevision, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            var identity = (quest.CharacterId, quest.QuestId);
            var current = quests.GetValueOrDefault(identity);
            if ((current?.Revision ?? 0) != expectedRevision)
                return Task.FromResult(false);
            quests[identity] = quest;
            return Task.FromResult(true);
        }
    }
}
