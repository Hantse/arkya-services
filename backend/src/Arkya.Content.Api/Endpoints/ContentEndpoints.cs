using Arkya.Content.Api.Application;
using Arkya.Content.Api.Contracts;
using Arkya.Content.Api.Infrastructure;
using Arkya.ServiceDefaults;

namespace Arkya.Content.Api.Endpoints;

public static class ContentEndpoints
{
    public static void MapContentEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/admin/content");
        admin.AddEndpointFilter(async (context, next) =>
        {
            var key = context.HttpContext.Request.Headers[ContentAdminKeyValidator.HeaderName];
            var validator = context.HttpContext.RequestServices.GetRequiredService<ContentAdminKeyValidator>();
            return key.Count == 1 && validator.IsValid(key[0])
                ? await next(context)
                : Results.Problem(statusCode: 403, title: "Content administrator authentication failed.");
        });
        admin.MapGet("/draft", async (ContentService service, CancellationToken cancellationToken) =>
            Results.Ok(DraftResponse.From(await service.GetDraftAsync(cancellationToken))));
        admin.MapPut("/draft", async (SaveDraftRequest request, ContentService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SaveDraftAsync(request, cancellationToken);
            return result.Status switch
            {
                ContentChangeStatus.Saved => Results.Ok(DraftResponse.From(result.Draft!)),
                ContentChangeStatus.Invalid => Results.ValidationProblem(result.Errors!),
                _ => Results.Problem(statusCode: 409, title: "The draft has changed. Reload before saving.")
            };
        });
        admin.MapGet("/catalogs", async (ContentService service, CancellationToken cancellationToken) =>
            Results.Ok((await service.ListCatalogsAsync(cancellationToken)).Select(CatalogResponse.From)));
        admin.MapPost("/catalogs", async (PublishCatalogRequest request, ContentService service, CancellationToken cancellationToken) =>
        {
            var result = await service.PublishAsync(request, cancellationToken);
            return result.Status switch
            {
                ContentChangeStatus.Published => Results.Ok(CatalogResponse.From(result.Catalog!)),
                ContentChangeStatus.Invalid => Results.ValidationProblem(result.Errors!),
                _ => Results.Problem(statusCode: 409, title: "The draft has changed. Reload before publishing.")
            };
        });

        app.MapGet("/internal/content/catalogs/{version}", async (string version, HttpContext context,
            ServiceKeyValidator validator, ContentService service, CancellationToken cancellationToken) =>
        {
            var key = context.Request.Headers[ServiceKeyValidator.HeaderName];
            if (key.Count != 1 || !validator.IsValid(key[0]))
                return Results.Problem(statusCode: 403, title: "Service authentication failed.");
            if (!Guid.TryParseExact(version, "D", out var id) || id == Guid.Empty)
                return Results.NotFound();
            var catalog = await service.FindCatalogAsync(id, cancellationToken);
            return catalog is null ? Results.NotFound() : Results.Ok(CatalogResponse.From(catalog));
        });
    }
}
