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
            Results.Ok((await repository.ListAsync(cancellationToken)).Where(realm => realm.Status != Domain.RealmStatus.Deleted).Select(RealmSummary.From).ToArray()));
        admin.MapGet("/options", (RealmService service) => Results.Ok(service.GetOptions()));
        admin.MapGet("/{id}", async (string id, IRealmRepository repository, CancellationToken cancellationToken) =>
        {
            var realm = await repository.FindAsync(id, cancellationToken);
            return realm is null || realm.Status == Domain.RealmStatus.Deleted ? Error("realm_not_found") : Results.Ok(RealmSummary.From(realm));
        });
        admin.MapGet("/{id}/operations-options", async (string id, IRealmRepository repository, RealmService service, CancellationToken cancellationToken) =>
        {
            var realm = await repository.FindAsync(id, cancellationToken);
            return realm is null || realm.Status == Domain.RealmStatus.Deleted ? Error("realm_not_found") : Results.Ok(service.GetOperationsOptions(realm));
        });
        admin.MapPost("/{id}/delete", async (string id, DeleteRealmRequest request, HttpContext context, RealmService service, CancellationToken cancellationToken) =>
        {
            var result = await service.RequestDeletionAsync(id, request, Operator(context), cancellationToken);
            return result.Realm is { } realm ? Results.Accepted($"/admin/realms/{realm.RealmId}", RealmSummary.From(realm)) : Error(result.Error!);
        });
        admin.MapPost("/{id}/restart", async (string id, RestartRealmRequest request, HttpContext context, RealmService service, CancellationToken cancellationToken) =>
        {
            var result = await service.RequestRestartAsync(id, request, Operator(context), cancellationToken);
            return result.Realm is { } realm ? Results.Accepted($"/admin/realms/{realm.RealmId}", RealmSummary.From(realm)) : Error(result.Error!);
        });
        admin.MapPost("/{id}/update", async (string id, UpdateRealmRequest request, HttpContext context, RealmService service, CancellationToken cancellationToken) =>
        {
            var result = await service.RequestUpdateAsync(id, request, Operator(context), cancellationToken);
            return result.Realm is { } realm ? Results.Accepted($"/admin/realms/{realm.RealmId}", RealmSummary.From(realm)) : Error(result.Error!);
        });
        admin.MapPost("", async (CreateRealmRequest request, RealmService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken);
            return result.Realm is { } realm ? Results.Created($"/admin/realms/{realm.RealmId}", RealmSummary.From(realm)) : Error(result.Error!);
        });
        admin.MapPut("/{id}/topology", async (string id, UpdateRealmTopologyRequest request, RealmService service, CancellationToken cancellationToken) =>
        {
            var result = await service.UpdateTopologyAsync(id, request, cancellationToken);
            return result.Realm is { } realm ? Results.Ok(RealmSummary.From(realm)) : Error(result.Error!);
        });
        admin.MapPost("/{id}/deploy", async (string id, DeployRealmRequest request, RealmService service, CancellationToken cancellationToken) =>
        {
            var result = await service.RequestDeploymentAsync(id, request.ExpectedRevision, cancellationToken);
            return result.Realm is { } realm ? Results.Accepted($"/admin/realms/{realm.RealmId}", RealmSummary.From(realm)) : Error(result.Error!);
        });
    }

    private static string? Operator(HttpContext context)
    {
        var actors = context.Request.Headers["X-Arkya-Admin-Actor"];
        return actors.Count == 1 ? actors[0] : null;
    }

    private static IResult Error(string code) => Results.Json(new ErrorResponse(code, code switch
    {
        "invalid_operator" => "A valid administrator identity header is required.",
        "invalid_operation_reason" => "Provide a reason containing 1 to 500 characters without control characters.",
        "invalid_update_image" => "Select the approved immutable image digest.",
        "invalid_delete_confirmation" => "Type the exact realm ID to confirm deletion.",
        "deletion_unavailable" => "Deletion or another deployment operation is already in progress. Reload the realm.",
        "deletion_disabled" => "Enable deployment cleanup before deleting a deployed realm.",
        "no_approved_update" => "No compatible image release is approved for this realm.",
        "approved_image_changed" => "The approved image has changed. Reload the realm before updating.",
        "operation_in_progress" or "operation_unavailable" => "This operation is unavailable while the realm is deploying or its configuration has changed.",
        "invalid_realm" => "Realm ID must contain 3 to 31 lowercase letters, digits or hyphens; display name must contain 1 to 80 characters.",
        "invalid_topology" => "Use up to 32 unique islands with IDs of 3 to 20 lowercase letters, digits or hyphens, 1 to 64 dedicateds per island, 0 to 64 instance or raid dedicateds, and 1 to 256 dedicateds in total. Optional gridColumns and gridRows must both be set, each from 1 to 64, and their product must equal dedicatedCount.",
        "topology_edit_unavailable" => "Only draft realms can change their profile or topology. Deployed realms require a planned migration.",
        "invalid_topology_endpoint" => "The approved endpoint template does not produce valid, distinct destinations for this topology.",
        "topology_runtime_unsupported" => "The topology draft is saved, but this profile has no explicitly approved partition, instance and raid runtime. Select a compatible profile before deploying.",
        "invalid_configuration_choice" => "Select an available deployment target and profile.",
        "target_realm_mismatch" => "This deployment target is reserved for its configured realm ID.",
        "realm_or_target_exists" => "This realm ID or target namespace is already reserved.",
        "realm_not_found" => "Realm not found.",
        "deployment_disabled" => "Realm deployment is disabled in service configuration.",
        "target_unavailable" => "The realm target is no longer allowed by service configuration.",
        _ => "The realm has changed or is already deploying. Reload its status."
    }), statusCode: code switch
    {
        "invalid_operator" or "invalid_operation_reason" or "invalid_update_image" or "invalid_delete_confirmation" or "invalid_realm" or "invalid_topology" or "invalid_topology_endpoint" or "invalid_configuration_choice" or "target_realm_mismatch" => 400,
        "realm_not_found" => 404,
        _ => 409
    });
}
