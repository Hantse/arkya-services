using Arkya.Content.Api.Domain;

namespace Arkya.Content.Api.Contracts;

public sealed record QuestRequest(string? QuestId, string? Title, string? Description, string? EnemyId, int RequiredKills);
public sealed record SaveDraftRequest(long? Revision, QuestRequest?[]? Quests);
public sealed record PublishCatalogRequest(long? DraftRevision);

public sealed record QuestResponse(string QuestId, string Title, string Description, string EnemyId, int RequiredKills)
{
    public static QuestResponse From(QuestDefinition quest) =>
        new(quest.QuestId, quest.Title, quest.Description, quest.EnemyId, quest.RequiredKills);
}

public sealed record DraftResponse(long Revision, IReadOnlyList<QuestResponse> Quests)
{
    public static DraftResponse From(QuestDraft draft) => new(draft.Revision, draft.Quests.Select(QuestResponse.From).ToArray());
}

public sealed record CatalogResponse(int SchemaVersion, Guid Version, DateTimeOffset PublishedAt,
    IReadOnlyList<QuestResponse> Quests)
{
    public static CatalogResponse From(PublishedCatalog catalog) =>
        new(catalog.SchemaVersion, catalog.Version, catalog.PublishedAt, catalog.Quests.Select(QuestResponse.From).ToArray());
}
