using System.ComponentModel.DataAnnotations;
using Arkya.Admin.Realms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class RealmsModel(RealmsAdminClient realms) : PageModel
{
    [BindProperty] public RealmInput Input { get; set; } = new();
    public RealmSummary[] Realms { get; private set; } = [];
    public RealmDeploymentOptions Options { get; private set; } = new([], [], false);
    public bool Enabled => realms.Enabled;
    public bool Loaded { get; private set; }
    public string? Error { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        await LoadAsync(ct);
        if (!Loaded) return Page();
        if (!Options.Targets.Any(target => target.Id == Input.TargetId))
            ModelState.AddModelError("Input.TargetId", "Choisissez une cible disponible.");
        if (!Options.Profiles.Any(profile => profile.Id == Input.ProfileId))
            ModelState.AddModelError("Input.ProfileId", "Choisissez un profil disponible.");
        if (Input.DisplayName?.Any(char.IsControl) == true) ModelState.AddModelError("Input.DisplayName", "Le nom ne peut pas contenir de caractères de contrôle.");
        if (!ModelState.IsValid) return Page();
        try
        {
            var realm = await realms.CreateAsync(new(Input.RealmId, Input.DisplayName!.Trim(), Input.TargetId, Input.ProfileId), ct);
            Notice = "Realm créé. Vérifiez sa configuration, puis lancez le déploiement.";
            return RedirectToPage("/Realm", new { realmId = realm.RealmId });
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct))
        {
            Error = RealmPresentation.ErrorMessage(ex);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        try
        {
            Options = await realms.OptionsAsync(ct);
            Realms = (await realms.ListAsync(ct)).OrderByDescending(realm => realm.UpdatedAt).ToArray();
            Loaded = true;
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error ??= RealmPresentation.ErrorMessage(ex); }
    }

    public sealed class RealmInput
    {
        [Required(ErrorMessage = "Renseignez un identifiant."), StringLength(31, MinimumLength = 3),
         RegularExpression("[a-z][a-z0-9-]{1,29}[a-z0-9]", ErrorMessage = "L'identifiant commence par une lettre minuscule, utilise des minuscules, chiffres ou tirets et finit par une lettre ou un chiffre.")]
        public string RealmId { get; set; } = "";
        [Required(ErrorMessage = "Renseignez un nom."), StringLength(80)] public string DisplayName { get; set; } = "";
        [Required(ErrorMessage = "Choisissez une cible."), StringLength(64)] public string TargetId { get; set; } = "";
        [Required(ErrorMessage = "Choisissez un profil."), StringLength(64)] public string ProfileId { get; set; } = "";
    }
}
