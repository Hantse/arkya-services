using Arkya.Content.Api.Application;
using Arkya.Content.Api.Contracts;
using Arkya.ServiceDefaults;

namespace Arkya.Content.Api.Endpoints;

public static class ItemContentEndpoints
{
    public static void MapItemContentEndpoints(this WebApplication app, RouteGroupBuilder admin)
    {
        admin.MapGet("/items/draft", async (ItemContentService service, CancellationToken ct) =>
            Results.Ok(ItemDraftResponse.From(await service.GetDraftAsync(ct))));
        admin.MapPut("/items/draft", async (SaveItemDraftRequest request, ItemContentService service, CancellationToken ct) =>
            Response(await service.SaveDraftAsync(request, ct)));
        admin.MapGet("/items/catalogs", async (ItemContentService service, CancellationToken ct) =>
            Results.Ok((await service.ListCatalogsAsync(ct)).Select(ItemCatalogResponse.From)));
        admin.MapPost("/items/catalogs", async (PublishItemCatalogRequest request, ItemContentService service, CancellationToken ct) =>
            Response(await service.PublishAsync(request, ct)));
        admin.MapGet("/item-assets", async (string? search, int? limit, int? offset, ItemContentService service, CancellationToken ct) =>
        {
            var count = limit ?? 24;
            var errors = ItemContentService.ValidateAssetQuery(search, count, offset ?? 0);
            if (errors.Count > 0) return Results.ValidationProblem(errors);
            var assets = await service.ListAssetsAsync(search, count, offset ?? 0, ct);
            return Results.Ok(new ItemAssetPage(assets.Take(count).Select(ItemAssetResponse.From).ToArray(), assets.Count > count));
        });
        admin.MapGet("/item-assets/{guid}", async (string guid, ItemContentService service, CancellationToken ct) =>
            ItemContentService.IsUnityHash(guid) && await service.FindAssetAsync(guid, ct) is { } asset
                ? Results.Ok(ItemAssetResponse.From(asset)) : Results.NotFound());
        admin.MapGet("/item-assets/{guid}/thumbnail", async (string guid, HttpContext context, ItemContentService service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "private, no-store";
            context.Response.Headers.XContentTypeOptions = "nosniff";
            return ItemContentService.IsUnityHash(guid) && await service.FindAssetAsync(guid, ct) is { ThumbnailPng: not null } asset
                ? Results.File(asset.ThumbnailPng, "image/png") : Results.NotFound();
        });
        admin.MapPut("/item-assets/{guid}", async (string guid, UpsertItemAssetRequest request, ItemContentService service, CancellationToken ct) =>
        {
            var result = await service.UpsertAssetAsync(guid, request, ct);
            return result.Asset is null ? Results.ValidationProblem(result.Errors) : Results.Ok(ItemAssetResponse.From(result.Asset));
        });
        admin.MapDelete("/item-assets/{guid}", async (string guid, ItemContentService service, CancellationToken ct) =>
            ItemContentService.IsUnityHash(guid) && await service.DeleteAssetAsync(guid, ct) ? Results.NoContent() : Results.NotFound());

        var catalogs = app.MapGroup("/internal/content/items/catalogs");
        catalogs.AddEndpointFilter(async (context, next) =>
        {
            var key = context.HttpContext.Request.Headers[ServiceKeyValidator.HeaderName];
            var validator = context.HttpContext.RequestServices.GetRequiredService<ServiceKeyValidator>();
            return key.Count == 1 && validator.IsValid(key[0]) ? await next(context) :
                Results.Problem(statusCode: 403, title: "Service authentication failed.");
        });
        catalogs.MapGet("/latest", async (ItemContentService service, CancellationToken ct) =>
            await service.LatestCatalogAsync(ct) is { } catalog ? Results.Ok(ItemCatalogResponse.From(catalog)) : Results.NotFound());
        catalogs.MapGet("/{version}", async (string version, ItemContentService service, CancellationToken ct) =>
            Guid.TryParseExact(version, "D", out var id) && id != Guid.Empty && await service.FindCatalogAsync(id, ct) is { } catalog
                ? Results.Ok(ItemCatalogResponse.From(catalog)) : Results.NotFound());
    }

    private static IResult Response(ItemChangeResult result) => result.Status switch
    {
        ItemChangeStatus.Saved => Results.Ok(ItemDraftResponse.From(result.Draft!)),
        ItemChangeStatus.Published => Results.Ok(ItemCatalogResponse.From(result.Catalog!)),
        ItemChangeStatus.Invalid => Results.ValidationProblem(result.Errors!),
        _ => Results.Problem(statusCode: 409, title: "The item draft has changed. Reload before trying again.")
    };
}
