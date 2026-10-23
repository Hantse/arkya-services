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
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid? id, CancellationToken ct)
    {
        PostId = id;
        if (!content.Enabled || id == Guid.Empty) return NotFound();
        if (id is { } postId) return await LoadAsync(postId, true, ct);
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
                Notice = "Brouillon enregistré. Publiez-le pour le rendre visible aux joueurs.";
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
            Notice = publish ? "Publication visible aux joueurs." : "Publication retirée du fil public. Le brouillon est conservé.";
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
        if (!ContentPublicationPresentation.IsKind(Input.Kind)) ModelState.AddModelError("Input.Kind", "Choisissez une actualité ou des notes de version.");
        if (!ContentPublicationPresentation.IsLanguage(Input.Language)) ModelState.AddModelError("Input.Language", "Choisissez le français ou l'anglais.");
        if (Input.Kind == "release-note" && (string.IsNullOrWhiteSpace(Input.GameVersion) ||
            !Regex.IsMatch(Input.GameVersion.Trim(), @"\A[A-Za-z0-9][A-Za-z0-9._+\-]{0,63}\z", RegexOptions.CultureInvariant)))
            ModelState.AddModelError("Input.GameVersion", "Renseignez une version de 1 à 64 caractères : lettres, chiffres, points, tirets, underscores ou +.");
        if (Input.Title?.Any(char.IsControl) == true) ModelState.AddModelError("Input.Title", "Le titre doit tenir sur une seule ligne.");
        if (Input.Summary?.Any(char.IsControl) == true) ModelState.AddModelError("Input.Summary", "Le résumé doit tenir sur une seule ligne.");
        if (Input.Body?.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')) == true)
            ModelState.AddModelError("Input.Body", "Le texte contient un caractère de contrôle non autorisé.");
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
        [Required(ErrorMessage = "Renseignez un titre."), StringLength(160, ErrorMessage = "Le titre est limité à 160 caractères.")]
        public string Title { get; set; } = "";
        [StringLength(400, ErrorMessage = "Le résumé est limité à 400 caractères.")] public string? Summary { get; set; } = "";
        [Required(ErrorMessage = "Renseignez le texte de la publication."), StringLength(20000, ErrorMessage = "Le texte est limité à 20 000 caractères.")]
        public string Body { get; set; } = "";
        [StringLength(64, ErrorMessage = "La version est limitée à 64 caractères.")] public string? GameVersion { get; set; }
        public static PublicationInput From(ContentPostResponse post) => new()
        { Kind = post.Kind, Language = post.Language, Title = post.Title, Summary = post.Summary, Body = post.Body, GameVersion = post.GameVersion };
    }
}
