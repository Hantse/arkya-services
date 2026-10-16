using Arkya.Characters.Api.Domain;

namespace Arkya.Characters.Api.Application;

public enum SaveCharacterQuestStatus { Saved, Invalid, CharacterNotFound, Conflict }
public sealed record SaveCharacterQuestResult(SaveCharacterQuestStatus Status, CharacterQuest? Quest = null);

public sealed class CharacterQuestService(ICharacterRepository characters, ICharacterQuestRepository quests)
{
    public async Task<IReadOnlyList<CharacterQuest>?> ListAsync(Guid characterId, CancellationToken cancellationToken)
    {
        if (!await characters.ExistsAsync(characterId, cancellationToken))
            return null;
        return await quests.ListAsync(characterId, cancellationToken);
    }

    public async Task<SaveCharacterQuestResult> SaveAsync(Guid characterId, string questId,
        string? catalogVersion, int requiredKills, int kills, long expectedRevision, CancellationToken cancellationToken)
    {
        if (!CharacterQuest.IsValidQuestId(questId) || !Guid.TryParseExact(catalogVersion, "D", out var version) ||
            version == Guid.Empty || requiredKills is < 1 or > 10000 || kills < 0 || kills > requiredKills ||
            expectedRevision is < 0 or long.MaxValue)
            return new(SaveCharacterQuestStatus.Invalid);

        if (!await characters.ExistsAsync(characterId, cancellationToken))
            return new(SaveCharacterQuestStatus.CharacterNotFound);

        var current = await quests.FindAsync(characterId, questId, cancellationToken);
        if (current is not null)
        {
            if (SameValues(current, version, requiredKills, kills) &&
                (current.Revision == expectedRevision || current.Revision == expectedRevision + 1))
                return new(SaveCharacterQuestStatus.Saved, current);
            if (current.Revision != expectedRevision || current.CatalogVersion != version ||
                current.RequiredKills != requiredKills || kills < current.Kills)
                return new(SaveCharacterQuestStatus.Conflict);
        }
        else if (expectedRevision != 0)
            return new(SaveCharacterQuestStatus.Conflict);

        var next = new CharacterQuest(characterId, questId, version, requiredKills, kills, expectedRevision + 1);
        if (await quests.TryWriteAsync(next, expectedRevision, cancellationToken))
            return new(SaveCharacterQuestStatus.Saved, next);

        // A lost response can cause a retry to race with the original successful write.
        current = await quests.FindAsync(characterId, questId, cancellationToken);
        return current is not null && current.Revision == next.Revision && SameValues(current, version, requiredKills, kills)
            ? new(SaveCharacterQuestStatus.Saved, current)
            : new(SaveCharacterQuestStatus.Conflict);
    }

    private static bool SameValues(CharacterQuest quest, Guid catalogVersion, int requiredKills, int kills) =>
        quest.CatalogVersion == catalogVersion && quest.RequiredKills == requiredKills && quest.Kills == kills;
}
