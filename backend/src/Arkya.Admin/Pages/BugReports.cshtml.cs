using System.ComponentModel.DataAnnotations;
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
    [BindProperty(SupportsGet = true), StringLength(16)] public string? Kind { get; set; }
    [BindProperty(SupportsGet = true), Range(0, 100000)] public int Offset { get; set; }
    [BindProperty(SupportsGet = true), Range(1, 100)] public int Limit { get; set; } = 20;
    public BugReportListResponse? Reports { get; private set; }
    public bool Enabled => reports.Enabled;
    public string? Error { get; private set; }
    public bool HasPrevious => Offset > 0;
    public bool HasNext => Reports is not null && (long)Offset + Limit < Reports.Total && Offset + Limit <= 100000;

    public async Task OnGetAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        Tag = Tag?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(Tag) && !ValidTag(Tag)) ModelState.AddModelError(nameof(Tag), "Le tag doit contenir 1 à 32 minuscules, chiffres, tirets ou underscores.");
        if (!string.IsNullOrEmpty(Status) && !BugReportStatuses.All.Contains(Status))
            ModelState.AddModelError(nameof(Status), "Choisissez un statut disponible.");
        if (!string.IsNullOrEmpty(Kind) && !BugReportKinds.All.Contains(Kind))
            ModelState.AddModelError(nameof(Kind), "Choisissez un type disponible.");
        if (!ModelState.IsValid) return;
        try { Reports = await reports.ListAsync(Tag, Status, Offset, Limit, ct, Kind); }
        catch (Exception ex) when (BugReportPresentation.IsDependencyError(ex, ct)) { Error = BugReportPresentation.ErrorMessage(ex); }
    }

    internal static bool ValidTag(string tag) => tag.Length is >= 1 and <= 32 &&
        Regex.IsMatch(tag, "\\A[a-z0-9][a-z0-9_-]*\\z", RegexOptions.CultureInvariant);
}
