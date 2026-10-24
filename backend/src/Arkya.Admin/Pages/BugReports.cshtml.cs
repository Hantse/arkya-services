using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using Arkya.Admin.Reports;
using Arkya.ServiceDefaults.Reports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class BugReportsModel(ReportsAdminClient reports) : PageModel
{
    [BindProperty(SupportsGet = true), StringLength(32)] public string? Tag { get; set; }
    [BindProperty(SupportsGet = true), StringLength(32)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true), StringLength(16)] public string? Kind { get; set; } = BugReportKinds.Bug;
    [BindProperty(SupportsGet = true), StringLength(120)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true), StringLength(128)] public string? Scene { get; set; }
    [BindProperty(SupportsGet = true), StringLength(64)] public string? ClientVersion { get; set; }
    [BindProperty(SupportsGet = true), StringLength(64)] public string? Platform { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateOnly? CreatedFrom { get; set; }
    [BindProperty(SupportsGet = true), DataType(DataType.Date)] public DateOnly? CreatedTo { get; set; }
    [BindProperty(SupportsGet = true), StringLength(16)] public string? Sort { get; set; } = BugReportSorts.Newest;
    [BindProperty(SupportsGet = true), Range(0, 100000)] public int Offset { get; set; }
    [BindProperty(SupportsGet = true), Range(1, 100)] public int Limit { get; set; } = 20;
    public BugReportListResponse? Reports { get; private set; }
    public bool Enabled => reports.Enabled;
    public string? Error { get; private set; }
    public bool HasPrevious => Offset > 0;
    public bool HasNext => Reports is not null && (long)Offset + Limit < Reports.Total && Offset + Limit <= 100000;
    public string KindPlural => Kind == BugReportKinds.Suggestion ? "Suggestions" : "Bugs";
    public bool HasFilters => new[] { Tag, Status, Search, Scene, ClientVersion, Platform }.Any(value => !string.IsNullOrEmpty(value))
        || CreatedFrom.HasValue || CreatedTo.HasValue;
    public long PageNumber => (long)Offset / Math.Max(1, Limit) + 1;
    public long PageCount => Math.Max(1, ((Reports?.Total ?? 0) - 1) / Math.Max(1, Limit) + 1);
    public long LastAccessiblePage => Math.Min(PageCount, 100000 / Math.Max(1, Limit) + 1);
    public long FirstResult => Reports is { Items.Length: > 0 } ? (long)Offset + 1 : 0;
    public long LastResult => Reports is null ? 0 : Math.Min((long)Offset + Reports.Items.Length, Reports.Total);
    public IEnumerable<long> VisiblePages => new[] { 1L, LastAccessiblePage }
        .Concat(Enumerable.Range(-2, 5).Select(delta => PageNumber + delta))
        .Where(page => page >= 1 && page <= LastAccessiblePage).Distinct().Order();

    public Dictionary<string, string> Routes(int? offset = null, string? kind = null, string? tag = null, bool clearFilters = false)
    {
        var route = new Dictionary<string, string>
        {
            ["kind"] = kind ?? Kind ?? BugReportKinds.Bug,
            ["limit"] = Limit.ToString(CultureInfo.InvariantCulture)
        };
        if ((offset ?? Offset) > 0) route["offset"] = (offset ?? Offset).ToString(CultureInfo.InvariantCulture);
        if (!clearFilters)
        {
            foreach (var (key, value) in new[] { ("tag", tag ?? Tag), ("status", Status), ("search", Search),
                ("scene", Scene), ("clientVersion", ClientVersion), ("platform", Platform), ("sort", Sort),
                ("createdFrom", CreatedFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("createdTo", CreatedTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) })
                if (!string.IsNullOrEmpty(value)) route[key] = value;
        }
        return route;
    }

    public Dictionary<string, string> PageRoutes(long page) => Routes(offset: (int)((page - 1) * Limit));

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        Kind = string.IsNullOrEmpty(Kind) ? BugReportKinds.Bug : Kind;
        Sort = string.IsNullOrEmpty(Sort) ? BugReportSorts.Newest : Sort;
        foreach (var (field, value) in new[] { (nameof(Search), Search), (nameof(Scene), Scene),
            (nameof(ClientVersion), ClientVersion), (nameof(Platform), Platform) })
            if (value?.Any(char.IsControl) == true)
                ModelState.AddModelError(field, "Filter values cannot contain control characters.");
        Tag = Tag?.Trim().ToLowerInvariant();
        Search = Search?.Trim();
        Scene = Scene?.Trim();
        ClientVersion = ClientVersion?.Trim();
        Platform = Platform?.Trim();
        if (!string.IsNullOrEmpty(Tag) && !ValidTag(Tag)) ModelState.AddModelError(nameof(Tag), "The tag must contain 1 to 32 lowercase letters, digits, hyphens or underscores.");
        if (!string.IsNullOrEmpty(Status) && !BugReportStatuses.All.Contains(Status))
            ModelState.AddModelError(nameof(Status), "Choose an available status.");
        if (!string.IsNullOrEmpty(Kind) && !BugReportKinds.All.Contains(Kind))
            ModelState.AddModelError(nameof(Kind), "Choose an available type.");
        if (!BugReportSorts.All.Contains(Sort))
            ModelState.AddModelError(nameof(Sort), "Choose an available sort order.");
        if (CreatedFrom > CreatedTo)
            ModelState.AddModelError(nameof(CreatedTo), "The end date must be on or after the start date.");
        if (!ModelState.IsValid) return;
        try { Reports = await reports.ListAsync(Tag, Status, Offset, Limit, ct, Kind,
            new BugReportListFilters(Search, Scene, ClientVersion, Platform, CreatedFrom, CreatedTo, Sort)); }
        catch (Exception ex) when (BugReportPresentation.IsDependencyError(ex, ct)) { Error = BugReportPresentation.ErrorMessage(ex); }
    }

    internal static bool ValidTag(string tag) => tag.Length is >= 1 and <= 32 &&
        Regex.IsMatch(tag, "\\A[a-z0-9][a-z0-9_-]*\\z", RegexOptions.CultureInvariant);
}
