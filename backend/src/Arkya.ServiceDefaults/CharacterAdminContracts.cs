namespace Arkya.ServiceDefaults.Characters;

public sealed record AdminCharacterSummary(string CharacterId, string AccountId, string DisplayName,
    string RealmId, string Race, string Gender, DateTimeOffset CreatedAt, bool IsOnline,
    DateTimeOffset? LeaseExpiresAt);

public sealed record AdminCharacterPage(AdminCharacterSummary[] Items, long Total, int Page, int PageSize);

public sealed record AdminCharacterOverview(long TotalCharacters, long OnlineCharacters, RealmOnlineCount[] Realms);

public sealed record RealmOnlineCount(string RealmId, long OnlineCharacters);

public sealed record AdminCharacterDetail(AdminCharacterSummary Character, int Level,
    AdminCharacterAppearance Appearance, DateTimeOffset ObservedAt, DateTimeOffset? LastConnectedAt,
    AdminCharacterWorldState? WorldState, AdminCharacterGameplay Gameplay, AdminCharacterQuestPage Quests,
    AdminCharacterSection Bags, AdminCharacterSection Bank, AdminCharacterSection Equipment);

public sealed record AdminCharacterAppearance(int Version, string Race, string Silhouette, int HeightCm,
    string Skin, string HairStyle, string HairColor, string EyeColor, string OutfitColor);

public sealed record AdminCharacterWorldState(long Revision, string ServerId, string? AreaId,
    string? ContentId, string? IslandId, float X, float Y, float Z, float ForwardX, float ForwardZ);

// Status distinguishes an absent snapshot from an unsupported or invalid persisted payload.
public sealed record AdminCharacterGameplay(string Status, AdminCharacterVitals? Vitals, string? ActiveClass,
    AdminCharacterClassProgress[] Classes, uint? LearnedRankOne, uint? LearnedRankTwo);

public sealed record AdminCharacterVitals(int Health, int MaxHealth, int Mana, int MaxMana, int Stamina, int MaxStamina);
public sealed record AdminCharacterClassProgress(string ClassId, int Level, int Experience);

public sealed record AdminCharacterQuestPage(AdminCharacterQuest[] Items, long Total, int Page, int PageSize);
public sealed record AdminCharacterQuest(string QuestId, Guid CatalogVersion, int RequiredKills,
    int Kills, bool Completed, long Revision);

public sealed record AdminCharacterSection(string Status)
{
    public int? SlotCapacity { get; init; }
    public AdminCharacterStoredItem[] Items { get; init; } = [];
    public AdminCharacterHarvestTool? HarvestTool { get; init; }
}
public sealed record AdminCharacterStoredItem(string Slot, string ItemId, int Quantity);
public sealed record AdminCharacterHarvestTool(string Family, int Level, int BaseYieldBonusBps,
    int YieldBonusPerLevelBps, int SocketCapacity, AdminCharacterSocketedStone[] Stones);
public sealed record AdminCharacterSocketedStone(int YieldBonusBps, int ResourceWeightReductionBps);
