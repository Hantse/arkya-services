using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Arkya.Admin.Realms;
using Arkya.ServiceDefaults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

[RequestFormLimits(ValueCountLimit = 4096)]
public sealed class RealmCreateModel(RealmsAdminClient realms) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? EditRealmId { get; set; }
    [BindProperty] public long? ExpectedRevision { get; set; }
    public bool IsEditing => EditRealmId is not null;
    [BindProperty] public RealmInput Input { get; set; } = new();
    public RealmDeploymentOptions Options { get; private set; } = new([], [], false);
    public bool Enabled => realms.Enabled;
    public bool Loaded { get; private set; }
    public string? Error { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken ct)
    {
        await LoadAsync(ct);
        Input.ConfigureTopology = Options.Profiles is not [{ TopologySupported: false }];
        Input.Islands = [new() { UseGrid = true, GridColumns = 2, GridRows = 2, Width = 1536, Depth = 1536 }];
        if (Options.Targets is [var target])
        {
            Input.TargetId = target.Id;
            if (target.RealmId is { } realmId)
            {
                Input.RealmId = realmId;
                Input.DisplayName = target.DisplayName;
            }
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
                    Islands = realm.Topology?.Islands.Select(island => new IslandInput { IslandId = island.IslandId, ContentId = island.ContentId,
                        DedicatedCount = island.DedicatedCount, Width = island.Width, Depth = island.Depth,
                        OriginX = island.OriginX, OriginZ = island.OriginZ,
                        UseGrid = island.GridColumns is not null || island.GridRows is not null,
                        GridColumns = island.GridColumns, GridRows = island.GridRows }).ToList() ?? [],
                    InstanceDedicatedCount = realm.Topology?.InstanceDedicatedCount ?? 0,
                    RaidDedicatedCount = realm.Topology?.RaidDedicatedCount ?? 0,
                    AreaTransitions = realm.Topology?.AreaTransitions?.Select(TransitionInput.From).ToList() ?? []
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
            ModelState.AddModelError("Input.TargetId", "Choose an available target.");
        if (Options.Targets.SingleOrDefault(target => target.Id == Input.TargetId)?.RealmId is { } requiredId && Input.RealmId != requiredId)
            ModelState.AddModelError("Input.RealmId", $"This target is reserved for realm {requiredId}.");
        if (!Options.Profiles.Any(profile => profile.Id == Input.ProfileId))
            ModelState.AddModelError("Input.ProfileId", "Choose an available profile.");
        if (Input.DisplayName?.Any(char.IsControl) == true) ModelState.AddModelError("Input.DisplayName", "The name cannot contain control characters.");
        ValidateTopology();
        if (!ModelState.IsValid) return Page();
        try
        {
            var realm = await realms.CreateAsync(new(Input.RealmId, Input.DisplayName!.Trim(), Input.TargetId, Input.ProfileId,
                Topology()), ct);
            Notice = "Realm created. Check its configuration, then start deployment.";
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
            ModelState.AddModelError("Input.ProfileId", "Choose an available profile.");
        ValidateTopology();
        if (!ModelState.IsValid) return Page();
        try
        {
            await realms.UpdateTopologyAsync(EditRealmId!, ExpectedRevision.Value, Input.ProfileId, Topology(), ct);
            Notice = "Realm plan saved.";
            return RedirectToPage("/Realm", new { realmId = EditRealmId });
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error = RealmPresentation.ErrorMessage(ex); }
        return Page();
    }

    private RealmTopology? Topology() => Input.ConfigureTopology
        ? new(Input.Islands.Select(island => new RealmIslandTopology(island.IslandId!, island.DedicatedCount,
            island.Width, island.Depth, island.OriginX, island.OriginZ,
            island.UseGrid ? island.GridColumns : null, island.UseGrid ? island.GridRows : null)
            { ContentId = string.IsNullOrWhiteSpace(island.ContentId) ? null : island.ContentId }).ToArray(), Input.InstanceDedicatedCount, Input.RaidDedicatedCount)
        { AreaTransitions = Input.AreaTransitions.Count == 0 ? null : Input.AreaTransitions.Select(transition => transition.ToDefinition()).ToArray() }
        : null;

    public static (int Columns, int Rows)? PreviewGrid(IslandInput island)
    {
        if (!double.IsFinite(island.Width) || island.Width is < 1 or > 1_000_000 ||
            !double.IsFinite(island.Depth) || island.Depth is < 1 or > 1_000_000) return null;
        if (island.UseGrid)
            return island.GridColumns is >= 1 and <= 64 && island.GridRows is >= 1 and <= 64 &&
                (long)island.GridColumns.Value * island.GridRows.Value <= 64
                ? (island.GridColumns.Value, island.GridRows.Value) : null;
        return island.DedicatedCount is >= 1 and <= 64
            ? RealmIslandPresentation.Grid(new(island.IslandId ?? "", island.DedicatedCount, island.Width, island.Depth)) : null;
    }

    private void SynchronizeGridCounts()
    {
        for (var index = 0; index < Input.Islands.Count; index++)
        {
            var island = Input.Islands[index];
            if (!island.UseGrid || island.GridColumns is not (>= 1 and <= 64) || island.GridRows is not (>= 1 and <= 64)) continue;
            island.DedicatedCount = island.GridColumns.Value * island.GridRows.Value;
            ModelState.Remove($"Input.Islands[{index}].DedicatedCount");
        }
    }

    private static bool ValidRealmId(string? id) => id is not null && Regex.IsMatch(id,
        @"\A[a-z][a-z0-9-]{1,29}[a-z0-9]\z", RegexOptions.CultureInvariant);

    public async Task<IActionResult> OnPostAddIslandAsync(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        await LoadAsync(ct);
        if (Input.Islands.Count >= 32)
            ModelState.AddModelError("Input.Islands", "A realm can contain up to 32 Areas.");
        else Input.Islands.Add(new IslandInput { IslandId = "", DedicatedCount = 1, UseGrid = true, GridColumns = 1, GridRows = 1 });
        SynchronizeGridCounts();
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
        SynchronizeGridCounts();
        return Page();
    }

    public async Task<IActionResult> OnPostAddTransitionAsync(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        await LoadAsync(ct);
        if (Input.AreaTransitions.Count >= 64)
            ModelState.AddModelError("Input.AreaTransitions", "A realm can contain up to 64 portals.");
        else Input.AreaTransitions.Add(new());
        SynchronizeGridCounts();
        return Page();
    }

    public async Task<IActionResult> OnPostRemoveTransitionAsync(int removeTransitionIndex, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        await LoadAsync(ct);
        if (removeTransitionIndex < 0 || removeTransitionIndex >= Input.AreaTransitions.Count) return BadRequest();
        Input.AreaTransitions.RemoveAt(removeTransitionIndex);
        foreach (var key in ModelState.Keys.Where(key => key.StartsWith("Input.AreaTransitions[", StringComparison.Ordinal)).ToArray())
            ModelState.Remove(key);
        SynchronizeGridCounts();
        return Page();
    }

    private void ValidateTopology()
    {
        if (!Input.ConfigureTopology) return;
        SynchronizeGridCounts();
        if (Input.Islands.Count > 32)
            ModelState.AddModelError("Input.Islands", "A realm can contain up to 32 Areas.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;
        for (var index = 0; index < Input.Islands.Count; index++)
        {
            var island = Input.Islands[index];
            if (!Regex.IsMatch(island.IslandId ?? "", @"\A[a-z][a-z0-9-]{1,18}[a-z0-9]\z", RegexOptions.CultureInvariant))
                ModelState.AddModelError($"Input.Islands[{index}].IslandId", "The Area ID uses 3 to 20 characters: lowercase letters, digits and hyphens.");
            else if (!ids.Add(island.IslandId!))
                ModelState.AddModelError($"Input.Islands[{index}].IslandId", "Each Area must have a distinct ID.");
            if (!string.IsNullOrWhiteSpace(island.ContentId) && !GameProtocol.ValidId(island.ContentId))
                ModelState.AddModelError($"Input.Islands[{index}].ContentId", "The content ID uses 3 to 31 characters: lowercase letters, digits and hyphens.");
            if (island.UseGrid && (island.GridColumns is not (>= 1 and <= 64) || island.GridRows is not (>= 1 and <= 64)))
                ModelState.AddModelError($"Input.Islands[{index}].GridColumns", "Enter whole X columns and Y rows from 1 to 64.");
            else if (island.UseGrid && (long)island.GridColumns!.Value * island.GridRows!.Value > 64)
                ModelState.AddModelError($"Input.Islands[{index}].GridColumns", "The grid cannot exceed 64 dedicated servers per Area.");
            if (island.DedicatedCount is < 1 or > 64)
                ModelState.AddModelError($"Input.Islands[{index}].DedicatedCount", "Plan from 1 to 64 dedicated servers per Area.");
            if (!double.IsFinite(island.Width) || island.Width is < 1 or > 1_000_000 ||
                !double.IsFinite(island.Depth) || island.Depth is < 1 or > 1_000_000)
                ModelState.AddModelError($"Input.Islands[{index}].Width", "The Area dimensions must be finite and from 1 to 1,000,000 units.");
            if (!double.IsFinite(island.OriginX) || Math.Abs(island.OriginX) > 10_000_000 ||
                !double.IsFinite(island.OriginZ) || Math.Abs(island.OriginZ) > 10_000_000)
                ModelState.AddModelError($"Input.Islands[{index}].OriginX", "The origin must be finite and limited to ±10,000,000 units.");
            total += island.DedicatedCount;
        }
        if (Input.InstanceDedicatedCount is < 0 or > 64)
            ModelState.AddModelError("Input.InstanceDedicatedCount", "Plan from 0 to 64 instance servers.");
        if (Input.RaidDedicatedCount is < 0 or > 64)
            ModelState.AddModelError("Input.RaidDedicatedCount", "Plan from 0 to 64 raid servers.");
        total += (long)Input.InstanceDedicatedCount + Input.RaidDedicatedCount;
        if (total <= 0) ModelState.AddModelError("Input.Islands", "Plan at least one server for an Area, instance or raid.");
        if (total > 256) ModelState.AddModelError("Input.Islands", "A realm can plan up to 256 dedicated servers in total.");
        ValidateTransitions();
    }

    private void ValidateTransitions()
    {
        if (Input.AreaTransitions.Count > 64)
            ModelState.AddModelError("Input.AreaTransitions", "A realm can contain up to 64 portals.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Input.AreaTransitions.Count; index++)
        {
            var portal = Input.AreaTransitions[index];
            var prefix = $"Input.AreaTransitions[{index}]";
            if (!GameProtocol.ValidId(portal.Id) || !ids.Add(portal.Id!))
                ModelState.AddModelError(prefix + ".Id", "Each portal needs a unique ID of 3 to 31 lowercase letters, digits and hyphens.");
            var source = Input.Islands.FirstOrDefault(area => area.IslandId == portal.SourceAreaId);
            var target = Input.Islands.FirstOrDefault(area => area.IslandId == portal.TargetAreaId);
            if (source is null || target is null || portal.SourceAreaId == portal.TargetAreaId)
                ModelState.AddModelError(prefix + ".SourceAreaId", "Choose two different Areas from this plan as the source and target.");
            var definition = portal.ToDefinition();
            var bounds = definition.SourceBounds;
            if (!GameTopology.ValidBounds(bounds) || source is not null &&
                (bounds.MinX < source.OriginX || bounds.MinZ < source.OriginZ ||
                 bounds.MaxX > source.OriginX + source.Width || bounds.MaxZ > source.OriginZ + source.Depth))
                ModelState.AddModelError(prefix + ".SourceMinX", "The trigger must be a finite, positive rectangle entirely inside its source Area.");
            var position = definition.TargetPosition;
            if (!GameTopology.ValidPosition(position) || target is not null &&
                !GameTopology.Contains(new(target.OriginX, target.OriginZ, target.OriginX + target.Width, target.OriginZ + target.Depth), position.X, position.Z))
                ModelState.AddModelError(prefix + ".TargetX", "The arrival position must be finite, within ±100,000 units and inside the target Area on X/Z.");
            if (!GameTopology.ValidForward(definition.TargetForward))
                ModelState.AddModelError(prefix + ".ForwardX", "The arrival direction must be finite, non-zero and have a squared length from 0.0001 to 1.01.");
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        try
        {
            Options = await realms.OptionsAsync(ct);
            Loaded = true;
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error ??= RealmPresentation.ErrorMessage(ex); }
    }

    public sealed class IslandInput
    {
        public string? IslandId { get; set; } = "starter-island";
        public string? ContentId { get; set; }
        public int DedicatedCount { get; set; } = 4;
        public bool UseGrid { get; set; }
        public int? GridColumns { get; set; }
        public int? GridRows { get; set; }
        public double Width { get; set; } = 1000;
        public double Depth { get; set; } = 1000;
        public double OriginX { get; set; }
        public double OriginZ { get; set; }
    }

    public sealed class TransitionInput
    {
        public string? Id { get; set; }
        public string? SourceAreaId { get; set; }
        public string? TargetAreaId { get; set; }
        public double SourceMinX { get; set; }
        public double SourceMinZ { get; set; }
        public double SourceMaxX { get; set; }
        public double SourceMaxZ { get; set; }
        public float TargetX { get; set; }
        public float TargetY { get; set; }
        public float TargetZ { get; set; }
        public bool UseTargetForward { get; set; }
        public float ForwardX { get; set; }
        public float ForwardZ { get; set; } = 1;

        public GameAreaTransition ToDefinition() => new(Id!, SourceAreaId!, TargetAreaId!,
            new(SourceMinX, SourceMinZ, SourceMaxX, SourceMaxZ), new(TargetX, TargetY, TargetZ),
            UseTargetForward ? new GameAreaForward(ForwardX, ForwardZ) : null);
        public static TransitionInput From(GameAreaTransition value) => new()
        {
            Id = value.Id, SourceAreaId = value.SourceAreaId, TargetAreaId = value.TargetAreaId,
            SourceMinX = value.SourceBounds.MinX, SourceMinZ = value.SourceBounds.MinZ,
            SourceMaxX = value.SourceBounds.MaxX, SourceMaxZ = value.SourceBounds.MaxZ,
            TargetX = value.TargetPosition.X, TargetY = value.TargetPosition.Y, TargetZ = value.TargetPosition.Z,
            UseTargetForward = value.TargetForward is not null,
            ForwardX = value.TargetForward?.X ?? 0, ForwardZ = value.TargetForward?.Z ?? 1
        };
    }

    public sealed class RealmInput
    {
        [Required(ErrorMessage = "Enter an ID."), StringLength(31, MinimumLength = 3),
         RegularExpression("[a-z][a-z0-9-]{1,29}[a-z0-9]", ErrorMessage = "The ID starts with a lowercase letter, uses lowercase letters, digits or hyphens and ends with a letter or digit.")]
        public string RealmId { get; set; } = "";
        [Required(ErrorMessage = "Enter a name."), StringLength(80)] public string DisplayName { get; set; } = "";
        [Required(ErrorMessage = "Choose a target."), StringLength(64)] public string TargetId { get; set; } = "";
        [Required(ErrorMessage = "Choose a profile."), StringLength(64)] public string ProfileId { get; set; } = "";
        public bool ConfigureTopology { get; set; }
        public List<IslandInput> Islands { get; set; } = [];
        public List<TransitionInput> AreaTransitions { get; set; } = [];
        public int InstanceDedicatedCount { get; set; }
        public int RaidDedicatedCount { get; set; }
    }
}
