using System.Text;
using Arkya.Admin.Reports;
using Arkya.Admin.Content;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Pages;

public sealed class ReportsConnectionModel(IOptions<AdminMcpOptions> mcp, IOptions<ReportsApiOptions> reports,
    IOptions<ContentApiOptions> content) : PageModel
{
    public const string McpUrl = "https://admin.arkya.online" + HostedReportsMcp.EndpointPath;
    public bool Enabled => mcp.Value.Enabled && (reports.Value.Enabled || content.Value.Enabled);
    public bool PublicationsEnabled => content.Value.Enabled;
    public bool ReportsEnabled => reports.Value.Enabled;

    public IActionResult OnPostDownload()
    {
        if (!Enabled) return NotFound();
        // The cookie and antiforgery token authorize this explicit export, never a GET.
        var authorization = ("Bearer " + mcp.Value.Key).Replace("\\", "\\\\").Replace("\"", "\\\"");
        var configuration = $"[mcp_servers.arkya_admin]\nurl = \"{McpUrl}\"\n\n" +
            $"[mcp_servers.arkya_admin.http_headers]\nAuthorization = \"{authorization}\"\n";
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return File(Encoding.UTF8.GetBytes(configuration), "application/toml", "arkya-admin-mcp.local.toml");
    }
}
