using System.Globalization;
using System.Security.Claims;
using Arkya.Reports.Api.Application;
using Arkya.Reports.Api.Infrastructure;
using Arkya.ServiceDefaults;
using Arkya.ServiceDefaults.Reports;
using Microsoft.Extensions.Options;

namespace Arkya.Reports.Api.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this WebApplication app)
    {
        var player = app.MapGroup("/v1/bug-reports").RequireAuthorization().RequireCors(BrowserClientOptions.Policy);
        player.MapPost("", async (SubmitBugReportRequest request, HttpContext context, ReportService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SubmitAsync(Account(context), request, cancellationToken);
            return Change(result, player: true);
        });
        player.MapGet("", async (HttpContext context, IReportRepository repository, CancellationToken cancellationToken) =>
        {
            if (!Query(context, false, out var tag, out var status, out var kind, out var offset, out var limit))
                return InvalidQuery();
            var result = await repository.ListAsync(Account(context), tag, status, offset, limit, cancellationToken, kind);
            return Results.Ok(result with { Items = result.Items.Select(ForPlayer).ToArray() });
        });
        player.MapGet("/{id:guid}", async (Guid id, HttpContext context, IReportRepository repository, CancellationToken cancellationToken) =>
        {
            var report = await repository.FindAsync(id, Account(context), cancellationToken);
            return report is null ? Results.NotFound() : Results.Ok(ForPlayer(report));
        });

        var admin = app.MapGroup("/internal/bug-reports").AllowAnonymous();
        admin.MapGet("", async (HttpContext context, IReportRepository repository, CancellationToken cancellationToken) =>
        {
            if (!Query(context, true, out var tag, out var status, out var kind, out var offset, out var limit))
                return InvalidQuery();
            return Results.Ok(await repository.ListAsync(null, tag, status, offset, limit, cancellationToken, kind));
        });
        admin.MapGet("/{id:guid}", async (Guid id, IReportRepository repository, CancellationToken cancellationToken) =>
        {
            var report = await repository.FindAsync(id, null, cancellationToken);
            return report is null ? Results.NotFound() : Results.Ok(report);
        });
        admin.MapGet("/{id:guid}/screenshot", async (Guid id, IReportRepository repository, CancellationToken cancellationToken) =>
        {
            var image = await repository.ScreenshotAsync(id, cancellationToken);
            return image is null ? Results.NotFound() : Results.Bytes(image, "image/png", enableRangeProcessing: false);
        });
        admin.MapPatch("/{id:guid}", async (Guid id, UpdateBugReportRequest request, HttpContext context,
            ReportService service, IOptions<ReportsAdminOptions> options, CancellationToken cancellationToken) =>
        {
            var values = context.Request.Headers[ReportsAdminKeyValidator.ActorHeaderName];
            if (values.Count > 1) return Results.BadRequest();
            var actor = values.Count == 1 ? values[0] : options.Value.Actor;
            return Change(await service.UpdateAsync(id, request, actor ?? "", cancellationToken), player: false);
        });
    }

    private static Guid Account(HttpContext context) => Guid.Parse(context.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static BugReportResponse ForPlayer(BugReportResponse report) => report with { History = [] };

    private static IResult Change(ReportChangeResult result, bool player)
    {
        var report = result.Report is null ? null : player ? ForPlayer(result.Report) : result.Report;
        return result.Status switch
        {
            ReportChangeStatus.Created => Results.Created($"/v1/bug-reports/{report!.Id:D}", report),
            ReportChangeStatus.Existing or ReportChangeStatus.Updated => Results.Ok(report),
            ReportChangeStatus.Invalid => Results.ValidationProblem(result.Errors!),
            ReportChangeStatus.NotFound => Results.NotFound(),
            ReportChangeStatus.TooLarge => Results.Problem(statusCode: 413, title: "The screenshot exceeds 2 MiB."),
            _ => Results.Problem(statusCode: 409, title: player
                ? "This client report ID was already submitted with different content."
                : "The report changed. Reload before saving.")
        };
    }

    private static bool Query(HttpContext context, bool admin, out string? tag, out string? status, out string? kind,
        out int offset, out int limit)
    {
        tag = null;
        status = null;
        kind = null;
        offset = 0;
        limit = 20;
        foreach (var (key, values) in context.Request.Query)
        {
            if (values.Count != 1) return false;
            switch (key)
            {
                case "offset":
                    if (!int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out offset) || offset is < 0 or > 100000)
                        return false;
                    break;
                case "limit":
                    if (!int.TryParse(values[0], NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100)
                        return false;
                    break;
                case "tag" when admin:
                    tag = values[0]?.Trim().ToLowerInvariant();
                    if (!ReportService.ValidTag(tag)) return false;
                    break;
                case "status" when admin:
                    status = values[0];
                    if (!BugReportStatuses.All.Contains(status)) return false;
                    break;
                case "kind":
                    kind = values[0];
                    if (!BugReportKinds.All.Contains(kind)) return false;
                    break;
                default: return false;
            }
        }
        return true;
    }

    private static IResult InvalidQuery() => Results.ValidationProblem(new Dictionary<string, string[]>
    {
        ["query"] = ["Use offset 0–100000 and limit 1–100, optional kind bug/suggestion, and valid admin tag/status filters."]
    });
}
