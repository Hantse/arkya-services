using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Contracts;
using Arkya.ServiceDefaults;

namespace Arkya.Characters.Api.Endpoints;

public static class CharacterQuestEndpoints
{
    public static void MapCharacterQuestEndpoints(this WebApplication app)
    {
        var quests = app.MapGroup("/internal/characters/{characterId:guid}/quests").AllowAnonymous();
        quests.AddEndpointFilter(async (context, next) =>
        {
            var keys = context.HttpContext.Request.Headers[ServiceKeyValidator.HeaderName];
            var validator = context.HttpContext.RequestServices.GetRequiredService<ServiceKeyValidator>();
            return keys.Count == 1 && validator.IsValid(keys[0])
                ? await next(context)
                : Results.Problem(statusCode: 403, title: "Service authentication failed.");
        });

        quests.MapGet("", async (Guid characterId, CharacterQuestService service, CancellationToken cancellationToken) =>
        {
            var entries = await service.ListAsync(characterId, cancellationToken);
            return entries is null ? Results.NotFound() : Results.Ok(new CharacterQuestsResponse(characterId,
                entries.Select(CharacterQuestResponse.From).ToArray()));
        });

        quests.MapPut("/{questId}", async (Guid characterId, string questId, SaveCharacterQuestRequest request,
            CharacterQuestService service, CancellationToken cancellationToken) =>
        {
            var result = await service.SaveAsync(characterId, questId, request.CatalogVersion, request.RequiredKills,
                request.Kills, request.ExpectedRevision, cancellationToken);
            return result.Status switch
            {
                SaveCharacterQuestStatus.Saved => Results.Ok(CharacterQuestResponse.From(result.Quest!)),
                SaveCharacterQuestStatus.CharacterNotFound => Results.NotFound(),
                SaveCharacterQuestStatus.Conflict => Results.Problem(statusCode: 409,
                    title: "Quest revision or accepted definition conflicts with persisted progress."),
                _ => Results.Problem(statusCode: 400, title: "Invalid quest progress.",
                    detail: "Use a valid quest ID, a catalog UUID, 1 to 10000 required kills, bounded kills and a nonnegative expected revision.")
            };
        });
    }
}
