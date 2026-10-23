using Arkya.Content.Api.Domain;

namespace Arkya.Content.Api.Application;

public interface IItemContentRepository
{
    Task<ItemDraft> GetDraftAsync(CancellationToken ct);
    Task<ItemDraft?> TrySaveDraftAsync(long expectedRevision, IReadOnlyList<ItemDefinition> items, CancellationToken ct);
    Task<ItemCatalog?> FindPublishedRevisionAsync(long revision, CancellationToken ct);
    Task<ItemPublicationResult> PublishAsync(long expectedRevision, IReadOnlyList<PublishedItem> items,
        DateTimeOffset publishedAt, CancellationToken ct);
    Task<ItemCatalog?> FindCatalogAsync(Guid version, CancellationToken ct);
    Task<IReadOnlyList<ItemCatalog>> ListCatalogsAsync(int limit, CancellationToken ct);
    Task<ItemVisualAsset?> FindAssetAsync(string guid, CancellationToken ct);
    Task<IReadOnlyList<ItemVisualAsset>> FindAssetsAsync(IEnumerable<string> guids, CancellationToken ct);
    Task<IReadOnlyList<ItemVisualAsset>> ListAssetsAsync(string? search, int limit, int offset, CancellationToken ct);
    Task UpsertAssetAsync(ItemVisualAsset asset, CancellationToken ct);
    Task<bool> DeleteAssetAsync(string guid, CancellationToken ct);
}
