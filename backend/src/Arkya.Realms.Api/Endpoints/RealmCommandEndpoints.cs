using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Infrastructure;

namespace Arkya.Realms.Api.Endpoints;

public static class RealmCommandEndpoints
{
    public static void MapRealmCommandEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/admin/realms/{realmId}/commands");
        admin.AddEndpointFilter(async (context, next) =>
        {
            var keys = context.HttpContext.Request.Headers[RealmsAdminKeyValidator.HeaderName];
            return keys.Count == 1 && context.HttpContext.RequestServices.GetRequiredService<RealmsAdminKeyValidator>().IsValid(keys[0])
                ? await next(context) : Results.StatusCode(403);
        });
        admin.MapGet("", async (string realmId, RealmCommandService service, CancellationToken ct) =>
            await service.GetAsync(realmId, ct) is { } overview ? Results.Ok(overview) : Error("realm_not_found"));
        admin.MapPost("", async (string realmId, EnqueueRealmCommand request, HttpContext context, RealmCommandService service, CancellationToken ct) =>
        {
            var actors = context.Request.Headers["X-Arkya-Admin-Actor"];
            if (actors.Count != 1 || !RealmCommandService.ValidActor(actors[0])) return Results.StatusCode(403);
            var result = await service.EnqueueAsync(realmId, request, actors[0], ct);
            return result.Value is { } command ? Results.Accepted($"/admin/realms/{realmId}/commands", command) : Error(result.Error!);
        });
        var runtime = app.MapGroup("/internal/realms/{realmId}/servers/{serverId}/commands");
        runtime.AddEndpointFilter(async (context, next) =>
        {
            var keys = context.HttpContext.Request.Headers["X-Arkya-Dedicated-Key"];
            var service = context.HttpContext.RequestServices.GetRequiredService<RealmCommandService>();
            return keys.Count == 1 && await service.AuthorizeAsync((string)context.HttpContext.Request.RouteValues["realmId"]!,
                (string)context.HttpContext.Request.RouteValues["serverId"]!, keys[0], context.HttpContext.RequestAborted)
                ? await next(context) : Results.StatusCode(403);
        });
        runtime.MapPost("/poll", async (string realmId, string serverId, PollRealmCommands request, RealmCommandService service, CancellationToken ct) =>
        {
            var result = await service.PollAsync(realmId, serverId, request, ct);
            return result.Value is { } response ? Results.Ok(response) : Error(result.Error!);
        });
        runtime.MapPost("/{commandId}/complete", async (string realmId, string serverId, string commandId, CompleteRealmCommand request,
            RealmCommandService service, CancellationToken ct) =>
        {
            var result = await service.CompleteAsync(realmId, serverId, commandId, request, ct);
            return result.Value is { } response ? Results.Ok(response) : Error(result.Error!);
        });
    }

    private static IResult Error(string code) => Results.Json(new ErrorResponse(code, code), statusCode: code switch
    {
        "invalid_command" or "invalid_poll" or "invalid_completion" or "invalid_server" => 400,
        "realm_not_found" or "command_not_found" => 404,
        _ => 409
    });
}
