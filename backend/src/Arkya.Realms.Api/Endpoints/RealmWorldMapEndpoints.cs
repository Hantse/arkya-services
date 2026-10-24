using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Infrastructure;
using Arkya.ServiceDefaults;

namespace Arkya.Realms.Api.Endpoints;

public sealed class RealmWorldMapBodyLimit { }

public static class RealmWorldMapEndpoints
{
    public static void MapRealmWorldMapEndpoints(this WebApplication app)
    {
        app.MapGet("/admin/realms/{realmId}/world-map", async (string realmId, HttpContext context,
            RealmsAdminKeyValidator validator, RealmWorldMapService service, CancellationToken ct) =>
        {
            var keys = context.Request.Headers[RealmsAdminKeyValidator.HeaderName];
            if (keys.Count != 1 || !validator.IsValid(keys[0])) return Results.StatusCode(403);
            return await service.GetAsync(realmId, ct) is { } map ? Results.Ok(map) : Error("realm_not_found");
        });
        app.MapPost("/internal/realms/{realmId}/servers/{serverId}/world-map", async (string realmId, string serverId,
            RealmWorldMapReport request, HttpContext context, RealmCommandService authorization, RealmWorldMapService service, CancellationToken ct) =>
        {
            var keys = context.Request.Headers["X-Arkya-Dedicated-Key"];
            if (keys.Count != 1 || !await authorization.AuthorizeAsync(realmId, serverId, keys[0], ct)) return Results.StatusCode(403);
            return await service.ReportAsync(realmId, serverId, request, ct) is { } error ? Error(error) : Results.NoContent();
        }).WithMetadata(new RealmWorldMapBodyLimit());
    }

    private static IResult Error(string code) => Results.Json(new ErrorResponse(code, code),
        statusCode: code == "realm_not_found" ? 404 : 400);
}
