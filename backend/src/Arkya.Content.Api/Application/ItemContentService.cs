using System.Text.RegularExpressions;
using Arkya.Content.Api.Contracts;
using Arkya.Content.Api.Domain;

namespace Arkya.Content.Api.Application;

public enum ItemChangeStatus { Saved, Published, Invalid, Conflict }
public sealed record ItemChangeResult(ItemChangeStatus Status, ItemDraft? Draft = null, ItemCatalog? Catalog = null,
    Dictionary<string, string[]>? Errors = null);
public sealed record ItemAssetChangeResult(ItemVisualAsset? Asset, Dictionary<string, string[]> Errors);

public sealed partial class ItemContentService(IItemContentRepository repository, TimeProvider clock)
{
    public const int MaxItems = 256;
    public const int MaxPageSize = 50;
    public const int MaxOffset = 10000;
    public const int MaxDropSources = 32;
    public static readonly string[] Categories = ["weapon", "armor", "tool", "consumable", "material", "quest"];
    public static readonly string[] Rarities = ["common", "uncommon", "rare", "epic", "legendary"];
    private static readonly string[] ArmorSlots = ["head", "chest", "hands", "legs", "feet", "neck", "shoulders", "back", "waist", "finger"];

    public Task<ItemDraft> GetDraftAsync(CancellationToken ct) => repository.GetDraftAsync(ct);
    public Task<ItemCatalog?> FindCatalogAsync(Guid version, CancellationToken ct) => repository.FindCatalogAsync(version, ct);
    public Task<IReadOnlyList<ItemCatalog>> ListCatalogsAsync(CancellationToken ct) => repository.ListCatalogsAsync(50, ct);
    public async Task<ItemCatalog?> LatestCatalogAsync(CancellationToken ct) =>
        (await repository.ListCatalogsAsync(1, ct)).FirstOrDefault();
    public Task<ItemVisualAsset?> FindAssetAsync(string guid, CancellationToken ct) => repository.FindAssetAsync(guid, ct);
    public Task<IReadOnlyList<ItemVisualAsset>> ListAssetsAsync(string? search, int limit, int offset, CancellationToken ct) =>
        repository.ListAssetsAsync(search, limit + 1, offset, ct);
    public Task<bool> DeleteAssetAsync(string guid, CancellationToken ct) => repository.DeleteAssetAsync(guid, ct);

    public async Task<ItemChangeResult> SaveDraftAsync(SaveItemDraftRequest request, CancellationToken ct)
    {
        var errors = ValidateDraft(request);
        if (errors.Count > 0) return new(ItemChangeStatus.Invalid, Errors: errors);
        await ResolveAssetsAsync(request.Items!.Select(item => item!.VisualAssetGuid!), errors, ct);
        if (errors.Count > 0) return new(ItemChangeStatus.Invalid, Errors: errors);
        var items = request.Items!.Select(item => new ItemDefinition(item!.ItemId!, item.Name!.Trim(), item.Description!,
            item.Category!, item.Rarity!, item.EquipmentSlot!, item.VisualAssetGuid!, item.MaxStack, item.WeightGrams,
            item.RequiredLevel, item.Attack, item.Defense)
        {
            DropSources = (item.DropSources ?? []).Select(source =>
                new ItemDropSource(source!.MobId!, source.ChanceBasisPoints)).ToArray()
        }).ToArray();
        var draft = await repository.TrySaveDraftAsync(request.Revision!.Value, items, ct);
        return draft is null ? new(ItemChangeStatus.Conflict) : new(ItemChangeStatus.Saved, Draft: draft);
    }

    public async Task<ItemChangeResult> PublishAsync(PublishItemCatalogRequest request, CancellationToken ct)
    {
        if (request.DraftRevision is null or < 0 or long.MaxValue)
            return new(ItemChangeStatus.Invalid, Errors: new() { ["draftRevision"] = ["An explicit non-negative revision below Int64.MaxValue is required."] });
        // A retry keeps its immutable version even if an asset was removed from the selectable registry later.
        var existing = await repository.FindPublishedRevisionAsync(request.DraftRevision.Value, ct);
        if (existing is not null) return new(ItemChangeStatus.Published, Catalog: existing);
        var draft = await repository.GetDraftAsync(ct);
        if (draft.Revision != request.DraftRevision) return new(ItemChangeStatus.Conflict);
        if (draft.Items.Count == 0)
            return new(ItemChangeStatus.Invalid, Errors: new() { ["items"] = ["At least one item is required to publish a catalog."] });
        var errors = new Dictionary<string, string[]>();
        var assets = await ResolveAssetsAsync(draft.Items.Select(item => item.VisualAssetGuid), errors, ct);
        if (errors.Count > 0) return new(ItemChangeStatus.Invalid, Errors: errors);
        var items = draft.Items.Select(item => new PublishedItem(item, assets[item.VisualAssetGuid].AssetPath,
            assets[item.VisualAssetGuid].DependencyHash)).ToArray();
        var result = await repository.PublishAsync(draft.Revision, items, clock.GetUtcNow(), ct);
        return result.Published ? new(ItemChangeStatus.Published, Catalog: result.Catalog) : new(ItemChangeStatus.Conflict);
    }

    public async Task<ItemAssetChangeResult> UpsertAssetAsync(string guid, UpsertItemAssetRequest request, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (!IsUnityHash(guid)) errors["guid"] = ["A nonzero lowercase Unity GUID with 32 hexadecimal characters is required."];
        ValidateText(request.Name, 120, "name", false, errors);
        if (!ValidAssetPath(request.AssetPath, request.Kind))
            errors["assetPath"] = ["Use an Assets/ path without traversal and a .prefab, .fbx or .obj extension matching kind."];
        if (request.Kind is not ("prefab" or "fbx" or "obj")) errors["kind"] = ["Use prefab, fbx or obj."];
        if (!IsUnityHash(request.DependencyHash)) errors["dependencyHash"] = ["A nonzero lowercase Unity dependency hash with 32 hexadecimal characters is required."];
        byte[]? thumbnail = null;
        if (request.ThumbnailPngBase64 is not null && !ItemThumbnail.TryRead(request.ThumbnailPngBase64, out thumbnail))
            errors["thumbnailPngBase64"] = ["Use a non-interlaced RGB/RGBA 8-bit PNG up to 128 KiB and 512x512."];
        if (errors.Count > 0) return new(null, errors);
        var asset = new ItemVisualAsset(guid, request.Name!.Trim(), request.AssetPath!, request.Kind!,
            request.DependencyHash!, thumbnail, clock.GetUtcNow());
        await repository.UpsertAssetAsync(asset, ct);
        return new(asset, errors);
    }

    public static Dictionary<string, string[]> ValidateAssetQuery(string? search, int limit, int offset)
    {
        var errors = new Dictionary<string, string[]>();
        if (search is not null && (search.Length > 120 || search.Any(char.IsControl))) errors["search"] = ["Search must contain at most 120 characters without control characters."];
        if (limit is < 1 or > MaxPageSize) errors["limit"] = [$"Use a limit from 1 to {MaxPageSize}."];
        if (offset is < 0 or > MaxOffset) errors["offset"] = [$"Use an offset from 0 to {MaxOffset}."];
        return errors;
    }

    public static bool IsUnityHash(string? value) => value is { Length: 32 } &&
        value.Any(character => character != '0') && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private async Task<Dictionary<string, ItemVisualAsset>> ResolveAssetsAsync(IEnumerable<string> guids,
        Dictionary<string, string[]> errors, CancellationToken ct)
    {
        var requested = guids.ToArray();
        var assets = (await repository.FindAssetsAsync(requested.Distinct(StringComparer.Ordinal), ct))
            .ToDictionary(asset => asset.Guid, StringComparer.Ordinal);
        for (var index = 0; index < requested.Length; index++)
            if (!assets.ContainsKey(requested[index])) errors[$"items[{index}].visualAssetGuid"] = ["Choose an asset exported from the current Unity project."];
        return assets;
    }

    private static Dictionary<string, string[]> ValidateDraft(SaveItemDraftRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Revision is null or < 0 or long.MaxValue) errors["revision"] = ["An explicit non-negative revision below Int64.MaxValue is required."];
        if (request.Items is null || request.Items.Length > MaxItems)
        {
            errors["items"] = [$"Provide an array with at most {MaxItems} items."];
            return errors;
        }
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < request.Items.Length; index++)
        {
            var item = request.Items[index];
            var prefix = $"items[{index}]";
            if (item is null) { errors[prefix] = ["An item is required."]; continue; }
            if (item.ItemId is null || item.ItemId.Length is < 1 or > 64 || !ItemIdPattern().IsMatch(item.ItemId))
                errors[$"{prefix}.itemId"] = ["Use a stable lowercase ASCII slug of 1 to 64 characters."];
            else if (!ids.Add(item.ItemId)) errors[$"{prefix}.itemId"] = ["Item IDs must be unique within the catalog."];
            ValidateText(item.Name, 120, $"{prefix}.name", false, errors);
            ValidateText(item.Description, 2000, $"{prefix}.description", true, errors);
            if (!Categories.Contains(item.Category)) errors[$"{prefix}.category"] = ["Choose a supported item category."];
            if (!Rarities.Contains(item.Rarity)) errors[$"{prefix}.rarity"] = ["Choose a supported item rarity."];
            var validSlot = item.Category switch
            {
                "weapon" or "tool" => item.EquipmentSlot is "main-hand" or "off-hand",
                "armor" => ArmorSlots.Contains(item.EquipmentSlot),
                _ => item.EquipmentSlot == "none"
            };
            if (!validSlot) errors[$"{prefix}.equipmentSlot"] = ["Choose an equipment slot compatible with the category."];
            if (!IsUnityHash(item.VisualAssetGuid)) errors[$"{prefix}.visualAssetGuid"] = ["A registered lowercase Unity GUID is required."];
            if (item.MaxStack is < 1 or > 9999 || (item.Category is "weapon" or "armor" or "tool" && item.MaxStack != 1))
                errors[$"{prefix}.maxStack"] = ["Use 1 to 9999, or exactly 1 for weapons, armor and tools."];
            if (item.WeightGrams is < 0 or > 1000000) errors[$"{prefix}.weightGrams"] = ["Use 0 to 1000000 grams."];
            if (item.RequiredLevel is < 1 or > 1000) errors[$"{prefix}.requiredLevel"] = ["Use a level from 1 to 1000."];
            if (item.Attack is < 0 or > 1000000) errors[$"{prefix}.attack"] = ["Use attack from 0 to 1000000."];
            if (item.Defense is < 0 or > 1000000) errors[$"{prefix}.defense"] = ["Use defense from 0 to 1000000."];
            ValidateDropSources(item.DropSources, prefix, errors);
        }
        return errors;
    }

    private static void ValidateDropSources(ItemDropSourceRequest?[]? sources, string itemPrefix,
        Dictionary<string, string[]> errors)
    {
        if (sources is null) return;
        if (sources.Length > MaxDropSources)
        {
            errors[$"{itemPrefix}.dropSources"] = [$"Use at most {MaxDropSources} mob drop sources."];
            return;
        }
        var mobIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < sources.Length; index++)
        {
            var source = sources[index];
            var prefix = $"{itemPrefix}.dropSources[{index}]";
            if (source is null) { errors[prefix] = ["A mob drop source is required."]; continue; }
            if (!ItemMobCatalog.Contains(source.MobId))
                errors[$"{prefix}.mobId"] = ["Choose a mob from the supported mob catalog."];
            else if (!mobIds.Add(source.MobId!))
                errors[$"{prefix}.mobId"] = ["Each mob can appear only once per item."];
            if (source.ChanceBasisPoints is < 1 or > 10000)
                errors[$"{prefix}.chanceBasisPoints"] = ["Use 1 to 10000 basis points (0.01% to 100%)."];
        }
    }

    private static void ValidateText(string? value, int maximum, string name, bool multiline, Dictionary<string, string[]> errors)
    {
        if (value is null || (!multiline && string.IsNullOrWhiteSpace(value)) || value.Length > maximum ||
            value.Any(character => char.IsControl(character) && !(multiline && character is '\r' or '\n' or '\t')))
            errors[name] = [$"Provide {(multiline ? "0" : "1")} to {maximum} characters without unsupported control characters."];
    }

    private static bool ValidAssetPath(string? path, string? kind) => path is { Length: <= 512 } &&
        path.StartsWith("Assets/", StringComparison.Ordinal) && path.Split('/').All(segment => segment is not ("" or "." or "..") &&
            !segment.Any(character => char.IsControl(character) || character is '\\' or ':' or '?' or '*' or '"' or '<' or '>' or '|')) &&
        kind is "prefab" or "fbx" or "obj" && path.EndsWith("." + kind, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("\\A[a-z0-9]+(?:-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant, 100)]
    private static partial Regex ItemIdPattern();
}
