using Arkya.Content.Api.Domain;

namespace Arkya.Content.Api.Application;

public interface IContentRepository
{
    Task<QuestDraft> GetDraftAsync(CancellationToken cancellationToken);
    Task<QuestDraft?> TrySaveDraftAsync(long expectedRevision, IReadOnlyList<QuestDefinition> quests, CancellationToken cancellationToken);
    Task<PublicationResult> PublishAsync(long expectedRevision, DateTimeOffset publishedAt, CancellationToken cancellationToken);
    Task<PublishedCatalog?> FindCatalogAsync(Guid version, CancellationToken cancellationToken);
    Task<IReadOnlyList<PublishedCatalog>> ListCatalogsAsync(CancellationToken cancellationToken);
}
