using Arkya.Realms.Api.Application;

namespace Arkya.Realms.Api.Endpoints;

public static class PublicRealmEndpoints
{
    public const string CorsPolicy = "PublicRealms";

    public static void MapPublicRealmEndpoints(this WebApplication app)
    {
        var catalog = app.MapGroup("/v1/public/realms").RequireCors(CorsPolicy);
        catalog.MapGet("", async (PublicRealmService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));
        catalog.MapGet("/{id}", async (string id, PublicRealmService service, CancellationToken cancellationToken) =>
            await service.FindAsync(id, cancellationToken) is { } realm ? Results.Ok(realm) : Results.NotFound());
    }
}
