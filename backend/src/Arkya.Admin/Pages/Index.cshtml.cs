using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Arkya.Admin.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Arkya.Admin.Pages;

public sealed class IndexModel(ContentAdminClient content) : PageModel
{
    [BindProperty, BindRequired, Range(0, long.MaxValue)] public long Revision { get; set; }
    [BindProperty] public List<QuestInput> Quests { get; set; } = [];
    public ContentCatalog[] Catalogs { get; private set; } = [];
    public string? Error { get; private set; }
    public bool Loaded { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(true, ct);

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken ct)
    {
        if (Quests.Count > 128) ModelState.AddModelError("", "A catalogue can contain up to 128 quests.");
        if (Quests.Select(q => q.QuestId).Distinct(StringComparer.Ordinal).Count() != Quests.Count)
            ModelState.AddModelError("", "Each quest must have a unique ID.");
        if (!ModelState.IsValid)
        {
            await LoadAsync(false, ct);
            return Page();
        }
        try
        {
            await content.SaveAsync(new ContentDraft(Revision, Quests.Select(q => q.ToDefinition()).ToArray()), ct);
            Notice = "Draft saved. You can publish this revision.";
            return RedirectToPage();
        }
        catch (Exception ex) when (IsDependencyError(ex, ct)) { Error = Message(ex); }
        await LoadAsync(false, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostPublishAsync(CancellationToken ct)
    {
        // This form publishes the saved revision, independently of the editor's unsaved fields.
        if (ModelState[nameof(Revision)]?.Errors.Count > 0 || Revision < 0) return BadRequest();
        ModelState.Clear();
        try
        {
            var published = await content.PublishAsync(Revision, ct);
            Notice = $"Version {published.Version} published.";
            return RedirectToPage();
        }
        catch (Exception ex) when (IsDependencyError(ex, ct)) { Error = Message(ex); }
        await LoadAsync(true, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken ct)
    {
        ModelState.Clear();
        if (Quests.Count < 128) Quests.Add(new QuestInput());
        else Error = "A catalogue can contain up to 128 quests.";
        await LoadAsync(false, ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveAsync(int index, CancellationToken ct)
    {
        ModelState.Clear();
        if (index >= 0 && index < Quests.Count) Quests.RemoveAt(index);
        await LoadAsync(false, ct);
        return Page();
    }

    public async Task<IActionResult> OnGetDownloadAsync(string version, CancellationToken ct)
    {
        try
        {
            var catalogs = await content.GetCatalogsAsync(ct);
            var catalog = catalogs.SingleOrDefault(c => c.Version == version);
            return catalog is null ? NotFound() : File(JsonSerializer.SerializeToUtf8Bytes(catalog,
                new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }), "application/json", $"arkya-content-{catalog.Version}.json");
        }
        catch (Exception ex) when (IsDependencyError(ex, ct)) { return StatusCode(503); }
    }

    private async Task LoadAsync(bool replaceEditor, CancellationToken ct)
    {
        try
        {
            var draft = await content.GetDraftAsync(ct);
            Catalogs = (await content.GetCatalogsAsync(ct)).OrderByDescending(c => c.PublishedAt).ToArray();
            if (replaceEditor)
            {
                Revision = draft.Revision;
                Quests = draft.Quests.Select(QuestInput.From).ToList();
            }
            Loaded = true;
        }
        catch (Exception ex) when (IsDependencyError(ex, ct)) { Error ??= Message(ex); }
    }

    private static bool IsDependencyError(Exception ex, CancellationToken ct) => ex is ContentApiException or HttpRequestException
        || ex is TaskCanceledException && !ct.IsCancellationRequested;
    private static string Message(Exception ex) => ex is ContentApiException ? ex.Message : "The catalogue is unavailable. Try again shortly.";

    public sealed class QuestInput
    {
        [Required(ErrorMessage = "Enter an ID."), StringLength(64), RegularExpression("[a-z0-9]+(-[a-z0-9]+)*", ErrorMessage = "The ID uses lowercase letters, digits and single hyphens.")]
        public string QuestId { get; set; } = "";
        [Required(ErrorMessage = "Enter a title."), StringLength(120)] public string Title { get; set; } = "";
        [StringLength(2000)] public string Description { get; set; } = "";
        [Range(1, 10000, ErrorMessage = "The target must be from 1 to 10,000 enemies.")] public int RequiredKills { get; set; } = 3;
        public QuestDefinition ToDefinition() => new(QuestId, Title, Description ?? "", "prototype-monster", RequiredKills);
        public static QuestInput From(QuestDefinition quest) => new() { QuestId = quest.QuestId, Title = quest.Title,
            Description = quest.Description, RequiredKills = quest.RequiredKills };
    }
}
