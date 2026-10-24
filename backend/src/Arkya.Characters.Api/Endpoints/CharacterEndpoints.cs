using System.Security.Claims;
using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.Characters.Api.Domain;
using Arkya.ServiceDefaults;

namespace Arkya.Characters.Api.Endpoints;

public static class CharacterEndpoints
{
    public static void MapCharacterEndpoints(this WebApplication app)
    {
        app.MapGet("/v1/characters/appearance-options", () => Results.Ok(new
        {
            version = 1,
            defaults = CharacterAppearanceResponse.From(CharacterAppearance.Default),
            races = CharacterAppearanceCatalog.Races,
            silhouettes = CharacterAppearanceCatalog.Silhouettes,
            hairStyles = CharacterAppearanceCatalog.HairStyles,
            palettes = new
            {
                skin = CharacterAppearanceCatalog.Skin, hair = CharacterAppearanceCatalog.Hair,
                eyes = CharacterAppearanceCatalog.Eyes, outfit = CharacterAppearanceCatalog.Outfit
            }
        })).AllowAnonymous().RequireCors(BrowserClientOptions.Policy);

        var characters = app.MapGroup("/v1/characters").RequireAuthorization().RequireCors(BrowserClientOptions.Policy);
        characters.MapGet("/entry-context", async (ClaimsPrincipal user, GameAdmissionService service,
            CancellationToken cancellationToken) => Results.Ok(await service.EntryContextAsync(AccountId(user), cancellationToken)));
        characters.MapGet("", async (ClaimsPrincipal user, CharacterService service, CancellationToken cancellationToken) =>
        {
            var owned = await service.ListOwnedAsync(AccountId(user), cancellationToken);
            return Results.Ok(owned.Select(CharacterResponse.From));
        });

        characters.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, CharacterService service,
            CancellationToken cancellationToken) =>
        {
            var character = await service.FindOwnedAsync(id, AccountId(user), cancellationToken);
            return character is null ? Results.NotFound() : Results.Ok(CharacterResponse.From(character));
        });

        characters.MapPost("", async (CreateCharacterRequest request, ClaimsPrincipal user, CharacterService service, GameAdmissionService game,
            IRealmStorageRouting storage, CancellationToken cancellationToken) =>
        {
            if (storage.Enabled && (request.RealmId is null || !storage.IsConfigured(request.RealmId)))
                return Results.Json(new { code = "realm_required" }, statusCode: 400);
            if (request.RealmId is not null && (!GameProtocol.ValidId(request.RealmId) ||
                !(await game.CatalogAsync(cancellationToken)).Realms.Any(value => value.RealmId == request.RealmId)))
                return Results.Json(new { code = "invalid_realm" }, statusCode: 400);
            var result = await service.CreateAsync(AccountId(user), request.Name, request.Appearance?.ToDomain(), cancellationToken, request.RealmId);
            return result.Status switch
            {
                CreateCharacterStatus.Created => Results.Created($"/v1/characters/{result.Character!.Id:D}",
                    CharacterResponse.From(result.Character)),
                CreateCharacterStatus.NameTaken => Results.Problem(statusCode: 409, title: "This character name is already taken."),
                CreateCharacterStatus.InvalidAppearance => Results.ValidationProblem(result.Errors!),
                _ => Results.ValidationProblem(new Dictionary<string, string[]> { ["name"] = [CharacterName.ValidationMessage] })
            };
        });

        app.MapPost("/internal/characters/{characterId:guid}/resolve", async (Guid characterId,
            ResolveCharacterRequest request, HttpContext context, ServiceKeyValidator serviceKeys,
            ISessionValidator sessions, CharacterService service, CancellationToken cancellationToken) =>
        {
            var serviceKey = context.Request.Headers["X-Arkya-Service-Key"];
            if (serviceKey.Count != 1 || !serviceKeys.IsValid(serviceKey[0]))
                return Results.Problem(statusCode: 403, title: "Service authentication failed.");
            if (string.IsNullOrWhiteSpace(request.SessionToken))
                return Results.Problem(statusCode: 401, title: "A valid session is required.");

            var validation = await sessions.ValidateAsync(request.SessionToken, cancellationToken);
            if (validation.Status != SessionValidationStatus.Valid)
                return Results.Problem(statusCode: validation.Status == SessionValidationStatus.Unavailable ? 503 : 401,
                    title: validation.Status == SessionValidationStatus.Unavailable
                        ? "Identity service unavailable." : "A valid session is required.");

            var session = validation.Session!;
            var character = await service.FindOwnedAsync(characterId, session.AccountId, cancellationToken);
            return character is null ? Results.NotFound() : Results.Ok(new ResolvedCharacterResponse(
                session.AccountId, CharacterResponse.From(character), session.ExpiresAt));
        }).AllowAnonymous();
    }

    private static Guid AccountId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
