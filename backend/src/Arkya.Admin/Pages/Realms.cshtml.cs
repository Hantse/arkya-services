using Arkya.Admin.Realms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class RealmsModel(RealmsAdminClient realms) : PageModel
{
    public RealmSummary[] Realms { get; private set; } = [];
    public bool Enabled => realms.Enabled;
    public bool Loaded { get; private set; }
    public string? Error { get; private set; }
    [TempData] public string? Notice { get; set; }

    public IActionResult OnPost() => BadRequest();

    public async Task<IActionResult> OnGetAsync(string? editRealmId, CancellationToken ct)
    {
        if (editRealmId is not null) return RedirectToPage("/RealmCreate", new { editRealmId });
        if (!Enabled) return Page();
        try
        {
            Realms = (await realms.ListAsync(ct)).OrderByDescending(realm => realm.UpdatedAt).ToArray();
            Loaded = true;
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error = RealmPresentation.ErrorMessage(ex); }
        return Page();
    }
}
