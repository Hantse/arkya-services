using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using Arkya.Admin.Accounts;
using Arkya.ServiceDefaults.Accounts;
using Arkya.ServiceDefaults.Characters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class AccountModel(IdentityAdminClient accounts, CharactersAdminClient characters) : PageModel
{
    [BindProperty, BindRequired, Range(0, long.MaxValue)] public long ExpectedRevision { get; set; }
    [BindProperty, Required(ErrorMessage = "Enter a reason for this action."), StringLength(500)] public string Reason { get; set; } = "";
    [BindProperty] public bool HasBetaAccess { get; set; }
    public AdminAccountSummary? Account { get; private set; }
    public Guid Id { get; private set; }
    public AdminCharacterPage? Characters { get; private set; }
    public bool CharactersEnabled => characters.Enabled;
    public string? Error { get; private set; }
    public string? CharactersError { get; private set; }
    [TempData] public string? Notice { get; set; }

    public Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct) => LoadAsync(id, true, ct);
    public Task<IActionResult> OnPostBanAsync(Guid id, CancellationToken ct) => MutateAsync(id, "ban", ct);
    public Task<IActionResult> OnPostUnbanAsync(Guid id, CancellationToken ct) => MutateAsync(id, "unban", ct);
    public Task<IActionResult> OnPostBetaAsync(Guid id, CancellationToken ct) => MutateAsync(id, "beta", ct);

    private async Task<IActionResult> MutateAsync(Guid id, string action, CancellationToken ct)
    {
        if (!accounts.Enabled || id == Guid.Empty) return NotFound();
        if (ModelState.TryGetValue(nameof(ExpectedRevision), out var revision) && revision.Errors.Count > 0) return BadRequest();
        Reason = Reason?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(Reason) || Reason.Any(char.IsControl)) ModelState.AddModelError(nameof(Reason), "Enter a reason without control characters.");
        if (action == "beta" && !Request.Form.ContainsKey(nameof(HasBetaAccess))) return BadRequest();
        if (!ModelState.IsValid) return await LoadAsync(id, false, ct);
        var discordId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var actor = discordId is not null && discordId.Length <= 32 && discordId.All(char.IsAsciiDigit) ? "admin:discord:" + discordId : "admin:operator";
        try
        {
            if (action == "ban") await accounts.BanAsync(id, ExpectedRevision, Reason, actor, ct);
            else if (action == "unban") await accounts.UnbanAsync(id, ExpectedRevision, Reason, actor, ct);
            else await accounts.SetBetaAccessAsync(id, ExpectedRevision, HasBetaAccess, Reason, actor, ct);
            Notice = action switch { "ban" => "Account banned. The player's sessions have been revoked.", "unban" => "Ban removed.", _ => HasBetaAccess ? "Beta access granted." : "Beta access removed." };
            return RedirectToPage(new { id });
        }
        catch (PlayerAdminApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
        catch (Exception ex) when (PlayerAdminPresentation.IsDependencyError(ex, ct))
        {
            Error = PlayerAdminPresentation.ErrorMessage(ex);
            return await LoadAsync(id, ex is PlayerAdminApiException { StatusCode: HttpStatusCode.Conflict }, ct);
        }
    }

    private async Task<IActionResult> LoadAsync(Guid id, bool resetInput, CancellationToken ct)
    {
        Id = id;
        if (!accounts.Enabled || id == Guid.Empty) return NotFound();
        try
        {
            Account = await accounts.GetAsync(id, ct);
            if (resetInput) { ExpectedRevision = Account.Revision; Reason = ""; HasBetaAccess = !Account.HasBetaAccess; ModelState.Clear(); }
        }
        catch (PlayerAdminApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
        catch (Exception ex) when (PlayerAdminPresentation.IsDependencyError(ex, ct)) { Error ??= PlayerAdminPresentation.ErrorMessage(ex); }
        if (Account is not null && characters.Enabled)
        {
            try { Characters = await characters.ListAsync(id.ToString("D"), null, 1, 25, false, ct); }
            catch (Exception ex) when (PlayerAdminPresentation.IsDependencyError(ex, ct)) { CharactersError = PlayerAdminPresentation.ErrorMessage(ex); }
        }
        return Page();
    }
}
