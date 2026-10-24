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
    public string ActiveSection => Section is "settings" or "maintenance" or "players" or "game" or "history" or "map" ? Section : "overview";
    public bool CharactersEnabled => characters.Enabled;
    public AdminCharacterPage? Characters { get; private set; }
    public string? CharactersError { get; private set; }
    public bool HasNextCharacters => Characters is not null && (long)Characters.Page * Characters.PageSize < Characters.Total;
    public string AccountUrl(string accountId) => (sites.Enabled ? sites.AdminOrigin : "") + "/Account/" + Uri.EscapeDataString(accountId);
    public string CharacterUrl(string characterId) => (sites.Enabled ? sites.AdminOrigin : "") + "/Character/" +
        Uri.EscapeDataString(characterId) + "?realmId=" + Uri.EscapeDataString(Realm!.RealmId);
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
    public RealmServerVersion[]? ServerVersions { get; private set; }
    public RealmConfiguration? Configuration { get; private set; }
    public bool DeploymentEnabled { get; private set; }
    public string? Error { get; private set; }
    public string? OperationsError { get; private set; }
    public string? CommandsError { get; private set; }
    public string? ServerVersionsError { get; private set; }
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

    public async Task<IActionResult> OnGetWorldMapAsync(string realmId, CancellationToken ct)
    {
        if (!realms.Enabled || !ValidId(realmId)) return NotFound();
        try { return new JsonResult(await realms.WorldMapAsync(realmId, ct)); }
        catch (RealmsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct))
        { return new JsonResult(new { error = "The live map is unavailable. Retrying automatically." }) { StatusCode = StatusCodes.Status503ServiceUnavailable }; }
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
                Error = "Deployment is disabled on this installation. The realm remains saved.";
                return await LoadAsync(realmId, ct);
            }
            await realms.DeployAsync(realmId, ExpectedRevision, ct);
            Notice = "Deployment requested. Refresh this page to track its progress.";
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
            ModelState.AddModelError(nameof(ConfirmRealmId), "Enter the exact realm ID to confirm its deletion.");
        if (!ModelState.IsValid) return await LoadAsync(realmId, ct);
        try
        {
            var current = await realms.GetAsync(realmId, ct);
            if (!RealmPresentation.CanDelete(current))
            {
                Error = "Deletion is unavailable while an operation is in progress. Refresh before trying again.";
                return await LoadAsync(realmId, ct);
            }
            await realms.DeleteAsync(realmId, ExpectedRevision, Reason!, ConfirmRealmId!, Actor(), ct);
            Notice = "Realm deletion requested. Its removal from the list will be confirmed after its resources are deleted. Characters and their saves are retained.";
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
        var xp = ValidateMultiplier(XpMultiplier, nameof(XpMultiplier), "experience");
        var harvest = ValidateMultiplier(HarvestMultiplier, nameof(HarvestMultiplier), "harvesting");
        if (!ModelState.IsValid) return await LoadAsync(realmId, ct, preserveConfigurationInput: true);
        try
        {
            await realms.UpdateConfigurationAsync(realmId, new(ConfigurationRevision.Value, xp, harvest, Reason!), Actor(), ct);
            Notice = "Configuration saved. Check the status to confirm receipt by each server.";
            return RedirectToPage(new { realmId, section = ActiveSection });
        }
        catch (RealmsApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
        {
            ConfigurationError = "The configuration has changed. The current version has been reloaded; review the rates before trying again.";
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
            ModelState.AddModelError(key, $"The {label} multiplier must be from 0 to 100, with at most three decimal places.");
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
            ModelState.AddModelError(nameof(CommandType), "Choose an available command from the list.");
        Message = Message?.Trim();
        if (type == "announce" && (string.IsNullOrEmpty(Message) || Message.Length > 400 || Encoding.UTF8.GetByteCount(Message) > 400 || Message.Any(char.IsControl)))
            ModelState.AddModelError(nameof(Message), "Enter a single-line message of up to 400 UTF-8 bytes (accented characters occupy multiple bytes).");
        CharacterId = CharacterId?.Trim();
        if (type == "kick" && (!Guid.TryParseExact(CharacterId, "D", out var characterId) || characterId == Guid.Empty))
            ModelState.AddModelError(nameof(CharacterId), "Enter the UUID of the character to disconnect.");
        if (!ModelState.IsValid) return await LoadAsync(realmId, ct);
        try
        {
            var commands = await realms.CommandsAsync(realmId, ct);
            if (!commands.Servers.Any(server => server.ServerId == ServerId && server.Available && server.SupportedCommands.Contains(type!)))
            {
                ModelState.AddModelError(nameof(ServerId), "This server is not responding or does not support this command. Refresh and try again.");
                return await LoadAsync(realmId, ct);
            }
            await realms.CommandAsync(realmId, new(requestId.ToString("D"), ExpectedRevision, ServerId!, type!,
                type == "announce" ? Message! : "", type == "kick" ? Guid.Parse(CharacterId!).ToString("D") : "", Reason!), Actor(), ct);
            Notice = "Command saved and awaiting the server. Check the history to confirm execution.";
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
            ModelState.AddModelError(nameof(ConfirmRealmId), "Enter the realm ID to confirm interrupting players.");
        if (update && string.IsNullOrWhiteSpace(ExpectedImage))
            ModelState.AddModelError(nameof(ExpectedImage), "Reload the page to view the approved version.");
        if (!ModelState.IsValid) return await LoadAsync(realmId, ct);
        try
        {
            if (update) await realms.UpdateAsync(realmId, ExpectedRevision, Reason!, ExpectedImage!, Actor(), ct);
            else await realms.RestartAsync(realmId, ExpectedRevision, Reason!, Actor(), ct);
            Notice = update ? "Update requested. Track its result in the operation history."
                : "Restart requested. Track its result in the operation history.";
            return RedirectToPage(new { realmId, section = ActiveSection });
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error = RealmPresentation.ErrorMessage(ex); }
        return await LoadAsync(realmId, ct);
    }

    private void ValidateReason()
    {
        Reason = Reason?.Trim();
        if (string.IsNullOrEmpty(Reason) || Reason.Length > 500 || Reason.Any(char.IsControl))
            ModelState.AddModelError(nameof(Reason), "Enter a reason of 1 to 500 characters without control characters.");
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
            if (ActiveSection is "overview" or "maintenance")
            {
                try { ServerVersions = await realms.ServerVersionsAsync(realmId, ct); }
                catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct))
                { ServerVersionsError = "Server versions are unavailable. Refresh this page to try again."; }
            }
            if (ActiveSection == "maintenance")
            {
                try
                {
                    DeploymentEnabled = (await realms.OptionsAsync(ct)).DeploymentEnabled;
                    OperationsOptions = await realms.OperationsOptionsAsync(realmId, ct);
                }
                catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct))
                { OperationsError = "Server actions are unavailable. " + RealmPresentation.ErrorMessage(ex); }
            }
            if (ActiveSection is "settings" or "game" or "history")
            {
                try { Commands = await realms.CommandsAsync(realmId, ct); }
                catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct))
                { CommandsError = "In-game control is unavailable. " + RealmPresentation.ErrorMessage(ex); }
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
                { ConfigurationError ??= "In-game configuration is unavailable. " + RealmPresentation.ErrorMessage(ex); }
            }
        }
        return Page();
    }

    private async Task LoadPlayersAsync(CancellationToken ct)
    {
        Search = Search?.Trim();
        if (!PlayerAdminPresentation.ValidSearch(Search))
            ModelState.AddModelError(nameof(Search), "Search accepts up to 100 characters without control characters.");
        if (!PlayerAdminPresentation.ValidPagination(PageNumber, PageSize))
            ModelState.AddModelError(nameof(PageNumber), "Choose a page from 1 to 10,000 with 1 to 100 results.");
        if (!CharactersEnabled || !ModelState.IsValid) return;
        try
        {
            var page = await characters.ListAsync(null, Search, PageNumber, PageSize, OnlineOnly, ct, Realm!.RealmId);
            if (page.Items.Any(character => OnlineOnly && !character.IsOnline)
                || page.Page != PageNumber || page.PageSize != PageSize)
                throw new PlayerAdminApiException(HttpStatusCode.BadGateway, "The service returned an invalid response for this realm. Try again shortly.");
            Characters = page;
        }
        catch (Exception ex) when (PlayerAdminPresentation.IsDependencyError(ex, ct))
        { CharactersError = "Realm players are unavailable. " + PlayerAdminPresentation.ErrorMessage(ex); }
    }

    private static bool ValidId(string? realmId) => realmId is { Length: >= 3 and <= 31 }
        && Regex.IsMatch(realmId, "\\A[a-z][a-z0-9-]{1,29}[a-z0-9]\\z", RegexOptions.CultureInvariant);
}
