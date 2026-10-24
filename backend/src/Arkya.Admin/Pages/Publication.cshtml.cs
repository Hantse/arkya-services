using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.RegularExpressions;
using Arkya.Admin.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class PublicationModel(ContentAdminClient content) : PageModel
{
    [BindProperty, BindRequired, Range(0, long.MaxValue)] public long Revision { get; set; }
    [BindProperty] public PublicationInput Input { get; set; } = new();
    public ContentPostResponse? Publication { get; private set; }
    public Guid? PostId { get; private set; }
    public bool Loaded { get; private set; }
    public string? Error { get; private set; }
    public string ListKind => ContentPublicationPresentation.IsKind(Publication?.Kind) ? Publication!.Kind
        : ContentPublicationPresentation.IsKind(Input.Kind) ? Input.Kind : "news";
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? id, string? kind, string? language, CancellationToken ct)
    {
        PostId = id;
        if (!content.Enabled || id == Guid.Empty) return NotFound();
        if (id is { } postId) return await LoadAsync(postId, true, ct);
        if (!string.IsNullOrEmpty(kind) && !ContentPublicationPresentation.IsKind(kind) ||
            !string.IsNullOrEmpty(language) && !ContentPublicationPresentation.IsLanguage(language)) return BadRequest();
        Input.Kind = string.IsNullOrEmpty(kind) ? "news" : kind;
        Input.Language = string.IsNullOrEmpty(language) ? "fr" : language;
        Loaded = true;
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(Guid? id, CancellationToken ct)
    {
        PostId = id;
        if (!content.Enabled || id == Guid.Empty) return NotFound();
        if (!ValidRevision(id.HasValue)) return BadRequest();
        ValidateInput();
        if (ModelState.IsValid)
        {
            try
            {
                var version = Input.Kind == "release-note" ? Input.GameVersion?.Trim() : null;
                var saved = id is { } postId
                    ? await content.SavePostAsync(postId, new(Revision, Input.Kind, Input.Language, Input.Title.Trim(),
                        Input.Summary?.Trim() ?? "", Input.Body.Trim(), version), ct)
                    : await content.CreatePostAsync(new(Input.Kind, Input.Language, Input.Title.Trim(),
                        Input.Summary?.Trim() ?? "", Input.Body.Trim(), version), ct);
                Notice = "Draft saved. Publish it to make it visible to players.";
                return RedirectToPage(new { id = saved.Id });
            }
            catch (ContentApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
            catch (Exception ex) when (ContentPublicationPresentation.IsDependencyError(ex, ct))
            { Error = ContentPublicationPresentation.ErrorMessage(ex); }
        }
        Loaded = true;
        if (id is { } existingId) return await LoadAsync(existingId, false, ct);
        return Page();
    }

    public Task<IActionResult> OnPostPublishAsync(Guid id, CancellationToken ct) => ChangeVisibilityAsync(id, true, ct);
    public Task<IActionResult> OnPostUnpublishAsync(Guid id, CancellationToken ct) => ChangeVisibilityAsync(id, false, ct);

    private async Task<IActionResult> ChangeVisibilityAsync(Guid id, bool publish, CancellationToken ct)
    {
        PostId = id;
        if (!content.Enabled || id == Guid.Empty) return NotFound();
        if (!ValidRevision(true)) return BadRequest();
        // These independent forms operate only on the saved revision, never on unsaved editor fields.
        ModelState.Clear();
        try
        {
            if (publish) await content.PublishPostAsync(id, Revision, ct);
            else await content.UnpublishPostAsync(id, Revision, ct);
            Notice = publish ? "Publication is visible to players." : "Publication removed from the public feed. The draft is retained.";
            return RedirectToPage(new { id });
        }
        catch (ContentApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
        catch (Exception ex) when (ContentPublicationPresentation.IsDependencyError(ex, ct))
        { Error = ContentPublicationPresentation.ErrorMessage(ex); }
        return await LoadAsync(id, true, ct);
    }

    private bool ValidRevision(bool existing) =>
        ModelState[nameof(Revision)]?.Errors.Count is not > 0 && (existing ? Revision is > 0 and < long.MaxValue : Revision == 0);

    private void ValidateInput()
    {
        if (!ContentPublicationPresentation.IsKind(Input.Kind)) ModelState.AddModelError("Input.Kind", "Choose news or release notes.");
        if (!ContentPublicationPresentation.IsLanguage(Input.Language)) ModelState.AddModelError("Input.Language", "Choose French or English.");
        if (Input.Kind == "release-note" && (string.IsNullOrWhiteSpace(Input.GameVersion) ||
            !Regex.IsMatch(Input.GameVersion.Trim(), @"\A[A-Za-z0-9][A-Za-z0-9._+\-]{0,63}\z", RegexOptions.CultureInvariant)))
            ModelState.AddModelError("Input.GameVersion", "Enter a version of 1 to 64 characters: letters, digits, dots, hyphens, underscores or +.");
        if (Input.Title?.Any(char.IsControl) == true) ModelState.AddModelError("Input.Title", "The title must fit on a single line.");
        if (Input.Summary?.Any(char.IsControl) == true) ModelState.AddModelError("Input.Summary", "The summary must fit on a single line.");
        if (Input.Body?.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) == true)
            ModelState.AddModelError("Input.Body", "The text contains a prohibited control character.");
    }

    private async Task<IActionResult> LoadAsync(Guid id, bool replaceEditor, CancellationToken ct)
    {
        try
        {
            Publication = await content.GetPostAsync(id, ct);
            if (replaceEditor)
            {
                Revision = Publication.Revision;
                Input = PublicationInput.From(Publication);
            }
            Loaded = true;
        }
        catch (ContentApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
        catch (Exception ex) when (ContentPublicationPresentation.IsDependencyError(ex, ct))
        { Error ??= ContentPublicationPresentation.ErrorMessage(ex); }
        return Page();
    }

    public sealed class PublicationInput
    {
        [Required] public string Kind { get; set; } = "news";
        [Required] public string Language { get; set; } = "fr";
        [Required(ErrorMessage = "Enter a title."), StringLength(160, ErrorMessage = "The title is limited to 160 characters.")]
        public string Title { get; set; } = "";
        [StringLength(400, ErrorMessage = "The summary is limited to 400 characters.")] public string? Summary { get; set; } = "";
        [Required(ErrorMessage = "Enter the publication text."), StringLength(20000, ErrorMessage = "The text is limited to 20,000 characters.")]
        public string Body { get; set; } = "";
        [StringLength(64, ErrorMessage = "The version is limited to 64 characters.")] public string? GameVersion { get; set; }
        public static PublicationInput From(ContentPostResponse post) => new()
        { Kind = post.Kind, Language = post.Language, Title = post.Title, Summary = post.Summary, Body = post.Body, GameVersion = post.GameVersion };
    }
}
