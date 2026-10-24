using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using Arkya.Admin.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class PublicationsModel(ContentAdminClient content) : PageModel
{
    [BindProperty(SupportsGet = true), StringLength(16)] public string? Kind { get; set; } = "news";
    [BindProperty(SupportsGet = true), StringLength(2)] public string? Language { get; set; }
    [BindProperty(SupportsGet = true), StringLength(100)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true), StringLength(64)] public string? GameVersion { get; set; }
    [BindProperty(SupportsGet = true), StringLength(10)] public string? From { get; set; }
    [BindProperty(SupportsGet = true), StringLength(10)] public string? To { get; set; }
    [BindProperty(SupportsGet = true), StringLength(6)] public string? Sort { get; set; } = "newest";
    [BindProperty(SupportsGet = true), Range(0, 10000)] public int Offset { get; set; }
    [BindProperty(SupportsGet = true), Range(1, 50)] public int Limit { get; set; } = 12;
    public ContentPostList? Publications { get; private set; }
    public bool Enabled => content.Enabled;
    public string? Error { get; private set; }
    public bool HasPrevious => Offset > 0;
    public bool HasNext => Publications?.HasMore == true && Offset + Limit <= 10000;
    public string KindPlural => ContentPublicationPresentation.KindLabel(Kind ?? "news");
    public bool HasFilters => new[] { Language, Q, GameVersion, From, To }.Any(value => !string.IsNullOrEmpty(value));

    public Dictionary<string, string> Routes(int? offset = null, string? kind = null, bool clearFilters = false)
    {
        var selectedKind = kind ?? Kind ?? "news";
        var routes = new Dictionary<string, string>
        {
            ["kind"] = selectedKind,
            ["limit"] = Limit.ToString(CultureInfo.InvariantCulture)
        };
        if ((offset ?? Offset) > 0) routes["offset"] = (offset ?? Offset).ToString(CultureInfo.InvariantCulture);
        if (!clearFilters)
        {
            foreach (var (key, value) in new[] { ("language", Language), ("q", Q), ("from", From), ("to", To), ("sort", Sort) })
                if (!string.IsNullOrEmpty(value)) routes[key] = value;
            if (selectedKind == "release-note" && !string.IsNullOrEmpty(GameVersion)) routes["gameVersion"] = GameVersion;
        }
        return routes;
    }

    public async Task OnGetAsync(CancellationToken ct)
    {
        Kind ??= "news";
        Sort ??= "newest";
        if (!ContentPublicationPresentation.IsKind(Kind))
            ModelState.AddModelError(nameof(Kind), "Choose an available publication type.");
        if (!string.IsNullOrEmpty(Language) && !ContentPublicationPresentation.IsLanguage(Language))
            ModelState.AddModelError(nameof(Language), "Choose French or English.");
        if (Q?.Any(char.IsControl) == true)
            ModelState.AddModelError(nameof(Q), "Search cannot contain control characters.");
        if (!string.IsNullOrEmpty(GameVersion) && (Kind != "release-note" ||
            !Regex.IsMatch(GameVersion, @"\A[A-Za-z0-9][A-Za-z0-9._+\-]{0,63}\z", RegexOptions.CultureInvariant)))
            ModelState.AddModelError(nameof(GameVersion), "Choose release notes and enter a version of 1 to 64 letters, digits, dots, hyphens, underscores or +.");
        if (Sort is not ("newest" or "oldest"))
            ModelState.AddModelError(nameof(Sort), "Choose newest or oldest first.");
        var fromDate = ValidateDate(From, nameof(From));
        var toDate = ValidateDate(To, nameof(To));
        if (fromDate > toDate)
            ModelState.AddModelError(nameof(To), "The end date must be on or after the start date.");
        if (!Enabled || !ModelState.IsValid) return;
        try { Publications = await content.GetPostsAsync(Kind, Language, Offset, Limit, ct, Q, GameVersion, From, To, Sort); }
        catch (Exception ex) when (ContentPublicationPresentation.IsDependencyError(ex, ct))
        { Error = ContentPublicationPresentation.ErrorMessage(ex); }
    }

    private DateOnly? ValidateDate(string? text, string field)
    {
        if (string.IsNullOrEmpty(text)) return null;
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) return date;
        ModelState.AddModelError(field, "Use a valid UTC calendar date in YYYY-MM-DD format.");
        return null;
    }
}
