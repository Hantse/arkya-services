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

    public Task<InferenceAgentSummary> GetAsync(string agentId, CancellationToken ct) =>
        ReadAsync<InferenceAgentSummary>(HttpMethod.Get, AgentPath(agentId), null, ct);

    public Task<CreateInferenceAgentResponse> CreateAsync(CreateInferenceAgentRequest agent, CancellationToken ct) =>
        ReadAsync<CreateInferenceAgentResponse>(HttpMethod.Post, "admin/inference/agents", agent, ct);

    public Task AddWorkerAsync(string agentId, int threads, CancellationToken ct) =>
        MutateAsync(HttpMethod.Post, AgentPath(agentId) + "/workers", new AddInferenceWorkerRequest(threads), ct);

    public Task DeleteWorkerAsync(string agentId, string workerId, CancellationToken ct) =>
        MutateAsync(HttpMethod.Delete, AgentPath(agentId) + "/workers/" + Uri.EscapeDataString(workerId), null, ct);

    public Task DeleteAgentAsync(string agentId, CancellationToken ct) =>
        MutateAsync(HttpMethod.Delete, AgentPath(agentId), null, ct);

    private static string AgentPath(string agentId) => "admin/inference/agents/" + Uri.EscapeDataString(agentId);

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body)
    {
        if (!Enabled)
            throw new InferenceApiException(HttpStatusCode.ServiceUnavailable,
                "La gestion de l’inférence est désactivée sur cette installation.");
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
        new(HttpStatusCode.BadGateway, "Le service d’inférence a renvoyé une réponse invalide.");

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new InferenceApiException(response.StatusCode, response.StatusCode switch
        {
            HttpStatusCode.BadRequest => "Vérifiez l’identifiant, le nom et les limites de workers et de threads.",
            HttpStatusCode.Conflict => "La configuration a changé ou une limite est atteinte. Actualisez avant de réessayer ; une machine doit être vide pour être supprimée.",
            HttpStatusCode.NotFound => "Cette machine ou ce worker n’existe plus. Actualisez la liste.",
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "L’accès à l’inférence a été refusé. Vérifiez la configuration de l’administration.",
            _ => "Le service d’inférence est indisponible. Réessayez dans un instant."
        });
    }
}

public static class InferencePresentation
{
    public static string StateLabel(string? state) => state?.ToLowerInvariant() switch
    {
        "ready" or "running" => "Prêt",
        "busy" => "En conversation",
        "offline" => "Hors ligne",
        "unhealthy" => "Indisponible",
        "starting" or "loading" => "Démarrage",
        "stopping" => "Arrêt en cours",
        "stopped" => "Arrêté",
        "failed" or "error" => "En échec",
        _ => "État inconnu"
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
        : "Le service d’inférence est indisponible. Réessayez dans un instant.";
}
