using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Arkya.Admin.Realms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class RealmModel(RealmsAdminClient realms) : PageModel
{
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

    public Task<IActionResult> OnPostRestartAsync(string realmId, CancellationToken ct) => LifecycleAsync(realmId, false, ct);
    public Task<IActionResult> OnPostUpdateAsync(string realmId, CancellationToken ct) => LifecycleAsync(realmId, true, ct);
    public Task<IActionResult> OnPostAnnounceAsync(string realmId, CancellationToken ct) => CommandAsync(realmId, "announce", ct);
    public Task<IActionResult> OnPostCommandAsync(string realmId, CancellationToken ct) => CommandAsync(realmId, CommandType, ct);

    public async Task<IActionResult> OnPostConfigurationAsync(string realmId, CancellationToken ct)
    {
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
            return RedirectToPage(new { realmId });
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
            return RedirectToPage(new { realmId });
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error = RealmPresentation.ErrorMessage(ex); }
        return await LoadAsync(realmId, ct);
    }

    private async Task<IActionResult> LifecycleAsync(string realmId, bool update, CancellationToken ct)
    {
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
            return RedirectToPage(new { realmId });
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
            DeploymentEnabled = (await realms.OptionsAsync(ct)).DeploymentEnabled;
            ExpectedRevision = Realm.Revision;
            // A retry must carry the current snapshot, never a stale form value.
            ModelState.Remove(nameof(ExpectedRevision));
        }
        catch (RealmsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error ??= RealmPresentation.ErrorMessage(ex); }
        if (Realm is not null)
        {
            try { OperationsOptions = await realms.OperationsOptionsAsync(realmId, ct); }
            catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct))
            { OperationsError = "Les actions serveur sont indisponibles. " + RealmPresentation.ErrorMessage(ex); }
            try { Commands = await realms.CommandsAsync(realmId, ct); }
            catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct))
            { CommandsError = "Le contrôle en jeu est indisponible. " + RealmPresentation.ErrorMessage(ex); }
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
        return Page();
    }

    private static bool ValidId(string? realmId) => realmId is { Length: >= 3 and <= 31 }
        && Regex.IsMatch(realmId, "\\A[a-z][a-z0-9-]{1,29}[a-z0-9]\\z", RegexOptions.CultureInvariant);
}
