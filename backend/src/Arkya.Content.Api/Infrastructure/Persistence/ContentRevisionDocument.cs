using Arkya.Content.Api.Domain;
using MongoDB.Bson.Serialization.Attributes;

namespace Arkya.Content.Api.Infrastructure.Persistence;

internal sealed class ContentRevisionDocument
{
    [BsonId]
    public long Revision { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string CatalogVersion { get; set; } = Guid.NewGuid().ToString("D");
    public DateTime? PublishedAt { get; set; }
    public QuestDocument[] Quests { get; set; } = [];

    public QuestDraft ToDraft() => new(Revision, Quests.Select(quest => quest.ToDomain()).ToArray());
    public PublishedCatalog ToCatalog() => new(SchemaVersion, Guid.Parse(CatalogVersion),
        new DateTimeOffset(DateTime.SpecifyKind(PublishedAt!.Value, DateTimeKind.Utc)),
        Quests.Select(quest => quest.ToDomain()).ToArray());
}

internal sealed class QuestDocument
{
    public string QuestId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string EnemyId { get; set; } = "";
    public int RequiredKills { get; set; }

    public QuestDefinition ToDomain() => new(QuestId, Title, Description, EnemyId, RequiredKills);
    public static QuestDocument From(QuestDefinition quest) => new()
    {
        QuestId = quest.QuestId, Title = quest.Title, Description = quest.Description,
        EnemyId = quest.EnemyId, RequiredKills = quest.RequiredKills
    };
}
