using System.Net.Http.Headers;
using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Contracts;
using Arkya.Identity.Api.Domain;

namespace Arkya.Identity.Api.Endpoints;

public static class DiscordSocialEndpoints
{
    public static void MapDiscordSocialEndpoints(this WebApplication app)
    {
        var social = app.MapGroup("/v1/auth/discord/social").RequireRateLimiting("auth");
        social.MapGet("/config", (HttpContext context, IdentityService identity, DiscordSocialService service, CancellationToken cancellationToken) =>
            ExecuteAsync(context, identity, _ => Task.FromResult<IResult>(Results.Ok(service.Configuration)), cancellationToken));
        social.MapPost("/exchange", (DiscordSocialExchangeRequest request, HttpContext context, IdentityService identity,
            DiscordSocialService service, CancellationToken cancellationToken) =>
            ExecuteAsync(context, identity, async session => Results.Ok(await service.ExchangeAsync(session, request, cancellationToken)), cancellationToken));
        social.MapPost("/refresh", (DiscordSocialRefreshRequest request, HttpContext context, IdentityService identity,
            DiscordSocialService service, CancellationToken cancellationToken) =>
            ExecuteAsync(context, identity, async session => Results.Ok(await service.RefreshAsync(session, request, cancellationToken)), cancellationToken));
    }

    private static async Task<IResult> ExecuteAsync(HttpContext context, IdentityService identity,
        Func<Session, Task<IResult>> operation, CancellationToken cancellationToken)
    {
        if (!context.Request.IsHttps) return Results.BadRequest(new { code = "https_required" });
        var bearer = AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var header) &&
            header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ? header.Parameter : null;
        var session = await identity.ValidateSessionAsync(bearer, cancellationToken);
        if (session is null) return Results.Unauthorized();
        try
        {
            return await operation(session);
        }
        catch (DiscordSocialException exception)
        {
            return Results.Json(new { code = exception.Code }, statusCode: exception.StatusCode);
        }
    }
}
