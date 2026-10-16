using Arkya.Characters.Api.Domain;
using MongoDB.Bson.Serialization.Attributes;

namespace Arkya.Characters.Api.Infrastructure.Persistence;

public sealed class CharacterQuestDocument
{
    [BsonId]
    public string Id { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string CharacterId { get; set; } = "";
    public string QuestId { get; set; } = "";
    public string CatalogVersion { get; set; } = "";
    public int RequiredKills { get; set; }
    public int Kills { get; set; }
    public long Revision { get; set; }

    public CharacterQuest ToDomain() => new(Guid.Parse(CharacterId), QuestId, Guid.Parse(CatalogVersion),
        RequiredKills, Kills, Revision);

    public static CharacterQuestDocument From(CharacterQuest quest) => new()
    {
        Id = $"{quest.CharacterId:D}:{quest.QuestId}",
        CharacterId = quest.CharacterId.ToString("D"),
        QuestId = quest.QuestId,
        CatalogVersion = quest.CatalogVersion.ToString("D"),
        RequiredKills = quest.RequiredKills,
        Kills = quest.Kills,
        Revision = quest.Revision
    };
}
