using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Arkya.Admin.Inference;
using Arkya.ServiceDefaults.Inference;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class InferenceModel(InferenceAdminClient inference) : PageModel
{
    [BindProperty] public AgentInput Input { get; set; } = new();
    public InferenceAgentSummary[] Agents { get; private set; } = [];
    public CreateInferenceAgentResponse? Created { get; private set; }
    public InferenceEnrollmentGrantResponse? InstallationGrant { get; private set; }
    public InferenceModelOption[] Models { get; private set; } = [];
    public bool ModelsLoaded { get; private set; }
    public bool Enabled => inference.Enabled;
    public bool Loaded { get; private set; }
    public string? Error { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostPrepareInstallationAsync(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        ClearCreateInput();
        try { InstallationGrant = await inference.PrepareInstallationAsync(ct); }
        catch (Exception ex) when (InferencePresentation.IsDependencyError(ex, ct))
        {
            Error = InferencePresentation.ErrorMessage(ex);
        }
        await LoadAsync(ct);
        // Credentials are rendered only in the issuing POST, never in TempData.
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (Input.DisplayName?.Any(char.IsControl) == true)
            ModelState.AddModelError("Input.DisplayName", "The name cannot contain control characters.");
        if (Input.InitialWorkers > Input.MaxWorkers)
            ModelState.AddModelError("Input.InitialWorkers", "The initial count cannot exceed the worker limit.");
        if ((long)Input.InitialWorkers * Input.ThreadsPerWorker > 96)
            ModelState.AddModelError("Input.ThreadsPerWorker", "Workers cannot request more than 96 threads in total.");
        await LoadModelsAsync(ct);
        if (ModelsLoaded && !ValidModel(Input.ModelId))
            ModelState.AddModelError("Input.ModelId", "Choose a model from the available catalogue.");
        if (ModelState.IsValid && ModelsLoaded)
        {
            try
            {
                Created = await inference.CreateAsync(new(Input.AgentId, Input.DisplayName!.Trim(),
                    Input.InitialWorkers, Input.ThreadsPerWorker, Input.MaxWorkers, Input.ModelId), ct);
                Input = new();
                ModelState.Clear();
            }
            catch (Exception ex) when (InferencePresentation.IsDependencyError(ex, ct))
            {
                Error = InferencePresentation.ErrorMessage(ex);
            }
        }
        await LoadAsync(ct);
        // The enrollment credential stays only in this POST response.
        return Page();
    }

    public async Task<IActionResult> OnPostAddWorkerAsync(string? agentId, int? threads, string? modelId, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        ClearCreateInput();
        if (!ValidAgentId(agentId) || threads is not (>= 1 and <= 24)) return BadRequest();
        await LoadModelsAsync(ct);
        if (!ModelsLoaded) { await LoadAsync(ct); return Page(); }
        if (!ValidModel(modelId)) return BadRequest();
        try
        {
            await inference.AddWorkerAsync(agentId!, threads.Value, ct, modelId!);
            Notice = "Worker requested. The agent will start it on its next check-in.";
            return RedirectToPage();
        }
        catch (Exception ex) when (InferencePresentation.IsDependencyError(ex, ct))
        {
            Error = InferencePresentation.ErrorMessage(ex);
            await LoadAsync(ct);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostUpdateWorkerAsync(string? agentId, string? workerId, int? threads,
        string? modelId, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        ClearCreateInput();
        if (!ValidAgentId(agentId) || !ValidWorkerId(workerId) || threads is not (>= 1 and <= 24)) return BadRequest();
        await LoadModelsAsync(ct);
        if (!ModelsLoaded) { await LoadAsync(ct); return Page(); }
        if (!ValidModel(modelId)) return BadRequest();
        try
        {
            await inference.UpdateWorkerAsync(agentId!, workerId!, threads.Value, modelId!, ct);
            Notice = "Worker configuration saved. The agent will apply the model and threads on its next check-in; a change restarts this worker.";
            return RedirectToPage();
        }
        catch (Exception ex) when (InferencePresentation.IsDependencyError(ex, ct))
        {
            Error = InferencePresentation.ErrorMessage(ex);
            await LoadAsync(ct);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteWorkerAsync(string? agentId, string? workerId, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        ClearCreateInput();
        if (!ValidAgentId(agentId) || !ValidWorkerId(workerId)) return BadRequest();
        try
        {
            await inference.DeleteWorkerAsync(agentId!, workerId!, ct);
            Notice = "Worker deletion requested. The agent will stop it on its next check-in.";
            return RedirectToPage();
        }
        catch (Exception ex) when (InferencePresentation.IsDependencyError(ex, ct))
        {
            Error = InferencePresentation.ErrorMessage(ex);
            await LoadAsync(ct);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostDeleteAgentAsync(string? agentId, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        ClearCreateInput();
        if (!ValidAgentId(agentId)) return BadRequest();
        try
        {
            await inference.DeleteAgentAsync(agentId!, ct);
            Notice = "Machine deleted. Its agent token is no longer usable.";
            return RedirectToPage();
        }
        catch (Exception ex) when (InferencePresentation.IsDependencyError(ex, ct))
        {
            Error = InferencePresentation.ErrorMessage(ex);
            await LoadAsync(ct);
            return Page();
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        try
        {
            Agents = (await inference.ListAsync(ct)).OrderBy(agent => agent.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
            Loaded = true;
        }
        catch (Exception ex) when (InferencePresentation.IsDependencyError(ex, ct))
        {
            Error ??= InferencePresentation.ErrorMessage(ex);
        }
        if (!ModelsLoaded) await LoadModelsAsync(ct);
    }

    private async Task LoadModelsAsync(CancellationToken ct)
    {
        try
        {
            var models = await inference.ModelsAsync(ct);
            if (models.Length == 0 || models.Any(model => model is null || string.IsNullOrWhiteSpace(model.ModelId) ||
                    string.IsNullOrWhiteSpace(model.Label)) || models.Select(model => model.ModelId).Distinct(StringComparer.Ordinal).Count() != models.Length)
                throw new InferenceApiException(System.Net.HttpStatusCode.BadGateway, "The model catalogue is unavailable. Configuration changes are disabled.");
            Models = models;
            ModelsLoaded = true;
        }
        catch (Exception ex) when (InferencePresentation.IsDependencyError(ex, ct))
        {
            Error ??= "The model catalogue is unavailable. Configuration changes are disabled.";
        }
    }

    private bool ValidModel(string? modelId) => ModelsLoaded && Models.Any(model => model.ModelId == modelId);

    public string ModelLabel(string modelId) => Models.FirstOrDefault(model => model.ModelId == modelId)?.Label ?? modelId;

    private void ClearCreateInput()
    {
        // These handlers validate their own parameters, not the separate creation form.
        ModelState.Clear();
        Input = new();
    }

    private static bool ValidAgentId(string? id) => id is { Length: >= 3 and <= 48 } &&
        Regex.IsMatch(id, "\\A[a-z][a-z0-9-]*[a-z0-9]\\z", RegexOptions.CultureInvariant);

    private static bool ValidWorkerId(string? id) => id is { Length: >= 1 and <= 64 } &&
        Regex.IsMatch(id, "\\A[a-z0-9][a-z0-9-]*\\z", RegexOptions.CultureInvariant);

    public sealed class AgentInput
    {
        [Required(ErrorMessage = "Enter an ID."), StringLength(48, MinimumLength = 3),
         RegularExpression("[a-z][a-z0-9-]*[a-z0-9]", ErrorMessage = "Use lowercase letters, digits and hyphens, starting with a letter and ending without a hyphen.")]
        public string AgentId { get; set; } = "";

        [Required(ErrorMessage = "Enter a name."), StringLength(80)]
        public string DisplayName { get; set; } = "";

        [Range(0, 12, ErrorMessage = "Choose from 0 to 12 initial workers.")]
        public int InitialWorkers { get; set; } = 3;

        [Range(1, 24, ErrorMessage = "Choose from 1 to 24 threads per worker.")]
        public int ThreadsPerWorker { get; set; } = 4;

        [Range(1, 12, ErrorMessage = "Choose a limit from 1 to 12 workers.")]
        public int MaxWorkers { get; set; } = 6;

        [Required(ErrorMessage = "Choose a model."), StringLength(64)]
        public string ModelId { get; set; } = InferenceModels.DefaultModelId;
    }
}
