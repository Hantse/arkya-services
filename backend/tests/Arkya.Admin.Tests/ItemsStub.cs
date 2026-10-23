using System.Net;
using System.Text.Json;
using Arkya.Admin.Content;

namespace Arkya.Admin.Tests;

internal sealed partial class ContentStub
{
    public const string AssetGuid = "1234567890abcdef1234567890abcdef";
    public const string OtherAssetGuid = "abcdef1234567890abcdef1234567890";
    public static readonly ItemDefinition Sword = new("sword", "Épée d'Aubefont", "Forgée pour les gardes.", "weapon", "rare", "main-hand", AssetGuid, 1, 1200, 2, 8, 0);
    public ItemDraft ItemsDraft { get; set; } = new(3, [Sword]);
    public ItemCatalog[] ItemCatalogs { get; set; } = [new(1, Version, new(2026, 10, 23, 9, 0, 0, TimeSpan.Zero),
        [new(Sword.ItemId, Sword.Name, Sword.Description, Sword.Category, Sword.Rarity, Sword.EquipmentSlot, Sword.VisualAssetGuid, Sword.MaxStack,
            Sword.WeightGrams, Sword.RequiredLevel, Sword.Attack, Sword.Defense, "Assets/Arkya/Items/Sword.prefab", "1234567890abcdef1234567890abcdef")])];
    public ItemVisualAsset[] ItemAssets { get; set; } = [new(AssetGuid, "Épée d'Aubefont", "Assets/Arkya/Items/Sword.prefab", "prefab", AssetGuid, true, DateTimeOffset.UtcNow),
        new(OtherAssetGuid, "Bouclier", "Assets/Arkya/Items/Shield.fbx", "fbx", OtherAssetGuid, false, DateTimeOffset.UtcNow)];
    public List<string> ItemAssetQueries { get; } = [];
    public bool ItemAssetsHaveMore { get; set; }
    public byte[] Thumbnail { get; set; } = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jF0kAAAAASUVORK5CYII=");
    public string ThumbnailType { get; set; } = "image/png";

    private HttpResponseMessage HandleItems(ContentCall call, string query)
    {
        if (call.Path == "/admin/content/items/draft")
        {
            if (call.Method == HttpMethod.Get) return Json(ItemsDraft);
            if (call.Method == HttpMethod.Put)
            {
                var submitted = call.Body!.Value.Deserialize<ItemDraft>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
                if (submitted.Revision != ItemsDraft.Revision) return new(HttpStatusCode.Conflict);
                ItemsDraft = submitted with { Revision = submitted.Revision + 1 };
                return Json(ItemsDraft);
            }
        }
        if (call.Path == "/admin/content/items/catalogs")
        {
            if (call.Method == HttpMethod.Get) return Json(ItemCatalogs);
            if (call.Method == HttpMethod.Post)
            {
                if (call.Body!.Value.GetProperty("draftRevision").GetInt64() != ItemsDraft.Revision) return new(HttpStatusCode.Conflict);
                return Json(ItemCatalogs[0]);
            }
        }
        if (call.Path == "/admin/content/item-assets")
        {
            ItemAssetQueries.Add(query);
            return Json(new ItemAssetPage(ItemAssets, ItemAssetsHaveMore));
        }
        var asset = ItemAssets.SingleOrDefault(a => call.Path == "/admin/content/item-assets/" + a.Guid || call.Path == "/admin/content/item-assets/" + a.Guid + "/thumbnail");
        if (asset is not null)
        {
            if (!call.Path.EndsWith("/thumbnail", StringComparison.Ordinal)) return Json(asset);
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Thumbnail) };
            response.Content.Headers.ContentType = new(ThumbnailType);
            return response;
        }
        return new(HttpStatusCode.NotFound);
    }
}
