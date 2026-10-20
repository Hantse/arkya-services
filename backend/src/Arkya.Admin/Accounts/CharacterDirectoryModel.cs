using Arkya.ServiceDefaults.Characters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Accounts;

public abstract class CharacterDirectoryModel(CharactersAdminClient characters, bool onlineOnly) : PageModel
{
    public bool Enabled => characters.Enabled;
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? AccountId { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    public AdminCharacterPage? Characters { get; private set; }
    public string? Error { get; private set; }
    public bool HasNext => Characters is not null && (long)Characters.Page * Characters.PageSize < Characters.Total;

    public async Task OnGetAsync(CancellationToken ct)
    {
        Search = Search?.Trim();
        AccountId = AccountId?.Trim();
        if (!PlayerAdminPresentation.ValidSearch(Search)) ModelState.AddModelError(nameof(Search), "La recherche accepte au plus 100 caractères sans caractères de contrôle.");
        if (!string.IsNullOrEmpty(AccountId))
        {
            if (!Guid.TryParse(AccountId, out var id) || id == Guid.Empty) ModelState.AddModelError(nameof(AccountId), "Renseignez un ID de compte Arkya valide.");
            else AccountId = id.ToString("D");
        }
        if (!PlayerAdminPresentation.ValidPagination(PageNumber, PageSize)) ModelState.AddModelError(nameof(PageNumber), "Choisissez une page de 1 à 10 000 et de 1 à 100 résultats.");
        if (!Enabled || !ModelState.IsValid) return;
        try { Characters = await characters.ListAsync(AccountId, Search, PageNumber, PageSize, onlineOnly, ct); }
        catch (Exception ex) when (PlayerAdminPresentation.IsDependencyError(ex, ct)) { Error = PlayerAdminPresentation.ErrorMessage(ex); }
    }
}
