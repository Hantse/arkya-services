using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json;
using Arkya.Admin.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class ItemsModel(ContentAdminClient content) : PageModel
{
    [BindProperty, BindRequired, Range(0, long.MaxValue - 1)] public long Revision { get; set; }
    [BindProperty(SupportsGet = true), StringLength(120)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Category { get; set; }
    [BindProperty(SupportsGet = true)] public string? Rarity { get; set; }
    [BindProperty(SupportsGet = true), StringLength(64)] public string? MobId { get; set; }
    [BindProperty(SupportsGet = true)] public string? Drops { get; set; }
    [BindProperty(SupportsGet = true), Range(1, 1000)] public int? MinLevel { get; set; }
    [BindProperty(SupportsGet = true), Range(1, 1000)] public int? MaxLevel { get; set; }
    [BindProperty(SupportsGet = true)] public string? Sort { get; set; } = "name";
    [BindProperty(SupportsGet = true), Range(1, int.MaxValue)] public int PageNumber { get; set; } = 1;
    public const int PageSize = 24;
    public ItemDraft? Draft { get; private set; }
    public ItemCatalog[] Catalogs { get; private set; } = [];
    public ItemMobDefinition[] Mobs { get; private set; } = [];
    public ItemDefinition[] VisibleItems { get; private set; } = [];
    public int MatchingCount { get; private set; }
    public int PageCount => Math.Max(1, (MatchingCount + PageSize - 1) / PageSize);
    public int FirstResult => MatchingCount == 0 ? 0 : (PageNumber - 1) * PageSize + 1;
    public int LastResult => Math.Min(PageNumber * PageSize, MatchingCount);
    public bool HasFilters => !string.IsNullOrEmpty(Search) || !string.IsNullOrEmpty(Category) || !string.IsNullOrEmpty(Rarity)
        || !string.IsNullOrEmpty(MobId) || !string.IsNullOrEmpty(Drops) || MinLevel.HasValue || MaxLevel.HasValue;
    public string? MobWarning { get; private set; }
    public bool Enabled => content.Enabled;
    public string? Error { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        Search = Search?.Trim();
        Sort = string.IsNullOrEmpty(Sort) ? "name" : Sort;
        if (ModelState.Any(entry => entry.Key != nameof(Revision) && entry.Value?.Errors.Count > 0)) return BadRequest();
        if ((MinLevel.HasValue && MaxLevel.HasValue && MinLevel > MaxLevel)
            || (!string.IsNullOrEmpty(Category) && !ContentItemPresentation.Categories.ContainsKey(Category))
            || (!string.IsNullOrEmpty(Rarity) && !ContentItemPresentation.Rarities.ContainsKey(Rarity))
            || Drops is not (null or "" or "linked" or "unlinked")
            || Sort is not ("name" or "name-desc" or "level" or "level-desc" or "rarity")) return BadRequest();
        await LoadAsync(ct);
        return Page();
    }

    public string MobName(string mobId) => Mobs.FirstOrDefault(m => m.MobId == mobId)?.Name ?? mobId;
    public static string DropChance(int basisPoints) => (basisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    public Dictionary<string, string> PageRoutes(int page) => new()
    {
        [nameof(Search)] = Search ?? "", [nameof(Category)] = Category ?? "", [nameof(Rarity)] = Rarity ?? "",
        [nameof(MobId)] = MobId ?? "", [nameof(Drops)] = Drops ?? "", [nameof(Sort)] = Sort ?? "name",
        [nameof(MinLevel)] = MinLevel?.ToString(CultureInfo.InvariantCulture) ?? "",
        [nameof(MaxLevel)] = MaxLevel?.ToString(CultureInfo.InvariantCulture) ?? "",
        [nameof(PageNumber)] = page.ToString(CultureInfo.InvariantCulture)
    };

    public async Task<IActionResult> OnPostPublishAsync(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!ModelState.IsValid) return BadRequest();
        try
        {
            var published = await content.PublishItemCatalogAsync(Revision, ct);
            Notice = $"Catalogue {published.Version} published. Synchronise this version in Unity before building the game.";
            return RedirectToPage();
        }
        catch (Exception ex) when (ContentItemPresentation.IsDependencyError(ex, ct)) { Error = ContentItemPresentation.ErrorMessage(ex); }
        ModelState.Clear();
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(string version, CancellationToken ct)
    {
        if (!Enabled || !Guid.TryParseExact(version, "D", out _)) return NotFound();
        try
        {
            var catalog = (await content.GetItemCatalogsAsync(ct)).SingleOrDefault(c => c.Version == version);
            return catalog is null ? NotFound() : File(JsonSerializer.SerializeToUtf8Bytes(catalog,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), "application/json", $"arkya-items-{catalog.Version}.json");
        }
        catch (Exception ex) when (ContentItemPresentation.IsDependencyError(ex, ct)) { return StatusCode(503); }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        Sort ??= "name";
        try
        {
            Draft = await content.GetItemDraftAsync(ct);
            Catalogs = (await content.GetItemCatalogsAsync(ct)).OrderByDescending(c => c.PublishedAt).ToArray();
            Revision = Draft.Revision;
            try { Mobs = (await content.GetItemMobsAsync(ct)).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToArray(); }
            catch (Exception ex) when (ContentItemPresentation.IsDependencyError(ex, ct))
            {
                MobWarning = "Mob names are temporarily unavailable. Saved mob IDs are shown.";
            }
            Mobs = Mobs.Concat(Draft.Items.SelectMany(i => i.DropSources ?? []).Select(d => d.MobId)
                .Distinct(StringComparer.Ordinal).Where(id => Mobs.All(m => m.MobId != id))
                .Select(id => new ItemMobDefinition(id, id))).ToArray();
            FilterItems();
        }
        catch (Exception ex) when (ContentItemPresentation.IsDependencyError(ex, ct)) { Error ??= ContentItemPresentation.ErrorMessage(ex); }
    }

    private void FilterItems()
    {
        IEnumerable<ItemDefinition> items = Draft?.Items ?? [];
        if (!string.IsNullOrWhiteSpace(Search)) items = items.Where(i => i.Name.Contains(Search, StringComparison.OrdinalIgnoreCase)
            || i.ItemId.Contains(Search, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(Category)) items = items.Where(i => i.Category == Category);
        if (!string.IsNullOrEmpty(Rarity)) items = items.Where(i => i.Rarity == Rarity);
        if (!string.IsNullOrEmpty(MobId)) items = items.Where(i => i.DropSources?.Any(d => d.MobId == MobId) == true);
        if (Drops == "linked") items = items.Where(i => i.DropSources is { Length: > 0 });
        if (Drops == "unlinked") items = items.Where(i => i.DropSources is not { Length: > 0 });
        if (MinLevel.HasValue) items = items.Where(i => i.RequiredLevel >= MinLevel.Value);
        if (MaxLevel.HasValue) items = items.Where(i => i.RequiredLevel <= MaxLevel.Value);
        IOrderedEnumerable<ItemDefinition> ordered = Sort switch
        {
            "name-desc" => items.OrderByDescending(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "level" => items.OrderBy(i => i.RequiredLevel).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "level-desc" => items.OrderByDescending(i => i.RequiredLevel).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            "rarity" => items.OrderByDescending(i => Array.IndexOf(["common", "uncommon", "rare", "epic", "legendary"], i.Rarity))
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase),
            _ => items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
        };
        var results = ordered.ThenBy(i => i.ItemId, StringComparer.Ordinal).ToArray();
        MatchingCount = results.Length;
        PageNumber = Math.Clamp(PageNumber, 1, PageCount);
        VisibleItems = results.Skip((PageNumber - 1) * PageSize).Take(PageSize).ToArray();
    }
}
