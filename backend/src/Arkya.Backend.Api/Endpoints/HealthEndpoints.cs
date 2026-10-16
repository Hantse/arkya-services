using Arkya.Backend.Api.Contracts.Health;

namespace Arkya.Backend.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/health",
            (TimeProvider timeProvider) =>
            {
                return Results.Ok(new HealthResponse(
                    "ok",
                    "arkya-backend",
                    timeProvider.GetUtcNow()));
            })
            .WithName("Health");

        return endpoints;
    }
}
