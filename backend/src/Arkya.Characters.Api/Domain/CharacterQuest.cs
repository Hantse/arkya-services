using System.Text.RegularExpressions;

namespace Arkya.Characters.Api.Domain;

public sealed record CharacterQuest(Guid CharacterId, string QuestId, Guid CatalogVersion,
    int RequiredKills, int Kills, long Revision)
{
    public bool Completed => Kills == RequiredKills;

    public static bool IsValidQuestId(string? questId) => questId is { Length: > 0 and <= 64 } &&
        Regex.IsMatch(questId, "\\A[a-z0-9]+(-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant);
}
