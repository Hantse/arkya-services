using System.Security.Claims;
using Arkya.Admin.Accounts;
using Arkya.ServiceDefaults.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class BetaRegistrationsModel(IdentityAdminClient accounts) : PageModel
{
    public bool Enabled => accounts.Enabled;
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; } = "pending";
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    [BindProperty(SupportsGet = true)] public bool Draw { get; set; }
    [BindProperty] public Guid RequestId { get; set; } = Guid.NewGuid();
    [BindProperty] public int Count { get; set; } = 1;
    [BindProperty] public string? Reason { get; set; } = "Beta access wave";
    [BindProperty] public Guid AccountId { get; set; }
    [BindProperty] public long? ExpectedRevision { get; set; }
    [BindProperty] public string? GrantReason { get; set; } = "Beta registration approved";
    public BetaRegistrationPage? Registrations { get; private set; }
    public string? Error { get; private set; }
    [TempData] public string? Notice { get; set; }
    public bool HasNext => Registrations is not null && (long)Registrations.Page * Registrations.PageSize < Registrations.Total;

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostGrantAsync(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        GrantReason = GrantReason?.Trim() ?? "";
        if (AccountId == Guid.Empty || ExpectedRevision is null or < 0 or long.MaxValue || !Request.Form.ContainsKey(nameof(GrantReason))) return BadRequest();
        if (!ValidReason(GrantReason)) ModelState.AddModelError(nameof(GrantReason), "Enter a reason of 1 to 500 characters without controls.");
        if (ModelState.IsValid)
        {
            try
            {
                await accounts.GrantBetaRegistrationAsync(AccountId, ExpectedRevision.Value, GrantReason, Actor(), ct);
                Notice = "Beta access granted.";
                return RedirectToPage(new { Search, Status, PageNumber, PageSize });
            }
            catch (Exception ex) when (PlayerAdminPresentation.IsDependencyError(ex, ct)) { Error = PlayerAdminPresentation.ErrorMessage(ex); }
        }
        await LoadAsync(ct);
        return Page();
    }

    public async Task<IActionResult> OnPostRandomAsync(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        Draw = true;
        Reason = Reason?.Trim() ?? "";
        if (RequestId == Guid.Empty || !Request.Form.ContainsKey(nameof(RequestId)) || !Request.Form.ContainsKey(nameof(Count))) return BadRequest();
        if (Count is < 1 or > 100) ModelState.AddModelError(nameof(Count), "Choose between 1 and 100 accesses.");
        if (!ValidReason(Reason)) ModelState.AddModelError(nameof(Reason), "Enter a reason of 1 to 500 characters without controls.");
        if (ModelState.IsValid)
        {
            try
            {
                var result = await accounts.GrantRandomBetaAccessAsync(RequestId, Count, Reason, Actor(), ct);
                Notice = $"Granted {result.GrantedCount} of {result.RequestedCount} requested beta accesses. Only eligible pending registrations were selected.";
                return RedirectToPage(new { Status = "granted" });
            }
            catch (Exception ex) when (PlayerAdminPresentation.IsDependencyError(ex, ct)) { Error = PlayerAdminPresentation.ErrorMessage(ex); }
        }
        await LoadAsync(ct);
        return Page();
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        Search = Search?.Trim();
        Status ??= "pending";
        if (!PlayerAdminPresentation.ValidSearch(Search)) ModelState.AddModelError(nameof(Search), "Search accepts up to 100 characters without controls.");
        if (!PlayerAdminPresentation.ValidPagination(PageNumber, PageSize)) ModelState.AddModelError(nameof(PageNumber), "Choose a page from 1 to 10,000 with 1 to 100 results.");
        if (Status is not ("all" or "pending" or "granted" or "banned")) ModelState.AddModelError(nameof(Status), "Choose a supported registration status.");
        if (!Enabled || !PlayerAdminPresentation.ValidSearch(Search) || !PlayerAdminPresentation.ValidPagination(PageNumber, PageSize) ||
            Status is not ("all" or "pending" or "granted" or "banned")) return;
        try { Registrations = await accounts.ListBetaRegistrationsAsync(Search, Status, PageNumber, PageSize, ct); }
        catch (Exception ex) when (PlayerAdminPresentation.IsDependencyError(ex, ct)) { Error ??= PlayerAdminPresentation.ErrorMessage(ex); }
    }

    private static bool ValidReason(string text) => !string.IsNullOrWhiteSpace(text) && text.Length <= 500 && !text.Any(char.IsControl);
    private string Actor()
    {
        var id = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return id is not null && id.Length <= 32 && id.All(char.IsAsciiDigit) ? "admin:discord:" + id : "admin:operator";
    }
}
