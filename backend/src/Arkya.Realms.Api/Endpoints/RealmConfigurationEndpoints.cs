using Arkya.Realms.Api.Application;
using Arkya.Realms.Api.Contracts;
using Arkya.Realms.Api.Infrastructure;

namespace Arkya.Realms.Api.Endpoints;

public static class RealmConfigurationEndpoints
{
    public static void MapRealmConfigurationEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/admin/realms/{realmId}/configuration");
        admin.AddEndpointFilter(async (context, next) =>
        {
            var keys = context.HttpContext.Request.Headers[RealmsAdminKeyValidator.HeaderName];
            var validator = context.HttpContext.RequestServices.GetRequiredService<RealmsAdminKeyValidator>();
            return keys.Count == 1 && validator.IsValid(keys[0]) ? await next(context) :
                Results.Json(new ErrorResponse("forbidden", "Realm administrator authentication failed."), statusCode: 403);
        });
        admin.MapGet("", async (string realmId, RealmConfigurationService service, CancellationToken cancellationToken) =>
            await service.GetAsync(realmId, cancellationToken) is { } configuration ? Results.Ok(configuration) : Error("realm_not_found"));
        admin.MapPut("", async (string realmId, UpdateRealmConfigurationRequest request, HttpContext context,
            RealmConfigurationService service, CancellationToken cancellationToken) =>
        {
            var actors = context.Request.Headers["X-Arkya-Admin-Actor"];
            var result = await service.UpdateAsync(realmId, request, actors.Count == 1 ? actors[0] : null, cancellationToken);
            return result.Configuration is { } configuration ? Results.Ok(configuration) : Error(result.Error!);
        });
    }

    private static IResult Error(string code) => Results.Json(new ErrorResponse(code, code switch
    {
        "invalid_operator" => "A valid administrator identity header is required.",
        "invalid_operation_reason" => "Provide a reason containing 1 to 500 characters without control characters.",
        "invalid_configuration" => "Multipliers must be between 0 and 100 with at most three decimal places; the expected revision must be valid.",
        "realm_not_found" => "Realm not found.",
        _ => "The realm configuration changed. Reload it before saving."
    }), statusCode: code switch
    {
        "invalid_operator" or "invalid_operation_reason" or "invalid_configuration" => 400,
        "realm_not_found" => 404,
        _ => 409
    });
}
