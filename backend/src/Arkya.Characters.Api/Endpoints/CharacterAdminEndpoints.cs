using Arkya.Characters.Api.Application;
using Arkya.Characters.Api.Infrastructure;
using Arkya.ServiceDefaults;

namespace Arkya.Characters.Api.Endpoints;

public static class CharacterAdminEndpoints
{
    public static void MapCharacterAdminEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/admin/characters").AllowAnonymous();
        admin.AddEndpointFilter(async (context, next) =>
        {
            var keys = context.HttpContext.Request.Headers[CharacterAdminKeyValidator.HeaderName];
            var validator = context.HttpContext.RequestServices.GetRequiredService<CharacterAdminKeyValidator>();
            return keys.Count == 1 && validator.IsValid(keys[0]) ? await next(context) :
                Results.Problem(statusCode: 403, title: "Character administrator authentication failed.");
        });
        admin.MapGet("", async (string? accountId, string? search, bool? onlineOnly, int? page, int? pageSize, string? realmId,
            ICharacterAdminRepository repository, TimeProvider clock, CancellationToken cancellationToken) =>
        {
            Guid? owner = null;
            if (!string.IsNullOrWhiteSpace(accountId))
            {
                if (!Guid.TryParse(accountId, out var parsed))
                    return Results.Problem(statusCode: 400, title: "Invalid account UUID.");
                owner = parsed;
            }
            var query = new CharacterAdminQuery(owner, search?.Trim() ?? "", onlineOnly ?? false, page ?? 1, pageSize ?? 25,
                string.IsNullOrEmpty(realmId) ? null : realmId);
            if (!query.IsValid()) return Results.Problem(statusCode: 400, title: "Invalid character query.",
                detail: "Use a valid account UUID and realm ID, a search of at most 100 printable characters, a page from 1 to 100000 and a page size from 1 to 100.");
            return Results.Ok(await repository.ListAsync(query, clock.GetUtcNow(), cancellationToken));
        });
        admin.MapGet("/overview", async (ICharacterAdminRepository repository, TimeProvider clock, CancellationToken cancellationToken) =>
            Results.Ok(await repository.OverviewAsync(clock.GetUtcNow(), cancellationToken)));
        admin.MapGet("/{characterId}", async (string characterId, string? realmId, int? questPage, int? questPageSize,
            CharacterAdminDetailService service, CancellationToken cancellationToken) =>
        {
            if (!Guid.TryParseExact(characterId, "D", out var id) || id == Guid.Empty ||
                !string.IsNullOrEmpty(realmId) && !GameProtocol.ValidId(realmId) ||
                (questPage ?? 1) is < 1 or > 100000 || (questPageSize ?? 25) is < 1 or > 100)
                return Results.Problem(statusCode: 400, title: "Invalid character detail query.");
            var detail = await service.GetAsync(id, string.IsNullOrEmpty(realmId) ? null : realmId,
                questPage ?? 1, questPageSize ?? 25, cancellationToken);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });
    }
}
