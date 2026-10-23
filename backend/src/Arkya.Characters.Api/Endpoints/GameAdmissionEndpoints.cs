using System.Globalization;
using System.Security.Claims;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.ServiceDefaults;

namespace Arkya.Characters.Api.Endpoints;

public static class GameAdmissionEndpoints
{
    public static void MapGameAdmissionEndpoints(this WebApplication app)
    {
        app.MapGet("/v1/realms", async (GameAdmissionService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.CatalogAsync(cancellationToken))).RequireAuthorization().RequireCors(BrowserClientOptions.Policy);
        app.MapPost("/v1/characters/{characterId:guid}/admissions", async (Guid characterId, AdmissionRequest request,
            ClaimsPrincipal user, GameAdmissionService service, CancellationToken cancellationToken) =>
        {
            var accountId = Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var expiry = DateTimeOffset.Parse(user.FindFirstValue("arkya:session-expires-at")!, CultureInfo.InvariantCulture);
            var reference = user.FindFirstValue(SessionReference.ClaimType)!;
            return Response(await service.IssueAsync(characterId, accountId, expiry, reference, request, cancellationToken));
        }).RequireAuthorization().RequireCors(BrowserClientOptions.Policy);

        var internalApi = app.MapGroup("/internal/game").AllowAnonymous();
        internalApi.AddEndpointFilter(async (context, next) =>
        {
            var values = context.HttpContext.Request.Headers["X-Arkya-Dedicated-Key"];
            var server = values.Count == 1 ? context.HttpContext.RequestServices.GetRequiredService<DedicatedKeyValidator>().Resolve(values[0]) : null;
            if (server is null) return Results.Json(new { code = "dedicated_forbidden" }, statusCode: 403);
            context.HttpContext.Items[typeof(DedicatedServerIdentity)] = server;
            return await next(context);
        });
        internalApi.MapPost("/admissions/consume", async (ConsumeAdmissionRequest request, HttpContext context,
            GameAdmissionService service, CancellationToken cancellationToken) =>
            Response(await service.ConsumeAsync(Server(context), request, cancellationToken)));
        internalApi.MapGet("/topology", async (HttpContext context, GameAdmissionService service, CancellationToken cancellationToken) =>
            Response(await service.TopologyAsync(Server(context), cancellationToken)));
        internalApi.MapPost("/leases/{characterId:guid}/transfers/prepare", async (Guid characterId, PrepareTransferRequest request,
            HttpContext context, GameAdmissionService service, CancellationToken cancellationToken) =>
            Response(await service.PrepareTransferAsync(characterId, Server(context), request, cancellationToken)));
        internalApi.MapPost("/transfers/claim", async (ClaimTransferRequest request, HttpContext context,
            GameAdmissionService service, CancellationToken cancellationToken) =>
            Response(await service.ClaimTransferAsync(Server(context), request, cancellationToken)));
        internalApi.MapGet("/leases/{characterId:guid}/transfers/{transferId:guid}", async (Guid characterId, Guid transferId,
            HttpContext context, GameAdmissionService service, CancellationToken cancellationToken) =>
            Response(await service.TransferStatusAsync(characterId, transferId, Server(context), cancellationToken)));
        internalApi.MapPost("/leases/{characterId:guid}/transfers/{transferId:guid}/seal", async (Guid characterId, Guid transferId,
            SealTransferRequest request, HttpContext context, GameAdmissionService service, CancellationToken cancellationToken) =>
            Response(await service.SealTransferAsync(characterId, transferId, Server(context), request, cancellationToken)));
        internalApi.MapPost("/leases/{characterId:guid}/transfers/{transferId:guid}/commit", async (Guid characterId, Guid transferId,
            CommitTransferRequest request, HttpContext context, GameAdmissionService service, CancellationToken cancellationToken) =>
            Response(await service.CommitTransferAsync(characterId, transferId, Server(context), request, cancellationToken)));
        internalApi.MapPost("/leases/{characterId:guid}/transfers/{transferId:guid}/abort", async (Guid characterId, Guid transferId,
            LeaseRequest request, HttpContext context, GameAdmissionService service, CancellationToken cancellationToken) =>
            Response(await service.AbortTransferAsync(characterId, transferId, Server(context), request, cancellationToken)));
        internalApi.MapPost("/leases/{characterId:guid}/heartbeat", async (Guid characterId, LeaseRequest request,
            HttpContext context, GameAdmissionService service, CancellationToken cancellationToken) =>
            Response(await service.HeartbeatAsync(characterId, Server(context), request, cancellationToken)));
        internalApi.MapPut("/leases/{characterId:guid}/state", async (Guid characterId, SaveWorldStateRequest request,
            HttpContext context, GameAdmissionService service, CancellationToken cancellationToken) =>
            Response(await service.SaveAsync(characterId, Server(context), request, cancellationToken)));
        internalApi.MapPost("/leases/{characterId:guid}/release", async (Guid characterId, LeaseRequest request,
            HttpContext context, GameAdmissionService service, CancellationToken cancellationToken) =>
            await service.ReleaseAsync(characterId, Server(context), request, cancellationToken)
                ? Results.NoContent() : Results.Json(new { code = "lease_rejected" }, statusCode: 409));
    }
    private static DedicatedServerIdentity Server(HttpContext context) => (DedicatedServerIdentity)context.Items[typeof(DedicatedServerIdentity)]!;
    private static IResult Response<T>(GameResult<T> result) => result.Error is null ? Results.Ok(result.Value) : Results.Json(new { code = result.Error }, statusCode: result.Status);
}
