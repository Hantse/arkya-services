using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Arkya.Admin.Content;

public sealed record ItemDefinition(string ItemId, string Name, string Description, string Category, string Rarity,
    string EquipmentSlot, string VisualAssetGuid, int MaxStack, int WeightGrams, int RequiredLevel, int Attack, int Defense);
public sealed record ItemDraft(long Revision, ItemDefinition[] Items);
public sealed record PublishedItemDefinition(string ItemId, string Name, string Description, string Category, string Rarity,
    string EquipmentSlot, string VisualAssetGuid, int MaxStack, int WeightGrams, int RequiredLevel, int Attack, int Defense,
    string VisualAssetPath, string VisualDependencyHash);
public sealed record ItemCatalog(int SchemaVersion, string Version, DateTimeOffset PublishedAt, PublishedItemDefinition[] Items);
public sealed record ItemVisualAsset(string Guid, string Name, string AssetPath, string Kind, string DependencyHash,
    bool HasThumbnail, DateTimeOffset UpdatedAt);
public sealed record ItemAssetPage(ItemVisualAsset[] Assets, bool HasMore);

public sealed partial class ContentAdminClient
{
    public Task<ItemDraft> GetItemDraftAsync(CancellationToken ct) => SendItemAsync<ItemDraft>(HttpMethod.Get, "admin/content/items/draft", null, ct);
    public Task<ItemDraft> SaveItemDraftAsync(ItemDraft draft, CancellationToken ct) => SendItemAsync<ItemDraft>(HttpMethod.Put, "admin/content/items/draft", draft, ct);
    public Task<ItemCatalog[]> GetItemCatalogsAsync(CancellationToken ct) => SendItemAsync<ItemCatalog[]>(HttpMethod.Get, "admin/content/items/catalogs", null, ct);
    public Task<ItemCatalog> PublishItemCatalogAsync(long revision, CancellationToken ct) => SendItemAsync<ItemCatalog>(HttpMethod.Post,
        "admin/content/items/catalogs", new { draftRevision = revision }, ct);
    public Task<ItemAssetPage> GetItemAssetsAsync(string? search, int offset, CancellationToken ct) => SendItemAsync<ItemAssetPage>(HttpMethod.Get,
        $"admin/content/item-assets?limit=24&offset={offset}" + (string.IsNullOrEmpty(search) ? "" : "&search=" + Uri.EscapeDataString(search)), null, ct);
    public Task<ItemVisualAsset> GetItemAssetAsync(string guid, CancellationToken ct) => SendItemAsync<ItemVisualAsset>(HttpMethod.Get,
        "admin/content/item-assets/" + Uri.EscapeDataString(guid), null, ct);

    public async Task<byte[]> GetItemThumbnailAsync(string guid, CancellationToken ct)
    {
        if (!Enabled) throw new ContentApiException(HttpStatusCode.ServiceUnavailable, ContentItemPresentation.ErrorMessage(HttpStatusCode.ServiceUnavailable));
        using var request = new HttpRequestMessage(HttpMethod.Get, "admin/content/item-assets/" + Uri.EscapeDataString(guid) + "/thumbnail");
        request.Headers.Add("X-Arkya-Admin-Key", options.Value.AdminKey);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new ContentApiException(response.StatusCode, ContentItemPresentation.ErrorMessage(response.StatusCode));
        if (response.Content.Headers.ContentType?.MediaType != "image/png" || response.Content.Headers.ContentLength is > 131072)
            throw new ContentApiException(HttpStatusCode.BadGateway, "L'aperçu de cet objet est indisponible.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (bytes.Length + count > 131072) throw new ContentApiException(HttpStatusCode.BadGateway, "L'aperçu de cet objet est indisponible.");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }

    private async Task<T> SendItemAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!Enabled) throw new ContentApiException(HttpStatusCode.ServiceUnavailable, "La gestion des objets est désactivée sur cette installation.");
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Arkya-Admin-Key", options.Value.AdminKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new ContentApiException(response.StatusCode, ContentItemPresentation.ErrorMessage(response.StatusCode));
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw new JsonException();
        }
        catch (JsonException) { throw new ContentApiException(HttpStatusCode.BadGateway, "Le catalogue d'objets a renvoyé une réponse invalide."); }
    }
}

public static class ContentItemPresentation
{
    public static readonly IReadOnlyDictionary<string, string> Categories = new Dictionary<string, string>
    { ["weapon"] = "Arme", ["armor"] = "Armure", ["tool"] = "Outil", ["consumable"] = "Consommable", ["material"] = "Matériau", ["quest"] = "Objet de quête" };
    public static readonly IReadOnlyDictionary<string, string> Rarities = new Dictionary<string, string>
    { ["common"] = "Commun", ["uncommon"] = "Peu commun", ["rare"] = "Rare", ["epic"] = "Épique", ["legendary"] = "Légendaire" };
    public static readonly IReadOnlyDictionary<string, string> Slots = new Dictionary<string, string>
    { ["none"] = "Aucun", ["main-hand"] = "Main principale", ["off-hand"] = "Main secondaire", ["head"] = "Tête", ["chest"] = "Torse", ["hands"] = "Mains", ["legs"] = "Jambes", ["feet"] = "Pieds", ["neck"] = "Cou", ["shoulders"] = "Épaules", ["back"] = "Dos", ["waist"] = "Taille", ["finger"] = "Doigt" };
    public static bool IsItemId(string? id) => id is { Length: > 0 and <= 64 } && Regex.IsMatch(id, "\\A[a-z0-9]+(-[a-z0-9]+)*\\z", RegexOptions.CultureInvariant);
    public static bool IsAssetGuid(string? guid) => guid is { Length: 32 } && Regex.IsMatch(guid, "\\A[0-9a-f]{32}\\z", RegexOptions.CultureInvariant);
    public static string CategoryLabel(string category) => Categories.GetValueOrDefault(category, category);
    public static string RarityLabel(string rarity) => Rarities.GetValueOrDefault(rarity, rarity);
    public static bool IsDependencyError(Exception ex, CancellationToken ct) => ContentPublicationPresentation.IsDependencyError(ex, ct);
    public static string ErrorMessage(Exception ex) => ex is ContentApiException ? ex.Message : ErrorMessage(HttpStatusCode.ServiceUnavailable);
    public static string ErrorMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Conflict => "Le catalogue a changé. Copiez vos modifications, puis rechargez le brouillon avant de réessayer.",
        HttpStatusCode.BadRequest => "Vérifiez les champs, l'emplacement d'équipement et le modèle Unity sélectionné. Les armes, armures et outils ne s'empilent pas.",
        HttpStatusCode.NotFound => "Cet objet ou ce modèle Unity n'existe plus. Actualisez le catalogue.",
        HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "L'accès aux objets a été refusé. Vérifiez la configuration de l'administration.",
        _ => "Le catalogue d'objets est indisponible. Réessayez dans un instant."
    };
}
