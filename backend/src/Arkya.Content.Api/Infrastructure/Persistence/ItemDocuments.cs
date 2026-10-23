using Arkya.Content.Api.Domain;
using MongoDB.Bson.Serialization.Attributes;

namespace Arkya.Content.Api.Infrastructure.Persistence;

internal sealed class ItemRevisionDocument
{
    [BsonId] public long Revision { get; set; }
    public int SchemaVersion { get; set; } = 1;
    public string CatalogVersion { get; set; } = Guid.NewGuid().ToString("D");
    public DateTime? PublishedAt { get; set; }
    public ItemDocument[] Items { get; set; } = [];
    public PublishedItemDocument[] PublishedItems { get; set; } = [];
    public ItemDraft ToDraft() => new(Revision, Items.Select(item => item.ToDomain()).ToArray());
    public ItemCatalog ToCatalog() => new(SchemaVersion, Guid.Parse(CatalogVersion), Revision,
        new DateTimeOffset(DateTime.SpecifyKind(PublishedAt!.Value, DateTimeKind.Utc)),
        PublishedItems.Select(item => new PublishedItem(item.Definition.ToDomain(), item.VisualAssetPath, item.VisualDependencyHash)).ToArray());
}

internal sealed class ItemDocument
{
    public string ItemId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = "";
    public string Rarity { get; set; } = "";
    public string EquipmentSlot { get; set; } = "";
    public string VisualAssetGuid { get; set; } = "";
    public int MaxStack { get; set; }
    public int WeightGrams { get; set; }
    public int RequiredLevel { get; set; }
    public int Attack { get; set; }
    public int Defense { get; set; }

    public ItemDefinition ToDomain() => new(ItemId, Name, Description, Category, Rarity, EquipmentSlot, VisualAssetGuid,
        MaxStack, WeightGrams, RequiredLevel, Attack, Defense);
    public static ItemDocument From(ItemDefinition item) => new()
    {
        ItemId = item.ItemId, Name = item.Name, Description = item.Description, Category = item.Category,
        Rarity = item.Rarity, EquipmentSlot = item.EquipmentSlot, VisualAssetGuid = item.VisualAssetGuid,
        MaxStack = item.MaxStack, WeightGrams = item.WeightGrams, RequiredLevel = item.RequiredLevel,
        Attack = item.Attack, Defense = item.Defense
    };
}

internal sealed class PublishedItemDocument
{
    public ItemDocument Definition { get; set; } = new();
    public string VisualAssetPath { get; set; } = "";
    public string VisualDependencyHash { get; set; } = "";
    public static PublishedItemDocument From(PublishedItem item) => new()
    {
        Definition = ItemDocument.From(item.Definition), VisualAssetPath = item.VisualAssetPath,
        VisualDependencyHash = item.VisualDependencyHash
    };
}

internal sealed class ItemVisualAssetDocument
{
    [BsonId] public string Guid { get; set; } = "";
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string AssetPath { get; set; } = "";
    public string Kind { get; set; } = "";
    public string DependencyHash { get; set; } = "";
    public byte[]? ThumbnailPng { get; set; }
    public DateTime UpdatedAt { get; set; }
    public ItemVisualAsset ToDomain() => new(Guid, Name, AssetPath, Kind, DependencyHash, ThumbnailPng,
        new DateTimeOffset(DateTime.SpecifyKind(UpdatedAt, DateTimeKind.Utc)));
    public static ItemVisualAssetDocument From(ItemVisualAsset asset) => new()
    {
        Guid = asset.Guid, Name = asset.Name, AssetPath = asset.AssetPath, Kind = asset.Kind,
        DependencyHash = asset.DependencyHash, ThumbnailPng = asset.ThumbnailPng, UpdatedAt = asset.UpdatedAt.UtcDateTime
    };
}
