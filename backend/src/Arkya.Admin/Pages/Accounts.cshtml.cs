using Arkya.Admin.Accounts;
using Arkya.ServiceDefaults.Accounts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class AccountsModel(IdentityAdminClient accounts) : PageModel
{
    public bool Enabled => accounts.Enabled;
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    public AdminAccountPage? Accounts { get; private set; }
    public string? Error { get; private set; }
    public bool HasNext => Accounts is not null && (long)Accounts.Page * Accounts.PageSize < Accounts.Total;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Search = Search?.Trim();
        if (!PlayerAdminPresentation.ValidSearch(Search)) ModelState.AddModelError(nameof(Search), "Search accepts up to 100 characters without control characters.");
        if (!PlayerAdminPresentation.ValidPagination(PageNumber, PageSize)) ModelState.AddModelError(nameof(PageNumber), "Choose a page from 1 to 10,000 with 1 to 100 results.");
        if (!Enabled || !ModelState.IsValid) return;
        try { Accounts = await accounts.ListAsync(Search, PageNumber, PageSize, ct); }
        catch (Exception ex) when (PlayerAdminPresentation.IsDependencyError(ex, ct)) { Error = PlayerAdminPresentation.ErrorMessage(ex); }
    }
}
