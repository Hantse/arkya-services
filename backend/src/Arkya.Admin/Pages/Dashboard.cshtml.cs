using Arkya.Admin.Operations;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Arkya.Admin.Pages;

public sealed class DashboardModel(OperationsDashboard dashboard) : PageModel
{
    public DashboardSnapshot Snapshot { get; private set; } = new(default, [], null, null, null);
    public async Task OnGetAsync(CancellationToken ct) => Snapshot = await dashboard.ReadAsync(ct);
}
