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
    public bool Enabled => inference.Enabled;
    public bool Loaded { get; private set; }
    public string? Error { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task OnGetAsync(CancellationToken ct) => await LoadAsync(ct);

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        if (Input.DisplayName?.Any(char.IsControl) == true)
            ModelState.AddModelError("Input.DisplayName", "Le nom ne peut pas contenir de caractères de contrôle.");
        if (Input.InitialWorkers > Input.MaxWorkers)
            ModelState.AddModelError("Input.InitialWorkers", "Le nombre initial ne peut pas dépasser le maximum de workers.");
        if ((long)Input.InitialWorkers * Input.ThreadsPerWorker > 96)
            ModelState.AddModelError("Input.ThreadsPerWorker", "Les workers ne peuvent pas demander plus de 96 threads au total.");
        if (ModelState.IsValid)
        {
            try
            {
                Created = await inference.CreateAsync(new(Input.AgentId, Input.DisplayName!.Trim(),
                    Input.InitialWorkers, Input.ThreadsPerWorker, Input.MaxWorkers), ct);
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

    public async Task<IActionResult> OnPostAddWorkerAsync(string? agentId, int? threads, CancellationToken ct)
    {
        if (!Enabled) return NotFound();
        ClearCreateInput();
        if (!ValidAgentId(agentId) || threads is not (>= 1 and <= 24)) return BadRequest();
        try
        {
            await inference.AddWorkerAsync(agentId!, threads.Value, ct);
            Notice = "Worker demandé. L’agent le démarrera à son prochain contact.";
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
            Notice = "Suppression du worker demandée. L’agent l’arrêtera à son prochain contact.";
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
            Notice = "Machine supprimée. Son jeton d’agent n’est plus utilisable.";
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
    }

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
        [Required(ErrorMessage = "Renseignez un identifiant."), StringLength(48, MinimumLength = 3),
         RegularExpression("[a-z][a-z0-9-]*[a-z0-9]", ErrorMessage = "Utilisez des minuscules, chiffres et tirets, avec une lettre au début et aucun tiret à la fin.")]
        public string AgentId { get; set; } = "";

        [Required(ErrorMessage = "Renseignez un nom."), StringLength(80)]
        public string DisplayName { get; set; } = "";

        [Range(0, 12, ErrorMessage = "Choisissez entre 0 et 12 workers initiaux.")]
        public int InitialWorkers { get; set; } = 3;

        [Range(1, 24, ErrorMessage = "Choisissez entre 1 et 24 threads par worker.")]
        public int ThreadsPerWorker { get; set; } = 4;

        [Range(1, 12, ErrorMessage = "Choisissez un maximum entre 1 et 12 workers.")]
        public int MaxWorkers { get; set; } = 6;
    }
}
