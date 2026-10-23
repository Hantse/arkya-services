using System.ComponentModel.DataAnnotations;
using System.Net;
using Arkya.Admin.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class ItemModel(ContentAdminClient content) : PageModel
{
    [BindProperty, BindRequired, Range(0, long.MaxValue - 1)] public long Revision { get; set; }
    [BindProperty] public ItemInput Input { get; set; } = new();
    public string? ItemId { get; private set; }
    public bool Loaded { get; private set; }
    public ItemAssetPage Assets { get; private set; } = new([], false);
    public ItemVisualAsset? SelectedAsset { get; private set; }
    public string? AssetError { get; private set; }
    public string? Error { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync(string? id, CancellationToken ct)
    {
        ItemId = id;
        if (!content.Enabled || id is not null && !ContentItemPresentation.IsItemId(id)) return NotFound();
        try
        {
            var draft = await content.GetItemDraftAsync(ct);
            Revision = draft.Revision;
            if (id is not null)
            {
                var item = draft.Items.SingleOrDefault(i => i.ItemId == id);
                if (item is null) return NotFound();
                Input = ItemInput.From(item);
            }
            Loaded = true;
            await LoadAssetsAsync(ct);
        }
        catch (Exception ex) when (ContentItemPresentation.IsDependencyError(ex, ct)) { Error = ContentItemPresentation.ErrorMessage(ex); }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(string? id, CancellationToken ct)
    {
        ItemId = id;
        if (!content.Enabled || id is not null && !ContentItemPresentation.IsItemId(id)) return NotFound();
        if (ModelState[nameof(Revision)]?.Errors.Count is > 0 || Revision is < 0 or long.MaxValue) return BadRequest();
        ValidateInput(id);
        if (ModelState.IsValid)
        {
            try
            {
                var draft = await content.GetItemDraftAsync(ct);
                if (draft.Revision != Revision) throw new ContentApiException(HttpStatusCode.Conflict, ContentItemPresentation.ErrorMessage(HttpStatusCode.Conflict));
                if (id is not null && !draft.Items.Any(i => i.ItemId == id)) return NotFound();
                if (id is null && draft.Items.Any(i => i.ItemId == Input.ItemId))
                    ModelState.AddModelError("Input.ItemId", "Cet identifiant est déjà utilisé par un autre objet.");
                else if (id is null && draft.Items.Length >= 256)
                    ModelState.AddModelError("", "Un catalogue contient au maximum 256 objets.");
                else
                {
                    var items = draft.Items.ToList();
                    var definition = Input.ToDefinition();
                    if (id is null) items.Add(definition);
                    else items[items.FindIndex(i => i.ItemId == id)] = definition;
                    await content.SaveItemDraftAsync(new(Revision, items.ToArray()), ct);
                    Notice = "Objet enregistré dans le brouillon. Publiez le catalogue pour préparer sa synchronisation Unity.";
                    return RedirectToPage(new { id = definition.ItemId });
                }
            }
            catch (Exception ex) when (ContentItemPresentation.IsDependencyError(ex, ct)) { Error = ContentItemPresentation.ErrorMessage(ex); }
        }
        Loaded = true;
        await LoadAssetsAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnGetAssetsAsync(string? search, int offset, CancellationToken ct)
    {
        if (!content.Enabled) return NotFound();
        if (!ModelState.IsValid || search is { Length: > 120 } || offset is < 0 or > 10000) return BadRequest();
        Response.Headers.CacheControl = "no-store, private";
        try { return new JsonResult(await content.GetItemAssetsAsync(search?.Trim(), offset, ct)); }
        catch (Exception ex) when (ContentItemPresentation.IsDependencyError(ex, ct))
        { return new JsonResult(new { error = ContentItemPresentation.ErrorMessage(ex) }) { StatusCode = 503 }; }
    }

    public async Task<IActionResult> OnGetThumbnailAsync(string guid, CancellationToken ct)
    {
        if (!content.Enabled || !ContentItemPresentation.IsAssetGuid(guid)) return NotFound();
        Response.Headers.CacheControl = "no-store, private";
        try { return File(await content.GetItemThumbnailAsync(guid, ct), "image/png"); }
        catch (ContentApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
        catch (Exception ex) when (ContentItemPresentation.IsDependencyError(ex, ct)) { return StatusCode(503); }
    }

    private void ValidateInput(string? id)
    {
        if (!ContentItemPresentation.IsItemId(Input.ItemId)) ModelState.AddModelError("Input.ItemId", "Utilisez des minuscules, chiffres et tirets simples, jusqu'à 64 caractères.");
        if (id is not null && Input.ItemId != id) ModelState.AddModelError("Input.ItemId", "L'identifiant d'un objet enregistré est permanent.");
        if (!ContentItemPresentation.Categories.ContainsKey(Input.Category ?? "")) ModelState.AddModelError("Input.Category", "Choisissez un type d'objet.");
        if (!ContentItemPresentation.Rarities.ContainsKey(Input.Rarity ?? "")) ModelState.AddModelError("Input.Rarity", "Choisissez une rareté.");
        var validSlot = Input.Category switch
        {
            "weapon" or "tool" => Input.EquipmentSlot is "main-hand" or "off-hand",
            "armor" => Input.EquipmentSlot is "head" or "chest" or "hands" or "legs" or "feet" or "neck" or "shoulders" or "back" or "waist" or "finger",
            _ => Input.EquipmentSlot == "none"
        };
        if (!validSlot) ModelState.AddModelError("Input.EquipmentSlot", "Choisissez un emplacement compatible avec ce type d'objet.");
        if (Input.Category is "weapon" or "armor" or "tool" && Input.MaxStack != 1) ModelState.AddModelError("Input.MaxStack", "Les armes, armures et outils ne s'empilent pas : indiquez 1.");
        if (!ContentItemPresentation.IsAssetGuid(Input.VisualAssetGuid)) ModelState.AddModelError("Input.VisualAssetGuid", "Choisissez un modèle dans la bibliothèque Unity.");
        if (Input.Name?.Any(char.IsControl) == true) ModelState.AddModelError("Input.Name", "Le nom doit tenir sur une seule ligne.");
        if (Input.Description?.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) == true)
            ModelState.AddModelError("Input.Description", "La description contient un caractère de contrôle non autorisé.");
    }

    private async Task LoadAssetsAsync(CancellationToken ct)
    {
        try
        {
            Assets = await content.GetItemAssetsAsync(null, 0, ct);
            if (ContentItemPresentation.IsAssetGuid(Input.VisualAssetGuid))
            {
                SelectedAsset = Assets.Assets.FirstOrDefault(a => a.Guid == Input.VisualAssetGuid);
                if (SelectedAsset is null)
                {
                    try { SelectedAsset = await content.GetItemAssetAsync(Input.VisualAssetGuid, ct); }
                    catch (ContentApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { AssetError = "Le modèle sélectionné n'est plus dans la bibliothèque. Choisissez un autre modèle."; }
                }
            }
        }
        catch (Exception ex) when (ContentItemPresentation.IsDependencyError(ex, ct)) { AssetError = ContentItemPresentation.ErrorMessage(ex); }
    }

    public sealed class ItemInput
    {
        [Required(ErrorMessage = "Renseignez un identifiant."), StringLength(64)] public string ItemId { get; set; } = "";
        [Required(ErrorMessage = "Renseignez un nom."), StringLength(120)] public string Name { get; set; } = "";
        [StringLength(2000)] public string? Description { get; set; } = "";
        [Required] public string Category { get; set; } = "weapon";
        [Required] public string Rarity { get; set; } = "common";
        [Required] public string EquipmentSlot { get; set; } = "main-hand";
        [Required(ErrorMessage = "Choisissez un modèle Unity.")] public string VisualAssetGuid { get; set; } = "";
        [Range(1, 9999)] public int MaxStack { get; set; } = 1;
        [Range(0, 1000000)] public int WeightGrams { get; set; }
        [Range(1, 1000)] public int RequiredLevel { get; set; } = 1;
        [Range(0, 1000000)] public int Attack { get; set; }
        [Range(0, 1000000)] public int Defense { get; set; }
        public ItemDefinition ToDefinition() => new(ItemId, Name.Trim(), Description?.Trim() ?? "", Category, Rarity,
            EquipmentSlot, VisualAssetGuid, MaxStack, WeightGrams, RequiredLevel, Attack, Defense);
        public static ItemInput From(ItemDefinition item) => new() { ItemId = item.ItemId, Name = item.Name,
            Description = item.Description, Category = item.Category, Rarity = item.Rarity, EquipmentSlot = item.EquipmentSlot,
            VisualAssetGuid = item.VisualAssetGuid, MaxStack = item.MaxStack, WeightGrams = item.WeightGrams,
            RequiredLevel = item.RequiredLevel, Attack = item.Attack, Defense = item.Defense };
    }
}
