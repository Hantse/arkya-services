using Arkya.Content.Api.Application;
using Arkya.Content.Api.Domain;

namespace Arkya.Content.Tests;

internal sealed class MemoryItemContentRepository : IItemContentRepository
{
    private readonly object gate = new();
    private ItemDraft draft = new(0, []);
    private readonly Dictionary<long, ItemCatalog> catalogs = [];
    private readonly Dictionary<string, ItemVisualAsset> assets = new(StringComparer.Ordinal);

    public Task<ItemDraft> GetDraftAsync(CancellationToken ct)
    {
        lock (gate) return Task.FromResult(draft with { Items = draft.Items.ToArray() });
    }
    public Task<ItemDraft?> TrySaveDraftAsync(long expectedRevision, IReadOnlyList<ItemDefinition> items, CancellationToken ct)
    {
        lock (gate)
        {
            if (expectedRevision < 0 || expectedRevision == long.MaxValue || expectedRevision != draft.Revision)
                return Task.FromResult<ItemDraft?>(null);
            draft = new(expectedRevision + 1, items.ToArray());
            return Task.FromResult<ItemDraft?>(draft);
        }
    }
    public Task<ItemCatalog?> FindPublishedRevisionAsync(long revision, CancellationToken ct)
    {
        lock (gate) return Task.FromResult(catalogs.GetValueOrDefault(revision));
    }
    public Task<ItemPublicationResult> PublishAsync(long expectedRevision, IReadOnlyList<PublishedItem> items,
        DateTimeOffset publishedAt, CancellationToken ct)
    {
        lock (gate)
        {
            if (catalogs.TryGetValue(expectedRevision, out var existing)) return Task.FromResult(new ItemPublicationResult(true, existing));
            if (draft.Revision != expectedRevision || draft.Items.Count == 0) return Task.FromResult(new ItemPublicationResult(false));
            var catalog = new ItemCatalog(1, Guid.NewGuid(), expectedRevision, publishedAt, items.ToArray());
            catalogs.Add(expectedRevision, catalog);
            return Task.FromResult(new ItemPublicationResult(true, catalog));
        }
    }
    public Task<ItemCatalog?> FindCatalogAsync(Guid version, CancellationToken ct)
    {
        lock (gate) return Task.FromResult(catalogs.Values.SingleOrDefault(catalog => catalog.Version == version));
    }
    public Task<IReadOnlyList<ItemCatalog>> ListCatalogsAsync(int limit, CancellationToken ct)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<ItemCatalog>>(catalogs.OrderByDescending(pair => pair.Key)
            .Take(limit).Select(pair => pair.Value).ToArray());
    }
    public Task<ItemVisualAsset?> FindAssetAsync(string guid, CancellationToken ct)
    {
        lock (gate) return Task.FromResult(assets.GetValueOrDefault(guid));
    }
    public Task<IReadOnlyList<ItemVisualAsset>> FindAssetsAsync(IEnumerable<string> guids, CancellationToken ct)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<ItemVisualAsset>>(guids.Where(assets.ContainsKey).Select(guid => assets[guid]).ToArray());
    }
    public Task<IReadOnlyList<ItemVisualAsset>> ListAssetsAsync(string? search, int limit, int offset, CancellationToken ct)
    {
        lock (gate) return Task.FromResult<IReadOnlyList<ItemVisualAsset>>(assets.Values.Where(asset => string.IsNullOrEmpty(search) ||
            asset.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || asset.AssetPath.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderBy(asset => asset.Name, StringComparer.Ordinal).ThenBy(asset => asset.Guid, StringComparer.Ordinal).Skip(offset).Take(limit).ToArray());
    }
    public Task UpsertAssetAsync(ItemVisualAsset asset, CancellationToken ct)
    {
        lock (gate) assets[asset.Guid] = asset;
        return Task.CompletedTask;
    }
    public Task<bool> DeleteAssetAsync(string guid, CancellationToken ct)
    {
        lock (gate) return Task.FromResult(assets.Remove(guid));
    }
}
