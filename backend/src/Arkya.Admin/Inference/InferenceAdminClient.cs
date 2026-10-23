using System.Net;
using System.Text.Json;
using Arkya.ServiceDefaults.Inference;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Inference;

public sealed class InferenceApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class InferenceAdminClient(HttpClient client, IOptions<InferenceApiOptions> options)
{
    public bool Enabled => options.Value.Enabled;

    public Task<InferenceAgentSummary[]> ListAsync(CancellationToken ct) =>
        ReadAsync<InferenceAgentSummary[]>(HttpMethod.Get, "admin/inference/agents", null, ct);

    public Task<InferenceModelOption[]> ModelsAsync(CancellationToken ct) =>
        ReadAsync<InferenceModelOption[]>(HttpMethod.Get, "admin/inference/models", null, ct);

    public Task<InferenceEnrollmentGrantResponse> PrepareInstallationAsync(CancellationToken ct) =>
        ReadAsync<InferenceEnrollmentGrantResponse>(HttpMethod.Post, "admin/inference/enrollment-grants", null, ct);

    public Task<InferenceAgentSummary> GetAsync(string agentId, CancellationToken ct) =>
        ReadAsync<InferenceAgentSummary>(HttpMethod.Get, AgentPath(agentId), null, ct);

    public Task<CreateInferenceAgentResponse> CreateAsync(CreateInferenceAgentRequest agent, CancellationToken ct) =>
        ReadAsync<CreateInferenceAgentResponse>(HttpMethod.Post, "admin/inference/agents", agent, ct);

    public Task AddWorkerAsync(string agentId, int threads, CancellationToken ct, string modelId = InferenceModels.DefaultModelId) =>
        MutateAsync(HttpMethod.Post, AgentPath(agentId) + "/workers", new AddInferenceWorkerRequest(threads, modelId), ct);

    public Task UpdateWorkerAsync(string agentId, string workerId, int threads, string modelId, CancellationToken ct) =>
        MutateAsync(HttpMethod.Patch, AgentPath(agentId) + "/workers/" + Uri.EscapeDataString(workerId),
            new UpdateInferenceWorkerRequest(threads, modelId), ct);

    public Task DeleteWorkerAsync(string agentId, string workerId, CancellationToken ct) =>
        MutateAsync(HttpMethod.Delete, AgentPath(agentId) + "/workers/" + Uri.EscapeDataString(workerId), null, ct);

    public Task DeleteAgentAsync(string agentId, CancellationToken ct) =>
        MutateAsync(HttpMethod.Delete, AgentPath(agentId), null, ct);

    private static string AgentPath(string agentId) => "admin/inference/agents/" + Uri.EscapeDataString(agentId);

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body)
    {
        if (!Enabled)
            throw new InferenceApiException(HttpStatusCode.ServiceUnavailable,
                "Inference management is disabled on this installation.");
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Arkya-Admin-Key", options.Value.AdminKey);
        if (body is not null) request.Content = JsonContent.Create(body);
        return request;
    }

    private async Task<T> ReadAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = CreateRequest(method, path, body);
        using var response = await client.SendAsync(request, ct);
        EnsureSuccess(response);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw InvalidResponse();
        }
        catch (JsonException) { throw InvalidResponse(); }
    }

    private async Task MutateAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = CreateRequest(method, path, body);
        using var response = await client.SendAsync(request, ct);
        EnsureSuccess(response);
    }

    private static InferenceApiException InvalidResponse() =>
        new(HttpStatusCode.BadGateway, "The inference service returned an invalid response.");

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InferenceApiException(response.StatusCode, response.StatusCode switch
        {
            HttpStatusCode.BadRequest => "Check the ID, name, model and worker and thread limits.",
            HttpStatusCode.Conflict => "The configuration has changed or a limit has been reached. Refresh before trying again; a machine must be empty before deletion.",
            HttpStatusCode.NotFound => "This machine or worker no longer exists. Refresh the list.",
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "Access to inference was denied. Check the administration configuration.",
            _ => "The inference service is unavailable. Try again shortly."
        });
    }
}

public static class InferencePresentation
{
    public static string StateLabel(string? state) => state?.ToLowerInvariant() switch
    {
        "ready" or "running" => "Ready",
        "busy" => "In conversation",
        "offline" => "Offline",
        "unhealthy" => "Unavailable",
        "starting" or "loading" => "Starting",
        "stopping" => "Stopping",
        "stopped" => "Stopped",
        "failed" or "error" => "Failed",
        _ => "Unknown state"
    };

    public static string StateClass(string? state) => state?.ToLowerInvariant() switch
    {
        "ready" or "running" => "ready",
        "starting" or "loading" or "stopping" or "busy" => "pending",
        "failed" or "error" or "unhealthy" => "failed",
        "offline" => "draft",
        _ => "draft"
    };

    public static bool IsDependencyError(Exception ex, CancellationToken ct) =>
        ex is InferenceApiException or HttpRequestException or IOException ||
        ex is OperationCanceledException && !ct.IsCancellationRequested;

    public static string ErrorMessage(Exception ex) => ex is InferenceApiException ? ex.Message
        : "The inference service is unavailable. Try again shortly.";
}
