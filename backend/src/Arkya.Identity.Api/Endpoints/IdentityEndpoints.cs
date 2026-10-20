using System.Net.Http.Headers;
using Arkya.Identity.Api.Application;
using Arkya.Identity.Api.Contracts;
using Arkya.Identity.Api.Domain;
using Arkya.ServiceDefaults;

namespace Arkya.Identity.Api.Endpoints;

public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/v1/auth").RequireRateLimiting("auth");
        auth.MapPost("/discord/challenges", async (ChallengeRequest request, IdentityService service, CancellationToken ct) =>
        {
            if (!service.IsAllowedReturnUrl(request.ReturnUrl))
                return Results.BadRequest(new { code = "invalid_return_url" });
            var result = await service.CreateChallengeAsync(request.CodeChallenge, ct, request.ReturnUrl);
            return result is null ? Results.BadRequest(new { code = "invalid_code_challenge" }) : Results.Ok(result);
        }).RequireCors(WebClientOptions.CorsPolicyName);
        auth.MapGet("/discord/authorize/{id}", async (string id, HttpContext context, IdentityService service, CancellationToken ct) =>
        {
            var result = await service.StartBrowserAsync(id, ct);
            if (result is null)
            {
                return Results.BadRequest(new { code = "invalid_challenge" });
            }
            context.Response.Cookies.Append(result.CookieName, result.CookieValue, new CookieOptions
            {
                HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, MaxAge = TimeSpan.FromMinutes(5),
                Path = "/v1/auth/discord/callback", IsEssential = true
            });
            return Results.Redirect(result.RedirectUrl);
        });
        auth.MapGet("/discord/callback", async (HttpContext context, IdentityService service, CancellationToken ct) =>
        {
            var completion = await service.CompleteBrowserWithReturnAsync(context.Request.Query["state"], context.Request.Query["code"], context.Request.Query["error"],
                name => context.Request.Cookies[name], ct);
            if (completion.ReturnUrl is not null) return Results.Redirect(completion.ReturnUrl);
            return Results.Content(completion.Succeeded ? "Connexion autorisée. Vous pouvez retourner dans Arkya." : "Connexion refusée ou expirée. Relancez la connexion depuis Arkya.",
                "text/plain; charset=utf-8", statusCode: completion.Succeeded ? 200 : 400);
        });
        auth.MapPost("/discord/exchange", async (ExchangeRequest request, HttpContext context, IdentityService service, CancellationToken ct) =>
        {
            var result = await service.ExchangeAsync(request.ChallengeId, request.CodeVerifier, ct);
            if (result.Status == "pending")
            {
                context.Response.Headers.RetryAfter = "5";
                return Results.Json(new { status = "pending" }, statusCode: 202);
            }
            return result.Status switch
            {
                "completed" => Results.Ok(result.Session),
                "expired" => Results.Json(new { code = "challenge_expired_or_consumed" }, statusCode: 410),
                _ => Results.BadRequest(new { code = "invalid_exchange" })
            };
        }).RequireCors(WebClientOptions.CorsPolicyName);
        auth.MapGet("/me", async (HttpContext context, IdentityService service, CancellationToken ct) =>
        {
            var session = await service.ValidateSessionAsync(ReadBearer(context), ct);
            return session is null ? Results.Unauthorized() : Results.Ok(ToResponse(session));
        }).RequireCors(WebClientOptions.CorsPolicyName);
        auth.MapPost("/logout", async (HttpContext context, IdentityService service, CancellationToken ct) =>
        {
            var token = ReadBearer(context);
            if (await service.ValidateSessionAsync(token, ct) is null)
            {
                return Results.Unauthorized();
            }
            await service.RevokeAsync(token!, ct);
            return Results.NoContent();
        }).RequireCors(WebClientOptions.CorsPolicyName);
        app.MapPost("/internal/sessions/introspect", async (IntrospectionRequest request, HttpContext context,
            ServiceKeyValidator keys, IdentityService service, CancellationToken ct) =>
        {
            if (!keys.IsValid(context.Request.Headers[ServiceKeyValidator.HeaderName]))
            {
                return Results.StatusCode(403);
            }
            var session = await service.ValidateSessionAsync(request.SessionToken, ct);
            return session is null ? Results.Unauthorized() : Results.Ok(ToResponse(session));
        });
        app.MapPost("/internal/sessions/introspect-reference", async (SessionReferenceRequest request, HttpContext context,
            ServiceKeyValidator keys, IdentityService service, CancellationToken ct) =>
        {
            if (!keys.IsValid(context.Request.Headers[ServiceKeyValidator.HeaderName])) return Results.StatusCode(403);
            var session = await service.ValidateSessionReferenceAsync(request.SessionReference, ct);
            return session is null ? Results.Unauthorized() : Results.Ok(ToResponse(session));
        });
    }

    private static AccountSessionResponse ToResponse(Session session) => new(session.AccountId, session.DisplayName, session.ExpiresAt);
    private static string? ReadBearer(HttpContext context) => AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var header)
        && header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ? header.Parameter : null;
}
