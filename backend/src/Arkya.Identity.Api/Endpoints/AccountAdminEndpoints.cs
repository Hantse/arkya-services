using Arkya.Identity.Api.Application;
using Arkya.ServiceDefaults.Accounts;
using Microsoft.Extensions.Options;

namespace Arkya.Identity.Api.Endpoints;

public static class AccountAdminEndpoints
{
    public static void MapAccountAdminEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/admin/accounts").RequireRateLimiting("auth");
        group.AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            if (!context.RequestServices.GetRequiredService<IOptions<AdministrationOptions>>().Value.Enabled)
                return Results.NotFound();
            var keys = context.Request.Headers[AccountAdminKeyValidator.HeaderName];
            if (keys.Count != 1 || !context.RequestServices.GetRequiredService<AccountAdminKeyValidator>().IsValid(keys[0]))
                return Results.StatusCode(403);
            if (context.Request.ContentLength > 8192) return Results.StatusCode(413);
            return await next(invocation);
        });
        group.MapGet("", async (string? search, int? page, int? pageSize, AccountAdministration service, CancellationToken ct) =>
        {
            if (search?.Length > 100 || search?.Any(char.IsControl) == true || page is < 1 or > 100_000 || pageSize is < 1 or > 100)
                return Results.BadRequest(new { code = "invalid_account_query" });
            return Results.Ok(await service.ListAsync(search, page ?? 1, pageSize ?? 25, ct));
        });
        group.MapGet("/{accountId:guid}", async (Guid accountId, AccountAdministration service, CancellationToken ct) =>
        {
            var result = await service.GetAsync(accountId.ToString("D"), ct);
            return result is null ? Results.NotFound(new { code = "account_not_found" }) : Results.Ok(result);
        });
        group.MapPost("/{accountId:guid}/ban", (Guid accountId, AccountBanRequest request, HttpContext context,
            AccountAdministration service, CancellationToken ct) =>
            ChangeAsync(accountId, request.ExpectedRevision, "ban", null, request.Reason, context, service, ct));
        group.MapPost("/{accountId:guid}/unban", (Guid accountId, AccountUnbanRequest request, HttpContext context,
            AccountAdministration service, CancellationToken ct) =>
            ChangeAsync(accountId, request.ExpectedRevision, "unban", null, request.Reason, context, service, ct));
        group.MapPost("/{accountId:guid}/beta-access", (Guid accountId, AccountBetaAccessRequest request, HttpContext context,
            AccountAdministration service, CancellationToken ct) =>
            ChangeAsync(accountId, request.ExpectedRevision, "beta-access", request.HasBetaAccess, request.Reason, context, service, ct));
    }

    private static async Task<IResult> ChangeAsync(Guid accountId, long expectedRevision, string action, bool? hasBetaAccess,
        string reason, HttpContext context, AccountAdministration service, CancellationToken ct)
    {
        var actors = context.Request.Headers[AccountAdminKeyValidator.ActorHeaderName];
        if (expectedRevision < 0 || expectedRevision == long.MaxValue || !AccountAdministration.ValidReason(reason) ||
            actors.Count != 1 || !AccountAdministration.ValidActor(actors[0]))
            return Results.BadRequest(new { code = "invalid_account_change", message = "Revision, reason (1–500 characters) and administrator identity are required." });
        var result = await service.ChangeAsync(accountId.ToString("D"), expectedRevision, action, hasBetaAccess, reason, actors[0]!, ct);
        return result.Status switch
        {
            "updated" => Results.Ok(result.Account),
            "not_found" => Results.NotFound(new { code = "account_not_found" }),
            _ => Results.Conflict(new { code = "account_revision_conflict" })
        };
    }
}
