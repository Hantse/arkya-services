using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Arkya.Admin.Accounts;
using Arkya.Admin.Realms;
using Arkya.ServiceDefaults.Characters;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class RealmModel(RealmsAdminClient realms, RealmSiteLinks sites, CharactersAdminClient characters) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Section { get; set; }
    [BindProperty(SupportsGet = true)] public string? Island { get; set; }
    [BindProperty(SupportsGet = true)] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public bool OnlineOnly { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public int PageSize { get; set; } = 25;
    public RealmIslandView[] Islands => Realm is null ? [] : RealmIslandPresentation.Islands(Realm);
    public RealmIslandView? SelectedIsland => Islands.FirstOrDefault(island => island.Plan.IslandId == Island) ?? Islands.FirstOrDefault();
    public string? ActiveAction { get; private set; }
    public string ActiveSection => Section is "settings" or "maintenance" or "players" or "game" or "history" ? Section : "overview";
    public bool CharactersEnabled => characters.Enabled;
    public AdminCharacterPage? Characters { get; private set; }
    public string? CharactersError { get; private set; }
    public bool HasNextCharacters => Characters is not null && (long)Characters.Page * Characters.PageSize < Characters.Total;
    public string AccountUrl(string accountId) => (sites.Enabled ? sites.AdminOrigin : "") + "/Account/" + Uri.EscapeDataString(accountId);
    public bool AccountsAreCentral => sites.Enabled;

    [BindProperty, BindRequired, Range(0, long.MaxValue)] public long ExpectedRevision { get; set; }
    [BindProperty] public string? Reason { get; set; }
    [BindProperty] public string? ConfirmRealmId { get; set; }
    [BindProperty] public string? ExpectedImage { get; set; }
    [BindProperty] public string? RequestId { get; set; } = Guid.NewGuid().ToString("D");
    [BindProperty] public string? ServerId { get; set; }
    [BindProperty] public string? CommandType { get; set; }
    [BindProperty] public string? Message { get; set; }
    [BindProperty] public string? CharacterId { get; set; }
    [BindProperty] public long? ConfigurationRevision { get; set; }
    [BindProperty] public string? XpMultiplier { get; set; }
    [BindProperty] public string? HarvestMultiplier { get; set; }
    public RealmSummary? Realm { get; private set; }
    public RealmOperationsOptions? OperationsOptions { get; private set; }
    public RealmCommands? Commands { get; private set; }
    public RealmConfiguration? Configuration { get; private set; }
    public bool DeploymentEnabled { get; private set; }
    public string? Error { get; private set; }
    public string? OperationsError { get; private set; }
    public string? CommandsError { get; private set; }
    public string? ConfigurationError { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync(string realmId, CancellationToken ct)
    {
        if (!realms.Enabled || !ValidId(realmId)) return NotFound();
        if (sites.Enabled && RealmSiteLinks.IsRealmId(realmId) &&
            Request.Host.Host.Equals(sites.AdminDomain, StringComparison.OrdinalIgnoreCase))
            return Redirect(sites.AdminUrl(realmId) + Request.QueryString);
        return await LoadAsync(realmId, ct);
    }

    public async Task<IActionResult> OnPostDeployAsync(string realmId, CancellationToken ct)
    {
        Section = "maintenance";
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
            return RedirectToPage(new { realmId, section = ActiveSection });
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error = RealmPresentation.ErrorMessage(ex); }
        return await LoadAsync(realmId, ct);
    }

    public Task<IActionResult> OnPostRestartAsync(string realmId, CancellationToken ct) => LifecycleAsync(realmId, false, ct);
    public Task<IActionResult> OnPostUpdateAsync(string realmId, CancellationToken ct) => LifecycleAsync(realmId, true, ct);
    public async Task<IActionResult> OnPostDeleteAsync(string realmId, CancellationToken ct)
    {
        Section = "maintenance";
        ActiveAction = "delete";
        if (!realms.Enabled) return NotFound();
        if (!ValidId(realmId) || !ModelState.IsValid) return BadRequest();
        ValidateReason();
        if (!string.Equals(ConfirmRealmId, realmId, StringComparison.Ordinal))
            ModelState.AddModelError(nameof(ConfirmRealmId), "Recopiez exactement l’identifiant du realm pour confirmer sa suppression.");
        if (!ModelState.IsValid) return await LoadAsync(realmId, ct);
        try
        {
            var current = await realms.GetAsync(realmId, ct);
            if (!RealmPresentation.CanDelete(current))
            {
                Error = "La suppression est indisponible pendant une opération en cours. Actualisez avant de réessayer.";
                return await LoadAsync(realmId, ct);
            }
            await realms.DeleteAsync(realmId, ExpectedRevision, Reason!, ConfirmRealmId!, Actor(), ct);
            Notice = "Suppression du realm demandée. Son retrait de la liste sera confirmé après la suppression de ses ressources. Les personnages et leurs sauvegardes sont conservés.";
            return sites.Enabled ? Redirect(sites.AdminOrigin + "/Realms") : RedirectToPage("/Realms");
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error = RealmPresentation.ErrorMessage(ex); }
        return await LoadAsync(realmId, ct);
    }
    public Task<IActionResult> OnPostAnnounceAsync(string realmId, CancellationToken ct) => CommandAsync(realmId, "announce", ct);
    public Task<IActionResult> OnPostCommandAsync(string realmId, CancellationToken ct) => CommandAsync(realmId, CommandType, ct);

    public async Task<IActionResult> OnPostConfigurationAsync(string realmId, CancellationToken ct)
    {
        Section = "settings";
        if (!realms.Enabled) return NotFound();
        // Live configuration has its own revision and does not modify deployment state.
        ModelState.Remove(nameof(ExpectedRevision));
        if (!ValidId(realmId) || ConfigurationRevision is null or < 0 ||
            ModelState.TryGetValue(nameof(ConfigurationRevision), out var revision) && revision.Errors.Count > 0) return BadRequest();
        ValidateReason();
        var xp = ValidateMultiplier(XpMultiplier, nameof(XpMultiplier), "expérience");
        var harvest = ValidateMultiplier(HarvestMultiplier, nameof(HarvestMultiplier), "récolte");
        if (!ModelState.IsValid) return await LoadAsync(realmId, ct, preserveConfigurationInput: true);
        try
        {
            await realms.UpdateConfigurationAsync(realmId, new(ConfigurationRevision.Value, xp, harvest, Reason!), Actor(), ct);
            Notice = "Configuration enregistrée. Consultez le suivi pour vérifier sa réception par chaque serveur.";
            return RedirectToPage(new { realmId, section = ActiveSection });
        }
        catch (RealmsApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            ConfigurationError = "La configuration a changé. La version actuelle a été rechargée ; vérifiez les taux avant de réessayer.";
            return await LoadAsync(realmId, ct);
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct))
        {
            ConfigurationError = RealmPresentation.ErrorMessage(ex);
            return await LoadAsync(realmId, ct, preserveConfigurationInput: true);
        }
    }

    private decimal ValidateMultiplier(string? input, string key, string label)
    {
        if (!decimal.TryParse(input, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value)
            || value is < 0 or > 100 || decimal.Round(value, 3) != value)
            ModelState.AddModelError(key, $"Le multiplicateur de {label} doit être compris entre 0 et 100, avec trois décimales maximum.");
        return value;
    }

    private async Task<IActionResult> CommandAsync(string realmId, string? type, CancellationToken ct)
    {
        Section = "game";
        ActiveAction = type == "announce" ? "announce" : "command";
        if (!realms.Enabled) return NotFound();
        if (!ValidId(realmId) || !ModelState.IsValid || !Guid.TryParseExact(RequestId, "D", out var requestId) || requestId == Guid.Empty)
            return BadRequest();
        ValidateReason();
        if (type is not ("announce" or "kick" or "save"))
            ModelState.AddModelError(nameof(CommandType), "Choisissez une commande disponible dans la liste.");
        Message = Message?.Trim();
        if (type == "announce" && (string.IsNullOrEmpty(Message) || Message.Length > 400 || Encoding.UTF8.GetByteCount(Message) > 400 || Message.Any(char.IsControl)))
            ModelState.AddModelError(nameof(Message), "Renseignez un message sans retour à la ligne, limité à 400 octets UTF-8 (les caractères accentués occupent plusieurs octets).");
        CharacterId = CharacterId?.Trim();
        if (type == "kick" && (!Guid.TryParseExact(CharacterId, "D", out var characterId) || characterId == Guid.Empty))
            ModelState.AddModelError(nameof(CharacterId), "Renseignez l’identifiant UUID du personnage à déconnecter.");
        if (!ModelState.IsValid) return await LoadAsync(realmId, ct);
        try
        {
            var commands = await realms.CommandsAsync(realmId, ct);
            if (!commands.Servers.Any(server => server.ServerId == ServerId && server.Available && server.SupportedCommands.Contains(type!)))
            {
                ModelState.AddModelError(nameof(ServerId), "Ce serveur ne répond pas ou ne prend pas en charge cette commande. Actualisez puis réessayez.");
                return await LoadAsync(realmId, ct);
            }
            await realms.CommandAsync(realmId, new(requestId.ToString("D"), ExpectedRevision, ServerId!, type!,
                type == "announce" ? Message! : "", type == "kick" ? Guid.Parse(CharacterId!).ToString("D") : "", Reason!), Actor(), ct);
            Notice = "Commande enregistrée en attente du serveur. Consultez l’historique pour vérifier son exécution.";
            return RedirectToPage(new { realmId, section = ActiveSection });
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error = RealmPresentation.ErrorMessage(ex); }
        return await LoadAsync(realmId, ct);
    }

    private async Task<IActionResult> LifecycleAsync(string realmId, bool update, CancellationToken ct)
    {
        Section = "maintenance";
        ActiveAction = update ? "update" : "restart";
        if (!realms.Enabled) return NotFound();
        if (!ValidId(realmId) || !ModelState.IsValid) return BadRequest();
        ValidateReason();
        if (!string.Equals(ConfirmRealmId, realmId, StringComparison.Ordinal))
            ModelState.AddModelError(nameof(ConfirmRealmId), "Recopiez l’identifiant du realm pour confirmer l’interruption des joueurs.");
        if (update && string.IsNullOrWhiteSpace(ExpectedImage))
            ModelState.AddModelError(nameof(ExpectedImage), "Rechargez la page pour consulter la version approuvée.");
        if (!ModelState.IsValid) return await LoadAsync(realmId, ct);
        try
        {
            if (update) await realms.UpdateAsync(realmId, ExpectedRevision, Reason!, ExpectedImage!, Actor(), ct);
            else await realms.RestartAsync(realmId, ExpectedRevision, Reason!, Actor(), ct);
            Notice = update ? "Mise à jour demandée. Suivez son résultat dans l’historique des opérations."
                : "Redémarrage demandé. Suivez son résultat dans l’historique des opérations.";
            return RedirectToPage(new { realmId, section = ActiveSection });
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error = RealmPresentation.ErrorMessage(ex); }
        return await LoadAsync(realmId, ct);
    }

    private void ValidateReason()
    {
        Reason = Reason?.Trim();
        if (string.IsNullOrEmpty(Reason) || Reason.Length > 500 || Reason.Any(char.IsControl))
            ModelState.AddModelError(nameof(Reason), "Renseignez un motif de 1 à 500 caractères, sans caractères de contrôle.");
    }

    private string Actor()
    {
        var discordId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return discordId is { Length: > 0 and <= 32 } && discordId.All(char.IsAsciiDigit)
            ? "admin:discord:" + discordId : "admin:operator";
    }

    private async Task<IActionResult> LoadAsync(string realmId, CancellationToken ct, bool preserveConfigurationInput = false)
    {
        RequestId ??= Guid.NewGuid().ToString("D");
        try
        {
            Realm = await realms.GetAsync(realmId, ct);
            ExpectedRevision = Realm.Revision;
            // A retry must carry the current snapshot, never a stale form value.
            ModelState.Remove(nameof(ExpectedRevision));
        }
        catch (RealmsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error ??= RealmPresentation.ErrorMessage(ex); }
        if (Realm is not null)
        {
            if (ActiveSection == "players") await LoadPlayersAsync(ct);
            if (ActiveSection == "maintenance")
            {
                try
                {
                    DeploymentEnabled = (await realms.OptionsAsync(ct)).DeploymentEnabled;
                    OperationsOptions = await realms.OperationsOptionsAsync(realmId, ct);
                }
                catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct))
                { OperationsError = "Les actions serveur sont indisponibles. " + RealmPresentation.ErrorMessage(ex); }
            }
            if (ActiveSection is "settings" or "game" or "history")
            {
                try { Commands = await realms.CommandsAsync(realmId, ct); }
                catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct))
                { CommandsError = "Le contrôle en jeu est indisponible. " + RealmPresentation.ErrorMessage(ex); }
            }
            if (ActiveSection is "settings" or "history")
            {
                try
                {
                    Configuration = await realms.ConfigurationAsync(realmId, ct);
                    if (!preserveConfigurationInput)
                    {
                        ConfigurationRevision = Configuration.Revision;
                        XpMultiplier = Configuration.XpMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
                        HarvestMultiplier = Configuration.HarvestMultiplier.ToString("0.###", CultureInfo.InvariantCulture);
                        ModelState.Remove(nameof(ConfigurationRevision));
                        ModelState.Remove(nameof(XpMultiplier));
                        ModelState.Remove(nameof(HarvestMultiplier));
                    }
                }
                catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct))
                { ConfigurationError ??= "La configuration en jeu est indisponible. " + RealmPresentation.ErrorMessage(ex); }
            }
        }
        return Page();
    }

    private async Task LoadPlayersAsync(CancellationToken ct)
    {
        Search = Search?.Trim();
        if (!PlayerAdminPresentation.ValidSearch(Search))
            ModelState.AddModelError(nameof(Search), "La recherche accepte au plus 100 caractères sans caractères de contrôle.");
        if (!PlayerAdminPresentation.ValidPagination(PageNumber, PageSize))
            ModelState.AddModelError(nameof(PageNumber), "Choisissez une page de 1 à 10 000 et de 1 à 100 résultats.");
        if (!CharactersEnabled || !ModelState.IsValid) return;
        try
        {
            var page = await characters.ListAsync(null, Search, PageNumber, PageSize, OnlineOnly, ct, Realm!.RealmId);
            if (page.Items.Any(character => OnlineOnly && !character.IsOnline)
                || page.Page != PageNumber || page.PageSize != PageSize)
                throw new PlayerAdminApiException(HttpStatusCode.BadGateway, "Le service a renvoyé une réponse invalide pour ce realm. Réessayez dans un instant.");
            Characters = page;
        }
        catch (Exception ex) when (PlayerAdminPresentation.IsDependencyError(ex, ct))
        { CharactersError = "Les joueurs du realm sont indisponibles. " + PlayerAdminPresentation.ErrorMessage(ex); }
    }

    private static bool ValidId(string? realmId) => realmId is { Length: >= 3 and <= 31 }
        && Regex.IsMatch(realmId, "\\A[a-z][a-z0-9-]{1,29}[a-z0-9]\\z", RegexOptions.CultureInvariant);
}
