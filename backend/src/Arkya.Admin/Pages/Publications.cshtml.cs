using System.ComponentModel.DataAnnotations;
using Arkya.Admin.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class PublicationsModel(ContentAdminClient content) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Kind { get; set; }
    [BindProperty(SupportsGet = true)] public string? Language { get; set; }
    [BindProperty(SupportsGet = true), Range(0, 10000)] public int Offset { get; set; }
    [BindProperty(SupportsGet = true), Range(1, 50)] public int Limit { get; set; } = 20;
    public ContentPostList? Publications { get; private set; }
    public bool Enabled => content.Enabled;
    public string? Error { get; private set; }
    public bool HasPrevious => Offset > 0;
    public bool HasNext => Publications?.HasMore == true && Offset + Limit <= 10000;

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(Kind) && !ContentPublicationPresentation.IsKind(Kind))
            ModelState.AddModelError(nameof(Kind), "Choose an available publication type.");
        if (!string.IsNullOrEmpty(Language) && !ContentPublicationPresentation.IsLanguage(Language))
            ModelState.AddModelError(nameof(Language), "Choose French or English.");
        if (!Enabled || !ModelState.IsValid) return;
        try { Publications = await content.GetPostsAsync(Kind, Language, Offset, Limit, ct); }
        catch (Exception ex) when (ContentPublicationPresentation.IsDependencyError(ex, ct))
        { Error = ContentPublicationPresentation.ErrorMessage(ex); }
    }
}
