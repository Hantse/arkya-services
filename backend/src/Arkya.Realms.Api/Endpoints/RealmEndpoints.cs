using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Infrastructure;

namespace Arkya.Realms.Api.Endpoints;

public static class RealmEndpoints
{
    public static void MapRealmEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/admin/realms");
        admin.AddEndpointFilter(async (context, next) =>
        {
            var key = context.HttpContext.Request.Headers[RealmsAdminKeyValidator.HeaderName];
            var validator = context.HttpContext.RequestServices.GetRequiredService<RealmsAdminKeyValidator>();
            return key.Count == 1 && validator.IsValid(key[0]) ? await next(context) :
                Results.Json(new ErrorResponse("forbidden", "Realm administrator authentication failed."), statusCode: 403);
        });
        admin.MapGet("", async (IRealmRepository repository, CancellationToken cancellationToken) =>
            Results.Ok((await repository.ListAsync(cancellationToken)).Select(RealmSummary.From).ToArray()));
        admin.MapGet("/options", (RealmService service) => Results.Ok(service.GetOptions()));
        admin.MapGet("/{id}", async (string id, IRealmRepository repository, CancellationToken cancellationToken) =>
        {
            var realm = await repository.FindAsync(id, cancellationToken);
            return realm is null ? Error("realm_not_found") : Results.Ok(RealmSummary.From(realm));
        });
        admin.MapPost("", async (CreateRealmRequest request, RealmService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken);
            return result.Realm is { } realm ? Results.Created($"/admin/realms/{realm.RealmId}", RealmSummary.From(realm)) : Error(result.Error!);
        });
        admin.MapPost("/{id}/deploy", async (string id, DeployRealmRequest request, RealmService service, CancellationToken cancellationToken) =>
        {
            var result = await service.RequestDeploymentAsync(id, request.ExpectedRevision, cancellationToken);
            return result.Realm is { } realm ? Results.Accepted($"/admin/realms/{realm.RealmId}", RealmSummary.From(realm)) : Error(result.Error!);
        });
    }

    private static IResult Error(string code) => Results.Json(new ErrorResponse(code, code switch
    {
        "invalid_realm" => "Realm ID must contain 3 to 31 lowercase letters, digits or hyphens; display name must contain 1 to 80 characters.",
        "invalid_configuration_choice" => "Select an available deployment target and profile.",
        "realm_or_target_exists" => "This realm ID or target namespace is already reserved.",
        "realm_not_found" => "Realm not found.",
        "deployment_disabled" => "Realm deployment is disabled in service configuration.",
        "target_unavailable" => "The realm target is no longer allowed by service configuration.",
        _ => "The realm has changed or is already deploying. Reload its status."
    }), statusCode: code switch
    {
        "invalid_realm" or "invalid_configuration_choice" => 400,
        "realm_not_found" => 404,
        _ => 409
    });
}
