using Arkya.Content.Api.Domain;

namespace Arkya.Content.Api.Contracts;

public sealed record ItemRequest(string? ItemId, string? Name, string? Description, string? Category, string? Rarity,
    string? EquipmentSlot, string? VisualAssetGuid, int MaxStack, int WeightGrams, int RequiredLevel, int Attack, int Defense);
public sealed record SaveItemDraftRequest(long? Revision, ItemRequest?[]? Items);
public sealed record PublishItemCatalogRequest(long? DraftRevision);
public sealed record UpsertItemAssetRequest(string? Name, string? AssetPath, string? Kind, string? DependencyHash,
    string? ThumbnailPngBase64);

public sealed record ItemResponse(string ItemId, string Name, string Description, string Category, string Rarity,
    string EquipmentSlot, string VisualAssetGuid, int MaxStack, int WeightGrams, int RequiredLevel, int Attack, int Defense)
{
    public static ItemResponse From(ItemDefinition item) => new(item.ItemId, item.Name, item.Description, item.Category,
        item.Rarity, item.EquipmentSlot, item.VisualAssetGuid, item.MaxStack, item.WeightGrams, item.RequiredLevel,
        item.Attack, item.Defense);
}

public sealed record ItemDraftResponse(long Revision, IReadOnlyList<ItemResponse> Items)
{
    public static ItemDraftResponse From(ItemDraft draft) => new(draft.Revision, draft.Items.Select(ItemResponse.From).ToArray());
}

public sealed record PublishedItemResponse(string ItemId, string Name, string Description, string Category, string Rarity,
    string EquipmentSlot, string VisualAssetGuid, int MaxStack, int WeightGrams, int RequiredLevel, int Attack, int Defense,
    string VisualAssetPath, string VisualDependencyHash)
{
    public static PublishedItemResponse From(PublishedItem item)
    {
        var definition = item.Definition;
        return new(definition.ItemId, definition.Name, definition.Description, definition.Category, definition.Rarity,
            definition.EquipmentSlot, definition.VisualAssetGuid, definition.MaxStack, definition.WeightGrams,
            definition.RequiredLevel, definition.Attack, definition.Defense, item.VisualAssetPath, item.VisualDependencyHash);
    }
}

public sealed record ItemCatalogResponse(int SchemaVersion, Guid Version, DateTimeOffset PublishedAt,
    IReadOnlyList<PublishedItemResponse> Items)
{
    public static ItemCatalogResponse From(ItemCatalog catalog) => new(catalog.SchemaVersion, catalog.Version,
        catalog.PublishedAt, catalog.Items.Select(PublishedItemResponse.From).ToArray());
}

public sealed record ItemAssetResponse(string Guid, string Name, string AssetPath, string Kind, string DependencyHash,
    bool HasThumbnail, DateTimeOffset UpdatedAt)
{
    public static ItemAssetResponse From(ItemVisualAsset asset) => new(asset.Guid, asset.Name, asset.AssetPath,
        asset.Kind, asset.DependencyHash, asset.ThumbnailPng is not null, asset.UpdatedAt);
}

public sealed record ItemAssetPage(IReadOnlyList<ItemAssetResponse> Assets, bool HasMore);
