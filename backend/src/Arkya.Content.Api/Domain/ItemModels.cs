namespace Arkya.Content.Api.Domain;

public sealed record ItemDefinition(string ItemId, string Name, string Description, string Category, string Rarity,
    string EquipmentSlot, string VisualAssetGuid, int MaxStack, int WeightGrams, int RequiredLevel, int Attack, int Defense);

public sealed record ItemDraft(long Revision, IReadOnlyList<ItemDefinition> Items);
public sealed record PublishedItem(ItemDefinition Definition, string VisualAssetPath, string VisualDependencyHash);
public sealed record ItemCatalog(int SchemaVersion, Guid Version, long DraftRevision, DateTimeOffset PublishedAt,
    IReadOnlyList<PublishedItem> Items);
public sealed record ItemPublicationResult(bool Published, ItemCatalog? Catalog = null);

public sealed record ItemVisualAsset(string Guid, string Name, string AssetPath, string Kind, string DependencyHash,
    byte[]? ThumbnailPng, DateTimeOffset UpdatedAt);
