using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Arkya.ServiceDefaults.Reports;
using Microsoft.Extensions.Options;
using ModelContextProtocol;

namespace Arkya.Admin.Mcp;

public sealed class ReportsMcpOptions
{
    public string BaseUrl { get; init; } = "https://localhost:7145";
    public string AdminKey { get; init; } = "";
    public string Actor { get; init; } = "codex";
    public bool AllowLoopbackHttp { get; init; }

    public bool IsValid() => Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri)
        && uri.UserInfo.Length == 0 && uri.Query.Length == 0 && uri.Fragment.Length == 0 && uri.AbsolutePath == "/"
        && (uri.Scheme == "https" || AllowLoopbackHttp && uri.Scheme == "http" && uri.IsLoopback)
        && AdminKey.Length is >= 32 and <= 512 && !AdminKey.Any(char.IsControl)
        && Actor.Length is > 0 and <= 128 && !Actor.Any(char.IsControl);
}

public sealed class ReportsMcpClient(HttpClient client, IOptions<ReportsMcpOptions> options)
{
    public Task<BugReportListResponse> ListAsync(string? tag, string? status, int offset, int limit, CancellationToken ct, string? kind = null)
    {
        if (offset is < 0 or > 100000 || limit is < 1 or > 100)
            throw new McpException("offset must be 0-100000 and limit 1-100.");
        if (tag is { Length: > 32 } || status is not null && !BugReportStatuses.All.Contains(status)
            || kind is not null && !BugReportKinds.All.Contains(kind))
            throw new McpException("Invalid tag, status or kind.");
        var query = $"internal/bug-reports?offset={offset}&limit={limit}";
        if (!string.IsNullOrWhiteSpace(tag)) query += "&tag=" + Uri.EscapeDataString(tag);
        if (status is not null) query += "&status=" + Uri.EscapeDataString(status);
        if (kind is not null) query += "&kind=" + Uri.EscapeDataString(kind);
        return SendAsync<BugReportListResponse>(HttpMethod.Get, query, null, ct);
    }

    public Task<BugReportResponse> GetAsync(Guid id, CancellationToken ct) =>
        SendAsync<BugReportResponse>(HttpMethod.Get, PathFor(id), null, ct);

    public Task<BugReportResponse> UpdateAsync(Guid id, UpdateBugReportRequest request, CancellationToken ct)
    {
        if (request.ExpectedRevision < 1 || !BugReportStatuses.All.Contains(request.Status)
            || request.Tags is null or { Length: > 10 } || request.Tags.Any(tag => string.IsNullOrWhiteSpace(tag) || tag.Length > 32)
            || request.Resolution is null or { Length: > 4000 })
            throw new McpException("Supply the current revision, a supported status, at most 10 tags (32 characters each), and a resolution of at most 4000 characters.");
        return SendAsync<BugReportResponse>(HttpMethod.Patch, PathFor(id), request, ct);
    }

    public async Task<byte[]> ScreenshotAsync(Guid id, CancellationToken ct)
    {
        return await WithDeadlineAsync(async token =>
        {
            using var response = await SendRequestAsync(HttpMethod.Get, PathFor(id) + "/screenshot", null, token);
            if (response.Content.Headers.ContentType?.MediaType != "image/png")
                throw new McpException("The report service returned an invalid screenshot.");
            var bytes = await ReadBoundedAsync(response.Content, 2 * 1024 * 1024, token);
            if (!bytes.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
                throw new McpException("The report service returned an invalid PNG.");
            return bytes;
        }, ct);
    }

    private async Task<T> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        return await WithDeadlineAsync(async token =>
        {
            using var response = await SendRequestAsync(method, path, body, token);
            var bytes = await ReadBoundedAsync(response.Content, 8 * 1024 * 1024, token);
            try
            {
                return JsonSerializer.Deserialize<T>(bytes, JsonSerializerOptions.Web)
                    ?? throw new McpException("The report service returned an empty response.");
            }
            catch (JsonException) { throw new McpException("The report service returned invalid JSON."); }
        }, ct);
    }

    private async Task<T> WithDeadlineAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(client.Timeout);
        try { return await action(deadline.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new McpException("The report service timed out."); }
        catch (HttpRequestException) { throw new McpException("Cannot read the report service response."); }
        catch (IOException) { throw new McpException("Cannot read the report service response."); }
    }

    private async Task<HttpResponseMessage> SendRequestAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Arkya-Reports-Admin-Key", options.Value.AdminKey);
        request.Headers.Add("X-Arkya-Admin-Actor", options.Value.Actor);
        if (body is not null) request.Content = JsonContent.Create(body);
        HttpResponseMessage response;
        try { response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException) { throw new McpException("Cannot reach the configured report service. Check its address, TLS and availability."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new McpException("The report service timed out."); }
        if (response.IsSuccessStatusCode) return response;
        var status = response.StatusCode;
        response.Dispose();
        throw new McpException(status switch
        {
            HttpStatusCode.Conflict => "The report changed. Read it again and use its current revision; do not overwrite another operator's update.",
            HttpStatusCode.NotFound => "Report not found.",
            HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized => "Report administration access was refused. Check the operator configuration.",
            HttpStatusCode.BadRequest => "The report service rejected the filters or triage fields.",
            HttpStatusCode.TooManyRequests => "Rate limit reached. Retry later.",
            _ => "The report service is unavailable or returned an unexpected response."
        });
    }

    private static string PathFor(Guid id) => id == Guid.Empty
        ? throw new McpException("A non-empty report ID is required.")
        : $"internal/bug-reports/{id:D}";

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, int maximum, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maximum) throw new McpException("The service response exceeded the size limit.");
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var result = new MemoryStream();
        var buffer = new byte[16384];
        int count;
        while ((count = await stream.ReadAsync(buffer, ct)) > 0)
        {
            if (result.Length + count > maximum) throw new McpException("The service response exceeded the size limit.");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }
}
