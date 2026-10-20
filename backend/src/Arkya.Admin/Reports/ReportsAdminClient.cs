using System.Net;
using System.Text.Json;
using Arkya.ServiceDefaults.Reports;
using Microsoft.Extensions.Options;

namespace Arkya.Admin.Reports;

public sealed class ReportsApiOptions
{
    public bool Enabled { get; init; }
    public string BaseUrl { get; init; } = "https://localhost:7145";
    public string AdminKey { get; init; } = "";
    public bool AllowClusterHttp { get; init; }
    public string CertificateAuthorityFile { get; init; } = "";
}

public sealed class ReportsApiException(HttpStatusCode statusCode, string message) : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
}

public sealed class ReportsAdminClient(HttpClient client, IOptions<ReportsApiOptions> options)
{
    private const int MaximumScreenshotBytes = 2 * 1024 * 1024;
    public bool Enabled => options.Value.Enabled;

    public Task<BugReportListResponse> ListAsync(string? tag, string? status, int offset, int limit, CancellationToken ct, string? kind = null)
    {
        var path = $"internal/bug-reports?offset={offset}&limit={limit}";
        if (!string.IsNullOrEmpty(tag)) path += "&tag=" + Uri.EscapeDataString(tag);
        if (!string.IsNullOrEmpty(status)) path += "&status=" + Uri.EscapeDataString(status);
        if (!string.IsNullOrEmpty(kind)) path += "&kind=" + Uri.EscapeDataString(kind);
        return SendAsync<BugReportListResponse>(HttpMethod.Get, path, null, null, ct);
    }

    public Task<BugReportResponse> GetAsync(Guid id, CancellationToken ct) =>
        SendAsync<BugReportResponse>(HttpMethod.Get, $"internal/bug-reports/{id:D}", null, null, ct);

    public Task<BugReportResponse> UpdateAsync(Guid id, UpdateBugReportRequest update, string actor, CancellationToken ct) =>
        SendAsync<BugReportResponse>(HttpMethod.Patch, $"internal/bug-reports/{id:D}", update, actor, ct);

    public async Task<byte[]> ScreenshotAsync(Guid id, CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, $"internal/bug-reports/{id:D}/screenshot");
        // ResponseHeadersRead needs its own deadline while consuming the body.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(client.Timeout);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        EnsureSuccess(response);
        if (response.Content.Headers.ContentType?.MediaType != "image/png" ||
            response.Content.Headers.ContentLength is > MaximumScreenshotBytes)
            throw InvalidResponse();
        await using var source = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await source.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (output.Length + count > MaximumScreenshotBytes) throw InvalidResponse();
            output.Write(buffer, 0, count);
        }
        var png = output.ToArray();
        if (png.Length < 8 || !png.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw InvalidResponse();
        return png;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        if (!Enabled) throw new ReportsApiException(HttpStatusCode.ServiceUnavailable, "La gestion des signalements est désactivée sur cette installation.");
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Arkya-Reports-Admin-Key", options.Value.AdminKey);
        return request;
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, string? actor, CancellationToken ct)
    {
        using var request = CreateRequest(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (actor is not null) request.Headers.Add("X-Arkya-Admin-Actor", actor);
        using var response = await client.SendAsync(request, ct);
        EnsureSuccess(response);
        try
        {
            return await response.Content.ReadFromJsonAsync<T>(ct) ?? throw InvalidResponse();
        }
        catch (JsonException) { throw InvalidResponse(); }
    }

    private static ReportsApiException InvalidResponse() =>
        new(HttpStatusCode.BadGateway, "Le service des signalements a renvoyé une réponse invalide.");

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        throw new ReportsApiException(response.StatusCode, response.StatusCode switch
        {
            HttpStatusCode.Conflict => "Ce rapport a changé. La version actuelle a été rechargée ; vérifiez-la avant de modifier à nouveau.",
            HttpStatusCode.BadRequest => "Vérifiez le statut, les tags et la résolution du rapport.",
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "L'accès aux rapports a été refusé. Vérifiez la configuration de l'administration.",
            HttpStatusCode.NotFound => "Ce rapport n'existe pas ou n'est plus disponible.",
            _ => "Le service des signalements est indisponible. Réessayez dans un instant."
        });
    }
}

public static class BugReportPresentation
{
    public static string KindLabel(string kind) => kind switch
    {
        BugReportKinds.Bug => "Bug",
        BugReportKinds.Suggestion => "Suggestion",
        _ => "Type inconnu"
    };

    public static string StatusLabel(string status, string? kind = BugReportKinds.Bug) => kind == BugReportKinds.Suggestion ? status switch
    {
        BugReportStatuses.New => "Nouvelle",
        BugReportStatuses.Triaged => "Examinée",
        BugReportStatuses.InProgress => "En cours",
        BugReportStatuses.Resolved => "Réalisée",
        BugReportStatuses.Closed => "Fermée",
        _ => "Statut inconnu"
    } : status switch
    {
        BugReportStatuses.New => "Nouveau",
        BugReportStatuses.Triaged => "Qualifié",
        BugReportStatuses.InProgress => "En cours",
        BugReportStatuses.Resolved => "Résolu",
        BugReportStatuses.Closed => "Fermé",
        _ => "Statut inconnu"
    };

    public static string StatusClass(string status) => status switch
    {
        BugReportStatuses.Resolved or BugReportStatuses.Closed => "ready",
        BugReportStatuses.InProgress => "pending",
        _ => "draft"
    };

    public static bool IsDependencyError(Exception ex, CancellationToken ct) => ex is ReportsApiException or HttpRequestException or IOException
        || ex is OperationCanceledException && !ct.IsCancellationRequested;

    public static string ErrorMessage(Exception ex) => ex is ReportsApiException ? ex.Message
        : "Le service des signalements est indisponible. Réessayez dans un instant.";
}
