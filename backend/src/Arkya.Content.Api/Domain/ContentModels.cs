namespace Arkya.Content.Api.Domain;

public sealed record QuestDefinition(string QuestId, string Title, string Description, string EnemyId, int RequiredKills);

public sealed record QuestDraft(long Revision, IReadOnlyList<QuestDefinition> Quests);

public sealed record PublishedCatalog(int SchemaVersion, Guid Version, DateTimeOffset PublishedAt,
    IReadOnlyList<QuestDefinition> Quests);

public enum PublicationStatus { Published, Conflict }

public sealed record PublicationResult(PublicationStatus Status, PublishedCatalog? Catalog = null);
