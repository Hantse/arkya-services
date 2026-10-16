using System.Text.Json;

using Arkya.Backend.Api.Application.Auth;
using Arkya.Backend.Api.Contracts;
using Arkya.Backend.Api.Contracts.Auth;

namespace Arkya.Backend.Api.Endpoints;

public static class DevAuthEndpoints
{
    public static IEndpointRouteBuilder MapDevAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(
            "/dev/auth/login",
            async (
                HttpContext httpContext,
                DevAuthService authService,
                CancellationToken cancellationToken) =>
            {
                var request = await ReadRequestAsync(httpContext, cancellationToken);
                if (request.Error is not null)
                {
                    return Results.BadRequest(request.Error);
                }

                var session = await authService.LoginAsync(request.Value, cancellationToken);

                return Results.Ok(DevAuthSessionResponse.FromSession(session));
            })
            .WithName("DevAuthLogin");

        return endpoints;
    }

    private static async Task<RequestReadResult> ReadRequestAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (httpContext.Request.ContentLength is null or 0)
        {
            return new RequestReadResult(null, null);
        }

        try
        {
            var request = await httpContext.Request.ReadFromJsonAsync<DevLoginRequest>(
                cancellationToken: cancellationToken);

            return new RequestReadResult(request, null);
        }
        catch (JsonException)
        {
            return new RequestReadResult(
                null,
                new ErrorResponse("invalid_json", "Request body must be valid JSON."));
        }
        catch (BadHttpRequestException)
        {
            return new RequestReadResult(
                null,
                new ErrorResponse("invalid_request", "Request body could not be read."));
        }
    }

    private sealed record RequestReadResult(
        DevLoginRequest? Value,
        ErrorResponse? Error);
}
