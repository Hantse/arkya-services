using Arkya.Characters.Api.Domain;

namespace Arkya.Characters.Api.Contracts;

public sealed record SaveCharacterQuestRequest(string? CatalogVersion, int RequiredKills, int Kills, long ExpectedRevision);

public sealed record CharacterQuestResponse(string QuestId, Guid CatalogVersion, int RequiredKills,
    int Kills, bool Completed, long Revision)
{
    public static CharacterQuestResponse From(CharacterQuest quest) =>
        new(quest.QuestId, quest.CatalogVersion, quest.RequiredKills, quest.Kills, quest.Completed, quest.Revision);
}

public sealed record CharacterQuestsResponse(Guid CharacterId, IReadOnlyList<CharacterQuestResponse> Quests);
