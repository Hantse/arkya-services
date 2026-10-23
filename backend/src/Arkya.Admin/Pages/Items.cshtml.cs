using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Arkya.Admin.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class ItemsModel(ContentAdminClient content) : PageModel
{
    [BindProperty, BindRequired, Range(0, long.MaxValue - 1)] public long Revision { get; set; }
    public ItemDraft? Draft { get; private set; }
    public ItemCatalog[] Catalogs { get; private set; } = [];
    public bool Enabled => content.Enabled;
    public string? Error { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

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
        try
        {
            Draft = await content.GetItemDraftAsync(ct);
            Catalogs = (await content.GetItemCatalogsAsync(ct)).OrderByDescending(c => c.PublishedAt).ToArray();
            Revision = Draft.Revision;
        }
        catch (Exception ex) when (ContentItemPresentation.IsDependencyError(ex, ct)) { Error ??= ContentItemPresentation.ErrorMessage(ex); }
    }
}
