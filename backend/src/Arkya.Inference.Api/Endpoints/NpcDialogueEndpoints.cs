using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Arkya.Inference.Api.Application;
using Arkya.ServiceDefaults.Inference;
using Microsoft.Extensions.Options;

namespace Arkya.Inference.Api.Endpoints;

public static class NpcDialogueEndpoints
{
    public static void MapNpcDialogueEndpoints(this WebApplication app)
    {
        var dedicated = app.MapGroup("/internal/inference/dialogues");
        dedicated.AddEndpointFilter(async (context, next) =>
        {
            var options = context.HttpContext.RequestServices.GetRequiredService<IOptions<NpcDialogueOptions>>().Value;
            var keys = context.HttpContext.Request.Headers["X-Arkya-Service-Key"];
            if (!options.Enabled) return Error("dialogue_disabled");
            if (keys.Count != 1 || keys[0] is not { Length: >= 32 and <= 512 } key ||
                !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(key)),
                    SHA256.HashData(Encoding.UTF8.GetBytes(options.ServiceKey)))) return Error("forbidden");
            return await next(context);
        });
        dedicated.MapPost("", async (NpcDialogueRequest request, NpcDialogueService service,
            CancellationToken cancellationToken) => Result(await service.SubmitAsync(request, cancellationToken)));
        dedicated.MapGet("/{requestId}", (string requestId, NpcDialogueService service) => Result(service.Get(requestId)));
        app.MapPost("/agent/inference/dialogues/claim", async (ClaimNpcDialogueRequest request, HttpContext context,
            NpcDialogueService service, CancellationToken cancellationToken) =>
            Result(await service.ClaimAsync(request, Bearer(context), cancellationToken)));
        app.MapPost("/agent/inference/dialogues/complete", async (CompleteNpcDialogueRequest request, HttpContext context,
            NpcDialogueService service, CancellationToken cancellationToken) =>
            Result(await service.CompleteAsync(request, Bearer(context), cancellationToken)));
    }

    private static string? Bearer(HttpContext context)
    {
        var values = context.Request.Headers.Authorization;
        return values.Count == 1 && AuthenticationHeaderValue.TryParse(values[0], out var header) &&
            header.Scheme.Equals("Bearer", StringComparison.OrdinalIgnoreCase) ? header.Parameter : null;
    }

    private static IResult Result<T>(InferenceResult<T> result) where T : class => result.Value is { } value
        ? Results.Json(value, statusCode: value is NpcDialogueResponse { Status: "pending" } ? 202 : 200)
        : Error(result.Error!);

    private static IResult Error(string code) => Results.Json(new ErrorResponse(code, code switch
    {
        "forbidden" => "Dedicated inference authentication failed.",
        "unauthorized_agent" => "Agent authentication failed.",
        "invalid_dialogue" => "The dialogue request is invalid.",
        _ => "The dialogue is unavailable. Use the authored NPC fallback."
    }), statusCode: code switch
    {
        "forbidden" => 403,
        "unauthorized_agent" => 401,
        "invalid_dialogue" => 400,
        "dialogue_not_found" => 404,
        "dialogue_capacity" => 429,
        "request_conflict" or "dialogue_lease_mismatch" or "worker_unavailable" => 409,
        _ => 503
    });
}
