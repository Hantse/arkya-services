using System.Net.Http.Headers;
using Arkya.Identity.Api.Application;
using Arkya.ServiceDefaults.Accounts;
using Microsoft.Extensions.Options;

namespace Arkya.Identity.Api.Endpoints;

public static class BetaAccessEndpoints
{
    public static void MapBetaAccessEndpoints(this WebApplication app)
    {
        var self = app.MapGroup("/v1/beta").RequireRateLimiting("auth").RequireCors(WebClientOptions.CorsPolicyName);
        self.AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.ContentLength > 1024) return Results.StatusCode(413);
            var token = context.Request.Headers.Authorization.Count == 1 &&
                AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var header) &&
                header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ? header.Parameter : null;
            var session = await context.RequestServices.GetRequiredService<IdentityService>().ValidateSessionAsync(token, context.RequestAborted);
            if (session is null) return Results.Unauthorized();
            context.Items["BetaAccountId"] = session.AccountId;
            return await next(invocation);
        });
        self.MapGet("/registration", async (HttpContext context, BetaAccessService service, CancellationToken ct) =>
        {
            var result = await service.GetAsync((string)context.Items["BetaAccountId"]!, ct);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        });
        self.MapPost("/registration", async (BetaRegistrationRequest request, HttpContext context, BetaAccessService service, CancellationToken ct) =>
        {
            var result = await service.RegisterAsync((string)context.Items["BetaAccountId"]!, ct);
            return result is null ? Results.Unauthorized() : Results.Ok(result);
        });

        var admin = app.MapGroup("/admin/beta").RequireRateLimiting("auth");
        admin.AddEndpointFilter(async (invocation, next) =>
        {
            var context = invocation.HttpContext;
            context.Response.Headers.CacheControl = "no-store";
            if (!context.RequestServices.GetRequiredService<IOptions<AdministrationOptions>>().Value.Enabled) return Results.NotFound();
            var keys = context.Request.Headers[AccountAdminKeyValidator.HeaderName];
            if (keys.Count != 1 || !context.RequestServices.GetRequiredService<AccountAdminKeyValidator>().IsValid(keys[0])) return Results.StatusCode(403);
            if (context.Request.ContentLength > 8192) return Results.StatusCode(413);
            return await next(invocation);
        });
        admin.MapGet("/registrations", async (string? search, string? status, int? page, int? pageSize, BetaAccessService service, CancellationToken ct) =>
        {
            if (search?.Length > 100 || search?.Any(char.IsControl) == true || page is < 1 or > 100_000 ||
                pageSize is < 1 or > 100 || status is not (null or "all" or "pending" or "granted" or "banned"))
                return Results.BadRequest(new { code = "invalid_beta_query" });
            return Results.Ok(await service.ListAsync(search, status ?? "all", page ?? 1, pageSize ?? 25, ct));
        });
        admin.MapPost("/registrations/{accountId:guid}/grant", async (Guid accountId, BetaGrantRequest request, HttpContext context,
            BetaAccessService service, CancellationToken ct) =>
        {
            if (request.ExpectedRevision is < 0 or long.MaxValue || !ValidChange(context, request.Reason, out var actor))
                return Results.BadRequest(new { code = "invalid_beta_grant" });
            var result = await service.GrantAsync(accountId.ToString("D"), request.ExpectedRevision, request.Reason, actor!, ct);
            return result.Status switch
            {
                "updated" => Results.Ok(result.Registration),
                "not_found" => Results.NotFound(new { code = "beta_registration_not_found" }),
                _ => Results.Conflict(new { code = "beta_registration_conflict" })
            };
        });
        admin.MapPost("/grants/random", async (BetaRandomGrantRequest request, HttpContext context, BetaAccessService service, CancellationToken ct) =>
        {
            if (request.RequestId == Guid.Empty || request.Count is < 1 or > 100 || !ValidChange(context, request.Reason, out var actor))
                return Results.BadRequest(new { code = "invalid_beta_random_grant" });
            var result = await service.GrantRandomAsync(request, actor!, ct);
            return result is null ? Results.Conflict(new { code = "beta_random_request_conflict" }) : Results.Ok(result);
        });
    }

    private static bool ValidChange(HttpContext context, string reason, out string? actor)
    {
        var actors = context.Request.Headers[AccountAdminKeyValidator.ActorHeaderName];
        actor = actors.Count == 1 ? actors[0] : null;
        return AccountAdministration.ValidActor(actor) && AccountAdministration.ValidReason(reason);
    }
}
