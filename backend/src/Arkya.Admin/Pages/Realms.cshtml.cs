using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Arkya.Admin.Realms;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class RealmsModel(RealmsAdminClient realms) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? EditRealmId { get; set; }
    [BindProperty] public long? ExpectedRevision { get; set; }
    public bool IsEditing => EditRealmId is not null;
    [BindProperty] public RealmInput Input { get; set; } = new();
    public RealmSummary[] Realms { get; private set; } = [];
    public RealmDeploymentOptions Options { get; private set; } = new([], [], false);
    public bool Enabled => realms.Enabled;
    public bool Loaded { get; private set; }
    public string? Error { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        Input.ConfigureTopology = true;
        Input.Islands = [new()];
        if (Options.Targets is [{ RealmId: { } realmId } target])
        {
            Input.RealmId = realmId;
            Input.TargetId = target.Id;
            Input.DisplayName = target.DisplayName;
        }
        if (Options.Profiles is [var profile]) Input.ProfileId = profile.Id;
        if (EditRealmId is not null)
        {
            if (!Enabled || !ValidRealmId(EditRealmId)) return NotFound();
            try
            {
                var realm = await realms.GetAsync(EditRealmId, ct);
                if (realm.Status != "Draft") return StatusCode(409);
                ExpectedRevision = realm.Revision;
                Input = new()
                {
                    RealmId = realm.RealmId, DisplayName = realm.DisplayName, TargetId = realm.TargetId, ProfileId = realm.ProfileId,
                    ConfigureTopology = realm.Topology is not null,
                    Islands = realm.Topology?.Islands.Select(island => new IslandInput { IslandId = island.IslandId,
                        DedicatedCount = island.DedicatedCount, Width = island.Width, Depth = island.Depth,
                        OriginX = island.OriginX, OriginZ = island.OriginZ }).ToList() ?? [],
                    InstanceDedicatedCount = realm.Topology?.InstanceDedicatedCount ?? 0,
                    RaidDedicatedCount = realm.Topology?.RaidDedicatedCount ?? 0
                };
            }
            catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error = RealmPresentation.ErrorMessage(ex); }
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        await LoadAsync(ct);
        if (!Loaded) return Page();
        if (!Options.Targets.Any(target => target.Id == Input.TargetId))
            ModelState.AddModelError("Input.TargetId", "Choisissez une cible disponible.");
        if (Options.Targets.SingleOrDefault(target => target.Id == Input.TargetId)?.RealmId is { } requiredId && Input.RealmId != requiredId)
            ModelState.AddModelError("Input.RealmId", $"Cette cible est réservée au realm {requiredId}.");
        if (!Options.Profiles.Any(profile => profile.Id == Input.ProfileId))
            ModelState.AddModelError("Input.ProfileId", "Choisissez un profil disponible.");
        if (Input.DisplayName?.Any(char.IsControl) == true) ModelState.AddModelError("Input.DisplayName", "Le nom ne peut pas contenir de caractères de contrôle.");
        ValidateTopology();
        if (!ModelState.IsValid) return Page();
        try
        {
            var realm = await realms.CreateAsync(new(Input.RealmId, Input.DisplayName!.Trim(), Input.TargetId, Input.ProfileId,
                Topology()), ct);
            Notice = "Realm créé. Vérifiez sa configuration, puis lancez le déploiement.";
            return RedirectToPage("/Realm", new { realmId = realm.RealmId });
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct))
        {
            Error = RealmPresentation.ErrorMessage(ex);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostSaveTopologyAsync(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (!ValidRealmId(EditRealmId) || ExpectedRevision is null or < 0) return BadRequest();
        await LoadAsync(ct);
        if (!Loaded) return Page();
        if (!Options.Profiles.Any(profile => profile.Id == Input.ProfileId))
            ModelState.AddModelError("Input.ProfileId", "Choisissez un profil disponible.");
        ValidateTopology();
        if (!ModelState.IsValid) return Page();
        try
        {
            await realms.UpdateTopologyAsync(EditRealmId!, ExpectedRevision.Value, Input.ProfileId, Topology(), ct);
            Notice = "Plan du realm enregistré.";
            return RedirectToPage("/Realm", new { realmId = EditRealmId });
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error = RealmPresentation.ErrorMessage(ex); }
        return Page();
    }

    private RealmTopology? Topology() => Input.ConfigureTopology
        ? new(Input.Islands.Select(island => new RealmIslandTopology(island.IslandId!, island.DedicatedCount,
            island.Width, island.Depth, island.OriginX, island.OriginZ)).ToArray(), Input.InstanceDedicatedCount, Input.RaidDedicatedCount)
        : null;

    private static bool ValidRealmId(string? id) => id is not null && Regex.IsMatch(id,
        @"\A[a-z][a-z0-9-]{1,29}[a-z0-9]\z", RegexOptions.CultureInvariant);

    public async Task<IActionResult> OnPostAddIslandAsync(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        await LoadAsync(ct);
        if (Input.Islands.Count >= 32)
            ModelState.AddModelError("Input.Islands", "Un realm peut contenir au maximum 32 îles.");
        else Input.Islands.Add(new IslandInput { IslandId = "", DedicatedCount = 1 });
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveIslandAsync(int removeIslandIndex, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        await LoadAsync(ct);
        if (removeIslandIndex < 0 || removeIslandIndex >= Input.Islands.Count) return BadRequest();
        Input.Islands.RemoveAt(removeIslandIndex);
        foreach (var key in ModelState.Keys.Where(key => key.StartsWith("Input.Islands[", StringComparison.Ordinal)).ToArray())
            ModelState.Remove(key);
        return Page();
    }

    private void ValidateTopology()
    {
        if (!Input.ConfigureTopology) return;
        if (Input.Islands.Count > 32)
            ModelState.AddModelError("Input.Islands", "Un realm peut contenir au maximum 32 îles.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        for (var index = 0; index < Input.Islands.Count; index++)
        {
            var island = Input.Islands[index];
            if (!Regex.IsMatch(island.IslandId ?? "", @"\A[a-z][a-z0-9-]{1,18}[a-z0-9]\z", RegexOptions.CultureInvariant))
                ModelState.AddModelError($"Input.Islands[{index}].IslandId", "L’identifiant d’île utilise 3 à 20 caractères : minuscules, chiffres et tirets.");
            else if (!ids.Add(island.IslandId!))
                ModelState.AddModelError($"Input.Islands[{index}].IslandId", "Chaque île doit avoir un identifiant distinct.");
            if (island.DedicatedCount is < 1 or > 64)
                ModelState.AddModelError($"Input.Islands[{index}].DedicatedCount", "Prévoyez entre 1 et 64 dédiés par île.");
            if (!double.IsFinite(island.Width) || island.Width is < 1 or > 1_000_000 ||
                !double.IsFinite(island.Depth) || island.Depth is < 1 or > 1_000_000)
                ModelState.AddModelError($"Input.Islands[{index}].Width", "Les dimensions de l’île doivent être finies et comprises entre 1 et 1 000 000 unités.");
            if (!double.IsFinite(island.OriginX) || Math.Abs(island.OriginX) > 10_000_000 ||
                !double.IsFinite(island.OriginZ) || Math.Abs(island.OriginZ) > 10_000_000)
                ModelState.AddModelError($"Input.Islands[{index}].OriginX", "L’origine doit être finie et limitée à ±10 000 000 unités.");
            total += island.DedicatedCount;
        }
        if (Input.InstanceDedicatedCount is < 0 or > 64)
            ModelState.AddModelError("Input.InstanceDedicatedCount", "Prévoyez entre 0 et 64 serveurs d’instances.");
        if (Input.RaidDedicatedCount is < 0 or > 64)
            ModelState.AddModelError("Input.RaidDedicatedCount", "Prévoyez entre 0 et 64 serveurs de raids.");
        total += (long)Input.InstanceDedicatedCount + Input.RaidDedicatedCount;
        if (total <= 0) ModelState.AddModelError("Input.Islands", "Prévoyez au moins un serveur pour une île, une instance ou un raid.");
        if (total > 256) ModelState.AddModelError("Input.Islands", "Un realm peut prévoir au maximum 256 serveurs dédiés au total.");
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

    public sealed class IslandInput
    {
        public string? IslandId { get; set; } = "starter-island";
        public int DedicatedCount { get; set; } = 4;
        public double Width { get; set; } = 1000;
        public double Depth { get; set; } = 1000;
        public double OriginX { get; set; }
        public double OriginZ { get; set; }
    }

    public sealed class RealmInput
    {
        [Required(ErrorMessage = "Renseignez un identifiant."), StringLength(31, MinimumLength = 3),
         RegularExpression("[a-z][a-z0-9-]{1,29}[a-z0-9]", ErrorMessage = "L'identifiant commence par une lettre minuscule, utilise des minuscules, chiffres ou tirets et finit par une lettre ou un chiffre.")]
        public string RealmId { get; set; } = "";
        [Required(ErrorMessage = "Renseignez un nom."), StringLength(80)] public string DisplayName { get; set; } = "";
        [Required(ErrorMessage = "Choisissez une cible."), StringLength(64)] public string TargetId { get; set; } = "";
        [Required(ErrorMessage = "Choisissez un profil."), StringLength(64)] public string ProfileId { get; set; } = "";
        public bool ConfigureTopology { get; set; }
        public List<IslandInput> Islands { get; set; } = [];
        public int InstanceDedicatedCount { get; set; }
        public int RaidDedicatedCount { get; set; }
    }
}
