using Arkya.Content.Api.Application;
using Arkya.Content.Api.Contracts;
using Arkya.Content.Api.Domain;

namespace Arkya.Content.Tests;

public sealed class ItemValidationTests
{
    public static IEnumerable<object[]> InvalidItems()
    {
        var valid = ItemFixtures.Request();
        yield return [valid with { ItemId = "../Sword" }, "itemId"];
        yield return [valid with { ItemId = new string('a', 65) }, "itemId"];
        yield return [valid with { Name = "\t" }, "name"];
        yield return [valid with { Name = "Sword\0" }, "name"];
        yield return [valid with { Description = null }, "description"];
        yield return [valid with { Description = "bad\0description" }, "description"];
        yield return [valid with { Category = "player-stat-override" }, "category"];
        yield return [valid with { Rarity = "RARE" }, "rarity"];
        yield return [valid with { EquipmentSlot = "head" }, "equipmentSlot"];
        yield return [valid with { MaxStack = 2 }, "maxStack"];
        yield return [valid with { WeightGrams = -1 }, "weightGrams"];
        yield return [valid with { RequiredLevel = 0 }, "requiredLevel"];
        yield return [valid with { Attack = 1000001 }, "attack"];
        yield return [valid with { Defense = -1 }, "defense"];
        yield return [valid with { VisualAssetGuid = new string('0', 32) }, "visualAssetGuid"];
    }

    [Theory]
    [MemberData(nameof(InvalidItems))]
    public async Task InvalidItemCannotChangeDraft(ItemRequest item, string expectedField)
    {
        var repository = new MemoryItemContentRepository();
        var service = new ItemContentService(repository, new FixedTimeProvider());
        await repository.UpsertAssetAsync(ItemFixtures.Asset(), CancellationToken.None);
        var result = await service.SaveDraftAsync(new(0, [item]), CancellationToken.None);
        Assert.Equal(ItemChangeStatus.Invalid, result.Status);
        Assert.Contains($"items[0].{expectedField}", result.Errors!.Keys);
        Assert.Equal(0, (await repository.GetDraftAsync(CancellationToken.None)).Revision);
    }

    [Fact]
    public async Task DuplicateIdsAndOversizedCatalogsAreRejectedButEmptyDraftCanBeSaved()
    {
        var repository = new MemoryItemContentRepository();
        var service = new ItemContentService(repository, new FixedTimeProvider());
        await repository.UpsertAssetAsync(ItemFixtures.Asset(), CancellationToken.None);
        var duplicate = await service.SaveDraftAsync(new(0, [ItemFixtures.Request(), ItemFixtures.Request()]), CancellationToken.None);
        Assert.Contains("items[1].itemId", duplicate.Errors!.Keys);
        var oversized = await service.SaveDraftAsync(new(0, Enumerable.Range(0, 257).Select(index => ItemFixtures.Request($"item-{index}")).ToArray()), CancellationToken.None);
        Assert.Contains("items", oversized.Errors!.Keys);
        var empty = await service.SaveDraftAsync(new(0, []), CancellationToken.None);
        Assert.Equal(ItemChangeStatus.Saved, empty.Status);
        Assert.Empty(empty.Draft!.Items);
    }

    [Fact]
    public async Task ArmorMaterialAndToolDefinitionsSupportTheirOwnEquipmentAndStackRules()
    {
        var repository = new MemoryItemContentRepository();
        var service = new ItemContentService(repository, new FixedTimeProvider());
        await repository.UpsertAssetAsync(ItemFixtures.Asset(), CancellationToken.None);
        var result = await service.SaveDraftAsync(new(0, [
            ItemFixtures.Request("steel-helmet") with { Category = "armor", EquipmentSlot = "head", Description = "", Defense = 12 },
            ItemFixtures.Request("iron-ore") with { Category = "material", EquipmentSlot = "none", MaxStack = 9999 },
            ItemFixtures.Request("wooden-axe") with { Category = "tool", EquipmentSlot = "off-hand", Description = "First line\nSecond line\tvalue" }
        ]), CancellationToken.None);
        Assert.Equal(ItemChangeStatus.Saved, result.Status);
        Assert.Equal(3, result.Draft!.Items.Count);
    }

    [Theory]
    [InlineData("Assets/../Sword.prefab", "prefab")]
    [InlineData("Assets/Items//Sword.prefab", "prefab")]
    [InlineData("Assets\\Items\\Sword.prefab", "prefab")]
    [InlineData("Packages/Items/Sword.prefab", "prefab")]
    [InlineData("Assets/Items/Sword.fbx", "prefab")]
    [InlineData("Assets/Items/https:evil.obj", "obj")]
    public async Task InvalidUnityAssetPathsAreRejected(string path, string kind)
    {
        var service = new ItemContentService(new MemoryItemContentRepository(), new FixedTimeProvider());
        var result = await service.UpsertAssetAsync(ItemFixtures.Guid, ItemFixtures.AssetRequest(path) with { Kind = kind }, CancellationToken.None);
        Assert.Null(result.Asset); Assert.Contains("assetPath", result.Errors.Keys);
    }

    [Theory]
    [InlineData("Assets/Items/Sword.prefab", "prefab")]
    [InlineData("Assets/Art/Épée.FBX", "fbx")]
    [InlineData("Assets/Art/Sword.obj", "obj")]
    public async Task UnityPrefabAndImportedModelsCanBeRegistered(string path, string kind)
    {
        var service = new ItemContentService(new MemoryItemContentRepository(), new FixedTimeProvider());
        var result = await service.UpsertAssetAsync(ItemFixtures.Guid, ItemFixtures.AssetRequest(path) with { Kind = kind }, CancellationToken.None);
        Assert.NotNull(result.Asset); Assert.Empty(result.Errors);
    }

    public static IEnumerable<object[]> UnsafePngs()
    {
        yield return ["data:image/png;base64,AAAA"];
        yield return [Convert.ToBase64String(new byte[ItemThumbnail.MaximumBytes + 1])];
        yield return [Convert.ToBase64String(ItemFixtures.Png(width: 513))];
        yield return [Convert.ToBase64String(ItemFixtures.Png(height: 513))];
        yield return [Convert.ToBase64String(ItemFixtures.Png(badFilter: true))];
        yield return [Convert.ToBase64String(ItemFixtures.Png(extraPixels: true))];
        yield return [Convert.ToBase64String(ItemFixtures.Png()[..^1])];
        var badCrc = ItemFixtures.Png(); badCrc[29] ^= 1;
        yield return [Convert.ToBase64String(badCrc)];
    }

    [Theory]
    [MemberData(nameof(UnsafePngs))]
    public async Task InvalidOrOverBudgetThumbnailIsRejectedWithoutStoringAsset(string png)
    {
        var repository = new MemoryItemContentRepository();
        var service = new ItemContentService(repository, new FixedTimeProvider());
        var result = await service.UpsertAssetAsync(ItemFixtures.Guid, ItemFixtures.AssetRequest() with { ThumbnailPngBase64 = png }, CancellationToken.None);
        Assert.Null(result.Asset); Assert.Contains("thumbnailPngBase64", result.Errors.Keys);
        Assert.Null(await repository.FindAssetAsync(ItemFixtures.Guid, CancellationToken.None));
    }
}
