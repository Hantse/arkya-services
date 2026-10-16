using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Text.RegularExpressions;
using Arkya.Admin.Realms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class RealmModel(RealmsAdminClient realms) : PageModel
{
    [BindProperty, BindRequired, Range(0, long.MaxValue)] public long ExpectedRevision { get; set; }
    public RealmSummary? Realm { get; private set; }
    public bool DeploymentEnabled { get; private set; }
    public string? Error { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync(string realmId, CancellationToken ct)
    {
        if (!realms.Enabled || !ValidId(realmId)) return NotFound();
        return await LoadAsync(realmId, ct);
    }

    public async Task<IActionResult> OnPostDeployAsync(string realmId, CancellationToken ct)
    {
        if (!realms.Enabled) return NotFound();
        if (!ValidId(realmId) || !ModelState.IsValid) return BadRequest();
        try
        {
            var options = await realms.OptionsAsync(ct);
            if (!options.DeploymentEnabled)
            {
                Error = "Le déploiement est désactivé sur cette installation. Le realm reste enregistré.";
                return await LoadAsync(realmId, ct);
            }
            await realms.DeployAsync(realmId, ExpectedRevision, ct);
            Notice = "Déploiement demandé. Actualisez cette page pour suivre son avancement.";
            return RedirectToPage(new { realmId });
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error = RealmPresentation.ErrorMessage(ex); }
        return await LoadAsync(realmId, ct);
    }

    private async Task<IActionResult> LoadAsync(string realmId, CancellationToken ct)
    {
        try
        {
            Realm = await realms.GetAsync(realmId, ct);
            DeploymentEnabled = (await realms.OptionsAsync(ct)).DeploymentEnabled;
            ExpectedRevision = Realm.Revision;
            // A retry must carry the current snapshot, never a stale form value.
            ModelState.Remove(nameof(ExpectedRevision));
        }
        catch (RealmsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error ??= RealmPresentation.ErrorMessage(ex); }
        return Page();
    }

    private static bool ValidId(string? realmId) => realmId is { Length: >= 3 and <= 31 }
        && Regex.IsMatch(realmId, "\\A[a-z][a-z0-9-]{1,29}[a-z0-9]\\z", RegexOptions.CultureInvariant);
}
