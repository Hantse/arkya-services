using System.Net.Http.Headers;
using Arkya.Inference.Api.Application;
using Arkya.Inference.Api.Infrastructure;
using Arkya.ServiceDefaults.Inference;

namespace Arkya.Inference.Api.Endpoints;

public sealed record ErrorResponse(string Code, string Message);

public static class InferenceEndpoints
{
    public static void MapInferenceEndpoints(this WebApplication app)
    {
        var admin = app.MapGroup("/admin/inference");
        admin.AddEndpointFilter(async (context, next) =>
        {
            var key = context.HttpContext.Request.Headers[InferenceAdminKeyValidator.HeaderName];
            var validator = context.HttpContext.RequestServices.GetRequiredService<InferenceAdminKeyValidator>();
            return key.Count == 1 && validator.IsValid(key[0]) ? await next(context) : Error("forbidden");
        });
        admin.MapGet("/models", () => Results.Ok(InferenceModels.Catalog));
        admin.MapPost("/enrollment-grants", async (EnrollmentService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.CreateGrantAsync(cancellationToken)));
        var agents = admin.MapGroup("/agents");
        agents.MapGet("", async (InferenceService service, CancellationToken cancellationToken) =>
            Results.Ok(await service.ListAsync(cancellationToken)));
        agents.MapGet("/{id}", async (string id, InferenceService service, CancellationToken cancellationToken) =>
            Result(await service.GetAsync(id, cancellationToken)));
        agents.MapPost("", async (CreateInferenceAgentRequest request, InferenceService service, CancellationToken cancellationToken) =>
        {
            var result = await service.CreateAsync(request, cancellationToken);
            return result.Value is { } value
                ? Results.Created($"/admin/inference/agents/{value.Agent.AgentId}", value) : Error(result.Error!);
        });
        agents.MapDelete("/{id}", async (string id, InferenceService service, CancellationToken cancellationToken) =>
        {
            var error = await service.DeleteAsync(id, cancellationToken);
            return error is null ? Results.NoContent() : Error(error);
        });
        agents.MapPost("/{id}/workers", async (string id, AddInferenceWorkerRequest request,
            InferenceService service, CancellationToken cancellationToken) =>
            Result(await service.AddWorkerAsync(id, request, cancellationToken)));
        agents.MapPatch("/{id}/workers/{workerId}", async (string id, string workerId, UpdateInferenceWorkerRequest request,
            InferenceService service, CancellationToken cancellationToken) =>
            Result(await service.UpdateWorkerAsync(id, workerId, request, cancellationToken)));
        agents.MapDelete("/{id}/workers/{workerId}", async (string id, string workerId,
            InferenceService service, CancellationToken cancellationToken) =>
            Result(await service.RemoveWorkerAsync(id, workerId, cancellationToken)));

        app.MapPost("/agent/inference/enroll", async (RegisterInferenceAgentRequest request, HttpContext context,
            EnrollmentService service, CancellationToken cancellationToken) =>
        {
            var values = context.Request.Headers.Authorization;
            string? token = null;
            if (values.Count != 0)
            {
                if (values.Count != 1 || !AuthenticationHeaderValue.TryParse(values[0], out var header) ||
                    !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                    return Error("unauthorized_enrollment");
                token = header.Parameter;
            }
            return Result(await service.RegisterAsync(request, token, cancellationToken));
        });

        app.MapPost("/agent/inference/heartbeat", async (InferenceHeartbeatRequest request, HttpContext context,
            InferenceService service, CancellationToken cancellationToken) =>
        {
            var values = context.Request.Headers.Authorization;
            if (values.Count != 1 || !AuthenticationHeaderValue.TryParse(values[0], out var header) ||
                !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                return Error("unauthorized_agent");
            return Result(await service.HeartbeatAsync(request, header.Parameter, cancellationToken));
        });
    }

    private static IResult Result<T>(InferenceResult<T> result) where T : class =>
        result.Value is { } value ? Results.Ok(value) : Error(result.Error!);

    private static IResult Error(string code) => Results.Json(new ErrorResponse(code, code switch
    {
        "forbidden" => "Inference administrator authentication failed.",
        "unauthorized_agent" => "Agent authentication failed.",
        "unauthorized_enrollment" => "A valid enrollment grant is required for a new agent.",
        "agent_not_found" => "Agent not found.",
        "worker_not_found" => "Worker not found.",
        "agent_exists" => "This agent identifier is already registered.",
        "agent_identity_unavailable" => "This machine identity was retired. Use a new device credential or a new agent identifier.",
        "worker_limit" => "The configured worker or thread limit has been reached.",
        "agent_not_drained" => "Remove desired workers and wait for a recent heartbeat confirming no workers before removing the agent.",
        "concurrent_update" => "The agent changed concurrently. Reload its status.",
        _ => "The request contains missing, invalid or out-of-range data."
    }), statusCode: code switch
    {
        "forbidden" => 403,
        "unauthorized_agent" or "unauthorized_enrollment" => 401,
        "agent_not_found" or "worker_not_found" => 404,
        "invalid_agent" or "invalid_worker" or "invalid_heartbeat" => 400,
        _ => 409
    });
}
