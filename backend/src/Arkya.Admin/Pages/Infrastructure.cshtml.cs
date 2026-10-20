using Arkya.Admin.Realms;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class InfrastructureModel(RealmsAdminClient realms) : PageModel
{
    public bool Enabled => realms.Enabled;
    public bool Loaded { get; private set; }
    public string? Error { get; private set; }
    public RealmDeploymentOptions Options { get; private set; } = new([], [], false);
    public RealmSummary[] Realms { get; private set; } = [];
    public async Task OnGetAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        try
        {
            var options = realms.OptionsAsync(ct);
            var definitions = realms.ListAsync(ct);
            await Task.WhenAll(options, definitions);
            Options = await options;
            Realms = await definitions;
            Loaded = true;
        }
        catch (Exception ex) when (RealmPresentation.IsDependencyError(ex, ct)) { Error = RealmPresentation.ErrorMessage(ex); }
    }
}
