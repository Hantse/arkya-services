using Arkya.ServiceDefaults.Characters;

namespace Arkya.Admin.Accounts;

internal static class CharacterDetailValidation
{
    private static readonly string[] ClassIds = ["warrior", "scout", "mage", "cleric", "druid"];

    public static bool IsValid(AdminCharacterDetail detail) =>
        detail.Character is { } character && Guid.TryParse(character.CharacterId, out var id) && id != Guid.Empty &&
        Guid.TryParse(character.AccountId, out var owner) && owner != Guid.Empty && character.DisplayName is not null &&
        character.RealmId is not null && character.Race is not null && character.Gender is not null && detail.Level >= 1 &&
        detail.Appearance is { Version: 1, Race: not null, Silhouette: not null, Skin: not null, HairStyle: not null,
            HairColor: not null, EyeColor: not null, OutfitColor: not null, HeightCm: > 0 } &&
        (detail.WorldState is null || detail.WorldState is { Revision: >= 0, ServerId: not null } world &&
            float.IsFinite(world.X) && float.IsFinite(world.Y) && float.IsFinite(world.Z) &&
            float.IsFinite(world.ForwardX) && float.IsFinite(world.ForwardZ)) &&
        ValidGameplay(detail.Gameplay) &&
        detail.Quests is { Items: not null, Total: >= 0, Page: >= 1 and <= 100000, PageSize: >= 1 and <= 100 } quests &&
        quests.Items.Length <= quests.PageSize && quests.Items.Length <= quests.Total &&
        quests.Items.All(quest => quest is { QuestId: not null, RequiredKills: >= 1 and <= 10000, Kills: >= 0, Revision: >= 0 } &&
            quest.CatalogVersion != Guid.Empty && quest.Kills <= quest.RequiredKills && quest.Completed == (quest.Kills == quest.RequiredKills)) &&
        detail.Bags is { Status: "not_persisted" } && detail.Bank is { Status: "not_persisted" } &&
        detail.Equipment is { Status: "not_persisted" };

    private static bool ValidGameplay(AdminCharacterGameplay? gameplay)
    {
        if (gameplay?.Classes is null) return false;
        if (gameplay.Status is "not_saved" or "unsupported" or "invalid")
            return gameplay.Vitals is null && gameplay.ActiveClass is null && gameplay.Classes.Length == 0 &&
                gameplay.LearnedRankOne is null && gameplay.LearnedRankTwo is null;
        return gameplay.Status == "available" && gameplay.Vitals is { Health: >= 0, MaxHealth: > 0, Mana: >= 0,
            MaxMana: > 0, Stamina: >= 0, MaxStamina: > 0 } vitals &&
            vitals.Health <= vitals.MaxHealth && vitals.Mana <= vitals.MaxMana && vitals.Stamina <= vitals.MaxStamina &&
            gameplay.Classes.Length == ClassIds.Length &&
            gameplay.Classes.Select(progress => progress?.ClassId).Order().SequenceEqual(ClassIds.Order()) &&
            gameplay.Classes.All(progress => progress is { Level: >= 0 and <= 10, Experience: >= 0 } &&
                (progress.Level is 0 or 10 ? progress.Experience == 0 : progress.Experience < progress.Level * 100)) &&
            gameplay.Classes.Any(progress => progress.ClassId == gameplay.ActiveClass && progress.Level > 0) &&
            gameplay.LearnedRankOne is { } rankOne && gameplay.LearnedRankTwo is { } rankTwo &&
            ((rankOne | rankTwo) & ~((1u << 25) - 1)) == 0 && (rankTwo & ~rankOne) == 0;
    }
}
