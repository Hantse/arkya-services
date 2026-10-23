using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using Arkya.Admin.Reports;
using Arkya.ServiceDefaults.Reports;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class BugReportModel(ReportsAdminClient reports) : PageModel
{
    [BindProperty, BindRequired, Range(1, long.MaxValue)] public long ExpectedRevision { get; set; }
    [BindProperty] public ReportInput Input { get; set; } = new();
    public BugReportResponse? Report { get; private set; }
    public string? Error { get; private set; }
    [TempData] public string? Notice { get; set; }

    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        if (!reports.Enabled || id == Guid.Empty) return NotFound();
        return await LoadAsync(id, true, ct);
    }

    public async Task<IActionResult> OnGetScreenshotAsync(Guid id, CancellationToken ct)
    {
        if (!reports.Enabled || id == Guid.Empty) return NotFound();
        try { return File(await reports.ScreenshotAsync(id, ct), "image/png"); }
        catch (ReportsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
        catch (Exception ex) when (BugReportPresentation.IsDependencyError(ex, ct)) { return StatusCode(StatusCodes.Status502BadGateway); }
    }

    public async Task<IActionResult> OnPostSaveAsync(Guid id, CancellationToken ct)
    {
        if (!reports.Enabled || id == Guid.Empty) return NotFound();
        if (!ModelState.IsValid && ModelState.TryGetValue(nameof(ExpectedRevision), out var revision) && revision.Errors.Count > 0)
            return BadRequest();
        if (!BugReportStatuses.All.Contains(Input.Status)) ModelState.AddModelError("Input.Status", "Choose an available status.");
        var tags = (Input.Tags ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray();
        if (tags.Length > 10 || tags.Any(tag => !BugReportsModel.ValidTag(tag)))
            ModelState.AddModelError("Input.Tags", "Use up to 10 comma-separated tags of 1 to 32 lowercase letters, digits, hyphens or underscores.");
        if (!ModelState.IsValid) return await LoadAsync(id, false, ct);
        try
        {
            var discordId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var actor = discordId is null ? "admin:operator" : "admin:discord:" + discordId;
            await reports.UpdateAsync(id, new(ExpectedRevision, Input.Status, tags, Input.Resolution ?? ""), actor, ct);
            Notice = "Report updated.";
            return RedirectToPage(new { id });
        }
        catch (ReportsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
        catch (Exception ex) when (BugReportPresentation.IsDependencyError(ex, ct))
        {
            Error = BugReportPresentation.ErrorMessage(ex);
            return await LoadAsync(id, ex is ReportsApiException { StatusCode: HttpStatusCode.Conflict }, ct);
        }
    }

    private async Task<IActionResult> LoadAsync(Guid id, bool resetInput, CancellationToken ct)
    {
        try
        {
            Report = await reports.GetAsync(id, ct);
            if (resetInput)
            {
                ExpectedRevision = Report.Revision;
                Input = new() { Status = Report.Status, Tags = string.Join(", ", Report.Tags), Resolution = Report.Resolution };
                // Reload the whole current triage state after a conflict before permitting another update.
                ModelState.Clear();
            }
        }
        catch (ReportsApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return NotFound(); }
        catch (Exception ex) when (BugReportPresentation.IsDependencyError(ex, ct)) { Error ??= BugReportPresentation.ErrorMessage(ex); }
        return Page();
    }

    public sealed class ReportInput
    {
        [Required, StringLength(32)] public string Status { get; set; } = BugReportStatuses.New;
        [StringLength(1024)] public string? Tags { get; set; } = "";
        [StringLength(4000)] public string? Resolution { get; set; } = "";
    }
}
